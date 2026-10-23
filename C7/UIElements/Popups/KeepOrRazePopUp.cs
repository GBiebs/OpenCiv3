using System;
using System.Collections.Generic;
using C7GameData;
using Godot;
using static AdvisorHead;

namespace C7.UIElements.Popups;

// Asks a conqueror whether to keep the city they've just captured or raze it,
// as Civ3 does. Keeping is selected, so confirming (or cancelling) keeps it.
public class KeepOrRazePopUp : InformationPopup {
	public KeepOrRazePopUp(ID controllerId, string message, Action raze)
		: base(controllerId, "Military Advisor", message, Advisor.Military, Mood.Happy, 400) {
		ButtonGroup buttons = new();
		this.buttonActions = new List<ButtonAction>() {
			new ButtonAction() {
				buttonGroup = buttons,
				message = "Great! Install a new governor.",
				action = () => { },
				pressed = true,
			},
			new ButtonAction() {
				buttonGroup = buttons,
				message = "We don't want it. Raze the city!",
				action = raze,
			},
		};
		this.hasConfirm = true;
		this.hasCancel = true;
	}
}
