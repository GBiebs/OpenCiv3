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

	// Where the buttons sit along the bottom, kept for Backspace to bring
	// them back to; null while they're there.
	private (Vector4 anchors, Vector4 offsets, GrowDirection h, GrowDirection v)? bottomPlace;

	// The columns the buttons move into, side by side, the primary ones
	// nearest the edge; made the first time. (Godot's HBoxContainer and
	// VBoxContainer can't change direction, so the buttons move.)
	private HBoxContainer columns;

	// Backspace, as in Civ3: the buttons in rows along the bottom, or in
	// columns down the left side.
	public void ToggleLayout() {
		HBoxContainer[] rows = [primaryControls, specializedControls, advancedControls];
		if (columns == null) {
			columns = new HBoxContainer { Visible = false, SizeFlagsVertical = SizeFlags.ExpandFill };
			AddChild(columns);
			foreach (HBoxContainer _ in rows) {
				columns.AddChild(new VBoxContainer { Alignment = AlignmentMode.Center });
			}
		}
		bool toColumns = bottomPlace == null;
		if (toColumns) {
			bottomPlace = (new Vector4(AnchorLeft, AnchorTop, AnchorRight, AnchorBottom),
				new Vector4(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom), GrowHorizontal, GrowVertical);
		}
		for (int i = 0; i < rows.Length; i++) {
			Container from = toColumns ? rows[i] : (Container)columns.GetChild(i);
			Container to = toColumns ? (Container)columns.GetChild(i) : rows[i];
			foreach (Node button in from.GetChildren()) {
				button.Reparent(to, false);
			}
			rows[i].Visible = !toColumns;
		}
		columns.Visible = toColumns;
		if (toColumns) {
			// Down the left side, between the menu buttons and the minimap.
			SetAnchorsPreset(LayoutPreset.LeftWide);
			(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom) = (0, 60, 0, -150);
			(GrowHorizontal, GrowVertical) = (GrowDirection.End, GrowDirection.Both);
		} else {
			(Vector4 a, Vector4 o, GrowDirection h, GrowDirection v) = bottomPlace.Value;
			(AnchorLeft, AnchorTop, AnchorRight, AnchorBottom) = (a.X, a.Y, a.Z, a.W);
			(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom) = (o.X, o.Y, o.Z, o.W);
			(GrowHorizontal, GrowVertical) = (h, v);
			bottomPlace = null;
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
		C7Action.UnitBuildAirfield,
		C7Action.UnitBuildRadarTower,
		C7Action.UnitBuildOutpost,
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

		//   ******* SPECIALIZED CONTROLS *************
		AddNewButton(specializedControls, C7Action.UnitLoad);
		AddNewButton(specializedControls, C7Action.UnitUnload);
		AddNewButton(specializedControls, C7Action.UnitAirlift);
		AddNewButton(specializedControls, C7Action.UnitPillage);
		AddNewButton(specializedControls, C7Action.UnitBombard);
		AddNewButton(specializedControls, C7Action.UnitAutoBombard);
		AddNewButton(specializedControls, C7Action.UnitAirdrop);
		AddNewButton(specializedControls, C7Action.UnitBombing);
		AddNewButton(specializedControls, C7Action.UnitPrecisionBombing);
		AddNewButton(specializedControls, C7Action.UnitRecon);
		AddNewButton(specializedControls, C7Action.UnitAirSuperiority);
		AddNewButton(specializedControls, C7Action.UnitRebase);
		AddNewButton(specializedControls, C7Action.UnitAutoBombing);
		AddNewButton(specializedControls, C7Action.UnitAutoPrecisionBombing);
		//superfortify?
		AddNewButton(specializedControls, C7Action.UnitHurryBuilding);
		AddNewButton(specializedControls, C7Action.UnitBuildArmy);
		AddNewButton(specializedControls, C7Action.UnitUpgrade);
		AddNewButton(specializedControls, C7Action.UnitSacrifice);
		AddNewButton(specializedControls, C7Action.UnitScienceAge);
		AddNewButton(specializedControls, C7Action.UnitBuildCity);
		AddNewButton(specializedControls, C7Action.UnitBuildColony);
		AddNewButton(specializedControls, C7Action.UnitJoinCity);

		SetUpTerraformButtons();

		AddNewButton(specializedControls, C7Action.UnitAutomate);

		// Civ3's other kinds of worker automation, shown with the "Show
		// Advanced Unit Action Buttons" preference.
		foreach ((string action, WorkerAutomation.Kind _) in C7Action.WorkerAutomations) {
			AddNewButton(advancedControls, action);
		}

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

	// The theme for the buttons' tooltips, shared by every button.
	private Theme tooltipTheme;

	private void AddNewButton(HBoxContainer row, string action) {
		TextureButton button = new();
		button.Hide();
		TextureLoader.SetButtonTextures(button, "ui.unit_control." + action);
		button.Pressed += () => { EmitSignal(SignalName.ActionRequested, action); };

		// Add a tooltip to the button to explain what it does, and ensure that
		// the tooltip is readable.
		string? tooltipText = C7Action.ToTooltip(action);
		if (tooltipText != null) {
			button.TooltipText = tooltipText;

			if (tooltipTheme == null) {
				tooltipTheme = new Theme();
				tooltipTheme.SetStylebox("panel", "TooltipPanel", TemporaryPopup.TooltipStyleBox());
				tooltipTheme.SetColor("font_color", "TooltipLabel", Colors.White);
			}
			button.Theme = tooltipTheme;
		}

		row.AddChild(button);
		buttonMap[action] = new ButtonAndTooltip(button, button.TooltipText);
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

	// The unit the buttons are for, to show them again when the
	// preference for the advanced ones changes.
	private MapUnit shownUnit;

	public override void _EnterTree() {
		Preferences.Changed += OnPreferenceChanged;
	}

	public override void _ExitTree() {
		Preferences.Changed -= OnPreferenceChanged;
	}

	private void OnPreferenceChanged(Preferences.Option option) {
		if (option == Preferences.Option.ShowAdvancedUnitActionButtons && shownUnit != null && Visible) {
			UpdateButtons(shownUnit);
		}
	}

	private void UpdateButtons(MapUnit unit) {
		if (!unit.CanBeActive()) return;
		shownUnit = unit;
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

		if (Preferences.Get(Preferences.Option.ShowAdvancedUnitActionButtons)) {
			foreach ((string action, WorkerAutomation.Kind kind) in C7Action.WorkerAutomations) {
				if (C7Action.CanAutomate(unit, kind) && buttonMap.TryGetValue(action, out ButtonAndTooltip value)) {
					value.button.Visible = true;
				}
			}
		}

		this.Visible = true;
	}
}
