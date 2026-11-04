using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using C7Engine;
using C7Engine.Online;
using C7GameData;
using C7GameData.Save;
using Godot;
using Serilog;

// Online play, before the game: the host's lobby (the invite to send, the
// game's password, the seats and who has them), or a joining player's (the
// invite and password to join with, then a seat to take). On the title
// screen's picture, a parchment like the hand-over screen's; the
// connections are the engine's (C7Engine.Online), read here each frame.
public partial class OnlineGameScreen : Control {
	private static readonly ILogger log = LogManager.ForContext<OnlineGameScreen>();

	private OnlinePanel panel;
	private OnlineHost host;
	private OnlineLobby lobby;
	private OnlineClient client;
	private bool joining;

	// The host's invite: the address chosen, and one typed in.
	private OptionButton addressChoice;
	private LineEdit typedAddress;
	private LineEdit invite;
	private List<HostAddresses.Address> addresses;

	// The seats (rebuilt on every change).
	private VBoxContainer seatRows;
	private Label others;
	private Label status;
	private LineEdit civPassword;
	private LobbyState state;

	public static string GameVersion() => (string)ProjectSettings.GetSetting("application/config/version");

	public override void _Ready() {
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		TextureRect picture = new() {
			Texture = GD.Load<Texture2D>("res://title-screen.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(picture);
		ColorRect dim = new() { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
		dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(dim);
		CenterContainer center = new() { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);
		panel = new OnlinePanel();
		center.AddChild(panel);

		GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		if (global.HostingOnline) {
			Host(global.SaveGame);
		} else {
			ShowJoinForm();
		}
	}

	public override void _Process(double delta) {
		if (host != null) {
			bool changed = false;
			while (host.TryNextEvent(out HostEvent hostEvent)) {
				changed |= lobby.Handle(hostEvent);
			}
			if (changed) {
				ShowSeats(lobby.State());
			}
		}
		if (client != null) {
			while (client != null && client.TryNextMessage(out OnlineMessage message)) {
				switch (message) {
					case LobbyState lobbyState:
						ShowSeats(lobbyState);
						break;
					case LobbyNotice notice:
						Say(notice.text, warning: true);
						break;
					case Goodbye goodbye:
						Leave();
						ShowJoinForm(goodbye.reason);
						break;
				}
			}
		}
	}

	public override void _ExitTree() {
		host?.Dispose();
		host = null;
		client?.Dispose();
		client = null;
	}

	private void BackToMainMenu() {
		_ExitTree();
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}

	private void Say(string text, bool warning = false) {
		if (status != null) {
			status.Text = text ?? "";
			status.AddThemeColorOverride("font_color", warning ? OnlinePanel.Warning : OnlinePanel.Ink);
		}
	}

	// Hosting: the human civs of the game just set up are the seats, the
	// host's own (the first, chosen on the player setup screen) taken.
	private void Host(SaveGame save) {
		List<SavePlayer> humans = save.Players.Where(p => p.human && p.isIncludedInGame && !p.defeated).ToList();
		Civilization CivOf(SavePlayer p) => save.Civilizations.First(c => c.name == p.civilization);
		List<OnlineLobby.Seat> seats = humans.Select((p, i) => new OnlineLobby.Seat {
			number = i,
			player = p.id,
			civ = CivOf(p).noun,
			leader = CivOf(p).leader,
			password = !string.IsNullOrEmpty(p.passwordHash),
		}).ToList();
		string hostName = CivOf(humans[0]).leader;
		int port = int.TryParse(C7Settings.GetSettingsValueOrDefault(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.Port, ""),
			out int configured) ? configured : OnlineProtocol.DefaultPort;
		string password = OnlineHost.NewPassword();

		host = new OnlineHost(hostName, password, GameVersion());
		try {
			host.Start(port);
		} catch (SocketException e) {
			log.Warning(e, "Couldn't host on port {Port}", port);
			host.Dispose();
			host = null;
			panel.Clear();
			panel.Header("Hosting a game online");
			panel.Text($"This computer couldn't listen on port {port}: {e.Message} Another program may be using the port "
				+ "(it's set as Port in the [Multiplayer] part of C7.ini).", 70, 14);
			panel.Button("Main Menu", new Vector2(OnlinePanel.Width - 150, OnlinePanel.Height - 40), BackToMainMenu);
			return;
		}
		lobby = new OnlineLobby(host, seats, (player, given) => {
			SavePlayer p = save.Players.First(sp => sp.id == player);
			return Passwords.Check(p.passwordHash, given, legacySalt: p.id.ToString());
		});
		lobby.TakeForHost(0);
		ShowHostLobby(password);
	}

	private void ShowHostLobby(string password) {
		panel.Clear();
		panel.Header("Hosting a game online");
		panel.Text("Send your players the invite and the game's password:", 54, 14, left: true);

		addresses = HostAddresses.OfThisComputer();
		addressChoice = panel.Show(OnlinePanel.Choice(new Vector2(30, 80), new Vector2(300, 28)));
		foreach (HostAddresses.Address address in addresses) {
			addressChoice.AddItem(address.Label);
		}
		addressChoice.AddItem("Internet address (port forwarding)...");
		typedAddress = panel.Show(OnlinePanel.Field(new Vector2(340, 80), 270, "Your internet address"));
		typedAddress.TextChanged += _ => UpdateInvite();
		addressChoice.ItemSelected += _ => UpdateInvite();

		invite = panel.Show(OnlinePanel.Field(new Vector2(30, 114), 470));
		invite.Editable = false;
		invite.SelectAllOnFocus = true;
		// Read-only, but as easy to read as a field to type in.
		invite.AddThemeColorOverride("font_uneditable_color", OnlinePanel.Ink);
		invite.AddThemeStyleboxOverride("read_only", invite.GetThemeStylebox("normal"));
		panel.Button("Copy", new Vector2(512, 114), () => {
			DisplayServer.ClipboardSet(invite.Text);
			Say("The invite is copied.");
		});

		panel.Text("Game password:", 156, 14, left: true, width: 140);
		LineEdit passwordField = panel.Show(OnlinePanel.Field(new Vector2(170, 152), 180));
		passwordField.Text = password;
		passwordField.TextChanged += text => host.SetPassword(text);
		panel.Text("(Change it if you like: it's asked of players who join after.)", 158, 12, left: true, x: 360, width: 250);

		panel.Text($"With Tailscale, share this computer with each player first. With port forwarding, forward TCP port {host.port} "
			+ "on your router to this computer.", 192, 12, left: true);
		UpdateInvite();

		ShowSeatArea(250);
		panel.Button("Start Game", new Vector2(30, OnlinePanel.Height - 40), StartGame);
		panel.Button("Stop Hosting", new Vector2(OnlinePanel.Width - 150, OnlinePanel.Height - 40), BackToMainMenu);
		ShowSeats(lobby.State());
	}

	private void UpdateInvite() {
		bool typing = addressChoice.Selected == addresses.Count;
		typedAddress.Visible = typing;
		string address = typing ? typedAddress.Text.Trim() : addresses[addressChoice.Selected].address;
		Invite made = host.InviteAt(address);
		invite.Text = Invite.Parse(made.ToString()) != null ? made.ToString() : "";
	}

	private void StartGame() {
		// (Step 2 of online play: the game itself.)
		Say(lobby.Full ? "Everyone is here. Playing the game online is the next step of this work."
			: "Every seat needs a player first.", warning: !lobby.Full);
	}

	// Joining: the invite, the game's password and the player's name.
	private void ShowJoinForm(string problem = null) {
		panel.Clear();
		seatRows = null;
		panel.Header("Joining a game online");
		panel.Text("Paste the invite the host sent you:", 60, 14, left: true);
		LineEdit inviteField = panel.Show(OnlinePanel.Field(new Vector2(30, 86), 580));
		inviteField.Text = C7Settings.GetSettingsValueOrDefault(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.LastInvite, "");
		panel.Text("Game password:", 136, 14, left: true, width: 150);
		LineEdit passwordField = panel.Show(OnlinePanel.Field(new Vector2(180, 132), 220));
		passwordField.Secret = true;
		panel.Text("Your name:", 176, 14, left: true, width: 150);
		LineEdit nameField = panel.Show(OnlinePanel.Field(new Vector2(180, 172), 220));
		nameField.MaxLength = OnlineMessage.MaxNameLength;
		nameField.Text = C7Settings.GetSettingsValueOrDefault(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.Name, "");
		status = panel.Text(problem ?? "", 224, 14);
		status.AddThemeColorOverride("font_color", OnlinePanel.Warning);
		panel.Text("The host's game needs to be the same version of OpenCiv3 as yours.", 300, 12);

		void Join() => _ = JoinWith(inviteField.Text, passwordField.Text, nameField.Text);
		inviteField.TextSubmitted += _ => Join();
		passwordField.TextSubmitted += _ => Join();
		nameField.TextSubmitted += _ => Join();
		panel.Button("Join", new Vector2(30, OnlinePanel.Height - 40), Join);
		panel.Button("Main Menu", new Vector2(OnlinePanel.Width - 150, OnlinePanel.Height - 40), BackToMainMenu);
		(inviteField.Text == "" ? inviteField : passwordField).CallDeferred(Control.MethodName.GrabFocus);
	}

	private async System.Threading.Tasks.Task JoinWith(string inviteText, string password, string name) {
		if (joining || client != null) {
			return;
		}
		Invite parsed = Invite.Parse(inviteText);
		name = name.Trim();
		if (parsed == null) {
			Say("That isn't an invite: it looks like 100.101.102.103:47300/ABCD... (copy it whole).", warning: true);
			return;
		}
		if (name == "" || name.Any(char.IsControl)) {
			Say("Give your name, for the host and the other players.", warning: true);
			return;
		}
		joining = true;
		Say("Joining...");
		try {
			OnlineClient joined = await OnlineClient.Join(parsed, name, password, GameVersion());
			if (!IsInsideTree()) {
				joined.Dispose();
				return;
			}
			client = joined;
			C7Settings.SetValue(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.Name, name);
			C7Settings.SetValue(C7Settings.Multiplayer.SectionName, C7Settings.Multiplayer.LastInvite, parsed.ToString());
			C7Settings.SaveSettings();
			ShowJoinedLobby();
		} catch (JoinFailedException e) {
			Say(e.Message, warning: true);
		} finally {
			joining = false;
		}
	}

	private void ShowJoinedLobby() {
		panel.Clear();
		panel.Header($"{client.hostName}'s game");
		panel.Text("Take a seat: the civilization you'll play. The host starts the game once every seat is taken.", 54, 14, left: true);
		ShowSeatArea(100);
		panel.Text("Civ password (a loaded game's civ may have one):", OnlinePanel.Height - 116, 12, left: true, width: 330);
		civPassword = panel.Show(OnlinePanel.Field(new Vector2(360, OnlinePanel.Height - 120), 250));
		civPassword.Secret = true;
		panel.Button("Leave", new Vector2(OnlinePanel.Width - 150, OnlinePanel.Height - 40), BackToMainMenu);
	}

	private void Leave() {
		client?.Dispose();
		client = null;
	}

	private void ShowSeatArea(int top) {
		panel.Text("Seats", top, 16, left: true);
		ScrollContainer scroll = panel.Show(new ScrollContainer {
			Position = new Vector2(30, top + 26),
			Size = new Vector2(OnlinePanel.Width - 60, OnlinePanel.Height - top - 190),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		});
		seatRows = new VBoxContainer();
		seatRows.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(seatRows);
		others = panel.Text("", OnlinePanel.Height - 156, 13, left: true);
		status = panel.Text("", OnlinePanel.Height - 82, 14);
	}

	// Each seat: the civ, and who has it; a joining player can take a free
	// one or give theirs up.
	private void ShowSeats(LobbyState shown) {
		state = shown;
		if (seatRows == null) {
			return;
		}
		foreach (Node row in seatRows.GetChildren()) {
			row.QueueFree();
		}
		int me = host != null ? OnlineLobby.Host : client.playerId;
		string NameOf(int player) => player == me ? "you" : shown.players.FirstOrDefault(p => p.player == player)?.name ?? "?";
		foreach (LobbySeat seat in shown.seats) {
			HBoxContainer row = new();
			Label civ = OnlinePanel.RowLabel($"{seat.civ} ({seat.leader})" + (seat.password ? ", with a password" : ""), 300);
			row.AddChild(civ);
			Label who = OnlinePanel.RowLabel(seat.player == null ? "open" : NameOf(seat.player.Value), 160);
			row.AddChild(who);
			if (client != null) {
				if (seat.player == me) {
					row.AddChild(OnlinePanel.RowButton("Leave seat", () => client.Send(new LeaveSeat())));
				} else if (seat.player == null) {
					int number = seat.seat;
					row.AddChild(OnlinePanel.RowButton("Take", () => {
						Say("");
						client.Send(new TakeSeat { seat = number, password = civPassword?.Text ?? "" });
					}));
				}
			}
			seatRows.AddChild(row);
		}
		List<string> unseated = shown.players.Where(p => shown.seats.All(s => s.player != p.player)).Select(p => NameOf(p.player)).ToList();
		others.Text = unseated.Count > 0 ? $"Here without a seat: {string.Join(", ", unseated)}" : "";
		if (host != null) {
			int open = shown.seats.Count(s => s.player == null);
			Say(open == 0 ? "Every seat is taken." : $"Waiting for players: {open} seat{(open == 1 ? "" : "s")} open.");
		}
	}
}

// The parchment the lobby is drawn on, and the pieces it's made of. What
// one step of the screen shows is cleared for the next (Clear).
public partial class OnlinePanel : Popup {
	public const int Width = 640;
	public const int Height = 600;
	public static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	public static readonly Color Warning = new(0.6f, 0.08f, 0.05f);

	private readonly List<Control> shown = new();

	public OnlinePanel() {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins();
	}

	public override void _Ready() {
		base._Ready();
		AddTexture(Width, Height);
		CustomMinimumSize = new Vector2(Width, Height);
		AddBackground(Width, Height);
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
			Size = new Vector2(Width, 34),
		});
		header.AddThemeFontOverride("font", ResourceLoader.Load<FontFile>("res://Fonts/NSansFont24Pt.tres"));
		header.AddThemeFontSizeOverride("font_size", 24);
		header.AddThemeColorOverride("font_color", Colors.Black);
	}

	public Label Text(string text, int y, int size, bool left = false, int x = 30, int width = Width - 60) {
		Label label = new() {
			Text = text,
			HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center,
			// (Before the size, or the text won't wrap.)
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(x, y),
			Size = new Vector2(width, 20),
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

	public static Label RowLabel(string text, int width) {
		Label label = new() { Text = text, CustomMinimumSize = new Vector2(width, 26), ClipText = true, VerticalAlignment = VerticalAlignment.Center };
		label.AddThemeColorOverride("font_color", Ink);
		label.AddThemeFontSizeOverride("font_size", 14);
		return label;
	}

	public static Civ3MenuButton RowButton(string text, Action action) {
		Civ3MenuButton button = new() { Text = text, FontSize = 13 };
		button.Pressed += action;
		return button;
	}
}
