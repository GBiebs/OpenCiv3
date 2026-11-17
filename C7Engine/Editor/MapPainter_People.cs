using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;
using C7GameData;

namespace C7Engine.Editor;

// The city and unit brushes: Civ3's editor places a city or a unit for the
// active player with each click. Placing isn't the game's founding or
// building: no history is recorded, no huts open, no camps disperse. Each
// is undone with the action (EditHistory.Record).
public sealed partial class MapPainter {
	// Why the active player can't have a city on the tile, or null if they
	// can: on land a city can stand on, not on or next to another city (the
	// game's spacing), not among another player's units; the barbarians
	// have no cities.
	public static string CantPlaceCity(Tile tile, Player owner) {
		if (owner == null) {
			return "Choose a player first (Map > Set Active Player).";
		}
		if (owner.isBarbarians) {
			return "The barbarians have no cities.";
		}
		if (tile.overlayTerrainType.IsWater) {
			return "Cities are built on land.";
		}
		if (!tile.overlayTerrainType.allowCities) {
			return $"Cities can't be on {NameOf(tile.overlayTerrainType)}.";
		}
		if (tile.HasCity()) {
			return "A city stands here.";
		}
		if (tile.neighbors.Values.FirstOrDefault(n => n != null && n != Tile.NONE && n.HasCity()) is Tile next) {
			return $"Too close to {next.cityAtTile.name}.";
		}
		if (tile.unitsOnTile.Any(u => u.owner != owner)) {
			return "Another player's units are here.";
		}
		return null;
	}

	// The first of the civilization's city names no city has yet, as
	// Civ3's editor names a placed city.
	public string NextCityName(Player owner) {
		HashSet<string> taken = gameData.cities.Select(c => c.name).ToHashSet();
		return owner.civilization?.cityNames.FirstOrDefault(n => !taken.Contains(n)) ?? owner.GetNextCityName();
	}

	// Places a size 1 city for the player (its first city gets the palace),
	// taking the hut or barbarian camp off its tile. Returns it, or null if
	// it can't be placed.
	public City PlaceCity(Tile tile, Player owner, string name = null) {
		if (CantPlaceCity(tile, owner) != null) {
			return null;
		}
		History.Touch(tile);
		tile.hasGoodyHut = false;
		tile.hasBarbarianCamp = false;
		City city = new(tile, owner, name ?? NextCityName(owner), gameData.ids.CreateID("city"));
		if (!owner.cities.Any(c => c.IsCapital())) {
			city.capital = true;
			if (gameData.Buildings.Find(b => b.isCenterOfEmpire) is Building palace) {
				city.AddBuilding(palace);
			}
		}
		CityResident first = new() {
			city = city,
			nationality = owner.civilization,
			citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen),
		};
		city.AddCitizen(first);
		AddCity(city);
		// What the game's founding gives a city besides: its citizen at
		// work, something to build, corruption worked out.
		CityTileAssignmentAI.AssignNewCitizenToTile(gameData, first);
		city.SetItemBeingProduced(ChooseProducible.Choose(city, owner));
		owner.DoCorruptionCalculations(gameData);
		History.Record(() => TakeCity(city), () => AddCity(city), changesCities: true);
		Journal([tile]);
		return city;
	}

	private void AddCity(City city) {
		gameData.cities.Add(city);
		city.owner.cities.Add(city);
		city.location.cityAtTile = city;
		foreach (CityResident resident in city.residents) {
			if (resident.tileWorked != Tile.NONE && resident.tileWorked.personWorkingTile == null) {
				resident.tileWorked.personWorkingTile = resident;
			}
		}
		TileChangeJournal.RecordTerrainChange(city.location);
		gameData.UpdateTileOwners();
		gameData.InvalidateCachedTradeNetwork();
	}

	// Takes the city off the map, keeping it whole for a redo (its citizens
	// remember their tiles; the tiles forget them).
	private void TakeCity(City city) {
		gameData.cities.Remove(city);
		city.owner.cities.Remove(city);
		city.location.cityAtTile = null;
		foreach (CityResident resident in city.residents) {
			if (resident.tileWorked != Tile.NONE && resident.tileWorked.personWorkingTile == resident) {
				resident.tileWorked.personWorkingTile = null;
			}
		}
		TileChangeJournal.RecordTerrainChange(city.location);
		gameData.UpdateTileOwnersOnCityDestruction(city);
		gameData.InvalidateCachedTradeNetwork();
	}

	// Why a unit of the type can't stand on the tile for the player, or
	// null: land units on land, ships at sea or in a coastal city, aircraft
	// on land; not with another player's units or in their city.
	public static string CantPlaceUnit(Tile tile, UnitPrototype type, Player owner) {
		if (owner == null) {
			return "Choose a player first (Map > Set Active Player).";
		}
		if (tile.HasCity() && tile.cityAtTile.owner != owner) {
			return $"{tile.cityAtTile.name} is another player's.";
		}
		if (tile.unitsOnTile.Any(u => u.owner != owner)) {
			return "Another player's units are here.";
		}
		bool water = tile.overlayTerrainType.IsWater;
		if (type.IsSeaUnit()) {
			if (!water && !(tile.HasCity() && tile.NeighborsWater())) {
				return "Ships go at sea or in a coastal city.";
			}
		} else if (water) {
			return $"A {type.name} can't be at sea.";
		}
		return null;
	}

	// Places a unit of the type for the player, as a new unit (the rules'
	// default experience). Returns it, or null if it can't be placed.
	public MapUnit PlaceUnit(Tile tile, UnitPrototype type, Player owner) {
		if (CantPlaceUnit(tile, type, owner) != null) {
			return null;
		}
		MapUnit unit = gameData.SpawnUnit(owner, type, tile);
		History.Record(() => TakeUnit(unit), () => AddUnit(unit));
		Journal([tile]);
		return unit;
	}

	private void AddUnit(MapUnit unit) {
		unit.location.unitsOnTile.Add(unit);
		gameData.AddMapUnit(unit);
		unit.owner.units.Add(unit);
		TileChangeJournal.Record(unit.location);
	}

	private void TakeUnit(MapUnit unit) {
		unit.location.unitsOnTile.Remove(unit);
		gameData.RemoveMapUnit(unit);
		unit.owner.units.Remove(unit);
		TileChangeJournal.Record(unit.location);
	}
}
