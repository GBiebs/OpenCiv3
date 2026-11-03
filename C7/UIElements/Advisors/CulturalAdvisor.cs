using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;
using static C7GameData.CultureReport;

// Civ3's cultural advisor: our total culture and culture a turn (from
// improvements and from wonders), each city's culture with a bar to its next
// border growth, our wonders, and how the civs we know see our culture.
[GlobalClass]
[Tool]
public partial class CulturalAdvisor : Control {
	[Export] public TextureRect background;

	private const int Rows = 9;
	private const float RowTop = 294, RowHeight = 45;
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	private static readonly Color Bar = new(0.30f, 0.30f, 0.62f);

	private TextureRect advisorHead;
	private Label advice;
	private Control content;
	private int cityPage, wonderPage;

	public CulturalAdvisor() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		background.Texture = TextureLoader.Load("advisors.culture.background");
		content = new Control { MouseFilter = MouseFilterEnum.Pass };
		background.AddChild(content);
		advisorHead = AdvisorUtils.CreateAdvisorHead(background, AdvisorHead.Advisor.Culture);
		AdvisorUtils.CreateExitButton(background).Pressed += () => GetParent<Advisors>().Hide();
		(_, advice) = AdvisorUtils.CreateAdvisorDialogBox(background);
		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "CULTURAL ADVISOR");
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
			AdvisorUtils.ShowAdvice(advisorHead, advice, AdvisorHead.Advisor.Culture, AdvisorAdvice.Culture(gameData, us), us.EraIndex());

			(int improvements, int wonders) = PerTurn(us);
			int perTurn = us.cities.Sum(c => c.GetCulturePerTurn());
			AddLabel($"Our Total Culture: {us.TotalCulture()}", new Rect2(90, 100, 280, 24), 16);
			AddLabel($"From improvements: {improvements} a turn\nFrom wonders: {wonders} a turn\n{gameData.timeOptions.GetDisplayTime(gameData.turn)}",
				new Rect2(90, 132, 280, 96), 13);
			AddLabel($"Culture a Turn\n+{perTurn}", new Rect2(493, 104, 187, 56), 15, HorizontalAlignment.Center);

			AddCities(us);
			AddWonders(us);
			AddOpinions(gameData, us);
		});
	}

	// Each city's culture and culture a turn, with a bar to its next border
	// growth in the blue column.
	private void AddCities(Player us) {
		List<CityCulture> cities = Cities(us);
		AddHeader("Cities", new Rect2(81, 247, 280, 42), cities.Count, cityPage, page => cityPage = page);
		List<CityCulture> page = cities.Skip(cityPage * Rows).Take(Rows).ToList();
		for (int i = 0; i < page.Count; i++) {
			CityCulture city = page[i];
			float y = RowTop + i * RowHeight;
			AddLabel(city.city.name, new Rect2(120, y + 2, 248, 18), 13);
			AddLabel($"{city.culture} culture · +{city.perTurn} a turn", new Rect2(120, y + 20, 248, 18), 12);

			// The bar: how far from the last border growth to the next.
			int from = city.nextExpansion / 10 <= 1 ? 0 : city.nextExpansion / 10;
			float share = Math.Clamp((city.culture - from) / (float)Math.Max(1, city.nextExpansion - from), 0, 1);
			int? turns = TurnsToExpand(city);
			string tip = $"{city.nextExpansion - city.culture} more culture for the borders of {city.city.name} to grow"
				+ (turns is int t ? $" ({AdvisorAdvice.Turns(t)})" : "");
			content.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.45f), Position = new Vector2(380, y + 16), Size = new Vector2(46, 10), TooltipText = tip });
			content.AddChild(new ColorRect { Color = Bar, Position = new Vector2(380, y + 16), Size = new Vector2(46 * share, 10), MouseFilter = MouseFilterEnum.Ignore });
		}
		if (cities.Count == 0) {
			AddLabel("We have no cities.", new Rect2(120, RowTop + 8, 240, 20), 13);
		}
	}

	private void AddWonders(Player us) {
		List<WonderCulture> wonders = Wonders(us);
		AddHeader("Wonders", new Rect2(445, 247, 236, 42), wonders.Count, wonderPage, page => wonderPage = page);
		List<WonderCulture> page = wonders.Skip(wonderPage * Rows).Take(Rows).ToList();
		for (int i = 0; i < page.Count; i++) {
			WonderCulture wonder = page[i];
			float y = RowTop + i * RowHeight;
			AddLabel(wonder.wonder.name, new Rect2(452, y + 2, 226, 18), 13);
			AddLabel($"in {wonder.city.name} · +{wonder.perTurn} a turn", new Rect2(452, y + 20, 226, 18), 12);
		}
		if (wonders.Count == 0) {
			AddLabel("We have built no wonders.", new Rect2(452, RowTop + 8, 226, 20), 13);
		}
	}

	// How the people of each civ we know see our culture, grouped by
	// opinion, the best first.
	private void AddOpinions(GameData gameData, Player us) {
		List<(Player civ, CultureOpinion opinion)> opinions = Opinions(gameData, us);
		AddLabel("What Others Think of Our Culture", new Rect2(734, 254, 244, 40), 14, wrap: true);
		string text = opinions.Count == 0 ? "We know no other civilization."
			: string.Join("\n\n", opinions.GroupBy(o => o.opinion).Select(g =>
				$"{char.ToUpperInvariant(g.Key.name[0])}{g.Key.name[1..]} our culture:\n{string.Join(", ", g.Select(o => o.civ.civilization.noun))}"));
		AddLabel(text, new Rect2(734, 304, 244, 396), 13, wrap: true);
	}

	// A header, with an arrow to page through more than a page of rows.
	private void AddHeader(string title, Rect2 rect, int count, int page, Action<int> setPage) {
		int pages = Math.Max(1, (count + Rows - 1) / Rows);
		AddLabel(pages > 1 ? $"{title} · {Math.Min(page, pages - 1) + 1}/{pages}" : title, new Rect2(rect.Position + new Vector2(10, 10), rect.Size - new Vector2(50, 10)), 15);
		if (pages > 1) {
			Button next = new() { Text = "▼", Position = new Vector2(rect.End.X - 34, rect.Position.Y + 8), Size = new Vector2(28, 26), FocusMode = FocusModeEnum.None };
			next.Pressed += () => {
				setPage((Math.Min(page, pages - 1) + 1) % pages);
				Rebuild();
			};
			content.AddChild(next);
		}
	}

	private void AddLabel(string text, Rect2 rect, int fontSize, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false) {
		Label label = new() { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore, ClipText = !wrap };
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
}
