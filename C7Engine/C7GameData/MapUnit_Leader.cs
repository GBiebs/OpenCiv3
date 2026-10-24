using System.Linq;
using C7Engine;
using C7GameData.Save;

namespace C7GameData;

// Civ3's military great leaders: born when elite units win battles, and spent
// to build an army or to hurry a city's production.
public partial class MapUnit {
	// Whether this unit is at the highest experience level.
	public bool IsElite() {
		return EngineStorage.gameData.GetExperienceLevelAfter(experienceLevel) == null;
	}

	// Called when this unit has won a battle and killed `defeated`. As in
	// Civ3 (CivFanatics, "Creating and Using Leaders"), an elite unit that
	// beats another civ's unit has a one in 16 chance of producing a great
	// leader, one in 12 if its owner has a building that increases the chance
	// (the Heroic Epic), and half that when it was defending. Each unit
	// produces one leader at most, a player has one leader at a time, and
	// nobody gets one from fighting barbarians. The leader appears with the
	// unit and is named from its civ's list. Returns the leader, if any.
	internal MapUnit MaybeProduceLeader(MapUnit defeated, bool attacking) {
		GameData gameData = EngineStorage.gameData;
		Rules rules = gameData.rules;
		UnitPrototype leaderType = gameData.unitPrototypes.Find(u => u.name == rules.BattleCreatedUnitType);
		if (leaderType == null || owner.isBarbarians || defeated.owner.isBarbarians || hasProducedLeader || !IsElite()) {
			return null;
		}
		if (owner.units.Any(u => u.unitType == leaderType)) {
			return null;
		}
		if (GameData.rng.Next(LeaderChanceOneIn(attacking)) != 0) {
			return null;
		}

		hasProducedLeader = true;
		MapUnit leader = gameData.SpawnUnit(owner, leaderType, location);
		leader.name = owner.NextGreatLeaderName(leaderType);
		log.Information($"{this} produced the great leader {leader.name}");
		new MsgLeaderBorn(owner.id, location, leader.name).send();
		return leader;
	}

	// The chance, one in this many, that an elite win of ours produces a
	// great leader (see MaybeProduceLeader).
	internal int LeaderChanceOneIn(bool attacking) {
		Rules rules = EngineStorage.gameData.rules;
		bool increasedChance = owner.OwnsBuilding(b => b.HasWonderFlag(SaveBuilding.WonderFlag.IncreasesLeaderChance));
		int oneIn = increasedChance ? rules.IncreasedLeaderChanceOneIn : rules.LeaderChanceOneIn;
		return attacking ? oneIn : oneIn * 2;
	}

	// Whether this unit can hurry the production of the city it's in: it has
	// the order and moves left, and it's in one of our cities building an
	// improvement or a small wonder that isn't finished. Conquests' military
	// leaders can't hurry great wonders or units.
	public bool CanHurryProduction() {
		if (!unitType.actions.Contains(UnitAction.HurryProduction) || !movementPoints.canMove) {
			return false;
		}
		City city = location.cityAtTile;
		return city != null && city.owner == owner
			&& city.itemBeingProduced is Building building && !building.IsGreatWonder()
			&& city.shieldsStored < owner.ShieldCost(building);
	}

	// Fills the production box of the city this unit is in, so it completes
	// what it's building at the end of the turn, and uses the unit up.
	// Returns the city, or null if the unit can't hurry its production.
	public City HurryProduction() {
		if (!CanHurryProduction()) {
			return null;
		}
		City city = location.cityAtTile;
		city.SetStoredShields(owner.ShieldCost(city.itemBeingProduced));
		log.Information($"{this} hurries the {city.itemBeingProduced.name} in {city.name}");
		if (owner.isHuman) {
			new MsgShowTemporaryPopup($"{name} hurries the {city.itemBeingProduced.name} in {city.name}!", location).send();
		}
		RemoveFromPlay();
		return city;
	}
}
