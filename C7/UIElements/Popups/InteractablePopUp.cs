using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;
using static AdvisorHead;
using static Godot.BoxContainer;
using static Godot.Control;

namespace C7.UIElements.Popups;

public class InteractablePopUp {
	public int hSize { get; init; } // the custom minimum size (X)
	public LayoutPreset layoutPreset { get; init; }
	public Margins margins { get; init; }
	public ID controllerId { get; init; }
	protected Player controller { get; init; }
	public string header { get; init; }
	public string message { get; init; }
	public AdvisorGraphicsDetails advisorDetails { get; init; }
	public LineEditComponent lineEditComponent { get; init; }
	public DropdownComponent dropdownComponent { get; init; }
	public ListComponent listComponent { get; init; }
	public int contollerEraIndex { get; init; } = 0;
	public List<ButtonAction> buttonActions { get; init; }
	public bool hasConfirm { get; init; }
	public bool hasCancel { get; init; }
	// What closing it without choosing (its X, or Escape) means: for a
	// question the engine waits on, the answer it gets then (else it would
	// wait forever). Null: nothing.
	public Action cancelAction { get; init; }

	protected InteractablePopUp(ID controllerId, string header, LayoutPreset layoutPreset, Margins margins) {
		this.controllerId = controllerId;
		this.header = header;
		this.layoutPreset = layoutPreset;
		this.margins = margins ?? new Margins();

		Player player = null;
		EngineStorage.ReadGameData(data => {
			player = data.players.First(p => p.id == controllerId);
		});
		this.controller = player;
		this.contollerEraIndex = this.controller.EraIndex();
	}
}

public class ButtonAction {
	public string message { get; init; }
	public Action action { get; init; }
	public ButtonGroup buttonGroup { get; init; }
	public bool pressed { get; set; } = false;
}

public class LineEditComponent {
	public string label;
	public string placeholderText;
	public Action<string> callback;
}

// A drop-down list under the text (Civ3's DROPLINK); the popup's actions
// read `selected`, which follows the player's pick.
public class DropdownComponent {
	public List<string> items;
	public int selected;
}

// A list under the text where one click picks (or the arrow keys and
// Enter, or typing the first letters of an item); it scrolls once there
// are more than `visibleRows` items.
public class ListComponent {
	public List<string> items;
	// Shown greyed; can't be picked.
	public HashSet<int> disabled = [];
	public Action<int> onChosen;
	public int visibleRows = 12;
	// Against misclicks: a first click only marks an item, a second click
	// on it (or a double click) picks it. Enter and Space still pick the
	// marked one.
	public bool clickTwice;
}
