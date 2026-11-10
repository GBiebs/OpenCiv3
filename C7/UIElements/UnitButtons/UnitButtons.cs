using C7.UIElements;
using Godot;
using System.Collections.Generic;
using C7Engine;
using C7GameData;
using Serilog;
using System.Linq;

/*
 UnitButtons contains the buttons at the bottom of the game UI when viewing the
 map, and control unit actions. Clicking buttons triggers Input.ActionPress
 calls which are checked and handled in Game.processActions.
*/

public partial class UnitButtons : VBoxContainer {
	[Signal] public delegate void ActionRequestedEventHandler(string action);

	private ILogger log = LogManager.ForContext<UnitButtons>();

	// A record holding a button and the base tooltip (without any indication
	// of how many turns an action might take).
	public record ButtonAndTooltip(TextureButton button, string baseTooltip);

	private Dictionary<string, ButtonAndTooltip> buttonMap = new();

	[Export]
	HBoxContainer primaryControls;

	[Export]
	HBoxContainer specializedControls;

	[Export]
	HBoxContainer advancedControls;

	// The bar's place along the bottom, kept for Backspace to go back to.
	private (Vector4 anchors, Vector4 offsets)? bottomBar;
	// The columns the buttons move into (Godot's row and column containers
	// can't turn), side by side; made the first time.
	private HBoxContainer columnsBox;

	// Backspace (Civ3's): the buttons in three rows along the bottom, or in
	// three columns down the left side.
	public void ToggleLayout() {
		HBoxContainer[] rows = [advancedControls, specializedControls, primaryControls];
		if (columnsBox == null) {
			columnsBox = new HBoxContainer() { Visible = false };
			AddChild(columnsBox);
			foreach (HBoxContainer _ in rows) {
				columnsBox.AddChild(new VBoxContainer() { Alignment = AlignmentMode.Center });
			}
		}
		bool columns = !columnsBox.Visible;
		bottomBar ??= (new Vector4(AnchorLeft, AnchorTop, AnchorRight, AnchorBottom), new Vector4(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom));
		for (int i = 0; i < rows.Length; i++) {
			Container from = columns ? rows[i] : (Container)columnsBox.GetChild(i);
			Container to = columns ? (Container)columnsBox.GetChild(i) : rows[i];
			foreach (Node button in from.GetChildren()) {
				button.Reparent(to);
			}
			rows[i].Visible = !columns;
		}
		columnsBox.Visible = columns;
		if (columns) {
			SetAnchorsAndOffsetsPreset(LayoutPreset.CenterLeft, LayoutPresetMode.Minsize, 8);
		} else {
			(Vector4 a, Vector4 o) = bottomBar.Value;
			(AnchorLeft, AnchorTop, AnchorRight, AnchorBottom) = (a.X, a.Y, a.Z, a.W);
			(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom) = (o.X, o.Y, o.Z, o.W);
		}
	}

	private static readonly string[] terraformOrder = [
		C7Action.UnitBuildRoad,
		C7Action.UnitBuildRailroad,
		C7Action.UnitBuildMine,
		C7Action.UnitIrrigate,
		C7Action.UnitClearForest,
		C7Action.UnitClearWetlands,
		C7Action.UnitBuildFortress,
		C7Action.UnitBuildBarricade,
		C7Action.UnitPlantForest,
		C7Action.UnitClearDamage,
	];

	private void SetUpControlButtons() {
		AddNewButton(primaryControls, C7Action.UnitHold);
		AddNewButton(primaryControls, C7Action.UnitWait);
		AddNewButton(primaryControls, C7Action.UnitFortify);
		AddNewButton(primaryControls, C7Action.UnitDisband);
		AddNewButton(primaryControls, C7Action.UnitGoto);
		AddNewButton(primaryControls, C7Action.UnitExplore);
		AddNewButton(primaryControls, C7Action.UnitSentry);
		AddNewButton(primaryControls, C7Action.UnitSentryEnemyOnly);
		AddNewButton(primaryControls, C7Action.UnitRename);

		//   ******* SPECIALIZED CONTROLS *************
		AddNewButton(specializedControls, C7Action.UnitLoad);
		AddNewButton(specializedControls, C7Action.UnitUnload);
		AddNewButton(specializedControls, C7Action.UnitPillage);
		AddNewButton(specializedControls, C7Action.UnitBombard);
		AddNewButton(specializedControls, C7Action.UnitBombing);
		AddNewButton(specializedControls, C7Action.UnitRecon);
		AddNewButton(specializedControls, C7Action.UnitAirSuperiority);
		AddNewButton(specializedControls, C7Action.UnitPrecisionStrike);
		AddNewButton(specializedControls, C7Action.UnitRebase);
		AddNewButton(specializedControls, C7Action.UnitAirdrop);
		AddNewButton(specializedControls, C7Action.UnitAirlift);
		// AddNewButton(specializedControls, "autobombard");
		// AddNewButton(specializedControls, "paradrop");
		//superfortify?
		AddNewButton(specializedControls, C7Action.UnitBuildArmy);
		AddNewButton(specializedControls, C7Action.UnitHurryProduction);
		AddNewButton(specializedControls, C7Action.UnitUpgrade);
		// AddNewButton(specializedControls, "sacrifice");
		AddNewButton(specializedControls, C7Action.UnitStartAgeOfScience);
		AddNewButton(specializedControls, C7Action.UnitBuildCity);
		AddNewButton(specializedControls, C7Action.UnitJoinCity);

		SetUpTerraformButtons();

		AddNewButton(specializedControls, C7Action.UnitAutomate);

		// Row index 4 and later not yet added
	}

	private void SetUpTerraformButtons() {
		var terraformOrderMap = terraformOrder
			.Select((action, index) => new { action, index })
			.ToDictionary(x => x.action, x => x.index);

		var sortedTerraforms = EngineStorage.gameData.Terraforms
			.OrderBy(tf => terraformOrderMap.TryGetValue(tf.UIAction, out var idx) ? idx : int.MaxValue);

		foreach (Terraform tf in sortedTerraforms) {
			AddNewButton(specializedControls, tf.UIAction);
		}
	}

	private void AddNewButton(HBoxContainer row, string action) {
		TextureButton button = new();
		button.Hide();
		TextureLoader.SetButtonTextures(button, "ui.unit_control." + action);
		button.Pressed += () => {
			EmitSignal(SignalName.ActionRequested, action);
			button.ReleaseFocus();
		};

		// Add a tooltip to the button to explain what it does, and ensure that
		// the tooltip is readable.
		string? tooltipText = C7Action.ToTooltip(action);
		if (tooltipText != null) {
			string? keys = KeyHint(action);
			button.TooltipText = keys == null ? tooltipText : $"{tooltipText} ({keys})";

			var customTheme = new Theme();
			customTheme.SetStylebox("panel", "TooltipPanel", TemporaryPopup.TooltipStyleBox());
			customTheme.SetColor("font_color", "TooltipLabel", Colors.White);
			button.Theme = customTheme;
		}

		row.AddChild(button);
		buttonMap[action] = new ButtonAndTooltip(button, button.TooltipText);
	}

	// The keys for `action` the way Civ3 writes them ("Shift-R"), from the
	// input map.
	private static string? KeyHint(string action) {
		// Game.HandleKeyboardInput handles G itself (Ctrl-G toggles the grid).
		if (action == C7Action.UnitGoto) {
			return "G";
		}
		if (!InputMap.HasAction(action)) {
			return null;
		}
		List<string> keys = InputMap.ActionGetEvents(action).OfType<InputEventKey>().Select(KeyName).ToList();
		return keys.Count > 0 ? string.Join(", ", keys) : null;
	}

	private static string KeyName(InputEventKey key) {
		Key code = key.PhysicalKeycode != Key.None ? DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode) : key.Keycode;
		return (key.CtrlPressed ? "Ctrl-" : "") + (key.ShiftPressed ? "Shift-" : "") + (key.AltPressed ? "Alt-" : "") + OS.GetKeycodeString(code);
	}

	private void OnNoMoreAutoselectableUnits() {
		this.Visible = false;
	}

	private void OnNewUnitSelected(ParameterWrapper<MapUnit> wrappedMapUnit) {
		UpdateButtons(wrappedMapUnit.Value);
	}

	private void OnUnitMoved(ParameterWrapper<MapUnit> wrappedMapUnit) {
		UpdateButtons(wrappedMapUnit.Value);
	}

	private void UpdateButtons(MapUnit unit) {
		if (!unit.CanBeActive()) return;
		// Reset the visibility and tooltip whenever the unit changes.
		foreach (ButtonAndTooltip btt in buttonMap.Values) {
			btt.button.Visible = false;
			btt.button.TooltipText = btt.baseTooltip;
		}

		var unitActions = unit.GetAvailableActions().Select(C7Action.ToActionString);

		// Mark all the buttons corresponding to the unit's available actions
		// as visible. We do this rather than using the unit prototype's actions
		// so that we don't display buttons that do nothing - we don't want to
		// show the "road" button if we can't build a road, etc.
		foreach (string actionKey in unitActions) {
			if (buttonMap.TryGetValue(actionKey, out ButtonAndTooltip value)) {
				value.button.Visible = true;
			} else {
				log.Warning("Could not find button " + actionKey);
			}
		}
		// Civ3's preference: the advanced row of buttons, or not.
		if (!Civ3Preferences.Get(Civ3Preferences.ShowAdvancedUnitButtons)) {
			foreach (Control button in advancedControls.GetChildren().OfType<Control>()) {
				button.Visible = false;
			}
		}

		// Do the same thing for each available terraform, but update the button
		// tooltip to include how many turns it will take.
		foreach (Terraform terraform in unit.GetAvailableTerraforms()) {
			string actionKey = terraform.UIAction;
			if (buttonMap.TryGetValue(actionKey, out ButtonAndTooltip value)) {
				value.button.Visible = true;
				value.button.TooltipText = $"{value.baseTooltip} ({unit.TurnsToCompleteTerraform(terraform)} turns)";
			} else {
				log.Warning("Could not find button " + actionKey);
			}
		}

		this.Visible = true;
	}
}
