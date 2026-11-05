using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// Online, at the top right: the turn timer's time left for whoever is
// playing ("Your turn" here), or with simultaneous moves the round's; red
// at the end; gone once their turn (or the round) is.
public partial class TurnClock : Label {
	private const double RedSeconds = 15;

	// (None: the round's.)
	private ID player;
	private bool round;
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
		round = player == null;
		left = seconds;
	}

	public override void _Process(double delta) {
		GameData gameData = EngineStorage.gameData;
		bool going = gameData != null && (round ? gameData.players.Any(p => TurnHandling.PlayingAtOnce(gameData, p.id))
			: player != null && EngineStorage.uiControllerID == player);
		if (!going) {
			Visible = false;
			return;
		}
		left = System.Math.Max(0, left - delta);
		int seconds = (int)System.Math.Ceiling(left);
		string whose = round ? (TurnHandling.PlayingAtOnce(gameData, EngineStorage.localPlayerID) ? "Your turn" : "The turn")
			: player == EngineStorage.localPlayerID ? "Your turn" : gameData.GetPlayer(player)?.civilization.leader ?? "?";
		Text = $"{whose}: {seconds / 60}:{seconds % 60:00}";
		AddThemeColorOverride("font_color", left <= RedSeconds ? new Color(1f, 0.45f, 0.35f) : Colors.White);
		Visible = true;
	}
}
