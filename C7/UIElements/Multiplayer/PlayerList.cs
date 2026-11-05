using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Online;
using C7GameData;
using C7.Textures;
using Godot;

// Online, at the top right: Civ3's player list (Play
// the World's art), a row for each human player with their civ's color,
// nickname, civ and score, whose turn it is, and who's away; by each other
// player, Civ3's chat balloon: lit, they see what this computer's player
// says in the chat; clicked off (Block Chat), they don't.
public partial class PlayerList : Control {
	private const int RowWidth = 273;
	private const int RowHeight = 25;
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	private static readonly Color Faded = new(0.13f, 0.10f, 0.05f, 0.45f);

	private sealed class Row {
		public ID civ;
		public Label name;
		public Label score;
		public TextureButton balloon;
	}

	private readonly HashSet<ID> blocked = new();
	private readonly List<Row> rows = new();
	private OnlineGame online;
	private Texture2D rowArt;
	private Texture2D balloonArt;
	private double sinceRefresh;

	public override void _Ready() {
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
		OffsetLeft = -(RowWidth + 12);
		OffsetRight = -12;
		// (Below the city screen's header, whose close button is at the top
		// right in a small window.)
		OffsetTop = 112;
		rowArt = Art("multiplayer.player_row");
		balloonArt = Art("multiplayer.chat_balloon");
	}

	private static Texture2D Art(string key) {
		try {
			return TextureLoader.Load(key);
		} catch (Exception) {
			return null;
		}
	}

	public void Begin(OnlineGame game) {
		online = game;
		online.PlayersChanged += Rebuild;
		Rebuild();
	}

	public override void _ExitTree() {
		if (online != null) {
			online.PlayersChanged -= Rebuild;
		}
	}

	// To whose players what's said goes: none for everyone (no one blocked).
	public IReadOnlyCollection<ID> Recipients() {
		if (blocked.Count == 0) {
			return null;
		}
		return Humans().Where(p => p.id != EngineStorage.localPlayerID && !blocked.Contains(p.id)).Select(p => p.id).ToList();
	}

	public bool EveryoneBlocked => Humans().Where(p => p.id != EngineStorage.localPlayerID).All(p => blocked.Contains(p.id));

	private static List<Player> Humans() => EngineStorage.gameData?.players.Where(p => p.isHuman && !p.defeated).ToList() ?? [];

	private void Rebuild() {
		foreach (Node child in GetChildren()) {
			child.QueueFree();
		}
		rows.Clear();
		int y = 0;
		foreach (Player player in Humans()) {
			Control row = new() { Position = new Vector2(0, y), Size = new Vector2(RowWidth, RowHeight), MouseFilter = MouseFilterEnum.Ignore };
			// The art's team color box, see-through, over the civ's color.
			row.AddChild(new ColorRect { Color = TextureLoader.LoadColor(player.GetPlayerColor()), Position = new Vector2(5, 5), Size = new Vector2(23, 17), MouseFilter = MouseFilterEnum.Ignore });
			if (rowArt != null) {
				row.AddChild(new TextureRect { Texture = rowArt, MouseFilter = MouseFilterEnum.Ignore });
			} else {
				row.AddChild(new ColorRect { Color = new Color(0.8f, 0.8f, 0.6f, 0.9f), Position = new Vector2(30, 0), Size = new Vector2(RowWidth - 30, RowHeight), MouseFilter = MouseFilterEnum.Ignore });
			}
			Row shown = new() { civ = player.id };
			shown.name = Text(new Vector2(33, 3), new Vector2(170, 19), HorizontalAlignment.Left);
			shown.score = Text(new Vector2(200, 3), new Vector2(44, 19), HorizontalAlignment.Right);
			row.AddChild(shown.name);
			row.AddChild(shown.score);
			if (player.id != EngineStorage.localPlayerID) {
				shown.balloon = new TextureButton { Position = new Vector2(251, 5), Size = new Vector2(16, 16), IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.Scale };
				ID civ = player.id;
				shown.balloon.Pressed += () => {
					if (!blocked.Remove(civ)) {
						blocked.Add(civ);
					}
					Refresh();
				};
				row.AddChild(shown.balloon);
			}
			AddChild(row);
			rows.Add(shown);
			y += RowHeight + 2;
		}
		Refresh();
	}

	private static Label Text(Vector2 position, Vector2 size, HorizontalAlignment alignment) {
		Label label = new() { Position = position, Size = size, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", 12);
		return label;
	}

	private void Refresh() {
		GameData gameData = EngineStorage.gameData;
		if (gameData == null || online == null) {
			return;
		}
		foreach (Row row in rows) {
			Player player = gameData.GetPlayer(row.civ);
			if (player == null) {
				continue;
			}
			string name = online.Names.TryGetValue(row.civ, out string nickname) ? nickname : player.civilization.leader;
			bool away = online.AwayCivs.Contains(row.civ);
			// (Civ3's turn indicator.)
			string turn = TurnHandling.IsTurnOf(gameData, row.civ) ? "▸ " : "";
			row.name.Text = $"{turn}{name} ({player.civilization.noun}){(away ? " (away)" : "")}";
			row.name.AddThemeColorOverride("font_color", away ? Faded : Ink);
			row.score.Text = gameData.history.TryGetValue(row.civ.ToString(), out List<HistTurnRecord> history) && history.Count > 0 ? history[^1].Score.ToString() : "0";
			row.score.AddThemeColorOverride("font_color", Ink);
			if (row.balloon != null) {
				bool sees = !blocked.Contains(row.civ);
				row.balloon.TextureNormal = sees ? balloonArt : null;
				row.balloon.TooltipText = sees ? $"{name} sees your chat messages. Click to block them."
					: $"{name} doesn't see your chat messages. Click to let them.";
			}
		}
	}

	public override void _Process(double delta) {
		sinceRefresh += delta;
		if (sinceRefresh >= 0.5) {
			sinceRefresh = 0;
			if (rows.Count != Humans().Count) {
				Rebuild();
			} else {
				Refresh();
			}
		}
	}
}
