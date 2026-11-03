using System;
using C7GameData;
using Godot;

// Hotseat: between two humans' turns, or before one answers what the
// engine asks them, the map is hidden behind this screen, which names the
// player whose turn it is, until they say they're there, so no one sees
// another's empire. The leader's portrait on Civ3's popup art; the wording
// is ours.
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
	private const int Height = 300;
	// The portrait stands above the parchment, as advisors do on popups.
	private const int PortraitHeight = 115;

	private readonly Player player;
	private readonly string message;
	private readonly Action onReady;

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

		Label text = new() {
			Text = message,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(30, top + 62),
			Size = new Vector2(Width - 60, 60),
		};
		text.AddThemeColorOverride("font_color", new Color(0.13f, 0.10f, 0.05f));
		text.AddThemeFontSizeOverride("font_size", 16);
		AddChild(text);

		AddButton("Begin", Height - 52, onReady);
	}
}
