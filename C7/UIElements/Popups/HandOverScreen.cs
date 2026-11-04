using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// Hotseat: between two humans' turns, or before one answers what the
// engine asks them, the map is hidden behind this screen, which names the
// player whose turn it is, until they say they're there, so no one sees
// another's empire. A player with a password gives it first; one without
// may choose one (Civ3's optional hotseat password). The leader's portrait
// on Civ3's popup art; the wording is ours. Playing by email, it also
// covers the map once a turn is sent (TurnSentPanel).
public partial class HandOverScreen : Control {
	private readonly Control panel;

	public HandOverScreen(Player player, string message, Action onMainMenu, Action onAdministration, Action onReady)
		: this(new HandOverPanel(player, message, onMainMenu, onAdministration, onReady)) { }

	public HandOverScreen(Control panel) {
		this.panel = panel;
	}

	public override void _Ready() {
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		ColorRect cover = new() { Color = new Color(0.05f, 0.05f, 0.06f), MouseFilter = MouseFilterEnum.Ignore };
		cover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(cover);

		CenterContainer center = new() { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);
		center.AddChild(panel);
	}
}

// A leader's portrait above a parchment, as advisors stand on popups.
public abstract partial class LeaderPanel : Popup {
	protected const int Width = 440;
	protected const int Height = 340;
	protected const int PortraitHeight = 115;
	protected const int Top = PortraitHeight - 10;
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);

	protected readonly Player player;

	protected LeaderPanel(Player player) {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins();
		this.player = player;
	}

	public override void _Ready() {
		base._Ready();
		AddTexture(Width, Height);
		CustomMinimumSize = new Vector2(Width, Height);

		TextureRect portrait = new() {
			Texture = TextureLoader.Load("leader_heads", new LeaderPortrait(player, 0), useCache: true),
			Position = new Vector2((Width - PortraitHeight) / 2, 0),
		};
		AddChild(portrait);
		AddBackground(Width, Height - Top, Top);
	}

	protected Label TextLabel(string text, int y, int size) {
		Label label = new() {
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(30, y),
			Size = new Vector2(Width - 60, 24),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		label.AddThemeColorOverride("font_color", Ink);
		label.AddThemeFontSizeOverride("font_size", size);
		return label;
	}
}

public partial class HandOverPanel : LeaderPanel {
	private readonly string message;
	private readonly Action onMainMenu;
	private readonly Action onAdministration;
	private readonly Action onReady;
	private LineEdit password;
	private Label wrong;

	public HandOverPanel(Player player, string message, Action onMainMenu, Action onAdministration, Action onReady) : base(player) {
		this.message = message;
		this.onMainMenu = onMainMenu;
		this.onAdministration = onAdministration;
		this.onReady = onReady;
	}

	public override void _Ready() {
		base._Ready();
		AddHeader($"{player.civilization.leader} of the {player.civilization.noun}", Top + 12);
		AddChild(TextLabel(message, Top + 58, 16));

		bool hasPassword = player.HasPassword();
		AddChild(TextLabel(hasPassword ? "Password:" : "Choose a password if you like (none to leave it open):", Top + 92, 13));
		password = new LineEdit {
			Secret = true,
			Position = new Vector2((Width - 220) / 2, Top + 116),
			Size = new Vector2(220, 30),
			MaxLength = 40,
		};
		password.TextSubmitted += _ => Begin();
		AddChild(password);
		wrong = TextLabel("", Top + 150, 13);
		wrong.AddThemeColorOverride("font_color", new Color(0.6f, 0.08f, 0.05f));
		AddChild(wrong);
		password.CallDeferred(Control.MethodName.GrabFocus);

		if (onMainMenu != null) {
			AddButton("Begin", Height - 66, Begin);
			AddButton("Main Menu", Height - 40, onMainMenu);
		} else {
			AddButton("Begin", Height - 52, Begin);
		}
		// The administrator's way in (Administration).
		if (onAdministration != null) {
			Civ3MenuButton administration = new() { Text = "Administration...", FontSize = 14, Position = new Vector2(Width - 170, onMainMenu != null ? Height - 40 : Height - 52) };
			administration.Pressed += onAdministration;
			AddChild(administration);
		}
	}

	private void Begin() {
		if (player.HasPassword()) {
			if (!player.CheckPassword(password.Text)) {
				wrong.Text = "That isn't the password.";
				password.Clear();
				return;
			}
		} else if (password.Text != "") {
			new MsgSetPassword(player.id, password.Text).send();
		}
		onReady();
	}
}

// Play by email: a turn is over, and the game is saved for the next human,
// whose leader this shows, to play on their computer (once the game is
// over, the last save, for the others to hear how it ended). The player
// names the save; the name it starts with is the next player's.
public partial class SaveTurnPanel : LeaderPanel {
	private static readonly Color Warning = new(0.6f, 0.08f, 0.05f);

	private readonly string suggestedName;
	private readonly string folder;
	private readonly bool gameOver;
	// Saves under the file name given: null once saved, else what went wrong.
	private readonly Func<string, string> save;
	private readonly Action onMainMenu;
	private LineEdit name;
	private Label problem;
	private Civ3MenuButton mainMenu;
	// A save of this name is there, and the player was told.
	private string replacing;

	public SaveTurnPanel(Player next, string suggestedName, string folder, bool gameOver, Func<string, string> save, Action onMainMenu)
		: base(next) {
		this.suggestedName = suggestedName;
		this.folder = folder;
		this.gameOver = gameOver;
		this.save = save;
		this.onMainMenu = onMainMenu;
	}

	public override void _Ready() {
		base._Ready();
		AddHeader(gameOver ? "The game is over" : "Your turn is over", Top + 12);
		AddChild(TextLabel(gameOver
			? "Name the last save, for the other players to see how it ended:"
			: $"Name the save for {player.civilization.leader} of the {player.civilization.noun}, who plays next:", Top + 50, 14));
		name = new LineEdit {
			Text = System.IO.Path.GetFileNameWithoutExtension(suggestedName),
			Position = new Vector2(40, Top + 98),
			Size = new Vector2(Width - 80, 30),
			MaxLength = 120,
		};
		name.TextSubmitted += _ => Save();
		name.TextChanged += _ => problem.Text = "";
		AddChild(name);
		name.CallDeferred(Control.MethodName.GrabFocus);
		name.CallDeferred(LineEdit.MethodName.SelectAll);
		problem = TextLabel("", Top + 134, 13);
		problem.AddThemeColorOverride("font_color", Warning);
		AddChild(problem);

		AddButton("Save", Height - 40, Save);
		// Leaving without the save loses the turn: only once saving failed.
		mainMenu = new() { Text = "Main Menu", FontSize = 14, Position = new Vector2(Width - 120, Height - 40), Visible = false };
		mainMenu.Pressed += onMainMenu;
		AddChild(mainMenu);
	}

	private void Save() {
		string fileName = SaveNameGenerator.FileNameTyped(name.Text);
		if (fileName == null) {
			problem.Text = $"A save needs a name, without any of these: {string.Join(" ", SaveNameGenerator.CharactersNotInAName.ToCharArray())}";
			return;
		}
		if (System.IO.File.Exists(System.IO.Path.Combine(folder, fileName)) && replacing != fileName) {
			replacing = fileName;
			problem.Text = "There's a save of that name already. Save again to replace it.";
			return;
		}
		string error = save(fileName);
		if (error != null) {
			problem.Text = $"The game couldn't be saved: {error}";
			mainMenu.Visible = true;
		}
	}
}

// Play by email: the turn is saved for the next human to play.
public partial class TurnSentPanel : LeaderPanel {
	private readonly string fileName;
	private readonly string folder;
	private readonly bool gameOver;
	private readonly Action onMainMenu;
	private readonly Action onBackToTheGame;

	public TurnSentPanel(Player next, string fileName, string folder, bool gameOver, Action onMainMenu, Action onBackToTheGame)
		: base(next) {
		this.fileName = fileName;
		this.folder = folder;
		this.gameOver = gameOver;
		this.onMainMenu = onMainMenu;
		this.onBackToTheGame = onBackToTheGame;
	}

	public override void _Ready() {
		base._Ready();
		AddHeader(gameOver ? "The game is over" : "Your turn is over", Top + 12);
		string them = $"{player.civilization.leader} of the {player.civilization.noun}";
		int y = Height - 40;
		AddChild(TextLabel(gameOver
			? $"Send this last save to the other players, starting with {them}, so they see how it ended:"
			: $"Send this save to the player of {them}, who plays next:", Top + 50, 14));
		AddChild(TextLabel(fileName, Top + 104, 13));
		// (Room for one more button once the game is over.)
		if (!gameOver) {
			AddChild(TextLabel("It's in your Saves folder. They load it with Load Game.", Top + 128, 13));
		}
		y -= 26;
		AddButton("Open the Saves Folder", y, () => OS.ShellOpen(folder));
		if (onBackToTheGame != null) {
			y -= 26;
			AddButton("Back to the Game", y, onBackToTheGame);
		}
		AddButton("Main Menu", Height - 40, onMainMenu);
	}
}

// The administrator (Administration), from a hand-over screen: their
// password first (Civ3's "Password for Administration"), then who plays
// each civ, whose forgotten password to clear, and a new admin password.
public partial class AdminPanel : Popup {
	private const int Width = 520;
	private const int Height = 470;
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);

	private readonly GameData gameData;
	// Whose turn it is.
	private readonly ID playing;
	private readonly Action<string, List<Administration.Change>, string> onDone;
	private readonly Action onCancel;
	// What the current step shows.
	private readonly List<Control> shown = new();

	public AdminPanel(GameData gameData, ID playing, Action<string, List<Administration.Change>, string> onDone, Action onCancel) {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins();
		this.gameData = gameData;
		this.playing = playing;
		this.onDone = onDone;
		this.onCancel = onCancel;
	}

	public override void _Ready() {
		base._Ready();
		AddTexture(Width, Height);
		CustomMinimumSize = new Vector2(Width, Height);
		AddBackground(Width, Height);
		AddHeader("Administration", 14);
		AskPassword();
	}

	private T Show<T>(T control) where T : Control {
		AddChild(control);
		shown.Add(control);
		return control;
	}

	private void ClearStep() {
		foreach (Control control in shown) {
			control.QueueFree();
		}
		shown.Clear();
	}

	private Label Text(string text, int y, int size, Color? color = null) {
		Label label = new() {
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(30, y),
			Size = new Vector2(Width - 60, 24),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		label.AddThemeColorOverride("font_color", color ?? Ink);
		label.AddThemeFontSizeOverride("font_size", size);
		return Show(label);
	}

	private void Button(string text, Vector2 position, Action action) {
		Civ3MenuButton button = Show(new Civ3MenuButton { Text = text, FontSize = 14, Position = position });
		button.Pressed += action;
	}

	private LineEdit PasswordField(int y) {
		return Show(new LineEdit { Secret = true, Position = new Vector2((Width - 220) / 2, y), Size = new Vector2(220, 30), MaxLength = 40 });
	}

	private void AskPassword() {
		ClearStep();
		Text("Password for Administration:", 70, 14);
		LineEdit field = PasswordField(96);
		Label wrong = Text("", 132, 13, new Color(0.6f, 0.08f, 0.05f));
		void Check() {
			if (!gameData.CheckAdminPassword(field.Text)) {
				wrong.Text = "That isn't the password.";
				field.Clear();
				return;
			}
			ShowChanges(field.Text);
		}
		field.TextSubmitted += _ => Check();
		field.CallDeferred(Control.MethodName.GrabFocus);
		Button("Continue", new Vector2(30, Height - 40), Check);
		Button("Cancel", new Vector2(Width - 120, Height - 40), onCancel);
	}

	private void ShowChanges(string password) {
		ClearStep();
		Text("A human who has left can be handed to the AI, or a new player can take over a civilization. "
			+ "A cleared password is chosen again on that player's turn.", 52, 13);

		ScrollContainer scroll = Show(new ScrollContainer {
			Position = new Vector2(30, 128),
			Size = new Vector2(Width - 60, Height - 268),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		});
		VBoxContainer rows = new();
		scroll.AddChild(rows);
		List<Player> civs = Administration.Civs(gameData);
		Dictionary<ID, CheckBox> humans = new();
		HashSet<ID> cleared = new();
		foreach (Player civ in civs) {
			HBoxContainer row = new();
			Label name = new() {
				Text = $"{civ.civilization.leader} of the {civ.civilization.noun}" + (civ.id == playing ? " (this turn)" : ""),
				CustomMinimumSize = new Vector2(240, 0),
				ClipText = true,
			};
			name.AddThemeColorOverride("font_color", Ink);
			name.AddThemeFontSizeOverride("font_size", 13);
			row.AddChild(name);

			CheckBox human = new() { Text = "Human", ButtonPressed = civ.isHuman, FocusMode = FocusModeEnum.None };
			foreach (string colorName in new[] { "font_color", "font_pressed_color", "font_hover_color" }) {
				human.AddThemeColorOverride(colorName, Ink);
			}
			human.AddThemeFontSizeOverride("font_size", 13);
			row.AddChild(human);
			humans[civ.id] = human;

			Button clear = new() { Text = civ.HasPassword() ? "Clear password" : "No password", Disabled = !civ.HasPassword(), FocusMode = FocusModeEnum.None };
			clear.AddThemeFontSizeOverride("font_size", 12);
			clear.Pressed += () => {
				cleared.Add(civ.id);
				clear.Text = "Password cleared";
				clear.Disabled = true;
			};
			row.AddChild(clear);
			rows.AddChild(row);
		}

		Text("New admin password (leave it blank to keep the old one):", Height - 132, 13);
		LineEdit newPassword = PasswordField(Height - 108);
		Label problem = Text("", Height - 72, 13, new Color(0.6f, 0.08f, 0.05f));
		Button("Done", new Vector2(30, Height - 40), () => {
			List<Administration.Change> changes = civs.Select(c => new Administration.Change {
				playerId = c.id,
				human = humans[c.id].ButtonPressed,
				clearPassword = cleared.Contains(c.id),
			}).ToList();
			if (!changes.Any(c => c.human)) {
				problem.Text = "At least one civilization must be played by a human.";
				return;
			}
			onDone(password, changes, newPassword.Text);
		});
		Button("Cancel", new Vector2(Width - 120, Height - 40), onCancel);
	}
}
