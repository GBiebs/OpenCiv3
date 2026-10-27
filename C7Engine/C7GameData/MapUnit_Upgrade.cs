using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;

namespace C7GameData;

public partial class MapUnit {
	// What this unit would become if upgraded where it is, and the gold it
	// would cost, or null if it can't be upgraded here. As in Civ3: it must be
	// in one of our cities with the building that trains veterans of its kind
	// (Barracks for ground units, Harbor for ships, Airport for aircraft); it
	// becomes the best unit of its upgrade line the city could build (with
	// that city's resources); and it costs the difference in shields times the
	// rules' upgrade cost (3 gold a shield in Conquests), half that with a
	// wonder that makes upgrades cheaper (Leonardo's Workshop). Units in an
	// army can't be upgraded.
	public (UnitPrototype target, int cost)? UpgradeOffer() {
		if (!unitType.actions.Contains(UnitAction.Upgrade) || IsInArmy()) {
			return null;
		}
		City city = location.cityAtTile;
		if (city == null || city.owner != owner || !CityTrainsMyKind(city)) {
			return null;
		}
		GameData gameData = EngineStorage.gameData;
		UnitPrototype target = unitType.GetProducibleUpgrade(city, city.GetAccessibleResources(gameData));
		if (target == null) {
			return null;
		}
		int cost = Math.Max(0, target.shieldCost - unitType.shieldCost) * gameData.rules.UpgradeCost;
		if (owner.GetActiveWonders().Any(w => w.Item2.building.HasWonderFlag(Save.SaveBuilding.WonderFlag.CheaperUpgrades))) {
			cost /= 2;
		}
		return (target, cost);
	}

	// Whether the city has the building that trains veterans of this unit's
	// kind (Barracks, Harbor, Airport), where it can upgrade and heal fully.
	private bool CityTrainsMyKind(City city) {
		return city.GetBuildings().Any(cb => !cb.building.IsObsolete(owner) && (
			(IsLandUnit() && cb.building.providesVeteranGroundUnits)
			|| (IsWaterUnit() && cb.building.providesVeteranSeaUnits)
			|| (IsAirUnit() && cb.building.providesVeteranAirUnits)));
	}

	// Whether this unit can be upgraded here now, with the gold we have.
	public bool CanUpgrade() {
		return UpgradeOffer() is (UnitPrototype, int cost) && cost <= owner.gold;
	}

	// Upgrades this unit where it is, paying for it. It keeps its experience,
	// nationality, damage and moves left, except that a unit that has
	// produced a great leader comes out a level lower (an elite one a veteran)
	// and can produce another once promoted again, as in Civ3. Returns false
	// if it couldn't.
	public bool Upgrade() {
		if (UpgradeOffer() is not (UnitPrototype target, int cost) || cost > owner.gold) {
			return false;
		}
		UnitPrototype from = unitType;
		owner.gold -= cost;
		unitType = target;
		name = target.name;
		if (hasProducedLeader) {
			hasProducedLeader = false;
			List<ExperienceLevel> levels = EngineStorage.gameData.experienceLevels;
			int level = levels.IndexOf(experienceLevel);
			if (level > 0) {
				experienceLevel = levels[level - 1];
				experienceLevelKey = experienceLevel.key;
			}
		}
		hitPointsRemaining = Math.Min(hitPointsRemaining, maxHitPoints);
		log.Information($"{owner} upgrades a {from.name} at {location} to a {target.name} for {cost} gold");
		return true;
	}
}
