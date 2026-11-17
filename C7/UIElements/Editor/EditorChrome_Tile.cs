using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using Godot;

// What Tile Properties edits on a tile: its city (Civ3's City tab) and its
// units (the Units tab), and the choices they offer. The dialog changes the
// facts here; the editor applies them (TileEditing.apply returns why it
// couldn't, or null).
public sealed class TileEditing {
	public List<Player> players;
	public List<Building> buildings;
	public List<UnitPrototype> unitTypes;
	public List<ExperienceLevel> experience;
	public List<string> cityNames;
	public CityFacts city;
	public List<MapUnit> units = [];
	public List<UnitFacts> unitFacts = [];
	public Func<TileEditing, string> apply;
}

public partial class EditorChrome {
	// Civ3's City tab: Name (with Select... and Auto), Owner, Improvements,
	// Size and Culture (screen_cityproperties.png).
	private Control CityTab(TileEditing editing) {
		VBoxContainer tab = new() { Name = "City" };
		tab.AddThemeConstantOverride("separation", 6);
		if (editing.city == null) {
			tab.AddChild(InkLabel("No city here."));
			return tab;
		}
		HBoxContainer nameHead = new();
		Label nameLabel = InkLabel("Name:");
		nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		nameHead.AddChild(nameLabel);
		CheckBox auto = Check("Auto", editing.city.AutoName);
		auto.TooltipText = "The game names the city from its owner's list when the scenario starts.";
		nameHead.AddChild(auto);
		tab.AddChild(nameHead);
		HBoxContainer nameRow = new();
		LineEdit name = new() { Text = editing.city.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, Editable = !editing.city.AutoName };
		StyleField(name);
		nameRow.AddChild(name);
		OptionButton select = SetupStyle.MakeDropdown("A name from the owner's list");
		SetupStyle.AddItems(select, ["Select...", .. editing.cityNames], 0);
		select.ItemSelected += i => {
			if (i > 0) {
				name.Text = editing.cityNames[(int)i - 1];
				editing.city = editing.city with { Name = name.Text };
			}
		};
		nameRow.AddChild(select);
		tab.AddChild(nameRow);
		name.TextChanged += text => editing.city = editing.city with { Name = text };
		auto.Toggled += on => {
			editing.city = editing.city with { AutoName = on };
			name.Editable = !on;
		};
		tab.AddChild(Rule());

		tab.AddChild(InkLabel("Owner:"));
		OptionButton owner = SetupStyle.MakeDropdown();
		List<Player> owners = editing.players.Where(p => !p.isBarbarians).ToList();
		SetupStyle.AddItems(owner, owners.Select(ScenarioPlayers.NameOf).ToList(), Math.Max(0, owners.IndexOf(editing.city.Owner)));
		owner.ItemSelected += i => editing.city = editing.city with { Owner = owners[(int)i] };
		tab.AddChild(owner);
		tab.AddChild(Rule());

		HBoxContainer lower = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
		lower.AddThemeConstantOverride("separation", 16);
		VBoxContainer improvements = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		improvements.AddChild(InkLabel("Improvements:"));
		ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 170) };
		VBoxContainer list = new();
		HashSet<Building> chosen = [.. editing.city.Buildings];
		foreach (Building building in editing.buildings) {
			CheckBox box = Check(building.name, chosen.Contains(building));
			box.Toggled += on => {
				if (on) {
					chosen.Add(building);
				} else {
					chosen.Remove(building);
				}
				// In the rules' order, as the dialog lists them.
				editing.city = editing.city with { Buildings = editing.buildings.Where(chosen.Contains).ToList() };
			};
			list.AddChild(box);
		}
		scroll.AddChild(list);
		improvements.AddChild(scroll);
		lower.AddChild(improvements);
		VBoxContainer numbers = new();
		numbers.AddChild(InkLabel("Size:"));
		SpinBox size = Spin(editing.city.Size, 1, 99);
		size.ValueChanged += v => editing.city = editing.city with { Size = (int)v };
		numbers.AddChild(size);
		numbers.AddChild(InkLabel("Culture:"));
		SpinBox culture = Spin(editing.city.Culture, 0, 1000000);
		culture.ValueChanged += v => editing.city = editing.city with { Culture = (int)v };
		numbers.AddChild(culture);
		lower.AddChild(numbers);
		tab.AddChild(lower);
		return tab;
	}

	// Civ3's Units tab: the unit chosen from those on the tile, its Owner,
	// Name, Experience, AI Strategy and "Use Civ-Specific King Unit"
	// (screen_unitproperties.png), and its type.
	private Control UnitsTab(TileEditing editing) {
		VBoxContainer tab = new() { Name = "Units" };
		tab.AddThemeConstantOverride("separation", 6);
		if (editing.units.Count == 0) {
			tab.AddChild(InkLabel("No units here."));
			return tab;
		}
		OptionButton which = SetupStyle.MakeDropdown("The unit to edit");
		SetupStyle.AddItems(which, editing.units.Select(u => $"{u.unitType.name} ({ScenarioPlayers.NameOf(u.owner)})").ToList(), 0);
		tab.AddChild(which);
		tab.AddChild(Rule());
		VBoxContainer form = new();
		form.AddThemeConstantOverride("separation", 6);
		tab.AddChild(form);

		void Show(int index) {
			foreach (Node child in form.GetChildren()) {
				form.RemoveChild(child);
				child.QueueFree();
			}
			UnitFacts facts = editing.unitFacts[index];
			void Set(UnitFacts changed) => editing.unitFacts[index] = changed;
			List<Player> owners = editing.players;
			GridContainer grid = new() { Columns = 2 };
			grid.AddThemeConstantOverride("h_separation", 16);
			grid.AddThemeConstantOverride("v_separation", 6);
			form.AddChild(grid);

			grid.AddChild(InkLabel("Type:"));
			OptionButton type = SetupStyle.MakeDropdown();
			SetupStyle.AddItems(type, editing.unitTypes.Select(t => t.name).ToList(), Math.Max(0, editing.unitTypes.IndexOf(facts.Type)));
			grid.AddChild(type);
			grid.AddChild(InkLabel("Owner:"));
			OptionButton owner = SetupStyle.MakeDropdown();
			SetupStyle.AddItems(owner, owners.Select(ScenarioPlayers.NameOf).ToList(), Math.Max(0, owners.IndexOf(facts.Owner)));
			owner.ItemSelected += i => Set(editing.unitFacts[index] with { Owner = owners[(int)i] });
			grid.AddChild(owner);
			grid.AddChild(InkLabel("Name:"));
			LineEdit name = new() { Text = facts.Name ?? "", CustomMinimumSize = new Vector2(220, 0) };
			StyleField(name);
			name.TextChanged += text => Set(editing.unitFacts[index] with { Name = string.IsNullOrWhiteSpace(text) ? null : text });
			grid.AddChild(name);
			grid.AddChild(InkLabel("Experience:"));
			OptionButton experience = SetupStyle.MakeDropdown();
			SetupStyle.AddItems(experience, editing.experience.Select(e => e.displayName ?? e.key).ToList(), Math.Max(0, editing.experience.IndexOf(facts.Experience)));
			experience.ItemSelected += i => Set(editing.unitFacts[index] with { Experience = editing.experience[(int)i] });
			grid.AddChild(experience);
			grid.AddChild(InkLabel("AI Strategy:"));
			OptionButton strategy = SetupStyle.MakeDropdown("Its type's own unless chosen (kept for scenarios; the game doesn't use it yet)");
			grid.AddChild(strategy);
			void Strategies(UnitPrototype t) {
				List<SaveUnitPrototype.Flag> options = MapPainter.StrategiesOf(t);
				strategy.Clear();
				string current = editing.unitFacts[index].AiStrategy;
				SetupStyle.AddItems(strategy, ["(its type's)", .. options.Select(o => o.ToString().Substring(2))],
					Math.Max(0, options.FindIndex(o => o.ToString() == current) + 1));
				strategy.ItemSelected += i => Set(editing.unitFacts[index] with { AiStrategy = i == 0 ? null : options[(int)i - 1].ToString() });
			}
			Strategies(facts.Type);
			type.ItemSelected += i => {
				Set(editing.unitFacts[index] with { Type = editing.unitTypes[(int)i], AiStrategy = null });
				Strategies(editing.unitTypes[(int)i]);
			};
			CheckBox king = Check("Use Civ-Specific King Unit", facts.CivSpecificKing);
			king.TooltipText = "Kept for scenarios; the game doesn't use it yet.";
			king.Toggled += on => Set(editing.unitFacts[index] with { CivSpecificKing = on });
			form.AddChild(king);
		}
		Show(0);
		which.ItemSelected += i => Show((int)i);
		return tab;
	}

	// Tile Properties with the City and Units tabs editable: OK and Apply
	// hand the edits to the editor; a refusal shows in red.
	public Control TileProperties(Tile tile, TileEditing editing, Action close) {
		const int width = 520, height = 600;
		TextureRect window = Popup.CreateBackground(width, height);
		window.CustomMinimumSize = new Vector2(width, height);
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "margin_left", "margin_right" }) {
			margin.AddThemeConstantOverride(side, 26);
		}
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		window.AddChild(margin);
		VBoxContainer box = new();
		box.AddThemeConstantOverride("separation", 8);
		margin.AddChild(box);
		box.AddChild(SetupStyle.MakeHeading(this, "TILE PROPERTIES", 18));

		TabContainer tabs = new() { Name = "Tabs", SizeFlagsVertical = SizeFlags.ExpandFill };
		StyleTabs(tabs);
		box.AddChild(tabs);
		tabs.AddChild(TileTab(tile));
		tabs.AddChild(CityTab(editing));
		tabs.AddChild(UnitsTab(editing));
		if (editing.city != null) {
			tabs.CurrentTab = 1;
		} else if (editing.units.Count > 0) {
			tabs.CurrentTab = 2;
		}

		Label problem = InkLabel("");
		problem.Name = "Problem";
		problem.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		problem.AddThemeColorOverride("font_color", SetupStyle.ProblemColor);
		box.AddChild(problem);
		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 8);
		bool Apply() {
			problem.Text = editing.apply(editing) ?? "";
			return problem.Text == "";
		}
		foreach ((string text, Action action) in new (string, Action)[] {
			("OK", () => { if (Apply()) { close(); } }), ("Cancel", close), ("Apply", () => Apply()) }) {
			Button button = TextButton(text, null, action);
			button.Name = text;
			button.CustomMinimumSize = new Vector2(84, 26);
			buttons.AddChild(button);
		}
		box.AddChild(buttons);
		return Backdrop(window, 0.25f);
	}
}
