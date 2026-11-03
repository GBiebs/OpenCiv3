using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;
using static C7GameData.ForeignAffairs;

// A leader's portrait with a mood: the row of the leader art (0 pleased, 1
// neutral, 2 angry). See textures/leader_heads.lua.
public record struct LeaderPortrait(Player player, int moodRow);

// Civ3's foreign advisor: the civs' leaders in a ring, ours at the bottom (a
// "?" for those we haven't met), and lines across it showing how they stand
// with each other; which kinds of line show is the player's choice. Picking
// a civ shows our treaties with it, or our running deals, on the tabs.
[GlobalClass]
[Tool]
public partial class ForeignAdvisor : Control {
	[Export] public TextureRect background;

	private const int PerPage = 8;
	private const float PortraitSize = 115;
	private static readonly Vector2 Center = new(400, 378);
	private static readonly Vector2 Radius = new(285, 212);
	private static readonly Vector2 TabsAt = new(770, 290);
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);

	// Portraits are round; some leader art has a black square behind.
	private static readonly ShaderMaterial Round = new() {
		Shader = new Shader { Code = "shader_type canvas_item;\nvoid fragment() {\n\tif (distance(UV, vec2(0.5)) > 0.5) {\n\t\tdiscard;\n\t}\n}\n" },
	};

	private static readonly List<(Relation relation, string name, Color color)> Lines = [
		(Relation.War, "War", new Color(0.80f, 0.10f, 0.08f)),
		(Relation.Peace, "Peace", new Color(0.55f, 0.50f, 0.40f)),
		(Relation.Alliance, "Alliance", new Color(0.55f, 0.20f, 0.65f)),
		(Relation.MutualProtection, "Mutual Protection", new Color(0.15f, 0.35f, 0.80f)),
		(Relation.RightOfPassage, "Right of Passage", new Color(0.15f, 0.55f, 0.20f)),
		(Relation.Embargo, "Embargo", new Color(0.10f, 0.08f, 0.05f)),
	];

	// The kinds of line shown; peace (between nearly everyone) starts off.
	private static readonly HashSet<Relation> shownLines = Lines.Select(l => l.relation).Where(r => r != Relation.Peace).ToHashSet();

	private TextureRect advisorHead;
	private Label advice;
	private Control content;
	private Player selected;
	private int page;
	private bool tradesTab;

	public ForeignAdvisor() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		background.Texture = TextureLoader.Load("advisors.foreign.background");
		content = new Control { MouseFilter = MouseFilterEnum.Pass };
		background.AddChild(content);
		advisorHead = AdvisorUtils.CreateAdvisorHead(background, AdvisorHead.Advisor.Foreign);
		AdvisorUtils.CreateExitButton(background).Pressed += () => GetParent<Advisors>().Hide();
		(_, advice) = AdvisorUtils.CreateAdvisorDialogBox(background);
		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "FOREIGN ADVISOR");
	}

	public void ShowAdvisor() {
		Show();
		Rebuild();
	}

	private void Rebuild() {
		foreach (Node child in content.GetChildren()) {
			content.RemoveChild(child);
			child.QueueFree();
		}
		EngineStorage.ReadGameData(gameData => {
			Player us = gameData.GetPlayer(EngineStorage.uiControllerID);
			AdvisorUtils.ShowAdvice(advisorHead, advice, AdvisorHead.Advisor.Foreign, AdvisorAdvice.Foreign(gameData, us), us.EraIndex());

			List<Player> rivals = gameData.players.Where(p => p != us && !p.isBarbarians && !p.defeated).ToList();
			HashSet<Player> known = gameData.GetKnownRivals(us).ToHashSet();
			if (selected != null && !known.Contains(selected)) {
				selected = null;
			}
			selected ??= rivals.FirstOrDefault(known.Contains);
			int pages = Math.Max(1, (rivals.Count + PerPage - 1) / PerPage);
			page = Math.Min(page, pages - 1);
			List<Player> shown = rivals.Skip(page * PerPage).Take(PerPage).ToList();

			// Us at the bottom, the others clockwise from there.
			List<Player> ring = shown.Prepend(us).ToList();
			Dictionary<Player, Vector2> at = new();
			for (int i = 0; i < ring.Count; i++) {
				double angle = Math.PI / 2 + 2 * Math.PI * i / ring.Count;
				at[ring[i]] = Center + new Vector2((float)Math.Cos(angle) * Radius.X, (float)Math.Sin(angle) * Radius.Y);
			}

			AddLines(at.Where(kv => kv.Key == us || known.Contains(kv.Key)).ToList());
			AddPortrait(gameData, us, us, at[us]);
			foreach (Player p in shown) {
				if (known.Contains(p)) {
					AddPortrait(gameData, us, p, at[p]);
				} else {
					AddStranger(at[p]);
				}
			}
			if (pages > 1) {
				Button more = AddButton($"More Civs ({page + 1}/{pages})", new Rect2(80, 74, 150, 26));
				more.Pressed += () => {
					page = (page + 1) % pages;
					Rebuild();
				};
			}
			AddTabs(gameData, us);
			AddLegend();
		});
	}

	// A line for each kind shown between every two civs placed here, side by
	// side where there are several.
	private void AddLines(List<KeyValuePair<Player, Vector2>> civs) {
		RelationLines lines = new() { Size = background.Size, MouseFilter = MouseFilterEnum.Ignore };
		for (int i = 0; i < civs.Count; i++) {
			for (int j = i + 1; j < civs.Count; j++) {
				List<Color> colors = Between(civs[i].Key, civs[j].Key).Where(shownLines.Contains)
					.Select(r => Lines.First(l => l.relation == r).color).ToList();
				if (colors.Count > 0) {
					lines.pairs.Add((civs[i].Value, civs[j].Value, colors));
				}
			}
		}
		content.AddChild(lines);
	}

	private void AddPortrait(GameData gameData, Player us, Player civ, Vector2 center) {
		bool atWar = civ != us && PlayerRelationship.AtWar(us, civ);
		Attitude.Level? attitude = civ == us || civ.isHuman ? null : Attitude.Of(gameData, civ, us);
		string ring = civ == selected ? "selected" : atWar ? "war" : null;
		if (ring != null) {
			ImageTexture highlight = TextureLoader.Load($"advisors.foreign.highlight.{ring}");
			content.AddChild(new TextureRect { Texture = highlight, Position = center - highlight.GetSize() / 2, MouseFilter = MouseFilterEnum.Ignore });
		}

		// Pleased while gracious or polite, angry when annoyed or at war.
		int mood = civ == us || attitude <= Attitude.Level.Polite ? 0 : atWar || attitude >= Attitude.Level.Annoyed ? 2 : 1;
		TextureButton portrait = new() {
			TextureNormal = TextureLoader.Load("leader_heads", new LeaderPortrait(civ, mood), useCache: true),
			Position = center - new Vector2(PortraitSize, PortraitSize) / 2,
			TooltipText = Describe(us, civ),
			Material = Round,
		};
		if (civ != us) {
			portrait.Pressed += () => {
				selected = civ;
				Rebuild();
			};
		}
		content.AddChild(portrait);

		bool embassy = civ == us || Embassies.Has(us, civ);
		string first = attitude is Attitude.Level level ? $"{civ.civilization.name} ({level.ToString().ToLowerInvariant()})" : civ.civilization.name;
		AddLabel(embassy ? $"{first}\n{civ.government.name}" : first,
			new Rect2(center.X - 90, center.Y + PortraitSize / 2 + 1, 180, 34), 13, HorizontalAlignment.Center);
	}

	private void AddStranger(Vector2 center) {
		ImageTexture mystery = TextureLoader.Load("advisors.foreign.mystery");
		content.AddChild(new TextureRect {
			Texture = mystery,
			Position = center - mystery.GetSize() / 2,
			TooltipText = "A civilization we haven't met",
		});
		AddLabel("Unknown", new Rect2(center.X - 90, center.Y + PortraitSize / 2 + 1, 180, 18), 13, HorizontalAlignment.Center);
	}

	// The portrait's tooltip: the leader, and what we know of the civ.
	private static string Describe(Player us, Player civ) {
		List<string> lines = [$"{civ.civilization.leaderTitle} {civ.civilization.leader} of the {civ.civilization.noun}".Trim()];
		if (civ == us) {
			return lines[0];
		}
		lines.Add(Embassies.Has(us, civ) ? $"We have an embassy there. Government: {civ.government.name}" : "We have no embassy there.");
		if (Espionage.HasSpy(us, civ)) {
			lines.Add("Our spy is planted there.");
		}
		UnitPrototype best = BestKnownUnit(us, civ);
		lines.Add(best == null ? "We know little of their military." : $"The strongest of their units we know of: {best.name}");
		return string.Join("\n", lines);
	}

	// The Treaties and Trades tabs, for the civ picked.
	private void AddTabs(GameData gameData, Player us) {
		content.AddChild(new TextureRect {
			Texture = TextureLoader.Load(tradesTab ? "advisors.foreign.navigation.trades" : "advisors.foreign.navigation.treaties"),
			Position = TabsAt,
			MouseFilter = MouseFilterEnum.Ignore,
		});
		foreach ((string name, bool trades, float x, float width) in new[] { ("Treaties", false, 0f, 80f), ("Trades", true, 88f, 82f) }) {
			AddLabel(name, new Rect2(TabsAt + new Vector2(x, 1), new Vector2(width, 18)), 12, HorizontalAlignment.Center);
			Button tab = new() { Flat = true, Position = TabsAt + new Vector2(x, 0), Size = new Vector2(width, 20), FocusMode = FocusModeEnum.None };
			tab.Pressed += () => {
				tradesTab = trades;
				Rebuild();
			};
			content.AddChild(tab);
		}

		string text;
		if (selected == null) {
			text = "Pick a civilization we know to see our treaties and deals with it.";
		} else {
			string noun = selected.civilization.noun;
			List<string> lines = tradesTab ? Deals(gameData, us, selected) : Treaties(gameData, us, selected);
			if (lines.Count == 0) {
				lines.Add($"We have no deals running with the {noun}.");
			}
			text = $"The {noun}\n\n{string.Join("\n", lines)}";
		}
		AddLabel(text, new Rect2(TabsAt + new Vector2(12, 30), new Vector2(199, 196)), 12, HorizontalAlignment.Left, wrap: true);
	}

	// Which kinds of line show, each in its color, and all or none at once.
	private void AddLegend() {
		float x = 82;
		const float y = 690;
		foreach ((Relation relation, string name, Color color) in Lines) {
			CheckBox box = new() {
				Text = name,
				ButtonPressed = shownLines.Contains(relation),
				Position = new Vector2(x, y),
				FocusMode = FocusModeEnum.None,
			};
			foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) {
				box.AddThemeColorOverride(state, color);
			}
			box.AddThemeFontSizeOverride("font_size", 12);
			foreach (string style in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" }) {
				box.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
			}
			box.Toggled += on => {
				if (on) {
					shownLines.Add(relation);
				} else {
					shownLines.Remove(relation);
				}
				Rebuild();
			};
			content.AddChild(box);
			x += LineChart.RegularFont.GetStringSize(name, fontSize: 12).X + 34;
		}
		Button all = AddButton("All", new Rect2(x + 4, y + 2, 36, 22));
		all.Pressed += () => {
			shownLines.UnionWith(Lines.Select(l => l.relation));
			Rebuild();
		};
		Button none = AddButton("None", new Rect2(x + 44, y + 2, 46, 22));
		none.Pressed += () => {
			shownLines.Clear();
			Rebuild();
		};
	}

	private Button AddButton(string text, Rect2 rect) {
		Button button = new() { Text = text, Position = rect.Position, Size = rect.Size, FocusMode = FocusModeEnum.None };
		button.AddThemeFontSizeOverride("font_size", 12);
		content.AddChild(button);
		return button;
	}

	private void AddLabel(string text, Rect2 rect, int fontSize, HorizontalAlignment align, bool wrap = false) {
		Label label = new() { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
		label.AddThemeColorOverride("font_color", Ink);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (wrap) {
			// Before the size, so it wraps at it rather than growing.
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}
		label.Position = rect.Position;
		label.Size = rect.Size;
		content.AddChild(label);
	}

	// The lines between the portraits.
	private partial class RelationLines : Control {
		public List<(Vector2 from, Vector2 to, List<Color> colors)> pairs = new();

		public override void _Draw() {
			foreach ((Vector2 from, Vector2 to, List<Color> colors) in pairs) {
				Vector2 across = (to - from).Normalized().Orthogonal();
				for (int i = 0; i < colors.Count; i++) {
					Vector2 offset = across * (i - (colors.Count - 1) / 2f) * 4;
					DrawLine(from + offset, to + offset, colors[i], 3, antialiased: true);
				}
			}
		}
	}
}
