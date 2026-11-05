using C7Engine;
using C7GameData;
using Godot;

// Online, at the top right: the turn timer's time left for whoever is
// playing ("Your turn" here), red at the end; gone once their turn is.
public partial class TurnClock : Label {
	private const double RedSeconds = 15;

	private ID player;
	private double left;

	public override void _Ready() {
		MouseFilter = MouseFilterEnum.Ignore;
		HorizontalAlignment = HorizontalAlignment.Right;
		SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
		OffsetLeft = -330;
		OffsetRight = -12;
		OffsetTop = 10;
		AddThemeFontSizeOverride("font_size", 16);
		AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
		AddThemeConstantOverride("shadow_offset_x", 1);
		AddThemeConstantOverride("shadow_offset_y", 1);
		AddThemeStyleboxOverride("normal", new StyleBoxFlat {
			BgColor = new Color(0, 0, 0, 0.45f),
			ContentMarginLeft = 8,
			ContentMarginRight = 8,
			ContentMarginTop = 2,
			ContentMarginBottom = 2,
		});
		Visible = false;
	}

	public void Start(ID player, int seconds) {
		this.player = player;
		left = seconds;
	}

	public override void _Process(double delta) {
		GameData gameData = EngineStorage.gameData;
		if (player == null || gameData == null || EngineStorage.uiControllerID != player) {
			Visible = false;
			return;
		}
		left = System.Math.Max(0, left - delta);
		int seconds = (int)System.Math.Ceiling(left);
		string whose = player == EngineStorage.localPlayerID ? "Your turn" : gameData.GetPlayer(player)?.civilization.leader ?? "?";
		Text = $"{whose}: {seconds / 60}:{seconds % 60:00}";
		AddThemeColorOverride("font_color", left <= RedSeconds ? new Color(1f, 0.45f, 0.35f) : Colors.White);
		Visible = true;
	}
}
