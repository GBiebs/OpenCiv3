using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using C7Engine;
using C7Engine.Network;
using C7GameData;
using C7GameData.Save;
using C7Relay;
using Godot;
using Serilog;
using static SetupStyle;

// Where players gather before a LAN game. The host waits here for a player to
// take each human seat, then starts the game; the others find the host, take
// a seat (or just watch), and wait for the host to start. A guest may take
// several seats, for players taking turns at their machine as in a hotseat
// game. In a new game the guests also choose their civilizations here, and
// the host creates the game when starting it.
//
// Looking for a game shows the list of games (LanGameBrowser). Hosting, or
// once in a game's lobby, everyone sees Civ3's Multiplayer Setup screen, in
// OpenCiv3's look (owner's request, 2026-11-07): the game's settings on the
// left, the players, rules and messages on the right (LanLobby.Setup.cs).
public partial class LanLobby : Control {
	private ILogger log = LogManager.ForContext<LanLobby>();

	// Whether we came here to join a game rather than host one, and whether
	// to open the server browser straight away.
	public static bool joining = false;
	public static bool openBrowser = false;

	private GlobalSingleton Global;

	// The list of games, in its own parchment frame, while looking for one.
	private Control browserRoot;
	private Label title;
	private VBoxContainer content;

	// The status line: under the list of games while looking for one, at the
	// top of the messages once in a lobby.
	private Label status;

	// Hosting a new game: whether it is being created, and why that failed.
	private bool creatingGame = false;
	private string createFailure;

	// Joining: the name to play under, the list of games, and the join
	// code typed.
	private LineEdit nameEdit;
	private LanGameBrowser gameList;
	private LineEdit codeEdit;

	// Joining a game with a password: where the player types it.
	private HBoxContainer passwordPrompt;
	private LineEdit passwordPromptEdit;

	// Joining: the connection being made, called off by leaving the lobby or
	// joining elsewhere.
	private CancellationTokenSource connecting;

	// Joining: in a game in progress, the open seats ticked to rejoin with.
	private readonly HashSet<ID> rejoinSeats = new();

	// Joining: names typed for our seats' players but not yet sent, and the
	// seat whose name is being typed, kept as the seats are redrawn.
	private readonly Dictionary<ID, string> seatNameDrafts = new();
	private ID editingSeatName;

	public override void _Ready() {
		Global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		ColorRect backdrop = new() { Color = Colors.Black };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(backdrop);

		status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		status.AddThemeFontSizeOverride("font_size", 16);

		BuildSetupScreen();
		if (joining) {
			BuildBrowserFrame();
			BuildJoinScreen();
			ShowSetup(false);
		} else {
			ShowSetup(true);
			BuildHostScreen();
		}
	}

	// The 1024x768 parchment frame the list of games is in, centered like
	// the other setup screens.
	private void BuildBrowserFrame() {
		CenterContainer center = new();
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);
		browserRoot = center;

		TextureRect background = new() { Texture = TextureLoader.Load("credits.background") };
		center.AddChild(background);

		// The title sits on the stone band above the parchment.
		title = new Label {
			Position = new Vector2(60, 14),
			Size = new Vector2(904, 52),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 30);
		background.AddChild(title);

		// The parchment's inner panel; scrolls if it outgrows it.
		ScrollContainer scroll = new() {
			Position = new Vector2(64, 90),
			Size = new Vector2(896, 590),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		background.AddChild(scroll);

		MarginContainer margin = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		foreach (string side in new[] { "left", "right", "top", "bottom" }) {
			margin.AddThemeConstantOverride($"margin_{side}", 12);
		}
		scroll.AddChild(margin);

		content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 12);
		margin.AddChild(content);

		TextureButton exit = new() {
			Position = new Vector2(952, 720),
			TooltipText = "Back to the main menu",
			Shortcut = new Shortcut { Events = [new InputEventKey { Keycode = Key.Escape }] },
		};
		TextureLoader.SetButtonTextures(exit, "ui.exit");
		exit.Pressed += BackToMenu;
		background.AddChild(exit);
	}

	// Shows the Multiplayer Setup screen, or else the list of games, with
	// the status line.
	private void ShowSetup(bool show) {
		setupRoot.Visible = show;
		if (browserRoot != null) {
			browserRoot.Visible = !show;
		}
		// The status line is smaller among the messages than under the list.
		status.AddThemeFontSizeOverride("font_size", show ? 14 : 16);
		Node place = show ? statusSlot : gameList?.Visible == true ? gameList.StatusSlot : content;
		if (place != null && status.GetParent() != place) {
			if (status.GetParent() == null) {
				place.AddChild(status);
			} else {
				status.Reparent(place, false);
			}
		}
	}

	private void BackToMenu() {
		connecting?.Cancel();
		LanSession.End();
		LanSession.HostNextGame = false;
		LanSession.PendingGame = null;
		LanSession.ResumeGame = null;
		Global.ResetLoadGameFields();
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}

	// ---- Hosting ----

	private void BuildHostScreen() {
		SaveGame save;
		try {
			if (LanSession.PendingGame is PendingLanGame pending) {
				// A new game, created once the guests have chosen.
				save = pending.save;
				Global.SaveGame = save;
				LanSession.BeginHosting(new LanHost(LanSession.PlayerName, pending.setup.playerCivilization.name,
					pending.guestSeats, save.Civilizations, teamCount: pending.teamCount, hostTeam: pending.setup.playerTeam,
					guestTeams: pending.guestTeams));
				// The map isn't made yet, but its size is known.
				if (pending.setup.worldCharacteristics?.worldSize is WorldSize size && size.width > 0) {
					LanSession.Host.MapSize = $"{size.width}x{size.height}";
				}
				// As set on the Multiplayer Setup screen.
				LanSession.Host.PublicName = pending.gameName;
				LanSession.Host.TurnTimeLimit = pending.turnTimeLimit;
				// For the game list's Game Type column; not in the save until the game is made.
				LanSession.Host.GameType = LanGameList.GameTypeName(pending.setup.scenario);
				(LanSession.Host.LobbySettings, LanSession.Host.LobbyRules) = LobbySummary.Of(pending.setup, save);
			} else if (LanSession.ResumeGame is LanResumeInfo resume) {
				// The last game hosted here, as it was autosaved, with its
				// guests' seats held for them.
				save = LoadAutosave();
				Global.SaveGame = save;
				Global.LoadGamePath = null;
				LanSession.BeginHosting(LanHost.Resume(resume.hostName ?? LanSession.PlayerName, save, resume));
				(LanSession.Host.LobbySettings, LanSession.Host.LobbyRules) = LobbySummary.Of(save);
			} else {
				save = Global.SaveGame ?? LoadSavedGame(Global.LoadGamePath);
				Global.SaveGame = save;
				Global.LoadGamePath = null;
				LanSession.BeginHosting(new LanHost(LanSession.PlayerName, save));
				(LanSession.Host.LobbySettings, LanSession.Host.LobbyRules) = LobbySummary.Of(save);
			}
		} catch (Exception e) when (e is SocketException or ArgumentException or InvalidOperationException) {
			log.Error(e, "Could not host the LAN game");
			status.Text = $"Could not host the game: {e.Message}";
			return;
		}
		// So that the game can be hosted again if this machine's game ends.
		LanSession.Host.AutosaveDirectory = LanAutosave.DefaultDirectory;

		BuildHostSettings();
		AddMessage(messages, $"Hosting {LanSession.Host.CurrentDetails().gameName}. Players join the Open slots from \"Join LAN Game\".");

		LanSession.Host.LobbyChanged += ShowHostSeats;
		ShowHostSeats();
	}

	// The choices for how long each player has for their turn, in minutes;
	// 0 means no limit.
	public static readonly int[] TurnTimeChoices = [0, 2, 5, 10, 15, 20, 30, 45, 60];

	// The latest autosave that loads, which is the one before the last if the
	// last was cut short.
	private SaveGame LoadAutosave() {
		Exception failure = null;
		foreach (string path in LanAutosave.SavesToResume(LanAutosave.DefaultDirectory)) {
			try {
				return LoadSavedGame(path);
			} catch (Exception e) {
				log.Warning("Could not load {Path}: {Error}", path, e.Message);
				failure = e;
			}
		}
		throw new InvalidOperationException($"There is no LAN game to resume ({failure?.Message ?? "no autosave"})");
	}

	private SaveGame LoadSavedGame(string path) {
		if (path == null) {
			throw new InvalidOperationException("There is no game to host");
		}
		return SaveManager.LoadSave(path, GamePaths.DefaultBicPath, Util.PediaIconsPathFor);
	}

	// The host's status line and Start Game button, as the seats stand.
	private void ShowHostStatus() {
		LanHost host = LanSession.Host;
		if (createFailure != null) {
			status.Text = $"Could not create the game: {createFailure}";
			startButton.Disabled = true;
		} else if (creatingGame) {
			status.Text = "Creating the world...";
			startButton.Disabled = true;
		} else if (host.Seats.Count == 0) {
			status.Text = "This game has only one human player. Start a new game and add players on the player setup screen, or load a game with more human players.";
			startButton.Disabled = true;
		} else if (!host.AllSeatsTakenOrHeld) {
			int open = host.Seats.Count(s => s.takenBy == null);
			status.Text = $"Waiting for {open} more {(open == 1 ? "player" : "players")} to join...";
			startButton.Disabled = true;
		} else if (!host.AllSeatsTaken) {
			// Resuming: some players haven't reconnected yet.
			List<string> missing = host.Seats.Where(s => s.disconnected).Select(s => s.takenBy).ToList();
			status.Text = $"Waiting for {string.Join(", ", missing)} to reconnect. You can start without them: "
				+ "the game waits for them to come back, or you can go on without them. Or release their seats for others to take.";
			startButton.Disabled = false;
		} else {
			status.Text = host.GuestsChooseCivilizations
				? "Everyone is here. Players can change their civilization" + (host.TeamCount > 0 ? " and team" : "")
					+ " until you start; anyone who hasn't chosen a civilization gets a random one."
				: "Everyone is here.";
			startButton.Disabled = false;
			if (LanSession.DevHostSave != null) {
				CallDeferred(nameof(StartHostedGame));
			}
		}
	}

	private void StartHostedGame() {
		if (!LanSession.Host.AllSeatsTakenOrHeld || creatingGame || createFailure != null) {
			return;
		}
		if (LanSession.Host.GuestsChooseCivilizations) {
			CreatePendingGame();
			return;
		}
		LanSession.Host.LobbyChanged -= ShowHostSeats;
		LanSession.HostNextGame = false;
		LanSession.ResumeGame = null;
		GetTree().ChangeSceneToFile(LanSession.GameScene);
	}

	// Creates the new game with the civilizations the guests chose, then
	// starts it. World generation can take a while, so it runs off the UI
	// thread while the lobby keeps going.
	private void CreatePendingGame() {
		PendingLanGame pending = LanSession.PendingGame;
		creatingGame = true;
		pending.setup.playerTeam = LanSession.Host.HostTeam;
		pending.setup.hotseatPlayers = LanSession.Host.BeginCreatingGame();
		AddMessage(messages, "Creating the world...");
		Thread thread = new(() => {
			string failure = "";
			try {
				pending.setup.Populate(pending.save);
			} catch (Exception e) {
				log.Error(e, "Could not create the LAN game");
				failure = e.Message;
			}
			try {
				CallDeferred(nameof(PendingGameCreated), failure);
			} catch (ObjectDisposedException) {
				// The host left the lobby meanwhile.
			}
		});
		thread.Start();
	}

	private void PendingGameCreated(string failure) {
		creatingGame = false;
		if (LanSession.Host == null) {
			return;
		}
		if (failure != "") {
			// The save is half made, so there's no trying again.
			createFailure = failure;
			AddMessage(messages, $"Could not create the game: {failure}", ProblemColor);
			ShowHostSeats();
			return;
		}
		PendingLanGame pending = LanSession.PendingGame;
		LanSession.PendingGame = null;
		LanSession.Host.GameCreated(pending.save);
		Global.SaveGame = pending.save;
		// If someone left while the world was made, this waits for their
		// seat to be taken again.
		StartHostedGame();
	}

	// ---- Joining ----

	private void BuildJoinScreen() {
		title.Text = "Join a LAN Game";

		// The list fills the parchment, as Steam's server browser fills its
		// window (owner's request, 2026-10-28); the Multiplayer Setup screen
		// takes its place once in the game's lobby.
		gameList = new LanGameBrowser { CustomMinimumSize = new Vector2(0, 552) };
		gameList.JoinRequested += (endpoint, watch, token) => Connect(endpoint, watch, token);
		content.AddChild(gameList);

		HBoxContainer nameRow = new();
		nameRow.AddThemeConstantOverride("separation", 8);
		Label nameLabel = new() { Text = "Your name:" };
		nameLabel.AddThemeFontSizeOverride("font_size", 16);
		nameRow.AddChild(nameLabel);
		nameEdit = new LineEdit { Text = LanSession.PlayerName, MaxLength = LanHost.MaxPlayerNameLength, CustomMinimumSize = new Vector2(170, 0) };
		nameRow.AddChild(nameEdit);
		gameList.AddToTopRow(nameRow);

		// A host elsewhere, hosting online, gives a join code; or one is
		// listed publicly on the relay.
		HBoxContainer onlineRow = new();
		onlineRow.AddThemeConstantOverride("separation", 10);
		Label onlineLabel = new() { Text = "Online game code:" };
		onlineLabel.AddThemeFontSizeOverride("font_size", 16);
		onlineRow.AddChild(onlineLabel);
		codeEdit = new LineEdit { PlaceholderText = "KQ7-4MZ", MaxLength = 200, CustomMinimumSize = new Vector2(180, 0) };
		codeEdit.TextSubmitted += _ => ConnectWithCode(false);
		onlineRow.AddChild(codeEdit);
		onlineRow.AddChild(LanGameBrowser.MakeButton("Join", () => ConnectWithCode(false)));
		onlineRow.AddChild(LanGameBrowser.MakeButton("Watch", () => ConnectWithCode(true)));
		onlineRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
		onlineRow.AddChild(LanGameBrowser.MakeButton("Browse Online Games", OpenBrowser));
		gameList.AddRow(onlineRow);

		// The game last joined from here, to get back into after closing
		// the game; the list's Rejoin also finds it at a new address.
		if (LanSession.LastJoinedGame is LanSession.LastGame last) {
			string where = last.endpoint is RelayEndpoint online ? $"online, code {RelayProtocol.FormatCode(online.Code)}" : $"at {last.address}";
			Button rejoin = LanGameBrowser.MakeButton("Rejoin Last Game", () => Connect(last.endpoint, false, last.token));
			rejoin.TooltipText = $"Your last game: {last.hostName} {where}";
			gameList.AddBottomButton(rejoin);
		}

		// Shown when the host asks for its game's password.
		passwordPrompt = new HBoxContainer { Visible = false };
		passwordPrompt.AddThemeConstantOverride("separation", 12);
		Label passwordLabel = new() { Text = "Password:" };
		passwordLabel.AddThemeFontSizeOverride("font_size", 18);
		passwordPrompt.AddChild(passwordLabel);
		passwordPromptEdit = new LineEdit { Secret = true, MaxLength = GamePassword.MaxLength, CustomMinimumSize = new Vector2(260, 0) };
		passwordPromptEdit.TextSubmitted += _ => SendPassword();
		passwordPrompt.AddChild(passwordPromptEdit);
		Button enter = new() { Text = "Enter", CustomMinimumSize = new Vector2(160, 36) };
		enter.AddThemeFontSizeOverride("font_size", 18);
		enter.Pressed += SendPassword;
		passwordPrompt.AddChild(enter);
		content.AddChild(passwordPrompt);

		if (LanSession.DevJoinAddress != null && RelayProtocol.NormalizeCode(LanSession.DevJoinAddress) != null
			&& !LanSession.DevJoinAddress.Contains('.')) {
			codeEdit.Text = LanSession.DevJoinAddress;
			ConnectWithCode(LanSession.DevWatch);
		} else if (LanSession.DevJoinAddress != null) {
			gameList.AddressEdit.Text = LanSession.DevJoinAddress;
			gameList.JoinAddress(LanSession.DevWatch);
		}

		if (openBrowser) {
			openBrowser = false;
			OpenBrowser();
		}
	}

	// The server browser, over the join screen; joining a game from it
	// joins as typing its code would.
	private void OpenBrowser() {
		ServerBrowser browser = new();
		browser.JoinRequested += (code, relayUrl, name, watch, password) => {
			codeEdit.Text = OnlineRelay.Invite(RelayProtocol.FormatCode(code), relayUrl);
			Connect(new RelayEndpoint(relayUrl, code), watch, password: password);
		};
		AddChild(browser);
	}

	private void SendPassword() {
		LanClient client = LanSession.Client;
		if (client?.PasswordRequest == null || passwordPromptEdit.Text == "") {
			return;
		}
		client.SendPassword(passwordPromptEdit.Text);
		passwordPromptEdit.Text = "";
		passwordPrompt.Visible = false;
		status.Text = "Checking the password...";
	}

	// The list of games while looking for one, or else the parchment with
	// the status line while connecting; the Multiplayer Setup screen once in
	// the game's lobby.
	private void ShowGameList(bool show) {
		if (gameList == null) {
			return;
		}
		if (show) {
			ShowSetup(false);
		}
		if (gameList.Visible == show) {
			return;
		}
		gameList.Visible = show;
		if (!setupRoot.Visible) {
			status.Reparent(show ? gameList.StatusSlot : content, false);
		}
		if (show) {
			passwordPrompt.Visible = false;
			ClearLobbyView();
			gameList.Refresh();
		}
	}

	// Joins the game with the code typed, through the relay.
	private void ConnectWithCode(bool watch) {
		if (codeEdit.Text.Trim() == "") {
			return;
		}
		// The host's invite may name its relay, as in KQ7-4MZ@relay.example.org.
		if (!OnlineRelay.TryParseInvite(codeEdit.Text, out string code, out string relayUrl)) {
			status.Text = "A join code is six letters and digits, like KQ7-4MZ, sometimes followed by the host's relay server. Check it with the host.";
			return;
		}
		if (OnlineRelay.Problem(relayUrl) is string problem) {
			status.Text = problem;
			return;
		}
		Connect(new RelayEndpoint(relayUrl, code), watch);
	}

	// Joins the host there; with the token from a game we were in, to have
	// our seats in it back. Connecting happens in the background, since a
	// host that doesn't answer can take a while to give up on.
	private async void Connect(LanEndpoint endpoint, bool watch, string reconnectToken = null, string password = null) {
		LanSession.PlayerName = nameEdit.Text;
		connecting?.Cancel();
		CancellationTokenSource cancel = new();
		connecting = cancel;
		status.Text = $"Connecting to {endpoint.Description}...";
		LanClient client;
		try {
			client = await LanClient.ConnectAsync(endpoint, LanSession.PlayerName, reconnectToken, cancel: cancel.Token, password: password);
		} catch (OperationCanceledException) when (cancel.IsCancellationRequested) {
			return;
		} catch (Exception e) {
			if (connecting == cancel && IsInstanceValid(status)) {
				connecting = null;
				status.Text = $"Could not connect to {endpoint.Description}: {e.Message}";
			}
			return;
		}
		// Left the lobby, or joining elsewhere, meanwhile.
		if (cancel.IsCancellationRequested || connecting != cancel || !IsInstanceValid(status)) {
			client.Dispose();
			return;
		}
		connecting = null;
		if (endpoint is LanAddressEndpoint joined) {
			RecentLanHosts.Remember(joined.Address);
		}
		LanSession.BeginJoining(client);
		ShowGameList(false);
		status.Text = $"Connected to {endpoint.Description}. Waiting for the host...";
		LanSession.Client.LobbyChanged += ShowJoinedSeats;
		if (watch) {
			LanSession.Client.Watch();
		}
	}

	private void ShowJoinedSeats() {
		LanClient client = LanSession.Client;
		if (client.RejectedReason != null) {
			passwordPrompt.Visible = false;
			ShowGameList(true);
			status.Text = client.RejectedReason;
			return;
		}
		if (client.PasswordRequest is PasswordChallengeInfo challenge) {
			passwordPrompt.Visible = true;
			passwordPromptEdit.GrabFocus();
			status.Text = challenge.wrong
				? $"That isn't the password. {challenge.attemptsLeft} more {(challenge.attemptsLeft == 1 ? "try" : "tries")} before you have to wait."
				: "This game needs a password. Ask the host for it.";
			return;
		}
		passwordPrompt.Visible = false;

		if (client.StartingGame != null) {
			// The host started the game; it's ours from here.
			client.LobbyChanged -= ShowJoinedSeats;
			Global.SaveGame = client.StartingGame;
			GetTree().ChangeSceneToFile(LanSession.GameScene);
			return;
		}

		LobbyInfo lobby = client.Lobby;
		if (lobby == null) {
			return;
		}
		ShowSetup(true);
		ShowGuestLobby(client, lobby);

		SeatInfo firstOpen = lobby.seats.FirstOrDefault(s => !s.isHost && s.takenBy == null);
		IReadOnlyList<ID> mine = client.YourSeats;
		if (LanSession.DevJoinAddress != null && !client.IsSpectator && mine.Count == 0 && firstOpen != null) {
			client.ClaimSeat(firstOpen.playerID);
		}
	}

	// The guest's status line, as the lobby stands.
	private static string GuestStatus(LanClient client, LobbyInfo lobby, bool canChoose) {
		IReadOnlyList<ID> mine = client.YourSeats;
		string hotseatTip = " To play several people at this computer, taking turns, take a slot for each of them"
			+ " and type each player's name in their slot.";
		return lobby.creatingGame ? $"{lobby.hostName} is creating the world..."
			: client.IsSpectator ? "Watching. Waiting for the host to start the game..."
			: lobby.started && mine.Count == 0 ? "This game is in progress. Tick the seats to play, then join it." + hotseatTip
			: mine.Count == 0 ? "Take an Open slot to play." + hotseatTip
			: canChoose ? $"Choose your civilization{(lobby.teams > 0 ? " and team" : "")}, then wait for the host to start the game. "
				+ "If you don't choose a civilization, you get a random one."
			: "Waiting for the host to start the game...";
	}

	// The name for the player in another seat we take: the one typed in,
	// numbered if one of our seats already has it.
	private string NameForAnotherSeat(LobbyInfo lobby, IReadOnlyList<ID> mine) {
		string name = string.IsNullOrWhiteSpace(nameEdit.Text) ? LanSession.PlayerName : nameEdit.Text.Trim();
		HashSet<string> used = lobby.seats.Where(s => mine.Contains(s.playerID)).Select(s => s.takenBy).ToHashSet();
		if (!used.Contains(name)) {
			return name;
		}
		for (int n = 2; ; ++n) {
			if (!used.Contains($"{name} {n}")) {
				return $"{name} {n}";
			}
		}
	}

	public override void _Process(double delta) {
		LanSession.Host?.Poll();
		joiningSection?.Update();

		LanClient client = LanSession.Client;
		if (client != null) {
			client.Poll();
			if (!client.IsConnected && client.StartingGame == null && client.RejectedReason == null) {
				LanSession.End();
				ShowGameList(true);
				status.Text = "Lost the connection to the host.";
			}
		}
	}
}
