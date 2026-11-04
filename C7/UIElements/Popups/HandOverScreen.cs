using System;
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

	public HandOverScreen(Player player, string message, Action onMainMenu, Action onReady)
		: this(new HandOverPanel(player, message, onMainMenu, onReady)) { }

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
	private readonly Action onReady;
	private LineEdit password;
	private Label wrong;

	public HandOverPanel(Player player, string message, Action onMainMenu, Action onReady) : base(player) {
		this.message = message;
		this.onMainMenu = onMainMenu;
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
// whose leader this shows, to play on their computer; once the game is
// over, the last save, for the others to hear how it ended.
public partial class TurnSentPanel : LeaderPanel {
	private readonly string fileName;
	private readonly string folder;
	private readonly string error;
	private readonly bool gameOver;
	private readonly Action onMainMenu;
	private readonly Action onBackToTheGame;

	public TurnSentPanel(Player next, string fileName, string folder, string error, bool gameOver, Action onMainMenu, Action onBackToTheGame)
		: base(next) {
		this.fileName = fileName;
		this.folder = folder;
		this.error = error;
		this.gameOver = gameOver;
		this.onMainMenu = onMainMenu;
		this.onBackToTheGame = onBackToTheGame;
	}

	public override void _Ready() {
		base._Ready();
		AddHeader(gameOver ? "The game is over" : "Your turn is over", Top + 12);
		string them = $"{player.civilization.leader} of the {player.civilization.noun}";
		int y = Height - 40;
		if (error != null) {
			AddChild(TextLabel($"The game couldn't be saved for {them}: {error}", Top + 50, 14));
		} else {
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
		}
		if (onBackToTheGame != null) {
			y -= 26;
			AddButton("Back to the Game", y, onBackToTheGame);
		}
		AddButton("Main Menu", Height - 40, onMainMenu);
	}
}
