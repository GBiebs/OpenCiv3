using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;
using static C7GameData.MilitaryReport;

// Civ3's military advisor: where we stand (wars, units in production,
// armies and leaders) and what our units cost; our units by type or by city
// on the left, with upgrades for a type; and what we know of each rival's
// forces on the right.
[GlobalClass]
[Tool]
public partial class MilitaryAdvisor : Control {
	[Export] public TextureRect background;

	private const int Rows = 8;
	private const float RowTop = 296, RowHeight = 45;
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	private static readonly Color WarRed = new(0.70f, 0.08f, 0.05f);

	private TextureRect advisorHead;
	private Label advice;
	private AdvisorList advisorList;
	private Control content;
	private bool byCity;
	private int ourPage, theirPage;
	private UnitPrototype selectedType;
	private City selectedCity;

	public MilitaryAdvisor() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		background.Texture = TextureLoader.Load("advisors.military.background");
		content = new Control { MouseFilter = MouseFilterEnum.Pass };
		background.AddChild(content);
		advisorHead = AdvisorUtils.CreateAdvisorHead(background, AdvisorHead.Advisor.Military);
		AdvisorUtils.CreateExitButton(background).Pressed += () => GetParent<Advisors>().Hide();
		(_, advice) = AdvisorUtils.CreateAdvisorDialogBox(background);
		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "MILITARY ADVISOR");
		advisorList = new AdvisorList(AdvisorHead.Advisor.Military);
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
			AdvisorUtils.ShowAdvice(advisorHead, advice, AdvisorHead.Advisor.Military, AdvisorAdvice.Military(gameData, us), us.EraIndex());
			advisorList.Refresh(us.EraIndex());
			AddSummary(gameData, us);
			if (byCity) {
				AddCities(us);
			} else {
				AddTypes(gameData, us);
			}
			AddIntelligence(gameData, us);
		});
	}

	// Wars, production, armies and leaders, and the three boxes of unit
	// support.
	private void AddSummary(GameData gameData, Player us) {
		List<Player> enemies = gameData.GetKnownRivals(us).Where(p => PlayerRelationship.AtWar(us, p)).ToList();
		int producing = us.cities.Count(c => c.itemBeingProduced is UnitPrototype u && (u.attack > 0 || u.defense > 0));
		int armies = us.units.Count(u => u.IsArmy());
		int leaders = us.units.Count(u => u.unitType.isLeader);
		string wars = enemies.Count == 0 ? "We are at peace." : "At war with: " + string.Join(", ", enemies.Select(p => p.civilization.noun));
		AddLabel(wars, new Rect2(88, 98, 280, 40), 14, wrap: true, color: enemies.Count > 0 ? WarRed : Ink);
		AddLabel($"Military units in production: {producing}\nArmies: {armies}    Leaders: {leaders}\n{(us.mobilized ? "War-time mobilization" : "Normalcy")}",
			new Rect2(88, 142, 280, 84), 13);

		(int total, int allowed, int cost) = us.TotalUnitsAllowedUnitsAndSupportCost();
		AddLabel($"Total Units\n{total}", new Rect2(388, 92, 150, 40), 13, HorizontalAlignment.Center);
		AddLabel($"Free Support\n{allowed}", new Rect2(388, 140, 150, 40), 13, HorizontalAlignment.Center);
		AddLabel($"Support Cost\n{cost} gold a turn", new Rect2(388, 189, 150, 40), 13, HorizontalAlignment.Center);
	}

	// Our units by type; picking one shows it in the box at the top and
	// offers its upgrades.
	private void AddTypes(GameData gameData, Player us) {
		List<UnitGroup> groups = ByType(gameData, us);
		if (selectedType != null && !groups.Any(g => g.type == selectedType)) {
			selectedType = null;
		}
		selectedType ??= groups.FirstOrDefault()?.type;
		AddHeader($"Our Units ({us.units.Count})", new Rect2(100, 250, 420, 40), groups.Count, ref ourPage, page => ourPage = page);
		List<UnitGroup> page = groups.Skip(ourPage * Rows).Take(Rows).ToList();
		for (int i = 0; i < page.Count; i++) {
			UnitGroup group = page[i];
			string detail = group.upgradable > 0 ? $"{Units(group.units.Count)} · {group.upgradable} can upgrade ({group.upgradeCost} gold)" : Units(group.units.Count);
			AddRow(new Rect2(106, RowTop + i * RowHeight, 410, RowHeight - 4), group.type.name, detail, Icon(group.type, us), group.type == selectedType, () => {
				selectedType = group.type;
				Rebuild();
			});
		}
		if (groups.Count == 0) {
			AddLabel("We have no units.", new Rect2(110, RowTop + 8, 400, 20), 14);
		}

		UnitGroup selected = groups.Find(g => g.type == selectedType);
		if (selected != null) {
			UnitPrototype t = selected.type;
			content.AddChild(new TextureRect { Texture = Icon(t, us), Position = new Vector2(592, 116), MouseFilter = MouseFilterEnum.Ignore });
			AddLabel(t.name, new Rect2(630, 114, 120, 36), 14, wrap: true);
			string upgrade = t.upgradesTo.Count > 0 ? $"\nUpgrades to {string.Join(", ", t.upgradesTo.Select(u => u.name))}" : "";
			AddLabel($"Attack {t.attack} · Defense {t.defense} · Moves {t.movement}{upgrade}", new Rect2(592, 152, 156, 54), 12, wrap: true);
		}
		AddButton(selected?.upgradable > 0 ? $"Upgrade {selected.upgradable} ({selected.upgradeCost} gold)" : "Upgrade", new Rect2(591, 656, 175, 30),
			selected?.upgradable > 0, () => Upgrade(selected));
		AddButton("Show a Unit", new Rect2(776, 656, 155, 30), selected != null, () => ShowUnit(selected.units));
		AddViewButtons();
	}

	// Our units in each city, then those in the field.
	private void AddCities(Player us) {
		List<(City city, List<MapUnit> units)> places = ByCity(us);
		if (selectedCity != null && selectedCity.owner != us) {
			selectedCity = null;
		}
		selectedCity ??= us.cities.FirstOrDefault();
		AddHeader($"Our Units by City ({us.units.Count})", new Rect2(100, 250, 420, 40), places.Count, ref ourPage, page => ourPage = page);
		if (selectedCity != null) {
			MapUnit defender = places.Find(p => p.city == selectedCity).units?.Where(u => u.CanDefendOnLand())
				.OrderByDescending(u => u.unitType.defense).FirstOrDefault();
			AddLabel(selectedCity.name, new Rect2(592, 114, 156, 20), 14);
			AddLabel(defender == null ? "No defender!" : $"Defended by {defender.unitType.name}", new Rect2(592, 138, 156, 40), 12,
				wrap: true, color: defender == null ? WarRed : Ink);
		}
		List<(City city, List<MapUnit> units)> page = places.Skip(ourPage * Rows).Take(Rows).ToList();
		for (int i = 0; i < page.Count; i++) {
			(City city, List<MapUnit> units) = page[i];
			string detail = units.Count == 0 ? "no units" : string.Join(", ", units.GroupBy(u => u.unitType.name).Select(g => $"{g.Count()} {g.Key}"));
			AddRow(new Rect2(106, RowTop + i * RowHeight, 410, RowHeight - 4), city?.name ?? "In the field", detail, null, city != null && city == selectedCity, () => {
				selectedCity = city;
				Rebuild();
			});
		}
		AddButton("Open City", new Rect2(591, 656, 175, 30), selectedCity != null, () => {
			GetParent<Advisors>().Hide();
			new MsgShowCityScreen(selectedCity).send();
		});
		List<MapUnit> there = places.Find(p => p.city == selectedCity).units ?? [];
		AddButton("Show a Unit", new Rect2(776, 656, 155, 30), there.Count > 0, () => ShowUnit(there));
		AddViewButtons();
	}

	private void AddViewButtons() {
		AddButton("By Unit Type", new Rect2(138, 656, 175, 30), byCity, () => {
			byCity = false;
			ourPage = 0;
			Rebuild();
		});
		AddButton("By City", new Rect2(323, 656, 155, 30), !byCity, () => {
			byCity = true;
			ourPage = 0;
			Rebuild();
		});
	}

	// What we know of each rival's forces.
	private void AddIntelligence(GameData gameData, Player us) {
		List<Player> rivals = gameData.GetKnownRivals(us);
		AddHeader("Military Intelligence", new Rect2(560, 250, 395, 40), rivals.Count, ref theirPage, page => theirPage = page);
		List<Player> page = rivals.Skip(theirPage * Rows).Take(Rows).ToList();
		for (int i = 0; i < page.Count; i++) {
			Player them = page[i];
			Intelligence known = On(us, them);
			bool war = PlayerRelationship.AtWar(us, them);
			string marks = string.Join(", ", new[] { Embassies.Has(us, them) ? "embassy" : null, known.spy ? "spy" : null }.Where(m => m != null));
			string title = $"{them.civilization.name}: {(war ? "at war" : "at peace")}{(marks == "" ? "" : $" ({marks})")}";
			string detail = known.seen == 0 ? (known.spy ? "They have no military units." : "We see none of their units.")
				: $"{(known.spy ? "Spy report" : "In sight")}: {Units(known.seen)}, {string.Join(", ", known.types.Take(3).Select(t => $"{t.count} {t.type.name}"))}{(known.types.Count > 3 ? "…" : "")}";
			Rect2 rect = new(566, RowTop + i * RowHeight, 384, RowHeight - 4);
			AddLabel(title, new Rect2(rect.Position + new Vector2(4, 2), new Vector2(rect.Size.X - 8, 18)), 13, color: war ? WarRed : Ink);
			AddLabel(detail, new Rect2(rect.Position + new Vector2(4, 20), new Vector2(rect.Size.X - 8, 18)), 12);
		}
		if (rivals.Count == 0) {
			AddLabel("We know of no other civilization.", new Rect2(570, RowTop + 8, 380, 20), 14);
		}
	}

	// Upgrades the type's units the gold covers, cheapest first; the engine
	// checks each again.
	private void Upgrade(UnitGroup group) {
		foreach (MapUnit unit in group.units.Where(u => u.UpgradeOffer() != null).OrderBy(u => u.UpgradeOffer().Value.cost).Take(group.upgradable)) {
			new MsgUpgradeUnit(unit.id).send();
		}
		GetTree().CreateTimer(0.2).Timeout += Rebuild;
	}

	// Selects one of these units on the map (one that can still move, if
	// any) and closes the advisors.
	private void ShowUnit(List<MapUnit> units) {
		MapUnit unit = units.FirstOrDefault(u => u.movementPoints.canMove && !u.IsLoaded()) ?? units.FirstOrDefault(u => !u.IsLoaded());
		if (unit == null || GetTree().CurrentScene is not Game game) {
			return;
		}
		GetParent<Advisors>().Hide();
		game.SelectUnit(unit);
	}

	private static string Units(int count) => count == 1 ? "1 unit" : $"{count} units";

	private static ImageTexture Icon(UnitPrototype type, Player us) =>
		TextureLoader.Load("unit_icons", new ItemContext(type, us), useCache: true);

	// A header bar, with arrows to page through more than a page of rows.
	private void AddHeader(string title, Rect2 rect, int count, ref int page, Action<int> setPage) {
		int pages = Math.Max(1, (count + Rows - 1) / Rows);
		page = Math.Clamp(page, 0, pages - 1);
		AddLabel(pages > 1 ? $"{title}  ·  {page + 1}/{pages}" : title, new Rect2(rect.Position + new Vector2(12, 9), rect.Size - new Vector2(80, 9)), 15);
		if (pages > 1) {
			int current = page;
			AddButton("▲", new Rect2(rect.End.X - 64, rect.Position.Y + 7, 28, 26), current > 0, () => {
				setPage(current - 1);
				Rebuild();
			});
			AddButton("▼", new Rect2(rect.End.X - 32, rect.Position.Y + 7, 28, 26), current < pages - 1, () => {
				setPage(current + 1);
				Rebuild();
			});
		}
	}

	private void AddRow(Rect2 rect, string title, string detail, Texture2D icon, bool selected, Action onPick) {
		if (selected) {
			content.AddChild(new ColorRect { Color = new Color(0.36f, 0.25f, 0.12f, 0.16f), Position = rect.Position, Size = rect.Size, MouseFilter = MouseFilterEnum.Ignore });
		}
		float left = 4;
		if (icon != null) {
			content.AddChild(new TextureRect { Texture = icon, Position = rect.Position + new Vector2(4, 4), MouseFilter = MouseFilterEnum.Ignore });
			left = 42;
		}
		AddLabel(title, new Rect2(rect.Position + new Vector2(left, 2), new Vector2(rect.Size.X - left - 4, 18)), 13);
		AddLabel(detail, new Rect2(rect.Position + new Vector2(left, 20), new Vector2(rect.Size.X - left - 4, 18)), 12);
		Button pick = new() { Flat = true, Position = rect.Position, Size = rect.Size, FocusMode = FocusModeEnum.None };
		pick.Pressed += onPick;
		content.AddChild(pick);
	}

	private void AddButton(string text, Rect2 rect, bool enabled, Action onPress) {
		Button button = new() { Text = text, Position = rect.Position, Size = rect.Size, FocusMode = FocusModeEnum.None, Disabled = !enabled };
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
