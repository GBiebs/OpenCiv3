using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// Civ3's keys on the map that weren't in the input map: renaming a unit,
// upgrading all of a type, locating a city, the embassy and spy missions
// from the capital, re-basing a stack of planes, hiding the interface and
// moving the unit buttons. Their wording is Civ3's: labels.txt's command
// list, script.txt's popups and the Conquests Civilopedia's hotkey pages
// (GCON_Hotkeys_Units, GCON_Hotkeys_Controls).
public partial class Game {
	// Handles one of these keys; returns whether it was one.
	private bool HandleCiv3Key(InputEventKey key) {
		if (key.Echo || key.AltPressed) {
			return false;
		}
		bool shift = key.ShiftPressed;
		bool ctrl = key.IsCommandOrControlPressed();
		bool playerTurn = CurrentState == GameState.PlayerTurn;
		bool unitSelected = playerTurn && MapUnit.IsMapUnitValid(CurrentlySelectedUnit) && CurrentlySelectedUnit.owner == controller;

		switch (key.Keycode) {
			// "Re(n)ame unit (Shift-N)" (labels.txt).
			case Key.N when shift && !ctrl && unitSelected:
				ShowRenameUnitDialog(CurrentlySelectedUnit);
				return true;
			// "Upgrade all Obsolete Units Shift-U" (GCON_Hotkeys_Units).
			case Key.U when shift && !ctrl && unitSelected:
				ConfirmUpgradeAll(CurrentlySelectedUnit);
				return true;
			// "Locate City Shift-L" (GCON_Hotkeys_Controls).
			case Key.L when shift && !ctrl:
				ShowLocateCityPopup();
				return true;
			// "Establish Embassy Ctrl+E", "Plant Spy Ctrl+Shift+E", "Use
			// Embassy or Spy Shift+E" (GCON_Hotkeys_Controls).
			case Key.E when ctrl && !shift && playerTurn:
				ShowEstablishEmbassyPopup();
				return true;
			case Key.E when ctrl && shift && playerTurn:
				ShowPlantSpyPopup();
				return true;
			case Key.E when shift && !ctrl && playerTurn:
				ShowEspionageMissions();
				return true;
			// "Stack Go to X, J" (GCON_Hotkeys_Units): for a plane, a re-base
			// of the planes on its tile. Other units' stack go-to is in
			// HandleKeyboardInput.
			case Key.J when !shift && !ctrl && unitSelected
					&& CurrentlySelectedUnit.GetAvailableActions().Contains(UnitAction.Rebase):
				EngineStorage.ReadGameData((GameData gameData) => {
					setBombard(gameData.GetUnit(CurrentlySelectedUnit.id), UnitAction.Rebase);
					if (bombardInfo != null) {
						bombardInfo.wholeStack = true;
					}
				});
				return true;
			// Hides or shows the interface, leaving only the map.
			case Key.Delete when !shift && !ctrl:
				Control hud = GetNode<Control>("CanvasLayer/Control");
				hud.Visible = !hud.Visible;
				return true;
			// Moves the unit buttons between rows along the bottom and
			// columns down the left side.
			case Key.Backspace when !shift && !ctrl:
				GetNode<UnitButtons>("CanvasLayer/Control/UnitButtons").ToggleLayout();
				return true;
		}
		return false;
	}

	// script.txt RENAME_UNIT: "Rename $UNIT0?", "Name:".
	private void ShowRenameUnitDialog(MapUnit unit) {
		ID unitID = unit.id;
		popupOverlay.ShowPopup(new TextDialog($"Rename {unit.name}?", "Name: ", unit.name, BoxContainer.AlignmentMode.Begin,
			name => new MsgRenameUnit(unitID, name).send()), PopupOverlay.PopupCategory.Advisor);
	}

	// Upgrade All, in script.txt's words: NO_UNITS_TO_UPGRADE_ALL, "We have
	// not a single $UNIT0 which can be upgraded!"; NO_GOLD_TO_UPGRADE_ALL,
	// "We would need $NUM0 gold in order to complete all the upgrades!";
	// and UPGRADE_ALL, "Upgrade each $UNIT0 ($NUM0 total) for $NUM1 gold?"
	// with "Yes." and "No.", all from the military advisor.
	private void ConfirmUpgradeAll(MapUnit selected) {
		ID unitID = selected.id;
		string type = null;
		int count = 0, cost = 0, gold = 0;
		EngineStorage.ReadGameData((GameData gameData) => {
			MapUnit unit = gameData.GetUnit(unitID);
			if (unit == null) {
				return;
			}
			type = unit.unitType.name;
			List<(MapUnit unit, UnitPrototype upgrade, int cost)> candidates = MapUnit.UpgradeAllCandidates(unit.owner, unit.unitType);
			count = candidates.Count;
			cost = candidates.Sum(c => c.cost);
			gold = unit.owner.gold;
		});
		if (type == null) {
			return;
		}
		if (count == 0) {
			popupOverlay.ShowPopup(new InformationalPopup($"We have not a single {type} which can be upgraded!",
				AdvisorHead.Advisor.Military, AdvisorHead.Mood.Sad), PopupOverlay.PopupCategory.Advisor);
		} else if (gold < cost) {
			popupOverlay.ShowPopup(new InformationalPopup($"We would need {cost} gold in order to complete all the upgrades!",
				AdvisorHead.Advisor.Military, AdvisorHead.Mood.Sad), PopupOverlay.PopupCategory.Advisor);
		} else {
			popupOverlay.ShowPopup(new ConfirmationPopup($"Upgrade each {type} ({count} total) for {cost} gold?", "Yes.", "No.",
				() => new MsgUpgradeAll(unitID).send()) {
				advisor = AdvisorHead.Advisor.Military,
				mood = AdvisorHead.Mood.Happy,
			}, PopupOverlay.PopupCategory.Advisor);
		}
	}

	// Locate City (Shift-L; labels.txt "Locate City", "You have no
	// cities!"): our cities by name; picking one centres the map on it.
	private void ShowLocateCityPopup() {
		List<Tile> tiles = [];
		List<string> names = [];
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer() ?? controller;
			foreach (City city in (player?.cities ?? new List<City>()).OrderBy(c => c.name, StringComparer.CurrentCultureIgnoreCase)) {
				tiles.Add(city.location);
				names.Add(city.name);
			}
		});
		if (tiles.Count == 0) {
			popupOverlay.ShowPopup(new InformationalPopup("You have no cities!", AdvisorHead.Advisor.Domestic, AdvisorHead.Mood.Sad),
				PopupOverlay.PopupCategory.Info);
			return;
		}
		popupOverlay.ShowPopup(new ListPopup("Locate City", names, -1, index => mapView.centerCameraOnTile(tiles[index])) {
			clickTwice = true,
		}, PopupOverlay.PopupCategory.Info);
	}

	// The civs we know that a mission could be sent to, in the game's order.
	private static IEnumerable<Player> KnownRivals(GameData gameData, Player player) {
		return gameData.players.Where(p => p != player && !p.defeated && !p.IsIndependent && !p.isBarbarians
			&& PlayerRelationship.TryGetRelationship(player, p, out _));
	}

	// Establish Embassy (Ctrl-E): script.txt ESPIONAGE_START_MISSION_ESTABLISH_EMBASSY,
	// "Establish an embassy with whom?", from the Foreign Ministry, or
	// ESPIONAGE_NO_EMBASSIES_AT_THIS_TIME, "We cannot establish any new
	// embassies at this time." Each civ is listed with what its embassy
	// costs.
	private void ShowEstablishEmbassyPopup() {
		ShowCapitalMissionPopup(EspionageMission.EstablishEmbassy, "Establish an embassy with whom?",
			(gameData, player) => "We cannot establish any new embassies at this time.");
	}

	// Plant Spy (Ctrl-Shift-E): script.txt ESPIONAGE_START_MISSION_PLANT_MOLE,
	// "Where do you want the spy.", or ESPIONAGE_NO_CIVS_WITHOUT_MOLES, "We
	// have spies in every civilization we know." When the spy can't go for
	// another reason (no Intelligence Agency, say), that reason.
	private void ShowPlantSpyPopup() {
		ShowCapitalMissionPopup(EspionageMission.PlantSpy, "Where do you want the spy.", (gameData, player) => {
			List<Player> rivals = KnownRivals(gameData, player).ToList();
			Player withoutSpy = rivals.FirstOrDefault(r => !Espionage.HasSpy(player, r));
			return withoutSpy == null
				? "We have spies in every civilization we know."
				: Espionage.Unavailable(gameData, player, EspionageMission.PlantSpy, withoutSpy, null);
		});
	}

	// A mission against a civ's capital, picked from the civs it can be sent
	// to now; otherwise the reason it can't.
	private void ShowCapitalMissionPopup(EspionageMission mission, string question, Func<GameData, Player, string> noneReason) {
		List<ChoicePopup.Choice> choices = [];
		string reason = null;
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			if (player == null) {
				return;
			}
			foreach (Player rival in KnownRivals(gameData, player)) {
				if (Espionage.Unavailable(gameData, player, mission, rival, null) != null) {
					continue;
				}
				int cost = Espionage.Cost(gameData, player, mission, rival, null);
				choices.Add(new ChoicePopup.Choice($"{AdvisorUtils.CivTitle(rival)} ({cost} gold)",
					() => new MsgPerformEspionage(mission, rival, null).send()));
			}
			if (choices.Count == 0) {
				reason = noneReason(gameData, player);
			}
		});
		if (choices.Count > 0) {
			popupOverlay.ShowPopup(new ChoicePopup("Foreign Ministry", question, choices), PopupOverlay.PopupCategory.Advisor);
		} else if (reason != null) {
			popupOverlay.ShowPopup(new InformationalPopup(Embassies.Wrap(reason)) { header = "Foreign Ministry" },
				PopupOverlay.PopupCategory.Advisor);
		}
	}
}
