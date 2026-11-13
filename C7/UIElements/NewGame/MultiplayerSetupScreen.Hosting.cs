using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Network;
using C7GameData;
using C7GameData.Save;
using Godot;
using static SetupStyle;

// The Multiplayer Setup screen as the host's lobby (owner's request,
// 2026-11-07), as Civ3's is: for a LAN or Internet game the game is hosted
// as soon as the screen is up, players join its Open slots and choose their
// civilizations while the host sets it up, and Launch creates the world and
// starts it once every Open slot is taken. A smaller world, or a slot set to
// Closed or Computer, takes away slots: Closed and computer slots go first,
// then Open slots nobody has taken, and only then the slots of the players
// who joined last, who watch the game instead.
public partial class MultiplayerSetupScreen {
	private LanHost Host => LanSession.Host;
	private bool Hosting => Host != null;

	// Parallel to slots: the seat each Open slot is while hosting, by its
	// ID; null for other slots, and for one just opened until the host has
	// a seat for it.
	private List<ID> slotSeats = [];
	private bool syncingSeats;

	// GAME SETTINGS: the list the settings are in, and at its foot how
	// players join.
	private VBoxContainer settingsList;
	private HostJoiningSection joiningSection;

	// PLAYER SETUP: who is watching; and Remove and Ban, for the player in
	// the selected slot.
	private Label watchingLabel;
	private Button removeButton;
	private Button banButton;

	// MESSAGES: how the lobby stands, over the messages.
	private Label hostStatus;
	private readonly LobbyEvents lobbyEvents = new();

	// The world is being made, or has been and waits for a player who left
	// meanwhile to be back.
	private bool creatingGame;
	private bool gameCreated;

	// How the game is set up, as last sent to the players.
	private string sentSummary;
	private double sinceSummary;

	public override void _Process(double delta) {
		if (!Hosting) {
			return;
		}
		Host.Poll();
		joiningSection?.Update();
		sinceSummary += delta;
		if (sinceSummary >= 0.5) {
			sinceSummary = 0;
			SendSummary();
		}
	}

	// Starts hosting for a networked game, or stops for hotseat.
	private void UpdateHosting() {
		bool networked = !MultiplayerSlots.SeatsSetUpHere(GameType);
		if (networked && !Hosting) {
			StartHosting();
		} else if (!networked && Hosting) {
			StopHosting();
		}
		if (!Hosting) {
			return;
		}
		bool online = Host.Online != null;
		if (GameType == MultiplayerGameType.Internet && !online) {
			joiningSection.HostOnline();
		} else if (GameType == MultiplayerGameType.Lan && online) {
			joiningSection.StopOnline();
		}
	}

	private void StartHosting() {
		try {
			LanSession.BeginHosting(new LanHost(HostName, null, 0, save.Civilizations));
		} catch (Exception e) when (e is SocketException or ArgumentException or InvalidOperationException) {
			log.Error(e, "Could not host the game");
			AddMessage($"Could not host the game: {e.Message}", ProblemColor);
			return;
		}
		Host.AutosaveDirectory = LanAutosave.DefaultDirectory;
		Host.LobbyChanged += OnLobbyChanged;
		slotSeats = slots.Select(_ => (ID)null).ToList();
		lobbyEvents.Reset();

		joiningSection = new HostJoiningSection();
		joiningSection.Build(this, hostOnlineButtonShown: false);
		settingsList.AddChild(joiningSection);
		hostStatus.Visible = true;
		removeButton.Visible = true;
		banButton.Visible = true;
		sentSummary = null;
		SendSummary();
		MakeSlotRows();
	}

	private void StopHosting() {
		if (Host != null) {
			Host.LobbyChanged -= OnLobbyChanged;
		}
		LanSession.End();
		joiningSection?.QueueFree();
		joiningSection = null;
		slotSeats = slots.Select(_ => (ID)null).ToList();
		hostStatus.Visible = false;
		watchingLabel.Visible = false;
		removeButton.Visible = false;
		banButton.Visible = false;
	}

	// Keeps slotSeats as long as slots.
	private void PadSlotSeats() {
		while (slotSeats.Count < slots.Count) {
			slotSeats.Add(null);
		}
		if (slotSeats.Count > slots.Count) {
			slotSeats.RemoveRange(slots.Count, slotSeats.Count - slots.Count);
		}
	}

	// The order the player in the slot joined in, or 0 if it has none.
	private long JoinOrder(int slot) {
		return Hosting && slot < slotSeats.Count && slotSeats[slot] != null && slots[slot].kind == SlotKind.Open
			? Host.JoinOrder(slotSeats[slot]) : 0;
	}

	// While hosting, a world with room for fewer slots keeps those
	// MultiplayerSlots.SlotsToKeep says, so that only players who joined
	// last lose theirs.
	private void FitSlots(int count) {
		PadSlotSeats();
		if (!Hosting || slots.Count <= count) {
			return;
		}
		List<int> kept = MultiplayerSlots.SlotsToKeep(slots, count, JoinOrder);
		slots = kept.Select(i => slots[i]).ToList();
		slotSeats = kept.Select(i => slotSeats[i]).ToList();
	}

	// Gives the host a seat for each Open slot, keeping the seats of those
	// still Open; players whose slot went watch instead.
	private void SyncSeats() {
		if (!Hosting || creatingGame || gameCreated) {
			return;
		}
		PadSlotSeats();
		List<int> open = Enumerable.Range(0, rows.Count).Where(i => slots[i].kind == SlotKind.Open).ToList();
		List<ID> layout = open.Select(i => slotSeats[i]).ToList();
		if (!layout.SequenceEqual(Host.Seats.Select(s => s.playerID))) {
			syncingSeats = true;
			try {
				Host.SetSeats(layout);
			} finally {
				syncingSeats = false;
			}
		}
		List<ID> ids = Host.Seats.Select(s => s.playerID).ToList();
		for (int i = 0; i < slotSeats.Count; ++i) {
			slotSeats[i] = null;
		}
		for (int k = 0; k < open.Count; ++k) {
			slotSeats[open[k]] = ids[k];
		}
		ReserveCivilizations();
		OnLobbyChanged();
	}

	// The host's civilization and the computer's chosen ones, which guests
	// can't choose.
	private void ReserveCivilizations() {
		if (!Hosting || creatingGame || gameCreated) {
			return;
		}
		string host = civNames[hostCivilization.Selected];
		Host.HostCivilization = host == NewGameChoices.Random ? null : host;
		Host.ReservedCivilizations = Enumerable.Range(0, rows.Count)
			.Where(i => slots[i].kind == SlotKind.Computer && slots[i].civilization is string c && c != NewGameChoices.Random)
			.Select(i => slots[i].civilization)
			.ToList();
	}

	// The civilizations the players who joined chose, which the host and
	// the computer can't have.
	private HashSet<string> GuestCivilizations() {
		return Hosting ? Host.Seats.Where(s => s.civilization != null).Select(s => s.civilization).ToHashSet() : [];
	}

	private SeatInfo SeatOf(int slot) {
		return Hosting && slot >= 0 && slot < slotSeats.Count && slotSeats[slot] is ID id
			? Host.Seats.FirstOrDefault(s => s.playerID == id) : null;
	}

	// The players as they stand: who has taken each Open slot and the
	// civilization they chose, who is watching, and what the messages say.
	private void OnLobbyChanged() {
		if (syncingSeats || !Hosting || rows.Count == 0) {
			return;
		}
		lobbyEvents.Note(Host.Seats, Host.Spectators, text => AddMessage(text));
		for (int i = 0; i < rows.Count && i < slots.Count; ++i) {
			if (slots[i].kind != SlotKind.Open) {
				continue;
			}
			(OptionButton kind, OptionButton civ) = rows[i];
			SeatInfo seat = SeatOf(i);
			string who = seat?.takenBy == null ? "Open" : seat.disconnected ? $"{seat.takenBy} (disconnected)" : seat.takenBy;
			kind.SetItemText(0, who);
			kind.TooltipText = seat?.takenBy == null
				? "Open: a slot for a player joining over the network. Closed: no civilization. Computer: a civilization the computer plays."
				: $"{seat.takenBy} has this slot. Set it to Closed or Computer to take it from them; they'd watch the game instead.";
			civ.Select(Math.Max(0, civNames.IndexOf(seat?.civilization ?? NewGameChoices.Random)));
			civ.TooltipText = seat?.takenBy == null ? "Whoever takes this slot chooses their civilization; Random if they don't."
				: $"The civilization {seat.takenBy} chose.";
		}
		IReadOnlyList<string> watching = Host.Spectators;
		watchingLabel.Visible = watching.Count > 0;
		watchingLabel.Text = $"Watching: {string.Join(", ", watching)}";
		UpdateCivilizations();
		ShowSelectedPlayer();
		ShowHostStatus();
	}

	private void ShowHostStatus() {
		if (!Hosting) {
			return;
		}
		int open = Host.Seats.Count(s => s.takenBy == null);
		hostStatus.Text = creatingGame ? "Creating the world..."
			: gameCreated && !Host.AllSeatsTakenOrHeld ? "Waiting for a player to take the slot left open, to start the game."
			: Host.Seats.Count == 0 ? "Set a slot to Open for each player who will join."
			: open > 0 ? $"Waiting for {open} more {(open == 1 ? "player" : "players")} to join. Players choose \"Join LAN Game\" and take an Open slot."
			: "Everyone is here. Launch starts the game.";
	}

	// Remove and Ban act on the player in the selected slot.
	private void ShowSelectedPlayer() {
		SeatInfo seat = SeatOf(selectedRow);
		bool taken = seat?.takenBy != null && !creatingGame && !gameCreated;
		removeButton.Disabled = !taken;
		banButton.Disabled = !taken;
		removeButton.TooltipText = taken ? $"Remove {seat.takenBy} from the game, opening their slot again." : "Select a player's slot to remove them.";
		banButton.TooltipText = taken ? $"Remove {seat.takenBy} and keep them out of this game." : "Select a player's slot to ban them.";
	}

	private void RemoveSelected() {
		if (SeatOf(selectedRow) is SeatInfo { takenBy: not null } seat) {
			HostActions.Remove(seat.playerID);
		}
	}

	private void BanSelected() {
		if (SeatOf(selectedRow) is SeatInfo { takenBy: not null } seat) {
			HostActions.ConfirmBan(this, seat.takenBy, seat.playerID);
		}
	}

	// Sends the players how the game is set up, as this screen shows it,
	// when it has changed.
	private void SendSummary() {
		if (!Hosting) {
			return;
		}
		List<LobbySetting> settings = [];
		foreach (Node child in settingsList.GetChildren()) {
			if (child is OptionButton { Visible: true } list && list != turnTimer && list.GetIndex() > 0
				&& settingsList.GetChild(list.GetIndex() - 1) is Label label && list.Selected >= 0) {
				settings.Add(new LobbySetting(label.Text, list.GetItemText(list.Selected)));
			}
		}
		List<LobbyRule> rules = ruleBoxes.Select(r => new LobbyRule(r.Text, r.ButtonPressed, r.TooltipText)).ToList();
		int minutes = LanLobby.TurnTimeChoices[turnTimer.Selected];
		string name = gameNameEdit.Text.Trim();
		string key = string.Join("\n", settings.Select(s => s.label + s.value)) + "|" + string.Join(",", rules.Select(r => r.on))
			+ $"|{minutes}|{name}|{simultaneousMoves.ButtonPressed}|{HostName}";
		if (key == sentSummary) {
			return;
		}
		sentSummary = key;
		Host.HostName = HostName;
		Host.PublicName = name == "" ? null : name;
		Host.TurnTimeLimit = minutes == 0 ? null : TimeSpan.FromMinutes(minutes);
		Host.SimultaneousTurns = simultaneousMoves.ButtonPressed;
		Host.GameType = LanGameList.GameTypeName(scenario);
		WorldSize size = worldSize.Selected < save.WorldSizes.Count ? save.WorldSizes[worldSize.Selected] : null;
		Host.MapSize = size != null && size.width > 0 ? $"{size.width}x{size.height}" : null;
		Host.LobbySettings = settings;
		Host.LobbyRules = rules;
	}

	// Creates the world with the civilizations the players chose, then
	// starts the game; or, once created, starts it when everyone is back.
	private void LaunchHosted(GameSetup setup, MultiplayerPlan plan) {
		if (gameCreated) {
			StartHostedGame();
			return;
		}
		if (Host.Seats.Count != plan.guestSeats || !Host.AllSeatsTakenOrHeld) {
			ShowHostStatus();
			AddMessage("Every Open slot needs a player before the game can start: wait for them to join, or set the slot to Closed or Computer.",
				ProblemColor);
			SetLaunching(false);
			return;
		}
		int minutes = LanLobby.TurnTimeChoices[turnTimer.Selected];
		string name = gameNameEdit.Text.Trim();
		LanSession.HostNextGame = true;
		LanSession.PendingGame = new PendingLanGame(setup, save, plan.guestSeats,
			gameName: name == "" ? null : name,
			turnTimeLimit: minutes == 0 ? null : TimeSpan.FromMinutes(minutes),
			simultaneousTurns: simultaneousMoves.ButtonPressed,
			hostOnline: GameType == MultiplayerGameType.Internet);
		creatingGame = true;
		setup.hotseatPlayers = Host.BeginCreatingGame();
		AddMessage("Creating the world...");
		ShowHostStatus();
		SaveGame rules = save;
		Task.Run(() => {
			try {
				setup.Populate(rules);
			} catch (Exception e) {
				Callable.From(() => HostedLaunchFailed(e)).CallDeferred();
				return;
			}
			Callable.From(() => HostedGameCreated(rules)).CallDeferred();
		});
	}

	private void HostedGameCreated(SaveGame created) {
		creatingGame = false;
		if (!IsInstanceValid(this) || !IsInsideTree() || !Hosting) {
			return;
		}
		Host.GameCreated(created);
		LanSession.PendingGame = null;
		global.SaveGame = created;
		gameCreated = true;
		StartHostedGame();
	}

	// Into the game, once everyone has a seat; a player who left while the
	// world was made is waited for.
	private void StartHostedGame() {
		if (!Host.AllSeatsTakenOrHeld) {
			SetLaunching(false);
			launchButton.Text = "Start Game";
			ShowHostStatus();
			return;
		}
		Host.LobbyChanged -= OnLobbyChanged;
		LanSession.HostNextGame = false;
		GetTree().ChangeSceneToFile(LanSession.GameScene);
	}

	private void HostedLaunchFailed(Exception e) {
		creatingGame = false;
		if (Hosting) {
			Host.CancelCreatingGame();
		}
		LaunchFailed(e);
		ShowHostStatus();
	}
}
