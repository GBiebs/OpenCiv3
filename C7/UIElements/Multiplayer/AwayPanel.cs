using System;
using System.Collections.Generic;
using C7Engine;
using C7GameData;
using Godot;

// Online, the host's: the players who lost their connection (Civ3's
// "Waiting on Player #2 (Sargon)"), each with Drop Player, which takes
// their civilization out of the game. Gone when nobody is away.
public partial class AwayPanel : PanelContainer {
	private readonly VBoxContainer rows = new();

	public event Action<ID> DropPlayer;

	public override void _Ready() {
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
		OffsetTop = 56;
		AddThemeStyleboxOverride("panel", new StyleBoxFlat {
			BgColor = new Color(0, 0, 0, 0.6f),
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
		});
		rows.AddThemeConstantOverride("separation", 4);
		AddChild(rows);
		Visible = false;
	}

	public void ShowWaiting(IReadOnlyDictionary<ID, string> away, GameData gameData) {
		foreach (Node row in rows.GetChildren()) {
			row.QueueFree();
		}
		foreach ((ID civ, string name) in away) {
			HBoxContainer row = new();
			row.AddThemeConstantOverride("separation", 12);
			Label text = new() { Text = $"Waiting for {name} ({gameData.GetPlayer(civ)?.civilization.noun ?? "?"}) to reconnect...", VerticalAlignment = VerticalAlignment.Center };
			text.AddThemeColorOverride("font_color", Colors.White);
			text.AddThemeFontSizeOverride("font_size", 14);
			row.AddChild(text);
			Button drop = new() { Text = "Drop Player", FocusMode = FocusModeEnum.None };
			drop.AddThemeFontSizeOverride("font_size", 13);
			// (A button, plainly, on any background.)
			foreach ((string state, Color color) in new[] { ("normal", new Color(0.55f, 0.2f, 0.1f, 0.95f)), ("hover", new Color(0.7f, 0.28f, 0.14f, 0.95f)), ("pressed", new Color(0.45f, 0.15f, 0.08f, 0.95f)) }) {
				drop.AddThemeStyleboxOverride(state, new StyleBoxFlat {
					BgColor = color,
					CornerRadiusTopLeft = 3,
					CornerRadiusTopRight = 3,
					CornerRadiusBottomLeft = 3,
					CornerRadiusBottomRight = 3,
					ContentMarginLeft = 10,
					ContentMarginRight = 10,
					ContentMarginTop = 3,
					ContentMarginBottom = 3,
				});
			}
			drop.AddThemeColorOverride("font_color", Colors.White);
			drop.AddThemeColorOverride("font_hover_color", Colors.White);
			drop.Pressed += () => DropPlayer?.Invoke(civ);
			row.AddChild(drop);
			rows.AddChild(row);
		}
		Visible = away.Count > 0;
		// (Centered on what it holds now.)
		ResetSize();
		OffsetLeft = -Size.X / 2;
		OffsetRight = Size.X / 2;
	}
}
