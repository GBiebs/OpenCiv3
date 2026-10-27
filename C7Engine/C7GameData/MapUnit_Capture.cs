using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;

namespace C7GameData;

public partial class MapUnit {
	// Whether this unit can fight back when attacked. Units that can't
	// (workers, settlers, artillery, explorers and so on) don't fight: an enemy
	// moving onto them captures or destroys them instead.
	public bool CanDefendItself() {
		return CombatType(CombatRole.Defense).defense > 0;
	}

	// Whether an enemy that can capture units takes this unit when it moves
	// onto it unguarded, rather than destroying it: workers and settlers (who
	// become workers), and artillery, as in Civ3.
	public bool IsCapturable() {
		return IsLandUnit() && !CanDefendItself() && (unitType.isWorker || unitType.isSettler || unitType.bombard > 0);
	}

	// Whether this unit can take an enemy city by moving in (Civ3: "only
	// combat units can capture cities"; an army needs members). Barbarians
	// ransack instead.
	public bool CanTakeCities() {
		return IsLandUnit() && (CombatType(CombatRole.Attack).attack > 0 || (unitType.canCapture && !IsArmy()));
	}

	// Whether this unit can pillage the tile it's on: it has the order and
	// moves left, there's something to destroy, and it's not a city, nor the
	// land of a civ we're at peace with.
	public bool CanPillage() {
		if (!unitType.actions.Contains(UnitAction.Pillage) || !IsLandUnit() || !movementPoints.canMove) {
			return false;
		}
		if (location.HasCity() || location.overlays.NextToPillage().Count == 0) {
			return false;
		}
		Player landOwner = location.owningCity?.owner;
		return landOwner == null || landOwner == owner || !owner.IsAtPeaceWith(landOwner);
	}

	// Pillages the tile this unit is on, destroying the next improvements in
	// Civ3's order (see TileOverlays.NextToPillage); it takes the rest of the
	// unit's turn. The land's owner is told. Returns what was destroyed.
	public List<TerrainImprovement> Pillage() {
		if (!CanPillage()) {
			return [];
		}
		List<TerrainImprovement> destroyed = location.overlays.PillageNext();
		movementPoints.onConsumeAll();
		animate(AnimatedAction.ATTACK1);
		Player landOwner = location.owningCity?.owner;
		WarWeariness.OnImprovementsDestroyed(location, owner, destroyed.Count);
		log.Information($"{this} pillages {string.Join(" and ", destroyed.Select(i => i.key))} at {location} (land of {landOwner?.ToString() ?? "nobody"})");
		new MsgTilePillaged(location, owner.id, landOwner?.id, destroyed.Select(i => i.key).ToList()).send();
		return destroyed;
	}

	// Whether this unit can join the city it's in (Civ3's settlers and
	// workers): one of our cities, with room under its size limit for the
	// unit's population cost.
	public bool CanJoinCity() {
		if (!unitType.actions.Contains(UnitAction.JoinCity) || unitType.populationCost <= 0 || !movementPoints.canMove) {
			return false;
		}
		City city = location.cityAtTile;
		return city != null && city.owner == owner
			&& city.residents.Count + unitType.populationCost <= city.SizeLimit(EngineStorage.gameData);
	}

	// Adds this unit's population cost to the city it's in, as citizens of the
	// unit's nationality (so captured workers become foreign nationals), and
	// removes the unit. Returns the city, or null if the unit can't join it.
	public City JoinCity() {
		if (!CanJoinCity()) {
			return null;
		}
		GameData gameData = EngineStorage.gameData;
		City city = location.cityAtTile;
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		for (int i = 0; i < unitType.populationCost; i++) {
			CityResident resident = new() { city = city, nationality = nationality, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
		log.Information($"{this} joins {city.name}, now size {city.residents.Count}");
		RemoveFromPlay();
		return city;
	}

	// Called when this unit has moved onto `tile`. Any enemy units left there
	// can't defend themselves, so they're captured or destroyed, and their
	// owners are told.
	internal void CaptureUnitsOn(Tile tile) {
		List<MapUnit> enemies = tile.unitsOnTile.Where(u => u.owner != owner && !owner.IsAtPeaceWith(u.owner)).ToList();
		GameData gameData = EngineStorage.gameData;
		foreach (IGrouping<Player, MapUnit> byOwner in enemies.GroupBy(u => u.owner)) {
			List<string> captured = new();
			List<string> destroyed = new();
			foreach (MapUnit enemy in byOwner) {
				// Barbarians have no use for captives.
				if (enemy.IsCapturable() && unitType.canCapture && !owner.isBarbarians) {
					captured.Add(enemy.name);
					gameData.CaptureUnit(enemy, owner);
				} else {
					destroyed.Add(enemy.name);
					enemy.RemoveFromPlay();
				}
			}
			WarWeariness.OnUnitsLost(byOwner.Key, owner, captured.Count + destroyed.Count);
			log.Information($"{this} at {tile} captured {string.Join(", ", captured)} and destroyed {string.Join(", ", destroyed)} of {byOwner.Key}");
			new MsgUnitsCaptured(owner.id, byOwner.Key.id, tile, captured, destroyed).send();
		}
	}

	// Called when this unit has won a battle and killed `defeated`, attacking
	// or defending. A unit whose victories start a golden age (Civ3's unique
	// units) starts its civ's, if it hasn't had one; beating barbarians
	// doesn't count (Civilopedia "Golden Age").
	internal bool MaybeStartGoldenAge(MapUnit defeated) {
		return unitType.startsGoldenAge && !defeated.owner.isBarbarians && defeated.owner != owner
			&& owner.StartGoldenAge(EngineStorage.gameData);
	}

	// Called when this unit has won a battle and killed `defeated`. A unit
	// type with an enslave result (in Civ3, the Maya Javelin Thrower makes
	// Workers; the Man-O-War and the Privateer make ships of their own type)
	// then has a one in three chance (Rules.EnslaveChanceOneIn) of turning
	// the loser into a unit of that type, attacking or defending. The new
	// unit joins the winner's stack and
	// can move at once, and keeps the loser's nationality (enslaved workers are
	// foreign). Barbarians don't enslave. Returns the new unit, if any.
	internal MapUnit MaybeEnslave(MapUnit defeated) {
		UnitPrototype result = unitType.enslaveResult;
		if (result == null || owner.isBarbarians) {
			return null;
		}
		bool canStandHere = result.categories.Contains("Sea") ? location.IsWater() || location.HasCity() : location.IsLand();
		if (!canStandHere || GameData.rng.Next(Math.Max(1, EngineStorage.gameData.rules.EnslaveChanceOneIn)) != 0) {
			return null;
		}
		MapUnit slave = EngineStorage.gameData.SpawnUnit(owner, result, location, defeated.nationality ?? defeated.owner.civilization);
		log.Information($"{this} enslaved {defeated} of {defeated.owner}, who becomes {slave} at {location}");
		new MsgUnitEnslaved(owner.id, defeated.owner.id, location, unitType.name, defeated.unitType.name, result.name).send();
		return slave;
	}
}
