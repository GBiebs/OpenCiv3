using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;

namespace C7GameData;

public partial class MapUnit {
	// The unit type this unit can upgrade to right now, or null. Units upgrade
	// in their owner's cities: land units need barracks, ships a harbor and
	// aircraft an airport ("A city with an [Airport] can be used to upgrade
	// air units", Conquests Civilopedia, BLDG_Airport). The new type must be
	// one the city could build. Units inside an army can't be upgraded:
	// "Units in armies cannot be upgraded."
	// (https://civfanatics.com/civ3/strategy/introductory/the-basics-of-armies/).
	public UnitPrototype GetAvailableUpgrade() {
		if (!unitType.actions.Contains(UnitAction.Upgrade) || IsInArmy()) {
			return null;
		}

		City city = location?.cityAtTile;
		if (!City.IsValidCity(city) || city.owner != owner) {
			return null;
		}

		if (!HasVeteranBuildingFor(city)) {
			return null;
		}

		HashSet<Resource> resources = city.GetAvailableResources(EngineStorage.gameData).Keys.ToHashSet();
		return unitType.GetProducibleUpgrade(city, resources);
	}

	// Whether the city has (built, or granted by a wonder) the building that
	// trains this kind of unit: barracks for land units, a harbor for ships,
	// an airport for aircraft.
	private bool HasVeteranBuildingFor(City city) {
		bool land = IsLandUnit();
		bool water = IsWaterUnit();
		bool air = IsAirUnit();
		foreach (CityBuilding cb in city.EffectiveBuildings()) {
			if ((land && cb.building.providesVeteranGroundUnits)
				|| (water && cb.building.providesVeteranSeaUnits)
				|| (air && cb.building.providesVeteranAirUnits)) {
				return true;
			}
		}
		return false;
	}

	// The gold needed to upgrade to the given type: a fixed amount per shield
	// of difference in production cost, halved (rounding down) while the owner
	// has an active wonder with cheaper upgrades (Leonardo's Workshop).
	// The amount is the BIQ's (RULE's upgrade cost), 3 in Conquests: "To
	// upgrade a unit, the costs have gone from 2 * shield_difference to 3 *
	// shield_difference" and "Leonardo's Workshop still halves the upgrade
	// cost (to 1.5 * shield_difference)"
	// (https://forums.civfanatics.com/threads/civ3-conquests-additions-changes-list.104294/);
	// "Obsolete military units can be upgraded at half price" (Conquests
	// Civilopedia, BLDG_Inventor's_Workshop, Leonardo's Workshop).
	// UNVERIFIED: that half of an odd cost rounds down.
	public int UpgradeCost(UnitPrototype upgrade) {
		int shieldDifference = owner.ShieldCost(upgrade) - owner.ShieldCost(unitType);
		int cost = Math.Max(0, shieldDifference) * EngineStorage.gameData.rules.UpgradeCostPerShield;
		return HasCheaperUpgrades(owner) ? cost / 2 : cost;
	}

	private static bool HasCheaperUpgrades(Player player) {
		foreach ((City _, CityBuilding cb) in player.GetBuildingSnapshot().activeWonders) {
			if (cb.building.cheaperUpgrades) {
				return true;
			}
		}
		return false;
	}

	public bool CanUpgrade() {
		UnitPrototype upgrade = GetAvailableUpgrade();
		return upgrade != null && owner.gold >= UpgradeCost(upgrade);
	}

	// Upgrades the unit, keeping its experience except that an elite drops
	// to veteran (see ChangeTypeForUpgrade). Returns whether it upgraded.
	public bool Upgrade() {
		return UpgradeTo(GetAvailableUpgrade());
	}

	// Civ3's Upgrade All (Shift-U): "To use the Upgrade All order (SHIFT-U)
	// you must activate a unit. All units of this type will then be
	// upgraded." (Civ3 README). The units of the type, of the owner's, that
	// can upgrade where they are now, each with its upgrade and its cost.
	public static List<(MapUnit unit, UnitPrototype upgrade, int cost)> UpgradeAllCandidates(Player owner, UnitPrototype type) {
		List<(MapUnit, UnitPrototype, int)> result = new();
		if (owner == null || type == null) {
			return result;
		}
		foreach (MapUnit unit in owner.units) {
			if (unit.unitType != type || unit.location == null) {
				continue;
			}
			UnitPrototype upgrade = unit.GetAvailableUpgrade();
			if (upgrade != null) {
				result.Add((unit, upgrade, unit.UpgradeCost(upgrade)));
			}
		}
		return result;
	}

	// Upgrades every unit UpgradeAllCandidates lists, or none: Civ3 refuses
	// when the gold doesn't cover them all (script.txt
	// NO_GOLD_TO_UPGRADE_ALL, "We would need $NUM0 gold in order to
	// complete all the upgrades!"). Fortified units stay fortified ("Upgrade
	// all now leaves fortified units fortified", Civ3 README 1.21f). Returns
	// the units upgraded.
	public static List<MapUnit> UpgradeAll(Player owner, UnitPrototype type) {
		List<(MapUnit unit, UnitPrototype upgrade, int cost)> candidates = UpgradeAllCandidates(owner, type);
		if (candidates.Count == 0 || owner.gold < candidates.Sum(c => c.cost)) {
			return new();
		}
		List<MapUnit> upgraded = new();
		foreach ((MapUnit unit, UnitPrototype upgrade, int _) in candidates) {
			if (unit.UpgradeTo(upgrade)) {
				upgraded.Add(unit);
			}
		}
		return upgraded;
	}

	// Like Upgrade, for callers that already looked up GetAvailableUpgrade
	// (or an equivalent) and pass its result.
	// UpgradeTo itself refuses a unit in an army too, since some callers
	// (the AI's) work the upgrade out their own way.
	internal bool UpgradeTo(UnitPrototype upgrade) {
		if (upgrade == null || IsInArmy()) {
			return false;
		}
		int cost = UpgradeCost(upgrade);
		if (owner.gold < cost) {
			return false;
		}

		log.Information("Upgrading {Unit} to {Upgrade} for {Cost} gold", this, upgrade.name, cost);
		owner.gold -= cost;
		ChangeTypeForUpgrade(upgrade);
		// Upgrading ends the unit's turn. Using upgraded units that same turn
		// was reported as an exploit, possible only between turns, in Vanilla,
		// PTW and C3C (Scop: "You can upgrade units and use them the turn
		// they're upgraded", https://forums.civfanatics.com/threads/two-bugs-ive-discovered.73675/).
		movementPoints.onConsumeAll();
		return true;
	}

	// Turns this unit into the given type, without charging anything.
	//
	// Experience is kept, except that an elite becomes a veteran: "elites are
	// dowgraded to vets but all others hold their status" (Tone,
	// https://forums.civfanatics.com/threads/upgrade-or-disband.231677, #13);
	// "if you later upgrade the unit it goes back to being veteran and can
	// again be promoted to Elite" (CerberusIV,
	// https://codehappy.net/apolyton/threads/90368-1.htm).
	//
	// The unit may make a great leader again: "Each time a unit is upgraded,
	// however, it can once again produce a great leader" (a rule alexman
	// relays as confirmed by Mike B. of Firaxis,
	// https://codehappy.net/apolyton/threads/50260-1.htm).
	//
	// And it comes out fully healed: "there remains only one, rarely used,
	// way to heal even redlined troops in one turn to mention - to upgrade
	// them" (Pyrrhos, https://forums.civfanatics.com/threads/fortification.266551/, #9).
	private void ChangeTypeForUpgrade(UnitPrototype upgrade) {
		if (name == unitType.name) {
			name = upgrade.name;
		}
		unitType = upgrade;
		if (IsElite()) {
			List<ExperienceLevel> levels = EngineStorage.gameData.experienceLevels;
			int index = levels.IndexOf(experienceLevel);
			if (index > 0) {
				experienceLevel = levels[index - 1];
				experienceLevelKey = experienceLevel.key;
			}
		}
		hasProducedLeader = false;
		TileChangeJournal.Record(location);
		hitPointsRemaining = maxHitPoints;
	}
}
