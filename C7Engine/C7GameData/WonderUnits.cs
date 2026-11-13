using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData;

// The per-turn effect of wonders that hand out units: buildings like the
// Statue of Zeus and Knights Templar, which grant a unit every few turns.
// (Leonardo's Workshop halves upgrade costs instead; see MapUnit.UpgradeCost.)
public static class WonderUnits {
	private static readonly ILogger log = Log.ForContext(typeof(WonderUnits));

	// Called once per player as the turn advances.
	public static void DoPerTurnUpdates(Player player, GameData gameData) {
		if (player.defeated) {
			return;
		}
		ProduceFreeUnits(player, gameData);
	}

	// Buildings with a unitProduced (the Statue of Zeus, Knights Templar)
	// give their city's owner a unit of that type every unitFrequency turns,
	// as long as the building isn't obsolete for the owner.
	//
	// Assumptions: the count is kept per building (so a captured wonder keeps
	// counting for its new owner), a newly built wonder starts counting on the
	// turn after it is completed (so its first unit comes five turns later),
	// and the unit is made like a normally produced one, so it is a veteran
	// when the city has barracks. Civ3 data has nothing else for these
	// wonders: the Statue of Zeus's unhappiness effect from Civ2 is not part
	// of Conquests' rules.
	public static void ProduceFreeUnits(Player player, GameData gameData) {
		foreach (City city in player.cities.ToArray()) {
			foreach (CityBuilding cb in city.constructed_buildings.ToArray()) {
				Building building = cb.building;
				if (string.IsNullOrEmpty(building?.unitProducedName) || building.unitFrequency <= 0) {
					continue;
				}
				// Any building may produce units, and stops once obsolete.
				if (building.IsObsoleteFor(player)) {
					continue;
				}

				cb.turnsTowardFreeUnit++;
				if (cb.turnsTowardFreeUnit < building.unitFrequency) {
					continue;
				}

				UnitPrototype proto = gameData.unitPrototypes.Find(p => p.name == building.unitProducedName);
				if (proto == null) {
					log.Warning("{Building} produces unknown unit type {Unit}", building.name, building.unitProducedName);
					continue;
				}

				cb.turnsTowardFreeUnit = 0;
				log.Information("{Building} in {City} produced a free {Unit}", building.name, city, proto.name);
				city.AddUnit(proto, gameData);
				if (player.IsToldNews) {
					new MsgShowTemporaryPopup($"{building.name} in {city.name} has produced a free {proto.name}.", city.location, player).send();
				}
			}
		}
	}
}
