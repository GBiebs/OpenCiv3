using System;
using System.Collections.Generic;
using Godot;

// The File menu's dialogs (Phase 8): what to import from another file, and
// a report (Check Scenario, and before packaging).
public partial class EditorChrome {
	// Civ3's Import: which parts of the other file (its name) to bring in.
	// ok gets rules, map and player data.
	public Control ImportDialog(string fileName, bool hasMap, Action<bool, bool, bool> ok, Action cancel) {
		(TextureRect window, VBoxContainer box) = DialogWindow("IMPORT", 520, 300);
		box.AddChild(InkLabel($"From {fileName}:"));
		CheckBox rules = Check("Rules (every rules page)", true);
		rules.Name = "Rules";
		CheckBox map = Check("Map", false);
		map.Name = "Map";
		map.Disabled = !hasMap;
		CheckBox players = Check("Player Data (players, cities and units; with the map)", false);
		players.Name = "Player Data";
		players.Disabled = !hasMap;
		players.Toggled += on => {
			if (on) {
				map.ButtonPressed = true;
			}
		};
		map.Toggled += on => {
			if (!on) {
				players.ButtonPressed = false;
			}
		};
		box.AddChild(rules);
		box.AddChild(map);
		box.AddChild(players);
		box.AddChild(Note("Importing replaces those parts of this scenario; it can't be undone. A map without its player data clears the cities and units."));
		box.AddChild(OkCancel(() => ok(rules.ButtonPressed, map.ButtonPressed, players.ButtonPressed), cancel));
		return Backdrop(window, 0.25f);
	}

	// A list of lines (problems, or a note when there are none), with OK, or
	// with action's button and Cancel.
	public Control ReportDialog(string heading, List<string> lines, string empty, string action, Action ok, Action cancel) {
		(TextureRect window, VBoxContainer box) = DialogWindow(heading, 640, 420);
		ScrollContainer scroll = new() { CustomMinimumSize = new Vector2(0, 250), SizeFlagsVertical = SizeFlags.ExpandFill };
		VBoxContainer list = new();
		foreach (string line in lines.Count == 0 ? [empty] : lines) {
			Label label = InkLabel(line);
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			label.CustomMinimumSize = new Vector2(560, 0);
			list.AddChild(label);
		}
		scroll.AddChild(list);
		box.AddChild(scroll);
		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 10);
		foreach ((string text, Action act) in action == null
				? new (string, Action)[] { ("OK", cancel) }
				: new (string, Action)[] { (action, ok), ("Cancel", cancel) }) {
			Button button = TextButton(text, null, act);
			button.Name = NodeName(text);
			button.CustomMinimumSize = new Vector2(110, 28);
			buttons.AddChild(button);
		}
		box.AddChild(buttons);
		return Backdrop(window, 0.25f);
	}
}
