using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;
using static C7GameData.TradeReport;
using Resource = C7GameData.Resource;

// Civ3's trade advisor: our luxuries and strategic resources at the top (our
// own, imported, exported), our cities on and off the capital's trade
// network, the civs we trade with or could, and for the civ picked, what
// each of us could give the other and the deals running.
[GlobalClass]
[Tool]
public partial class TradeAdvisor : Control {
	[Export] public TextureRect background;

	private const int PartnerRows = 8;
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	private static readonly Color Red = new(0.70f, 0.08f, 0.05f);
	private static readonly Color Green = new(0.12f, 0.45f, 0.12f);

	private TextureRect advisorHead;
	private Label advice;
	private AdvisorList advisorList;
	private Control content;
	private Player selected;
	private int page;

	public TradeAdvisor() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		background.Texture = TextureLoader.Load("advisors.trade.background");
		content = new Control { MouseFilter = MouseFilterEnum.Pass };
		background.AddChild(content);
		advisorHead = AdvisorUtils.CreateAdvisorHead(background, AdvisorHead.Advisor.Trade);
		AdvisorUtils.CreateExitButton(background).Pressed += () => GetParent<Advisors>().Hide();
		(_, advice) = AdvisorUtils.CreateAdvisorDialogBox(background);
		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "TRADE ADVISOR");
		advisorList = new AdvisorList(AdvisorHead.Advisor.Trade);
		background.AddChild(advisorList);
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
			AdvisorUtils.ShowAdvice(advisorHead, advice, AdvisorHead.Advisor.Trade, AdvisorAdvice.Trade(gameData, us), us.EraIndex());
			advisorList.Refresh(us.EraIndex());
			AddResources(gameData, us);
			AddNetwork(gameData, us);
			List<Partner> partners = Partners(gameData, us);
			AddPartners(partners);
			AddDetails(gameData, us, partners.Find(p => p.civ == selected));
		});
	}

	// One column per resource we know about, rows for our own, imported and
	// exported.
	private void AddResources(GameData gameData, Player us) {
		List<ResourceRow> rows = Resources(gameData, us);
		AddLabel("Resources", new Rect2(96, 122, 80, 20), 13);
		AddLabel("Our own", new Rect2(96, 164, 80, 20), 13);
		AddLabel("Imported", new Rect2(96, 222, 80, 20), 13);
		AddLabel("Exported", new Rect2(96, 270, 80, 20), 13);
		if (rows.Count == 0) {
			AddLabel("We know of no luxuries or strategic resources yet.", new Rect2(180, 164, 600, 20), 13);
			return;
		}
		float width = Math.Min(44, 604f / rows.Count);
		for (int i = 0; i < rows.Count; i++) {
			ResourceRow row = rows[i];
			float x = 180 + i * width;
			content.AddChild(new TextureRect {
				Texture = TextureLoader.Load("resources.large", row.resource, useCache: true),
				Position = new Vector2(x + (width - 34) / 2, 110),
				Size = new Vector2(34, 34),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				TooltipText = $"{row.resource.Name}: {row.local} of our own, {row.imported} imported, {row.exported} exported",
			});
			foreach ((int value, float y, Color color) in new[] { (row.local, 164f, Ink), (row.imported, 222f, Green), (row.exported, 270f, Red) }) {
				AddLabel(value == 0 ? "·" : $"{value}", new Rect2(x, y, width, 20), 14, HorizontalAlignment.Center, color: value == 0 ? Ink : color);
			}
		}
	}

	// Our cities: joined to the capital's network, or not (in red).
	private void AddNetwork(GameData gameData, Player us) {
		HashSet<City> off = Unconnected(gameData, us).ToHashSet();
		AddLabel("Trade Network", new Rect2(94, 322, 152, 20), 13);
		List<City> cities = us.cities.OrderBy(off.Contains).ToList();
		const int fits = 15;
		for (int i = 0; i < Math.Min(cities.Count, fits); i++) {
			City city = cities[i];
			AddLabel(off.Contains(city) ? $"{city.name} (cut off)" : city.name, new Rect2(96, 348 + i * 23, 150, 20), 12,
				color: off.Contains(city) ? Red : Ink);
		}
		if (cities.Count > fits) {
			AddLabel($"and {cities.Count - fits} more", new Rect2(96, 348 + fits * 23, 150, 20), 12);
		}
	}

	// The civs we know, trading first; picking one shows it on the right.
	private void AddPartners(List<Partner> partners) {
		if (selected != null && !partners.Any(p => p.civ == selected)) {
			selected = null;
		}
		selected ??= partners.FirstOrDefault()?.civ;
		int pages = Math.Max(1, (partners.Count + PartnerRows - 1) / PartnerRows);
		page = Math.Clamp(page, 0, pages - 1);
		AddLabel(pages > 1 ? $"Civilizations · {page + 1}/{pages}" : "Civilizations", new Rect2(278, 322, 180, 20), 13);
		if (pages > 1) {
			AddButton("▼", new Rect2(496, 318, 28, 24), () => {
				page = (page + 1) % pages;
				Rebuild();
			});
		}
		if (partners.Count == 0) {
			AddLabel("We know of no one to trade with.", new Rect2(280, 352, 246, 40), 13, wrap: true);
		}
		List<Partner> shown = partners.Skip(page * PartnerRows).Take(PartnerRows).ToList();
		for (int i = 0; i < shown.Count; i++) {
			Partner partner = shown[i];
			Rect2 rect = new(276, 344 + i * 44, 252, 40);
			if (partner.civ == selected) {
				content.AddChild(new ColorRect { Color = new Color(0.36f, 0.25f, 0.12f, 0.16f), Position = rect.Position, Size = rect.Size, MouseFilter = MouseFilterEnum.Ignore });
			}
			(string status, Color color) = partner.standing switch {
				Standing.Trading => ("Trading with us", Green),
				Standing.CanTrade => ("We can trade", Ink),
				_ => (partner.reason, Red),
			};
			AddLabel(partner.civ.civilization.name, new Rect2(rect.Position + new Vector2(4, 1), new Vector2(244, 18)), 13);
			AddLabel(status, new Rect2(rect.Position + new Vector2(4, 19), new Vector2(244, 18)), 12, color: color);
			Button pick = new() { Flat = true, Position = rect.Position, Size = rect.Size, FocusMode = FocusModeEnum.None };
			pick.Pressed += () => {
				selected = partner.civ;
				Rebuild();
			};
			content.AddChild(pick);
		}
	}

	// What the civ picked and we could give each other, and our deals.
	private void AddDetails(GameData gameData, Player us, Partner partner) {
		if (partner == null) {
			return;
		}
		Player them = partner.civ;
		List<string> lines = [$"The {them.civilization.noun}", ""];
		if (partner.standing == Standing.CannotTrade) {
			lines.Add($"We can't trade resources: {partner.reason.TrimEnd('.').ToLowerInvariant()}.");
		} else {
			List<Resource> theirs = TheyCouldSell(gameData, us, them);
			List<Resource> ours = TheyCouldSell(gameData, them, us);
			lines.Add(theirs.Count == 0 ? "They have nothing we lack." : $"They could trade us: {string.Join(", ", theirs.Select(r => r.Name))}");
			lines.Add(ours.Count == 0 ? "We have nothing they lack." : $"We could trade them: {string.Join(", ", ours.Select(r => r.Name))}");
		}
		List<string> deals = ForeignAffairs.Deals(gameData, us, them);
		lines.Add("");
		lines.Add(deals.Count == 0 ? "No deals running." : "Deals running:");
		lines.AddRange(deals);
		AddLabel(string.Join("\n", lines), new Rect2(584, 322, 350, 370), 13, wrap: true);
	}

	private void AddButton(string text, Rect2 rect, Action onPress) {
		Button button = new() { Text = text, Position = rect.Position, Size = rect.Size, FocusMode = FocusModeEnum.None };
		button.AddThemeFontSizeOverride("font_size", 12);
		button.Pressed += onPress;
		content.AddChild(button);
	}

	private void AddLabel(string text, Rect2 rect, int fontSize, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false, Color? color = null) {
		Label label = new() { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore, ClipText = !wrap };
		label.AddThemeColorOverride("font_color", color ?? Ink);
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
