using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData.Save;

namespace C7GameData;

// Civ3's armies (Conquests Civilopedia, "Armies"; upstream proposal #1011): an
// Army unit that up to three other units (four with the Pentagon) load into,
// to move and fight together. The members' hit points are pooled in the
// army's, and in battle the army fights with one member at a time, from the
// strongest down, each standing for its share of the pool. An army moves as
// fast as its slowest member plus one (the Conquests readme), and units can't
// leave it. Members are units loaded into the army (loadedOnUnitId); they
// can't act on their own, and they die with it.
public partial class MapUnit {
	public bool IsArmy() {
		return unitType.isArmy;
	}

	// The units loaded into this unit: an army's members, or a ship's cargo.
	public List<MapUnit> Passengers() {
		return location.unitsOnTile.Where(u => u.IsLoadedIn(this)).ToList();
	}

	// The unit this one is loaded into, or null.
	public MapUnit Container() {
		return loadedOnUnitId == null ? null : location.unitsOnTile.FirstOrDefault(u => u.id == loadedOnUnitId);
	}

	public bool IsInArmy() {
		return Container()?.IsArmy() ?? false;
	}

	// How many units this army can hold: its type's capacity, one more if its
	// owner has a building that allows larger armies (the Pentagon).
	public int ArmyCapacity() {
		bool larger = owner.OwnsBuilding(b => b.HasWonderFlag(SaveBuilding.WonderFlag.AllowsLargerArmies));
		return unitType.capacity + (larger ? 1 : 0);
	}

	// Whether `unit` may join this army: one of our land units that can
	// attack (Civ3: "only combat units are able to join armies"), not a
	// leader, an army or a unit already loaded somewhere, with room for it.
	// It must already be on the army's tile, so units join with the load
	// order, never by moving onto an army as they board a ship.
	public bool CanTakeIntoArmy(MapUnit unit) {
		return IsArmy() && unit.owner == owner && unit != this && unit.location == location
			&& unit.IsLoadable() && unit.IsLandUnit() && unit.unitType.attack > 0
			&& !unit.unitType.isArmy && !unit.unitType.isLeader && !unit.IsLoaded()
			&& Passengers().Count < ArmyCapacity();
	}

	// Puts `unit` into this army: it joins the army's pool of hit points and
	// can't act again on its own.
	internal void TakeIntoArmy(MapUnit unit) {
		bool wasEmpty = Passengers().Count == 0;
		unit.loadedOnUnitId = id;
		unit.isFortified = false;
		unit.isAutomated = false;
		unit.path = TilePath.NONE;
		unit.movementPoints.onConsumeAll();
		if (unit.currentAI != null) {
			unit.currentAI.UpdateOnDeath();
			unit.currentAI = null;
		}
		// An empty army's own hit points don't count once it has members.
		hitPointsRemaining = (wasEmpty ? 0 : hitPointsRemaining) + unit.hitPointsRemaining;
		log.Information($"{unit} joins {this}");
	}

	// The member of this army that fights at the army's current hit points:
	// members take turns from the strongest in `role` down, each standing for
	// as many of the pooled hit points as it has itself, so an army that is
	// barely scratched fights with its best unit (C3X calls this the army's
	// "HP-band rotation"). An empty army fights as itself.
	public MapUnit ArmyFighter(CombatRole role) {
		List<MapUnit> members = Passengers();
		if (!IsArmy() || members.Count == 0) {
			return this;
		}
		List<MapUnit> strongestFirst = members.OrderByDescending(m => m.unitType.BaseStrength(role)).ToList();
		int belowThisMember = members.Sum(m => m.maxHitPoints);
		foreach (MapUnit member in strongestFirst) {
			belowThisMember -= member.maxHitPoints;
			if (hitPointsRemaining > belowThisMember) {
				return member;
			}
		}
		return strongestFirst.Last();
	}

	// Called when this army has won a battle and killed `defeated`: its owner
	// now has a victorious army (which the Military Academy and the Heroic
	// Epic require), and the member that fought last may be promoted, adding
	// the hit point it gains to the army's.
	internal void ArmyWon(MapUnit defeated, CombatRole role) {
		if (!owner.isBarbarians) {
			owner.hasVictoriousArmy = true;
		}
		MapUnit fighter = ArmyFighter(role);
		if (fighter == this) {
			return;
		}
		int maxBefore = fighter.maxHitPoints;
		fighter.RollToPromote(defeated);
		hitPointsRemaining += fighter.maxHitPoints - maxBefore;
	}

	// The unit type whose strength this unit fights with: for an army, its
	// fighting member's (see ArmyFighter), otherwise its own.
	public UnitPrototype CombatType(CombatRole role) {
		return ArmyFighter(role).unitType;
	}

	// How far this unit moves in a turn: an army with members as fast as its
	// slowest member plus one (Conquests), anything else its type's movement.
	public int MovementPerTurn() {
		if (IsArmy()) {
			List<MapUnit> members = Passengers();
			if (members.Count > 0) {
				return members.Min(m => m.unitType.movement) + 1;
			}
		}
		return unitType.movement;
	}

	// Whether this leader can build an army here: it has the order and moves
	// left, it's in one of our cities, and we have the cities to support
	// another army (the rules' cities per army).
	public bool CanBuildArmy() {
		if (!unitType.actions.Contains(UnitAction.BuildArmy) || !movementPoints.canMove) {
			return false;
		}
		City city = location.cityAtTile;
		return city != null && city.owner == owner && ArmyType() != null && owner.CanSupportAnotherArmy();
	}

	internal static UnitPrototype ArmyType() {
		GameData gameData = EngineStorage.gameData;
		return gameData.unitPrototypes.Find(u => u.name == gameData.rules.ArmyUnitType);
	}

	// Turns this leader into an empty army in the city it's in. Returns the
	// army, or null if it can't build one.
	public MapUnit BuildArmy() {
		if (!CanBuildArmy()) {
			return null;
		}
		MapUnit army = EngineStorage.gameData.SpawnUnit(owner, ArmyType(), location);
		log.Information($"{this} builds {army} in {location.cityAtTile.name}");
		if (owner.isHuman) {
			new MsgShowTemporaryPopup($"{name} has formed an army in {location.cityAtTile.name}!", location).send();
		}
		RemoveFromPlay();
		return army;
	}
}
