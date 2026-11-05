using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using C7Engine;
using C7Engine.Lua;
using C7Engine.Online;
using C7GameData;
using C7GameData.Save;
using Godot;
using Serilog;
using static C7Engine.Online.OnlineLobby;

// Multiplayer, from the main menu: Civ3's "Choose Multiplayer Game Type"
// (host or join a game online, hotseat, play by email), then its
// Multiplayer Setup screen, on Civ3's own background: the game's settings
// down the left, eight slots on the right (who plays each, and which civ),
// the messages box under them. Hotseat and play-by-email games are set up
// and begun here. Online, the host's slots and settings go to the players
// who join (C7Engine.Online), who take an open slot, pick its civ and say
// they're ready; who joins and leaves, and what anyone says, goes in the
// messages box.
public partial class MultiplayerScreen : Control {
	private enum Mode { Hotseat, PlayByEmail, Host, Join }

	private static readonly ILogger log = LogManager.ForContext<MultiplayerScreen>();
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	private static readonly Color Light = new(0.95f, 0.91f, 0.80f);
	private static readonly Color Gold = new(1f, 0.83f, 0.45f);
	private static readonly Color Warning = new(1f, 0.55f, 0.40f);

	// Civ3's setup screen: where its fields and boxes are.
	private const int SettingsX = 44;
	private const int SettingsWidth = 272;
	private static int SettingY(int i) => 171 + 45 * i;
	private static int SlotY(int i) => 122 + 35 * i;
	private const int PlayerX = 404;
	private const int PlayerWidth = 272;
	private const int CivX = 686;
	private const int CivWidth = 234;

	private Mode mode;
	private TextureRect screen;
	private Control popupLayer;
	private OnlinePanel popup;

	// The game being set up (by the host, or at this computer).
	private SaveGame save;
	private WorldSize worldSize;
	private BarbarianActivity barbarians;
	private WorldCharacteristics.Landform landform;
	private WorldCharacteristics.OceanCoverage ocean;
	private WorldCharacteristics.Climate climate;
	private WorldCharacteristics.Temperature temperature;
	private WorldCharacteristics.Age age;
	private Difficulty difficulty;
	private VictoryConditions victoryConditions;
	private OnlineLobby lobby;
	// Civ3's "Load Game" (online): the saved game to go on with, and its
	// file's name; null for a new game.
	private SaveGame loadedSave;
	private string loadedName;
	private OnlineHost host;
	private string gamePassword;
	private int messagesShown;

	// Joined: the host's game, as it last told us.
	private OnlineClient client;
	private LobbyState state;
	private bool joining;

	// What the screen shows, rebuilt when it changes.
	private readonly List<Control> settingsShown = new();
	private readonly List<Control> slotsShown = new();
	private VBoxContainer messageLines;
	private ScrollContainer messageScroll;
	private Button launch;
	private Label loading;
	private Label hint;

	private sealed record Setting(string name, List<string> values, int selected, Action<int> set);

	public static string GameVersion() => (string)ProjectSettings.GetSetting("application/config/version");

	public override void _Ready() {
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		ColorRect black = new() { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
		black.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(black);
		CenterContainer center = new() { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);
		screen = new TextureRect { CustomMinimumSize = new Vector2(1024, 768) };
		try {
			screen.Texture = TextureLoader.Load("multiplayer.setup_background");
		} catch (Exception e) {
			// (No Civ3 art: plain parchment.)
			log.Information("No multiplayer setup art: {Error}", e.Message);
			ColorRect parchment = new() { Color = new Color(0.82f, 0.77f, 0.63f), Size = new Vector2(1024, 768), MouseFilter = MouseFilterEnum.Ignore };
			screen.AddChild(parchment);
			foreach (Rect2 box in new[] { new Rect2(25, 90, 310, 610), new Rect2(360, 90, 640, 335), new Rect2(360, 448, 605, 140), new Rect2(360, 606, 605, 94) }) {
				screen.AddChild(new ColorRect { Color = new Color(0.36f, 0.33f, 0.27f), Position = box.Position, Size = box.Size, MouseFilter = MouseFilterEnum.Ignore });
			}
		}
		center.AddChild(screen);

		popupLayer = new Control { MouseFilter = MouseFilterEnum.Stop, Visible = false };
		popupLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(popupLayer);
		ColorRect dim = new() { Color = new Color(0, 0, 0, 0.5f), MouseFilter = MouseFilterEnum.Ignore };
		dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		popupLayer.AddChild(dim);

		save = GameMode.Load(GamePaths.GameModesDir, GamePaths.GameMode).GetSave();
		worldSize = GameSetup.WorldSizeFromSettings(save.WorldSizes);
		barbarians = GameSetup.BarbarianActivityFromSettings();
		landform = GameSetup.LandformFromSettings();
		ocean = GameSetup.OceanCoverageFromSettings();
		climate = GameSetup.ClimateFromSettings();
		temperature = GameSetup.TemperatureFromSettings();
		age = GameSetup.AgeFromSettings();
		string lastDifficulty = C7Settings.GetSettingsValueOrDefault(C7Settings.LastGame.SectionName, C7Settings.LastGame.Difficulty, "Regent");
		difficulty = save.Difficulties.FirstOrDefault(d => d.Name == lastDifficulty) ?? save.Difficulties.First();
		victoryConditions = VictoryConditions.FromSettings();

		ChooseGameType();
	}

	public override void _Process(double delta) {
		if (host != null) {
			bool changed = false;
			while (host != null && host.TryNextEvent(out HostEvent hostEvent)) {
				changed |= lobby.Handle(hostEvent);
			}
			if (changed) {
				ShowSlots();
			}
			ShowNewMessages();
		}
		while (client != null && client.TryNextMessage(out OnlineMessage message)) {
			switch (message) {
				case LobbyState lobbyState:
					bool settingsChanged = state == null || !SameSettings(state, lobbyState);
					state = lobbyState;
					if (settingsChanged) {
						ShowSettings();
					}
					ShowSlots();
					break;
				case ChatLine line:
					AddMessage(line.text);
					break;
				case LobbyNotice notice:
					AddMessage(notice.text, warning: true);
					break;
				case Goodbye goodbye:
					LeaveHost();
					ShowJoin(goodbye.reason);
					break;
				case GameStart start:
					BeginJoinedGame(start);
					return;
			}
		}
	}

	public override void _ExitTree() => StopNetwork();

	private void StopNetwork() {
		host?.Dispose();
		host = null;
		client?.Dispose();
		client = null;
	}

	private void BackToMainMenu() {
		StopNetwork();
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}

	// ---- Popups over the setup screen ----

	private OnlinePanel OpenPopup(int width, int height) {
		ClosePopup();
		popup = new OnlinePanel(width, height);
		CenterContainer center = new() { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		center.AddChild(popup);
		popupLayer.AddChild(center);
		popupLayer.Visible = true;
		return popup;
	}

	private void ClosePopup() {
		if (popup != null) {
			popup.GetParent().QueueFree();
			popup = null;
		}
		popupLayer.Visible = false;
	}

	// Civ3's "Choose Multiplayer Game Type".
	private void ChooseGameType() {
		OnlinePanel panel = OpenPopup(420, 330);
		panel.Header("Multiplayer");
		panel.Text("Choose Multiplayer Game Type", 58, 15);
		panel.Button("Host a Game Online", new Vector2(110, 100), () => { ClosePopup(); StartHosting(); });
		panel.Button("Join a Game Online", new Vector2(110, 136), () => ShowJoin());
		panel.Button("Hotseat", new Vector2(110, 172), () => { ClosePopup(); SetUpHere(Mode.Hotseat); });
		panel.Button("Play by Email", new Vector2(110, 208), () => { ClosePopup(); SetUpHere(Mode.PlayByEmail); });
		panel.Button("Main Menu", new Vector2(110, 270), BackToMainMenu);
	}

	// ---- At this computer: hotseat, play by email ----

	private void SetUpHere(Mode chosen) {
		mode = chosen;
		lobby = new OnlineLobby(null, NewSlots(MaxCivs(), humans: 2, online: false), CivNames());
		ShowSetupScreen();
		AddMessage(mode == Mode.Hotseat
			? "Hotseat: every human takes their turn at this computer, in the slots' order. Each may choose a password when they first play."
			: "Play by email: each human plays their turn on their own computer and sends the save to the next. Each may choose a password when they first play.");
		AddMessage("Set each slot to a human, a computer or closed, and pick the civs (or leave them Random). Then Launch Game.");
	}

	// ---- Hosting online ----

	private void StartHosting() {
		string name = C7Settings.GetSettingsValueOrDefault(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.Name, "");
		if (string.IsNullOrWhiteSpace(name)) {
			ChooseNickname(StartHosting);
			return;
		}
		mode = Mode.Host;
		int port = int.TryParse(C7Settings.GetSettingsValueOrDefault(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.Port, ""),
			out int configured) ? configured : OnlineProtocol.DefaultPort;
		gamePassword = OnlineHost.NewPassword();
		host = new OnlineHost(name.Trim(), gamePassword, GameVersion());
		try {
			host.Start(port);
		} catch (SocketException e) {
			log.Warning(e, "Couldn't host on port {Port}", port);
			host.Dispose();
			host = null;
			OnlinePanel panel = OpenPopup(460, 230);
			panel.Header("Multiplayer");
			panel.Text($"This computer couldn't listen on port {port}: {e.Message} Another program may be using it "
				+ "(it's Port in the [Multiplayer] part of C7.ini).", 60, 14);
			panel.Button("Back", new Vector2(180, 190), ChooseGameType);
			return;
		}
		lobby = new OnlineLobby(host, NewSlots(MaxCivs(), humans: 2, online: true), CivNames());
		lobby.SetSettings(LobbySettings());
		ShowSetupScreen();
		AddMessage("Send your players the invite code and the game's password (Invite Players...).");
		ShowInvite();
	}

	// Civ3's "Choose a Nickname for Multiplayer Use".
	private void ChooseNickname(Action then) {
		OnlinePanel panel = OpenPopup(440, 220);
		panel.Header("Multiplayer");
		panel.Text("Choose a Nickname for Multiplayer Use", 58, 15);
		LineEdit field = panel.Show(OnlinePanel.Field(new Vector2(70, 92), 300));
		field.MaxLength = OnlineMessage.MaxNameLength;
		Label problem = panel.Text("", 128, 13);
		problem.AddThemeColorOverride("font_color", OnlinePanel.Warning);
		void Done() {
			string name = field.Text.Trim();
			if (name == "" || name.Any(char.IsControl)) {
				problem.Text = "Please enter a nickname.";
				return;
			}
			C7Settings.SetValue(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.Name, name);
			C7Settings.SaveSettings();
			ClosePopup();
			then();
		}
		field.TextSubmitted += _ => Done();
		field.CallDeferred(Control.MethodName.GrabFocus);
		panel.Button("OK", new Vector2(30, 180), Done);
		panel.Button("Back", new Vector2(320, 180), ChooseGameType);
	}

	// The invite code to send, and the game's password. The code holds the
	// addresses players' games may find this computer at (Tailscale's, the
	// home network's), never its internet address unless the host gave one
	// for port forwarding (C7.ini); no address is shown.
	private void ShowInvite() {
		OnlinePanel panel = OpenPopup(640, 330);
		panel.Header("Invite Players");
		string internetAddress = C7Settings.GetSettingsValueOrDefault(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.InternetAddress, "");
		List<string> addresses = HostAddresses.ForInvite(HostAddresses.OfThisComputer(), internetAddress);
		if (addresses.Count == 0) {
			panel.Text("This computer isn't on a network your players can reach. Start Tailscale (or connect to your home network), "
				+ "then open Invite Players again.", 80, 14);
			panel.Button("Close", new Vector2(270, 290), ClosePopup);
			return;
		}
		panel.Text("Send your players this invite code and the game's password:", 54, 14, left: true);
		LineEdit invite = panel.Show(OnlinePanel.Field(new Vector2(30, 80), 580));
		invite.Text = host.InviteAt(addresses).Code;
		invite.Editable = false;
		invite.SelectAllOnFocus = true;
		invite.AddThemeFontSizeOverride("font_size", 12);
		// Read-only, but as easy to read as a field to type in.
		invite.AddThemeColorOverride("font_uneditable_color", OnlinePanel.Ink);
		invite.AddThemeStyleboxOverride("read_only", invite.GetThemeStylebox("normal"));
		panel.Button("Copy the Code", new Vector2(30, 116), () => DisplayServer.ClipboardSet(invite.Text));
		panel.Text("Game password:", 160, 14, left: true, width: 140);
		LineEdit password = panel.Show(OnlinePanel.Field(new Vector2(170, 156), 180));
		password.Text = gamePassword;
		password.TextChanged += text => {
			gamePassword = text;
			host.SetPassword(text);
		};
		panel.Text("(For players who join after a change.)", 162, 12, left: true, x: 360, width: 250);
		panel.Text("With Tailscale, share this computer with each player first: the code finds it through Tailscale "
			+ "(or your home network) and never holds your internet address. Windows may ask whether to let the game "
			+ "accept connections: allow it.", 198, 12, left: true);
		panel.Button("Close", new Vector2(270, 290), ClosePopup);
	}

	// ---- Joining online ----

	private void ShowJoin(string problem = null) {
		OnlinePanel panel = OpenPopup(640, 330);
		panel.Header("Join a Game Online");
		panel.Text("Paste the invite code the host sent you:", 56, 14, left: true);
		LineEdit inviteField = panel.Show(OnlinePanel.Field(new Vector2(30, 82), 580));
		inviteField.Text = C7Settings.GetSettingsValueOrDefault(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.LastInvite, "");
		panel.Text("Password:", 128, 14, left: true, width: 150);
		LineEdit passwordField = panel.Show(OnlinePanel.Field(new Vector2(180, 124), 220));
		passwordField.Secret = true;
		panel.Text("Nickname:", 166, 14, left: true, width: 150);
		LineEdit nameField = panel.Show(OnlinePanel.Field(new Vector2(180, 162), 220));
		nameField.MaxLength = OnlineMessage.MaxNameLength;
		nameField.Text = C7Settings.GetSettingsValueOrDefault(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.Name, "");
		Label status = panel.Text(problem ?? "", 206, 14);
		status.AddThemeColorOverride("font_color", OnlinePanel.Warning);
		panel.Text("The host's game needs to be the same version of OpenCiv3 as yours.", 246, 12);
		void Join() => _ = JoinWith(inviteField.Text, passwordField.Text, nameField.Text, status);
		inviteField.TextSubmitted += _ => Join();
		passwordField.TextSubmitted += _ => Join();
		nameField.TextSubmitted += _ => Join();
		panel.Button("Join", new Vector2(30, 290), Join);
		panel.Button("Back", new Vector2(520, 290), () => {
			if (mode == Mode.Join) {
				BackToMainMenu();
			} else {
				ChooseGameType();
			}
		});
		(inviteField.Text == "" ? inviteField : passwordField).CallDeferred(Control.MethodName.GrabFocus);
	}

	private async System.Threading.Tasks.Task JoinWith(string inviteText, string password, string name, Label status) {
		if (joining || client != null) {
			return;
		}
		Invite parsed = Invite.Parse(inviteText);
		name = name.Trim();
		if (parsed == null) {
			status.Text = "That isn't an invite code: copy it whole (it begins with OC3-).";
			return;
		}
		if (name == "" || name.Any(char.IsControl)) {
			status.Text = "Please enter a nickname.";
			return;
		}
		joining = true;
		status.Text = "Attempting to connect to multiplayer game...";
		try {
			OnlineClient joined = await OnlineClient.Join(parsed, name, password, GameVersion());
			if (!IsInsideTree()) {
				joined.Dispose();
				return;
			}
			client = joined;
			mode = Mode.Join;
			state = null;
			C7Settings.SetValue(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.Name, name);
			C7Settings.SetValue(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.LastInvite, parsed.ToString());
			C7Settings.SaveSettings();
			ClosePopup();
			ShowSetupScreen();
		} catch (JoinFailedException e) {
			if (IsInstanceValid(status)) {
				status.Text = e.Message;
			}
		} finally {
			joining = false;
		}
	}

	private void LeaveHost() {
		client?.Dispose();
		client = null;
		state = null;
	}

	// ---- The setup screen ----

	private void ShowSetupScreen() {
		foreach (Node child in screen.GetChildren().Where(c => c is not ColorRect)) {
			child.QueueFree();
		}
		settingsShown.Clear();
		slotsShown.Clear();
		messagesShown = 0;

		Label title = Add(new Label {
			Text = mode switch { Mode.Hotseat => "HOTSEAT SETUP", Mode.PlayByEmail => "PLAY BY EMAIL SETUP", _ => "MULTIPLAYER SETUP" },
			HorizontalAlignment = HorizontalAlignment.Center,
			Position = new Vector2(170, 14),
			Size = new Vector2(690, 38),
		});
		title.AddThemeFontOverride("font", ResourceLoader.Load<FontFile>("res://Fonts/NSansFont24Pt.tres"));
		title.AddThemeFontSizeOverride("font_size", 24);
		title.AddThemeColorOverride("font_color", Ink);
		Add(TextAt("GAME SETTINGS", new Vector2(43, 118), new Vector2(273, 25), 14, Light, center: true));
		Add(TextAt("PLAYER", new Vector2(PlayerX, 96), new Vector2(PlayerWidth, 22), 13, Light));
		Add(TextAt("CIVILIZATION", new Vector2(CivX, 96), new Vector2(CivWidth, 22), 13, Light));
		if (mode is Mode.Host or Mode.Join) {
			Add(TextAt("READY", new Vector2(918, 96), new Vector2(70, 22), 11, Light));
		}

		Add(BarButton("GAME RULES", new Vector2(404, 397), new Vector2(272, 24), ShowRules));
		launch = Add(BarButton(mode == Mode.Join ? "MAKE READY" : "LAUNCH GAME", new Vector2(686, 395), new Vector2(238, 30), Launch));

		messageScroll = Add(new ScrollContainer {
			Position = new Vector2(372, 456),
			Size = new Vector2(585, 126),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		});
		messageLines = new VBoxContainer();
		messageLines.AddThemeConstantOverride("separation", 2);
		messageScroll.AddChild(messageLines);
		messageLines.CustomMinimumSize = new Vector2(570, 0);

		if (mode is Mode.Host or Mode.Join) {
			Add(TextAt("Message:", new Vector2(372, 622), new Vector2(80, 26), 13, Light));
			LineEdit chat = Add(new LineEdit { Position = new Vector2(452, 618), Size = new Vector2(400, 30), MaxLength = Chat.MaxLength });
			chat.AddThemeFontSizeOverride("font_size", 13);
			void Send() {
				string text = chat.Text.Trim();
				chat.Clear();
				if (text == "") {
					return;
				}
				if (client != null) {
					client.Send(new Chat { text = text });
				} else {
					lobby.Say(text);
				}
			}
			chat.TextSubmitted += _ => Send();
			Add(BarButton("Send", new Vector2(862, 618), new Vector2(90, 30), Send));
			hint = Add(TextAt(mode == Mode.Host ? "When every slot is filled and every player is ready, launch the game."
				: "Take an open slot, pick your civilization, then Make Ready. The host launches the game.",
				new Vector2(372, 656), new Vector2(580, 40), 12, Light));
		} else {
			Add(TextAt("An administrator's password is asked when the game is launched (optional): it lets whoever knows it hand a "
				+ "player who leaves to the computer, or clear a forgotten password.", new Vector2(372, 616), new Vector2(580, 80), 13, Light));
		}

		if (mode == Mode.Host) {
			Add(new Civ3MenuButton { Text = "Invite Players...", FontSize = 14, Position = new Vector2(40, 728) }).Pressed += ShowInvite;
		}
		Add(new Civ3MenuButton { Text = mode == Mode.Join ? "Leave" : "Back", FontSize = 14, Position = new Vector2(900, 728) }).Pressed += BackToMainMenu;
		loading = Add(TextAt("Loading...", new Vector2(360, 300), new Vector2(640, 60), 38, Light, center: true));
		loading.Visible = false;

		ShowSettings();
		ShowSlots();
	}

	private T Add<T>(T control) where T : Control {
		screen.AddChild(control);
		return control;
	}

	private static Label TextAt(string text, Vector2 position, Vector2 size, int fontSize, Color color, bool center = false) {
		Label label = new() {
			Text = text,
			HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center,
			// (Before the size, or the text won't wrap.)
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = position,
			Size = size,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	// A button drawn on one of Civ3's dark bars.
	private static Button BarButton(string text, Vector2 position, Vector2 size, Action action) {
		Button button = new() { Text = text, Flat = true, Position = position, Size = size, FocusMode = FocusModeEnum.None };
		button.AddThemeFontSizeOverride("font_size", 14);
		button.AddThemeColorOverride("font_color", Light);
		button.AddThemeColorOverride("font_hover_color", Gold);
		button.AddThemeColorOverride("font_pressed_color", Gold);
		button.Pressed += action;
		return button;
	}

	// A drop-down drawn on one of Civ3's light fields.
	private static OptionButton FieldChoice(Vector2 position, Vector2 size, IEnumerable<string> items, int selected) {
		OptionButton choice = new() { Position = position, Size = size, FocusMode = FocusModeEnum.None, ClipText = true };
		StyleBoxEmpty empty = new() { ContentMarginLeft = 6, ContentMarginRight = 24 };
		foreach (string name in new[] { "normal", "hover", "pressed", "disabled", "focus" }) {
			choice.AddThemeStyleboxOverride(name, empty);
		}
		foreach (string name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) {
			choice.AddThemeColorOverride(name, Ink);
		}
		choice.AddThemeColorOverride("font_disabled_color", new Color(Ink, 0.55f));
		choice.AddThemeFontSizeOverride("font_size", 13);
		foreach (string item in items) {
			choice.AddItem(item);
		}
		choice.Select(selected);
		PopupMenu menu = choice.GetPopup();
		menu.MaxSize = new Vector2I(menu.MaxSize.X, 300);
		for (int i = 0; i < menu.ItemCount; i++) {
			menu.SetItemAsRadioCheckable(i, false);
		}
		return choice;
	}

	private static Label FieldText(string text, Vector2 position, Vector2 size) {
		Label label = TextAt(text, position, size, 13, Ink);
		label.AutowrapMode = TextServer.AutowrapMode.Off;
		label.ClipText = true;
		label.Position = position + new Vector2(6, 0);
		return label;
	}

	// ---- Settings ----

	private int MaxCivs() => Math.Min(LobbyState.MaxSlots, worldSize.numberOfCivs);

	private List<string> CivNames() => save.Civilizations.Where(c => !c.isBarbarian).Select(c => c.name).ToList();

	private string ModeName() => mode switch { Mode.Hotseat => "Hotseat", Mode.PlayByEmail => "Play By Email", _ => "Turn Based" };

	// Civ3's eleven, in its order (a saved game's own instead).
	private List<Setting> Settings() {
		List<WorldCharacteristics.OceanCoverage> oceans = [WorldCharacteristics.OceanCoverage.Percent_60, WorldCharacteristics.OceanCoverage.Percent_70, WorldCharacteristics.OceanCoverage.Percent_80];
		// Online, the host may go on with a saved game.
		Setting gameType = mode == Mode.Host
			? new("Game Type", ["Standard", "Load Game"], loadedSave != null ? 1 : 0, i => {
				if (i == 1) {
					ChooseSavedGame();
				} else if (loadedSave != null) {
					UseNewGame();
				}
			})
			: new("Game Type", ["Standard"], 0, null);
		if (loadedSave != null) {
			return [
				gameType,
				new("Game Mode", [ModeName()], 0, null),
				new("Turn Timer", ["None"], 0, null),
				new("Saved Game", [loadedName], 0, null),
				// (Its date needs the game's scripts, which aren't loaded here.)
				new("Turn", [loadedSave.TurnNumber.ToString()], 0, null),
				new("Difficulty", [loadedSave.GameDifficulty?.Name ?? "?"], 0, null),
			];
		}
		return [
			gameType,
			new("Game Mode", [ModeName()], 0, null),
			new("Turn Timer", ["None"], 0, null),
			new("World Size", save.WorldSizes.Select(w => w.name).ToList(), save.WorldSizes.IndexOf(worldSize), i => {
				worldSize = save.WorldSizes[i];
				for (int slot = MaxCivs(); slot < LobbyState.MaxSlots; slot++) {
					lobby.SetKind(slot, Kind.Closed);
				}
			}),
			new("Barbarian Activity", ["No Barbarians", "Sedentary", "Roaming", "Restless", "Raging"], (int)barbarians + 1, i => barbarians = (BarbarianActivity)(i - 1)),
			new("Land Mass", ["Archipelago", "Continents", "Pangaea"], (int)landform, i => landform = (WorldCharacteristics.Landform)i),
			new("Water Coverage", ["60%", "70%", "80%"], oceans.IndexOf(ocean), i => ocean = oceans[i]),
			new("Climate", ["Arid", "Normal", "Wet"], (int)climate, i => climate = (WorldCharacteristics.Climate)i),
			new("Temperature", ["Cool", "Temperate", "Warm"], (int)temperature, i => temperature = (WorldCharacteristics.Temperature)i),
			new("Age", ["3 Billion", "4 Billion", "5 Billion"], (int)age, i => age = (WorldCharacteristics.Age)i),
			new("Difficulty", save.Difficulties.Select(d => d.Name).ToList(), save.Difficulties.IndexOf(difficulty), i => difficulty = save.Difficulties[i]),
		];
	}

	// What players are told: the settings, then the rules.
	private List<LobbySetting> LobbySettings() {
		return Settings().Select(s => new LobbySetting { name = s.name, value = s.values[s.selected] })
			.Concat(VictoryConditions.Options.Select(o => new LobbySetting { name = o.label, value = o.get(Rules) ? "Yes" : "No" }))
			.ToList();
	}

	// The victory conditions: a saved game's as they were.
	private VictoryConditions Rules => loadedSave?.VictoryConditions ?? victoryConditions;

	private static bool IsRule(LobbySetting setting) => VictoryConditions.Options.Any(o => o.label == setting.name);

	private static bool SameSettings(LobbyState a, LobbyState b) {
		return a.settings.Count == b.settings.Count && a.settings.Zip(b.settings).All(p => p.First.name == p.Second.name && p.First.value == p.Second.value);
	}

	private void ShowSettings() {
		foreach (Control shown in settingsShown) {
			shown.QueueFree();
		}
		settingsShown.Clear();
		List<Setting> settings = mode == Mode.Join ? null : Settings();
		for (int i = 0; i < 11; i++) {
			Vector2 position = new(SettingsX, SettingY(i));
			Vector2 size = new(SettingsWidth, 24);
			if (mode == Mode.Join) {
				LobbySetting setting = state?.settings.ElementAtOrDefault(i);
				if (setting != null && !IsRule(setting)) {
					settingsShown.Add(Add(FieldText($"{setting.name}: {setting.value}", position, size)));
				}
				continue;
			}
			if (i >= settings.Count) {
				break;
			}
			Setting choice = settings[i];
			settingsShown.Add(Add(FieldText(choice.name + ":", position, size)));
			if (choice.set == null) {
				settingsShown.Add(Add(TextAt(choice.values[choice.selected], position + new Vector2(130, 0), size - new Vector2(160, 0), 13, Ink)));
				continue;
			}
			OptionButton field = Add(FieldChoice(position + new Vector2(124, 0), size - new Vector2(124, 0), choice.values, choice.selected));
			field.Alignment = HorizontalAlignment.Left;
			int index = i;
			field.ItemSelected += selected => {
				Settings()[index].set((int)selected);
				SettingsChanged();
			};
			settingsShown.Add(field);
		}
	}

	private void SettingsChanged() {
		lobby.SetSettings(LobbySettings());
		ShowSlots();
	}

	// GAME RULES: Civ3's victory conditions and game types.
	private void ShowRules() {
		OnlinePanel panel = OpenPopup(460, 120 + 26 * VictoryConditions.Options.Count);
		panel.Header("Game Rules");
		int y = 56;
		foreach (VictoryConditions.Option option in VictoryConditions.Options) {
			if (mode == Mode.Join) {
				string value = state?.settings.FirstOrDefault(s => s.name == option.label)?.value ?? "?";
				panel.Text($"{option.label}: {value}", y, 14, left: true);
			} else if (loadedSave != null) {
				panel.Text($"{option.label}: {(option.get(Rules) ? "Yes" : "No")}", y, 14, left: true);
			} else {
				CheckBox box = panel.Show(new CheckBox { Text = option.label, ButtonPressed = option.get(victoryConditions), Position = new Vector2(40, y - 2), FocusMode = FocusModeEnum.None });
				foreach (string colorName in new[] { "font_color", "font_pressed_color", "font_hover_color" }) {
					box.AddThemeColorOverride(colorName, OnlinePanel.Ink);
				}
				box.Toggled += on => {
					option.set(victoryConditions, on);
					SettingsChanged();
				};
			}
			y += 26;
		}
		panel.Button("Close", new Vector2(190, y + 16), ClosePopup);
	}

	// ---- Slots ----

	private void ShowSlots() {
		if (messageLines == null) {
			return;
		}
		foreach (Control shown in slotsShown) {
			shown.QueueFree();
		}
		slotsShown.Clear();
		LobbyState shownState = mode == Mode.Join ? state : lobby.State();
		if (shownState == null) {
			return;
		}
		int me = mode == Mode.Join ? client.playerId : Host;
		List<string> civs = shownState.civs;
		string NameOf(int? player) => shownState.players.FirstOrDefault(p => p.player == player)?.name ?? "?";
		foreach (LobbySlot slot in shownState.slots) {
			int number = slot.slot;
			int y = SlotY(number);
			Vector2 playerAt = new(PlayerX, y);
			Vector2 playerSize = new(PlayerWidth, 22);
			bool playable = mode == Mode.Join || (lobby.loaded ? lobby.slots[number].loadedPlayer != null : number < MaxCivs());
			bool fixedCivs = mode == Mode.Join ? shownState.loaded : lobby.loaded;

			// Who plays it.
			if (number == 0 && mode != Mode.Join) {
				slotsShown.Add(Add(FieldText(mode == Mode.Host ? $"{NameOf(Host)} (you)" : "Human", playerAt, playerSize)));
			} else if (mode == Mode.Join) {
				if (slot.kind == "open") {
					Button take = Add(new Button { Text = "Open: take this slot", Flat = true, Position = playerAt, Size = playerSize, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left });
					take.AddThemeColorOverride("font_color", Ink);
					take.AddThemeColorOverride("font_hover_color", new Color(0.55f, 0.25f, 0.05f));
					take.AddThemeFontSizeOverride("font_size", 13);
					take.Pressed += () => TakeSlot(slot);
					slotsShown.Add(take);
				} else if (slot.kind == "human" && slot.player == me) {
					OptionButton mine = Add(FieldChoice(playerAt, playerSize, [$"{NameOf(me)} (you)", "Leave this slot"], 0));
					mine.ItemSelected += i => {
						if (i == 1) {
							client.Send(new LeaveSeat());
						}
					};
					slotsShown.Add(mine);
				} else {
					slotsShown.Add(Add(FieldText(slot.kind switch {
						"human" => NameOf(slot.player),
						"computer" => "Computer",
						_ => "Closed",
					}, playerAt, playerSize)));
				}
			} else if (lobby.loaded && !lobby.slots[number].loadedHuman) {
				slotsShown.Add(Add(FieldText(slot.kind == "computer" ? "Computer" : "", playerAt, playerSize)));
			} else {
				List<string> items;
				List<Kind> kinds;
				if (lobby.loaded) {
					kinds = [Kind.Open, Kind.Computer];
					items = ["Open", "Computer"];
					if (slot.kind == "human") {
						kinds.Insert(0, Kind.Human);
						items.Insert(0, NameOf(slot.player));
					}
				} else if (mode == Mode.Host) {
					kinds = [Kind.Open, Kind.Computer, Kind.Closed];
					items = ["Open", "Computer", "Closed"];
					if (slot.kind == "human") {
						kinds.Insert(0, Kind.Human);
						items.Insert(0, NameOf(slot.player));
					}
				} else {
					kinds = [Kind.Human, Kind.Computer, Kind.Closed];
					items = ["Human", "Computer", "Closed"];
				}
				Kind current = (Kind)Array.IndexOf(LobbySlot.Kinds, slot.kind);
				OptionButton who = Add(FieldChoice(playerAt, playerSize, items, Math.Max(0, kinds.IndexOf(current))));
				who.Disabled = !playable;
				who.ItemSelected += i => {
					if (kinds[(int)i] != Kind.Human) {
						lobby.SetKind(number, kinds[(int)i]);
						ShowSlots();
					}
				};
				slotsShown.Add(who);
			}

			// Its civ.
			Vector2 civAt = new(CivX, y);
			Vector2 civSize = new(CivWidth, 22);
			bool mine2 = mode == Mode.Join ? slot.kind == "human" && slot.player == me
				: slot.kind != "closed" && (mode != Mode.Host || number == 0 || slot.kind == "computer");
			if (fixedCivs) {
				if (slot.kind != "closed") {
					slotsShown.Add(Add(FieldText(slot.civ, civAt, civSize)));
				}
			} else if (slot.kind == "closed" || slot.kind == "open" && mode != Mode.Join) {
				if (slot.kind == "open") {
					slotsShown.Add(Add(FieldText(slot.civ, civAt, civSize)));
				}
			} else if (mine2 && playable) {
				List<string> items = [OnlineLobby.Random, .. civs];
				OptionButton civ = Add(FieldChoice(civAt, civSize, items, Math.Max(0, items.IndexOf(slot.civ))));
				HashSet<string> taken = shownState.slots.Where(s => s.slot != number && s.kind != "closed").Select(s => s.civ).ToHashSet();
				for (int i = 1; i < items.Count; i++) {
					civ.SetItemDisabled(i, taken.Contains(items[i]));
				}
				civ.ItemSelected += i => ChooseCiv(number, items[(int)i]);
				slotsShown.Add(civ);
			} else if (slot.kind != "open") {
				slotsShown.Add(Add(FieldText(slot.civ, civAt, civSize)));
			}

			// Ready, and the host's boot.
			if (mode is Mode.Host or Mode.Join && slot.kind == "human" && slot.player != Host && slot.player != null) {
				slotsShown.Add(Add(new TextureRect { Texture = Icon(slot.ready ? "multiplayer.ready" : "multiplayer.not_ready"), Position = new Vector2(930, y - 1), MouseFilter = MouseFilterEnum.Ignore }));
				if (mode == Mode.Host) {
					int player = slot.player.Value;
					TextureButton kick = Add(new TextureButton {
						TextureNormal = Icon("multiplayer.kick"),
						TextureHover = Icon("multiplayer.kick_hover"),
						Position = new Vector2(958, y - 1),
						TooltipText = $"Kick {NameOf(player)}",
					});
					kick.Pressed += () => lobby.Kick(player);
					slotsShown.Add(kick);
				}
			}
		}
		if (mode == Mode.Join) {
			hint.Text = shownState.loaded ? "Take the slot of the civilization you play in this saved game, then Make Ready. The host launches the game."
				: "Take an open slot, pick your civilization, then Make Ready. The host launches the game.";
			LobbySlot mine = shownState.slots.FirstOrDefault(s => s.kind == "human" && s.player == me);
			launch.Text = mine?.ready == true ? "MAKE NOT READY" : "MAKE READY";
			launch.Disabled = mine == null;
		}
	}

	private static Texture2D Icon(string key) {
		try {
			return TextureLoader.Load(key);
		} catch (Exception) {
			return null;
		}
	}

	private void ChooseCiv(int number, string civ) {
		if (mode == Mode.Join) {
			client.Send(new ChooseCiv { civ = civ });
			return;
		}
		string problem = lobby.SetCiv(number, civ);
		if (problem != null) {
			AddMessage(problem, warning: true);
		}
		ShowSlots();
	}

	private void TakeSlot(LobbySlot slot) {
		if (!slot.password) {
			client.Send(new TakeSeat { seat = slot.slot });
			return;
		}
		// Civ3's "Enter Password for" the civ.
		OnlinePanel panel = OpenPopup(420, 200);
		panel.Header("Password");
		panel.Text($"Enter Password for the {slot.civ}:", 56, 14);
		LineEdit field = panel.Show(OnlinePanel.Field(new Vector2(60, 88), 300));
		field.Secret = true;
		void Done() {
			client.Send(new TakeSeat { seat = slot.slot, password = field.Text });
			ClosePopup();
		}
		field.TextSubmitted += _ => Done();
		panel.Button("OK", new Vector2(30, 160), Done);
		panel.Button("Cancel", new Vector2(300, 160), ClosePopup);
	}

	// ---- The messages box ----

	private void AddMessage(string text, bool warning = false) {
		if (messageLines == null || string.IsNullOrEmpty(text)) {
			return;
		}
		Label line = TextAt(text, Vector2.Zero, new Vector2(570, 18), 13, warning ? Warning : Light);
		line.VerticalAlignment = VerticalAlignment.Top;
		line.CustomMinimumSize = new Vector2(570, 0);
		messageLines.AddChild(line);
		// (To the newest, once it's laid out.)
		Callable.From(() => messageScroll.ScrollVertical = (int)messageScroll.GetVScrollBar().MaxValue).CallDeferred();
	}

	private void ShowNewMessages() {
		for (; messagesShown < lobby.messages.Count; messagesShown++) {
			AddMessage(lobby.messages[messagesShown]);
		}
	}

	// ---- Beginning the game ----

	private void Launch() {
		if (mode == Mode.Join) {
			LobbySlot mine = state?.slots.FirstOrDefault(s => s.kind == "human" && s.player == client.playerId);
			if (mine != null) {
				client.Send(new SetReady { ready = !mine.ready });
			}
			return;
		}
		string problem = lobby.LaunchProblem();
		if (problem != null) {
			AddMessage(problem, warning: true);
			return;
		}
		if (mode == Mode.Host) {
			if (loadedSave != null) {
				BeginSavedOnline();
			} else {
				BeginOnline();
			}
			return;
		}
		AskAdminPassword();
	}

	// Civ3's "Enter a Password for Administration" (none if left blank).
	private void AskAdminPassword() {
		OnlinePanel panel = OpenPopup(460, 220);
		panel.Header("Administration");
		panel.Text("Enter a Password for Administration", 56, 15);
		LineEdit field = panel.Show(OnlinePanel.Field(new Vector2(80, 88), 300));
		field.Secret = true;
		field.MaxLength = 40;
		panel.Text("Leave blank or cancel for none.", 124, 13);
		field.TextSubmitted += _ => BeginHere(field.Text);
		field.CallDeferred(Control.MethodName.GrabFocus);
		panel.Button("OK", new Vector2(30, 180), () => BeginHere(field.Text));
		panel.Button("Cancel", new Vector2(340, 180), () => BeginHere(""));
	}

	// Hotseat or play by email: the game is made as the slots say (a Random
	// civ is picked here, from those no slot has), then the map opens.
	private void BeginHere(string adminPassword) {
		ClosePopup();
		GameSetup gameSetup = Setup(adminPassword, out _);
		// World generation can take a while: not on the UI thread.
		new Thread(() => {
			gameSetup.Populate(save);
			CallDeferred(nameof(OpenMap));
		}).Start();
	}

	// Online: the host makes the game, then sends it to every player and
	// opens it from the same bytes they do.
	private List<Slot> playingOnline;

	private void BeginOnline() {
		host.SendToAll(new ChatLine { text = "The host is starting the game..." });
		lobby.messages.Add("Starting the game...");
		GameSetup gameSetup = Setup("", out playingOnline);
		new Thread(() => {
			gameSetup.Populate(save);
			CallDeferred(nameof(SendOnlineGame));
		}).Start();
	}

	private void SendOnlineGame() {
		// GameSetup adds the barbarians, then the host's civ, then the other
		// slots' in order.
		List<SavePlayer> civs = save.Players.Where(p => !save.Civilizations.First(c => c.name == p.civilization).isBarbarian).ToList();
		Dictionary<int, ID> players = new();
		for (int i = 0; i < playingOnline.Count; i++) {
			if (playingOnline[i].kind == Kind.Human && playingOnline[i].player is int player && player != Host) {
				players[player] = civs[i].id;
			}
		}
		string packed = OnlineGame.Pack(save);
		HostGame game = new(host, civs[0].id, players);
		game.SendGame(packed, resumed: false);
		GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		global.Online = game;
		global.OnlineSeq = 0;
		global.SaveGame = OnlineGame.Unpack(packed);
		// (The game has the host now.)
		host = null;
		lobby = null;
		OpenMap();
	}

	// Online, a saved game going on: as the slots say (a human civ nobody
	// took is the computer's), sent to every player and opened here from
	// the same bytes.
	private void BeginSavedOnline() {
		host.SendToAll(new ChatLine { text = "The host is starting the game..." });
		lobby.ApplyTo(loadedSave);
		string packed = OnlineGame.Pack(loadedSave);
		HostGame game = new(host, lobby.slots[0].loadedPlayer, lobby.PlayersOf());
		game.SendGame(packed, resumed: true);
		GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		global.ResetLoadGameFields();
		global.Online = game;
		global.OnlineSeq = 0;
		global.SaveGame = OnlineGame.Unpack(packed);
		global.OnlineResumed = true;
		host = null;
		lobby = null;
		loading.Visible = true;
		launch.Disabled = true;
		OpenMap();
	}

	// ---- Civ3's "Load Game" ----

	private void ChooseSavedGame() {
		FileDialog dialog = new() {
			Title = "Load Game",
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			Filters = ["*.json, *.SAV ; Saved games"],
		};
		try {
			dialog.CurrentDir = C7Settings.GetSaveFolder();
		} catch (Exception) {
		}
		AddChild(dialog);
		// (Back to what the settings were.)
		dialog.Canceled += () => {
			dialog.QueueFree();
			ShowSettings();
		};
		dialog.FileSelected += path => {
			dialog.QueueFree();
			ReadSavedGame(path);
		};
		dialog.PopupCentered(new Vector2I(900, 600));
	}

	// (Reading a save can take a while: not on the UI thread.)
	private void ReadSavedGame(string path) {
		loading.Visible = true;
		new Thread(() => {
			SaveGame read = null;
			try {
				read = SaveManager.LoadSave(path, GamePaths.DefaultBicPath, MediaPath("Text/PediaIcons.txt"), MediaPath("Text/Civilopedia.txt"));
			} catch (Exception e) {
				log.Warning(e, "Couldn't read {Path}", path);
			}
			Callable.From(() => SavedGameRead(path, read)).CallDeferred();
		}).Start();
	}

	private static Func<string, string> MediaPath(string file) => scenarioSearchPath => {
		Util.setModPath(scenarioSearchPath);
		return Util.Civ3MediaPath(file);
	};

	private void SavedGameRead(string path, SaveGame read) {
		if (!IsInsideTree() || host == null) {
			return;
		}
		loading.Visible = false;
		List<SavePlayer> humans = read == null ? [] : HumanCivs(read);
		string problem = read == null ? "That saved game couldn't be read."
			: humans.Count == 0 ? "That saved game has no human civilization to play."
			: LoadedSlots(read, humans[0].id) == null ? $"A game online has at most {LobbyState.MaxSlots} civilizations."
			: null;
		if (problem != null) {
			AddMessage(problem, warning: true);
			ShowSettings();
			return;
		}
		string name = System.IO.Path.GetFileNameWithoutExtension(path);
		if (humans.Count == 1) {
			TakeSavedCiv(read, name, humans[0]);
			return;
		}
		// Which of its humans the host plays.
		OnlinePanel panel = OpenPopup(420, 120 + 36 * humans.Count);
		panel.Header("Load Game");
		panel.Text("Which civilization do you play?", 56, 15);
		for (int i = 0; i < humans.Count; i++) {
			SavePlayer civ = humans[i];
			panel.Button(civ.civilization, new Vector2(110, 92 + 36 * i), () => TakeSavedCiv(read, name, civ));
		}
		panel.Button("Cancel", new Vector2(110, 92 + 36 * humans.Count), () => {
			ClosePopup();
			ShowSettings();
		});
	}

	// The host's civ in the saved game: its password first, if it has one.
	private void TakeSavedCiv(SaveGame read, string name, SavePlayer civ) {
		if (string.IsNullOrEmpty(civ.passwordHash)) {
			UseSavedGame(read, name, civ.id);
			return;
		}
		OnlinePanel panel = OpenPopup(420, 220);
		panel.Header("Password");
		panel.Text($"Enter Password for the {civ.civilization}:", 56, 14);
		LineEdit field = panel.Show(OnlinePanel.Field(new Vector2(60, 88), 300));
		field.Secret = true;
		Label wrong = panel.Text("", 124, 13);
		wrong.AddThemeColorOverride("font_color", OnlinePanel.Warning);
		void Done() {
			if (!SavedPasswords(read)(civ.id, field.Text)) {
				wrong.Text = "That isn't the civ's password.";
				return;
			}
			UseSavedGame(read, name, civ.id);
		}
		field.TextSubmitted += _ => Done();
		field.CallDeferred(Control.MethodName.GrabFocus);
		panel.Button("OK", new Vector2(30, 180), Done);
		panel.Button("Cancel", new Vector2(300, 180), () => {
			ClosePopup();
			ShowSettings();
		});
	}

	private void UseSavedGame(SaveGame read, string name, ID hostCiv) {
		ClosePopup();
		loadedSave = read;
		loadedName = name;
		List<string> civs = read.Civilizations.Where(c => !c.isBarbarian).Select(c => c.name).ToList();
		ReplaceLobby(new OnlineLobby(host, LoadedSlots(read, hostCiv), civs, SavedPasswords(read), loaded: true));
		AddMessage($"Going on with {name}: each player takes their civilization (with its password, if it has one); "
			+ "a civilization nobody takes can be given to the computer.");
	}

	private void UseNewGame() {
		loadedSave = null;
		loadedName = null;
		ReplaceLobby(new OnlineLobby(host, NewSlots(MaxCivs(), humans: 2, online: true), CivNames()));
	}

	// (The players who joined stay, without a slot.)
	private void ReplaceLobby(OnlineLobby replacement) {
		lobby = replacement;
		messagesShown = 0;
		lobby.SetSettings(LobbySettings());
		ShowSettings();
		ShowSlots();
	}

	// A joining player: the host's game, and this one's player in it.
	private void BeginJoinedGame(GameStart start) {
		ID you;
		SaveGame received;
		try {
			you = ID.FromString(start.you);
			received = OnlineGame.Unpack(start.save);
		} catch (Exception e) {
			log.Warning(e, "Couldn't read the host's game");
			AddMessage("The host's game couldn't be read.", warning: true);
			return;
		}
		GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		global.ResetLoadGameFields();
		global.Online = new ClientGame(client, you);
		global.OnlineSeq = start.seq;
		global.SaveGame = received;
		// (A saved game going on: no new-game welcome.)
		global.OnlineResumed = start.resumed;
		client = null;
		OpenMap();
	}

	// The game the slots say (a Random civ picked here, from those no slot
	// has).
	private GameSetup Setup(string adminPassword, out List<Slot> playing) {
		playing = lobby.slots.Where(s => s.kind != Kind.Closed).ToList();
		HashSet<string> taken = playing.Select(s => s.civ).Where(c => c != OnlineLobby.Random).ToHashSet();
		Random random = new();
		Queue<string> free = new(lobby.civs.Where(c => !taken.Contains(c)).OrderBy(_ => random.Next()));
		List<string> chosen = playing.Select(s => s.civ != OnlineLobby.Random ? s.civ : free.Dequeue()).ToList();
		List<Slot> slots = playing;

		GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		global.ResetLoadGameFields();
		WorldCharacteristics world = new(save) {
			landform = landform,
			oceanCoverage = ocean,
			age = age,
			climate = climate,
			temperature = temperature,
			worldSize = worldSize,
			barbarianActivity = barbarians,
			mapSeed = new Random().Next(),
		};
		global.WorldCharacteristics = world;
		global.SaveGame = save;
		GameSetup gameSetup = new() {
			playerCivilization = save.Civilizations.First(c => c.name == chosen[0]),
			difficulty = difficulty,
			worldCharacteristics = world,
			opponents = slots.Skip(1).Select((s, i) => new SelectedOpponent { Name = chosen[i + 1], isHuman = s.kind == Kind.Human }).ToList(),
			victoryConditions = victoryConditions,
			playByEmail = mode == Mode.PlayByEmail,
			adminPassword = adminPassword,
		};
		RememberSettings();
		loading.Visible = true;
		launch.Disabled = true;
		return gameSetup;
	}

	private void OpenMap() {
		GetTree().ChangeSceneToFile("res://C7Game.tscn");
	}

	// The settings are the next game's, single player too.
	private void RememberSettings() {
		try {
			C7Settings.SetValue(C7Settings.LastGame.SectionName, C7Settings.LastGame.WorldSize, worldSize.name);
			C7Settings.SetValue(C7Settings.LastGame.SectionName, C7Settings.LastGame.BarbarianActivity, barbarians.ToString());
			C7Settings.SetValue(C7Settings.LastGame.SectionName, C7Settings.LastGame.Landform, landform.ToString());
			C7Settings.SetValue(C7Settings.LastGame.SectionName, C7Settings.LastGame.OceanCoverage, ocean.ToString());
			C7Settings.SetValue(C7Settings.LastGame.SectionName, C7Settings.LastGame.Climate, climate.ToString());
			C7Settings.SetValue(C7Settings.LastGame.SectionName, C7Settings.LastGame.Temperature, temperature.ToString());
			C7Settings.SetValue(C7Settings.LastGame.SectionName, C7Settings.LastGame.Age, age.ToString());
			C7Settings.SetValue(C7Settings.LastGame.SectionName, C7Settings.LastGame.Difficulty, difficulty.Name);
			foreach (VictoryConditions.Option option in VictoryConditions.Options) {
				C7Settings.SetValue(C7Settings.Rules.SectionName, option.key, option.get(victoryConditions).ToString());
			}
			C7Settings.SaveSettings();
		} catch (Exception e) {
			log.Error(e, "Couldn't keep the game settings in C7.ini");
		}
	}
}

// A parchment popup and the pieces it's made of. What one step shows is
// cleared for the next (Clear).
public partial class OnlinePanel : Popup {
	public static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	public static readonly Color Warning = new(0.6f, 0.08f, 0.05f);

	public readonly int width;
	public readonly int height;
	private readonly List<Control> shown = new();

	public OnlinePanel(int width, int height) {
		this.width = width;
		this.height = height;
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins();
	}

	public override void _Ready() {
		base._Ready();
		AddTexture(width, height);
		CustomMinimumSize = new Vector2(width, height);
		AddBackground(width, height);
	}

	public T Show<T>(T control) where T : Control {
		AddChild(control);
		shown.Add(control);
		return control;
	}

	public void Clear() {
		foreach (Control control in shown) {
			control.QueueFree();
		}
		shown.Clear();
	}

	public void Header(string text) {
		Label header = Show(new Label {
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			Position = new Vector2(0, 12),
			Size = new Vector2(width, 34),
		});
		header.AddThemeFontOverride("font", ResourceLoader.Load<FontFile>("res://Fonts/NSansFont24Pt.tres"));
		header.AddThemeFontSizeOverride("font_size", 24);
		header.AddThemeColorOverride("font_color", Colors.Black);
	}

	public Label Text(string text, int y, int size, bool left = false, int x = 30, int width = -1) {
		Label label = new() {
			Text = text,
			HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center,
			// (Before the size, or the text won't wrap.)
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(x, y),
			Size = new Vector2(width < 0 ? this.width - 60 : width, 20),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		label.AddThemeColorOverride("font_color", Ink);
		label.AddThemeFontSizeOverride("font_size", size);
		return Show(label);
	}

	public void Button(string text, Vector2 position, Action action) {
		Civ3MenuButton button = Show(new Civ3MenuButton { Text = text, FontSize = 14, Position = position });
		button.Pressed += action;
	}

	public static LineEdit Field(Vector2 position, int width, string placeholder = "") {
		LineEdit field = new() { Position = position, Size = new Vector2(width, 28), MaxLength = 200, PlaceholderText = placeholder };
		field.AddThemeFontSizeOverride("font_size", 13);
		return field;
	}

	public static OptionButton Choice(Vector2 position, Vector2 size) {
		OptionButton choice = new() { Position = position, Size = size, FocusMode = FocusModeEnum.None };
		StyleBoxFlat box = new() {
			BorderColor = Color.Color8(150, 150, 150, 220),
			BgColor = Color.Color8(255, 255, 255, 0),
			BorderWidthBottom = 2,
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			BorderWidthTop = 2,
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
			ContentMarginTop = 4,
			ContentMarginBottom = 4,
		};
		foreach (string name in new[] { "normal", "hover", "pressed" }) {
			choice.AddThemeStyleboxOverride(name, box);
		}
		choice.AddThemeColorOverride("font_color", Ink);
		choice.AddThemeColorOverride("font_hover_color", Ink);
		choice.AddThemeColorOverride("font_pressed_color", Ink);
		choice.AddThemeColorOverride("font_focus_color", Ink);
		choice.AddThemeFontSizeOverride("font_size", 13);
		return choice;
	}
}
