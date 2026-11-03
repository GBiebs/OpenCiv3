using System;
using C7Engine;
using C7GameData;
using Godot;

// Hotseat: between two humans' turns, or before one answers what the
// engine asks them, the map is hidden behind this screen, which names the
// player whose turn it is, until they say they're there, so no one sees
// another's empire. A player with a password gives it first; one without
// may choose one (Civ3's optional hotseat password). The leader's portrait
// on Civ3's popup art; the wording is ours.
public partial class HandOverScreen : Control {
	private readonly Player player;
	private readonly string message;
	private readonly Action onReady;

	public HandOverScreen(Player player, string message, Action onReady) {
		this.player = player;
		this.message = message;
		this.onReady = onReady;
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
		center.AddChild(new HandOverPanel(player, message, onReady));
	}
}

public partial class HandOverPanel : Popup {
	private const int Width = 440;
	private const int Height = 340;
	// The portrait stands above the parchment, as advisors do on popups.
	private const int PortraitHeight = 115;
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);

	private readonly Player player;
	private readonly string message;
	private readonly Action onReady;
	private LineEdit password;
	private Label wrong;

	public HandOverPanel(Player player, string message, Action onReady) {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins();
		this.player = player;
		this.message = message;
		this.onReady = onReady;
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

		int top = PortraitHeight - 10;
		AddBackground(Width, Height - top, top);
		AddHeader($"{player.civilization.leader} of the {player.civilization.noun}", top + 12);
		AddChild(TextLabel(message, top + 58, 16));

		bool hasPassword = player.HasPassword();
		AddChild(TextLabel(hasPassword ? "Password:" : "Choose a password if you like (none to leave it open):", top + 92, 13));
		password = new LineEdit {
			Secret = true,
			Position = new Vector2((Width - 220) / 2, top + 116),
			Size = new Vector2(220, 30),
			MaxLength = 40,
		};
		password.TextSubmitted += _ => Begin();
		AddChild(password);
		wrong = TextLabel("", top + 150, 13);
		wrong.AddThemeColorOverride("font_color", new Color(0.6f, 0.08f, 0.05f));
		AddChild(wrong);
		password.CallDeferred(Control.MethodName.GrabFocus);

		AddButton("Begin", Height - 52, Begin);
	}

	private Label TextLabel(string text, int y, int size) {
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
