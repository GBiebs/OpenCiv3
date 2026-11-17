using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using Godot;

// Civ3's editor's World Setup (Map > Generate Map) and Clear Map dialogs,
// laid out as Civ3's (screen_worldsetup.png): two columns of choices, the
// flags and the world seed, OK and Cancel.
public partial class EditorChrome {
	private static readonly (string label, WorldCharacteristics.OceanCoverage value)[] Oceans = [
		("60% Ocean", WorldCharacteristics.OceanCoverage.Percent_60),
		("70% Ocean", WorldCharacteristics.OceanCoverage.Percent_70),
		("80% Ocean", WorldCharacteristics.OceanCoverage.Percent_80),
	];
	private static readonly (string label, WorldCharacteristics.Age value)[] Ages = [
		("3 Billion Years", WorldCharacteristics.Age.Billion_3),
		("4 Billion Years", WorldCharacteristics.Age.Billion_4),
		("5 Billion Years", WorldCharacteristics.Age.Billion_5),
	];
	private static readonly (string label, BarbarianActivity value)[] Barbarians = [
		("No Barbarians", BarbarianActivity.None),
		("Sedentary", BarbarianActivity.Sedentary),
		("Roaming", BarbarianActivity.Roaming),
		("Restless", BarbarianActivity.Restless),
		("Raging", BarbarianActivity.Raging),
	];

	// World Setup. ok gets the choices; the map's own switches start as they
	// are.
	public Control WorldSetupDialog(GameMap map, List<WorldSize> sizes, Action<WorldMaker.WorldSetup> ok, Action cancel) {
		(TextureRect window, VBoxContainer box) = DialogWindow("WORLD SETUP", 560, 450);

		GridContainer grid = new() { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 24);
		grid.AddThemeConstantOverride("v_separation", 6);
		box.AddChild(grid);

		int standard = Math.Max(0, sizes.FindIndex(w => w.name == "Standard"));
		OptionButton size = Choice(grid, "Select world size:", sizes.Select(w => w.name), standard);
		HBoxContainer dimensions = new();
		dimensions.AddThemeConstantOverride("separation", 10);
		SpinBox width = Spin(sizes[standard].width), height = Spin(sizes[standard].height);
		dimensions.AddChild(LabeledColumn("Width:", width));
		dimensions.AddChild(LabeledColumn("Height:", height));
		grid.AddChild(dimensions);
		size.ItemSelected += i => {
			width.Value = sizes[(int)i].width;
			height.Value = sizes[(int)i].height;
		};

		OptionButton ocean = Choice(grid, "Select ocean coverage:", Oceans.Select(o => o.label), 1);
		OptionButton landform = Choice(grid, "Select land form:", Enum.GetNames<WorldCharacteristics.Landform>(), 1);
		OptionButton climate = Choice(grid, "Select climate:", Enum.GetNames<WorldCharacteristics.Climate>(), 1);
		OptionButton temperature = Choice(grid, "Select temperature:", Enum.GetNames<WorldCharacteristics.Temperature>(), 1);
		OptionButton age = Choice(grid, "Select age:", Ages.Select(a => a.label), 1);
		OptionButton barbarians = Choice(grid, "Select barbarian activity:", Barbarians.Select(b => b.label), 2);

		HBoxContainer lower = new();
		lower.AddThemeConstantOverride("separation", 24);
		VBoxContainer flags = new();
		flags.AddChild(InkLabel("Flags"));
		CheckBox polar = Check("Polar ice caps", map.polarIceCaps);
		CheckBox wrapX = Check("Allow X-wrapping", map.wrapHorizontally);
		CheckBox wrapY = Check("Allow Y-wrapping", map.wrapVertically);
		flags.AddChild(polar);
		flags.AddChild(wrapX);
		flags.AddChild(wrapY);
		lower.AddChild(flags);
		VBoxContainer seedBox = new();
		seedBox.AddChild(InkLabel("World seed:"));
		HBoxContainer seedRow = new();
		CheckBox useSeed = Check("", false);
		useSeed.TooltipText = "Use this seed (otherwise a random one)";
		SpinBox seed = Spin(new Random().Next(1, 99999999), 0, int.MaxValue);
		seed.CustomMinimumSize = new Vector2(150, 0);
		seed.Editable = false;
		useSeed.Toggled += on => seed.Editable = on;
		seedRow.AddChild(useSeed);
		seedRow.AddChild(seed);
		seedBox.AddChild(seedRow);
		lower.AddChild(seedBox);
		box.AddChild(lower);

		box.AddChild(Note("Generating replaces the whole map; it can't be undone."));
		box.AddChild(OkCancel(() => ok(new WorldMaker.WorldSetup {
			worldSize = sizes[size.Selected],
			width = (int)width.Value / 2 * 2,
			height = (int)height.Value / 2 * 2,
			ocean = Oceans[ocean.Selected].value,
			landform = (WorldCharacteristics.Landform)landform.Selected,
			climate = (WorldCharacteristics.Climate)climate.Selected,
			temperature = (WorldCharacteristics.Temperature)temperature.Selected,
			age = Ages[age.Selected].value,
			barbarians = Barbarians[barbarians.Selected].value,
			seed = useSeed.ButtonPressed ? (int)seed.Value : -1,
			polarIceCaps = polar.ButtonPressed,
			wrapX = wrapX.ButtonPressed,
			wrapY = wrapY.ButtonPressed,
		}), cancel));
		return Backdrop(window, 0.25f);
	}

	// Clear Map: a base terrain and a size. ok gets the terrain's key, the
	// width and the height.
	public Control ClearMapDialog(List<TerrainType> terrains, List<WorldSize> sizes, Action<string, int, int> ok, Action cancel) {
		(TextureRect window, VBoxContainer box) = DialogWindow("CLEAR MAP", 560, 290);
		GridContainer grid = new() { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 24);
		grid.AddThemeConstantOverride("v_separation", 6);
		box.AddChild(grid);
		List<TerrainType> choices = terrains.Where(t => t != TerrainType.NONE && !string.IsNullOrEmpty(t.Key)).ToList();
		OptionButton terrain = Choice(grid, "Base terrain:", choices.Select(t => t.DisplayName ?? t.Key), Math.Max(0, choices.FindIndex(t => t.IsOcean)));
		int standard = Math.Max(0, sizes.FindIndex(w => w.name == "Standard"));
		OptionButton size = Choice(grid, "World size:", sizes.Select(w => w.name), standard);
		SpinBox width = Spin(sizes[standard].width), height = Spin(sizes[standard].height);
		HBoxContainer dimensions = new();
		dimensions.AddThemeConstantOverride("separation", 10);
		dimensions.AddChild(LabeledColumn("Width:", width));
		dimensions.AddChild(LabeledColumn("Height:", height));
		grid.AddChild(InkLabel(""));
		grid.AddChild(dimensions);
		size.ItemSelected += i => {
			width.Value = sizes[(int)i].width;
			height.Value = sizes[(int)i].height;
		};
		box.AddChild(Note("Clearing replaces the whole map; it can't be undone."));
		box.AddChild(OkCancel(() => ok(choices[terrain.Selected].Key, (int)width.Value / 2 * 2, (int)height.Value / 2 * 2), cancel));
		return Backdrop(window, 0.25f);
	}

	private (TextureRect, VBoxContainer) DialogWindow(string heading, int width, int height) {
		TextureRect window = Popup.CreateBackground(width, height);
		window.CustomMinimumSize = new Vector2(width, height);
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "margin_left", "margin_right" }) {
			margin.AddThemeConstantOverride(side, 28);
		}
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		window.AddChild(margin);
		VBoxContainer box = new();
		box.AddThemeConstantOverride("separation", 10);
		margin.AddChild(box);
		box.AddChild(SetupStyle.MakeHeading(this, heading, 18));
		return (window, box);
	}

	// A label over a dropdown, in a grid cell.
	private static OptionButton Choice(GridContainer grid, string label, IEnumerable<string> items, int selected) {
		OptionButton dropdown = SetupStyle.MakeDropdown();
		dropdown.CustomMinimumSize = new Vector2(220, 0);
		SetupStyle.AddItems(dropdown, items.ToArray(), selected);
		grid.AddChild(LabeledColumn(label, dropdown));
		return dropdown;
	}

	private static VBoxContainer LabeledColumn(string label, Control control) {
		VBoxContainer column = new();
		column.AddThemeConstantOverride("separation", 2);
		column.AddChild(InkLabel(label));
		column.AddChild(control);
		return column;
	}

	private static SpinBox Spin(int value, int min = 10, int max = 362) {
		SpinBox spin = new() { MinValue = min, MaxValue = max, Step = 1, Value = value, CustomMinimumSize = new Vector2(80, 0) };
		StyleField(spin.GetLineEdit());
		return spin;
	}

	// A text field the parchment shows: a light fill, a brown frame, ink.
	internal static void StyleField(Control field) {
		StyleBoxFlat normal = SetupStyle.Box(LightColor, new Color(SetupStyle.Brown, 0.8f), 1, 3);
		normal.ContentMarginLeft = normal.ContentMarginRight = 5;
		StyleBoxFlat focus = SetupStyle.Box(LightColor, SetupStyle.Ink, 1, 3);
		focus.ContentMarginLeft = focus.ContentMarginRight = 5;
		field.AddThemeStyleboxOverride("normal", normal);
		field.AddThemeStyleboxOverride("focus", focus);
		field.AddThemeStyleboxOverride("read_only", normal);
		foreach (string color in new[] { "font_color", "font_readonly_color", "caret_color" }) {
			field.AddThemeColorOverride(color, SetupStyle.Ink);
		}
		field.AddThemeColorOverride("font_placeholder_color", SetupStyle.Faded);
	}

	private static CheckBox Check(string text, bool on) {
		CheckBox check = new() { Text = text, ButtonPressed = on };
		check.AddThemeColorOverride("font_disabled_color", SetupStyle.Faded);
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) {
			check.AddThemeColorOverride(color, SetupStyle.Ink);
		}
		return check;
	}

	private static Label Note(string text) {
		Label note = InkLabel(text);
		note.AddThemeColorOverride("font_color", SetupStyle.Faded);
		note.AddThemeFontSizeOverride("font_size", 13);
		return note;
	}

	private HBoxContainer OkCancel(Action ok, Action cancel) {
		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 10);
		foreach ((string text, Action action) in new (string, Action)[] { ("OK", ok), ("Cancel", cancel) }) {
			Button button = TextButton(text, null, action);
			button.Name = text;
			button.CustomMinimumSize = new Vector2(90, 28);
			buttons.AddChild(button);
		}
		return buttons;
	}
}
