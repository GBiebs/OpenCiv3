using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;

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

// What Civ3's City Properties set: the name (or Auto), the owner, the size,
// the culture and the improvements and wonders the city starts with.
public sealed record CityFacts(string Name, bool AutoName, Player Owner, int Size, int Culture, List<Building> Buildings);

// What Civ3's Unit Properties set: the type, the owner, the name, the
// experience level, the AI strategy (null for its type's own) and "Use
// Civ-Specific King Unit".
public sealed record UnitFacts(UnitPrototype Type, Player Owner, string Name, ExperienceLevel Experience, string AiStrategy, bool CivSpecificKing);

public sealed partial class MapPainter {
	public CityFacts FactsOf(City city, ScenarioInfo info) {
		return new CityFacts(city.name, info?.autoNamedCities.Contains(city.id.ToString()) ?? false, city.owner, city.residents.Count,
			city.perPlayerCulture.GetValueOrDefault(city.owner), city.constructed_buildings.Select(b => b.building).ToList());
	}

	// Why the city can't have these facts, or null: a name, a size of at
	// least 1, no negative culture, an owner with cities (not the
	// barbarians); a great wonder in one city of the world, a small wonder
	// or the palace in one city of its owner's.
	public string CantEdit(City city, CityFacts facts) {
		if (!facts.AutoName && string.IsNullOrWhiteSpace(facts.Name)) {
			return "The city needs a name, or Auto.";
		}
		if (facts.Size < 1) {
			return "A city's size is at least 1.";
		}
		if (facts.Culture < 0) {
			return "Culture can't be below 0.";
		}
		if (facts.Owner == null || facts.Owner.isBarbarians) {
			return "The barbarians have no cities.";
		}
		if (city.location.unitsOnTile.FirstOrDefault(u => u.owner != facts.Owner) is MapUnit stranger) {
			return $"{ScenarioPlayers.NameOf(stranger.owner)}'s units are in the city.";
		}
		foreach (Building building in facts.Buildings) {
			if (building.IsGreatWonder() && gameData.cities.FirstOrDefault(c => c != city && c.constructed_buildings.Any(b => b.building == building)) is City other) {
				return $"{building.name} is already in {other.name}: a great wonder is in one city only.";
			}
			if ((building.isSmallWonder || building.isCenterOfEmpire)
				&& facts.Owner.cities.FirstOrDefault(c => c != city && c.constructed_buildings.Any(b => b.building == building)) is City mine) {
				return $"{building.name} is already in {mine.name}: one city of a player's has it.";
			}
		}
		return null;
	}

	// Gives the city the facts, as one action. Returns whether it did.
	public bool EditCity(City city, CityFacts facts, ScenarioInfo info) {
		if (CantEdit(city, facts) != null) {
			return false;
		}
		CityFacts before = FactsOf(city, info);
		History.Begin($"Edit {city.name}");
		Apply(city, facts, info);
		History.Record(() => Apply(city, before, info), () => Apply(city, facts, info), changesCities: true);
		History.End();
		return true;
	}

	private void Apply(City city, CityFacts facts, ScenarioInfo info) {
		if (!facts.AutoName || !string.IsNullOrWhiteSpace(facts.Name)) {
			city.name = facts.Name;
		}
		if (info != null) {
			info.autoNamedCities.Remove(city.id.ToString());
			if (facts.AutoName) {
				info.autoNamedCities.Add(city.id.ToString());
			}
		}
		if (city.owner != facts.Owner) {
			city.owner.cities.Remove(city);
			city.perPlayerCulture.Remove(city.owner);
			city.owner = facts.Owner;
			facts.Owner.cities.Add(city);
			foreach (CityResident resident in city.residents) {
				resident.nationality = facts.Owner.civilization;
			}
		}
		city.perPlayerCulture[city.owner] = facts.Culture;
		foreach (CityBuilding built in city.constructed_buildings.ToList()) {
			if (!facts.Buildings.Contains(built.building)) {
				city.RemoveBuilding(built);
			}
		}
		foreach (Building building in facts.Buildings) {
			if (!city.constructed_buildings.Any(b => b.building == building)) {
				city.AddBuilding(building);
			}
		}
		city.capital = city.constructed_buildings.Any(b => b.building.isCenterOfEmpire);
		while (city.residents.Count > facts.Size) {
			city.RemoveCitizens(1);
		}
		while (city.residents.Count < facts.Size) {
			CityResident resident = new() {
				city = city,
				nationality = city.owner.civilization,
				citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen),
			};
			city.AddCitizen(resident);
			CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
		TileChangeJournal.RecordTerrainChange(city.location);
		gameData.UpdateTileOwners();
		gameData.InvalidateCachedTradeNetwork();
		city.owner.DoCorruptionCalculations(gameData);
	}

	public static UnitFacts FactsOf(MapUnit unit) {
		return new UnitFacts(unit.unitType, unit.owner, unit.name, unit.experienceLevel, unit.aiStrategy, unit.useCivSpecificKing);
	}

	// The AI strategies a unit of the type can have: its type's (the rules'
	// AI strategy flags).
	public static List<SaveUnitPrototype.Flag> StrategiesOf(UnitPrototype type) {
		return type.flags.Where(f => f.ToString().StartsWith("AI")).OrderBy(f => f).ToList();
	}

	// Why the unit can't have these facts, or null: its type on its tile
	// (as the unit brush places), an owner sharing the tile with no one,
	// an AI strategy its type has.
	public string CantEdit(MapUnit unit, UnitFacts facts) {
		if (facts.Owner == null) {
			return "A unit needs an owner.";
		}
		Tile tile = unit.location;
		if (tile.unitsOnTile.Any(u => u != unit && u.owner != facts.Owner) || (tile.HasCity() && tile.cityAtTile.owner != facts.Owner)) {
			return "Another player has units or a city here.";
		}
		bool water = tile.overlayTerrainType.IsWater;
		if (facts.Type.IsSeaUnit() ? !water && !(tile.HasCity() && tile.NeighborsWater()) : water) {
			return $"A {facts.Type.name} can't be here.";
		}
		if (facts.AiStrategy != null && !StrategiesOf(facts.Type).Any(f => f.ToString() == facts.AiStrategy)) {
			return $"A {facts.Type.name} hasn't that AI strategy.";
		}
		return null;
	}

	public bool EditUnit(MapUnit unit, UnitFacts facts) {
		if (CantEdit(unit, facts) != null) {
			return false;
		}
		UnitFacts before = FactsOf(unit);
		History.Begin($"Edit {facts.Type.name}");
		Apply(unit, facts);
		History.Record(() => Apply(unit, before), () => Apply(unit, facts));
		History.End();
		return true;
	}

	private void Apply(MapUnit unit, UnitFacts facts) {
		unit.unitType = facts.Type;
		if (unit.owner != facts.Owner) {
			unit.owner.units.Remove(unit);
			unit.owner = facts.Owner;
			facts.Owner.units.Add(unit);
		}
		unit.name = facts.Name;
		unit.experienceLevel = facts.Experience;
		unit.experienceLevelKey = facts.Experience?.key;
		unit.hitPointsRemaining = unit.maxHitPoints;
		unit.aiStrategy = facts.AiStrategy;
		unit.useCivSpecificKing = facts.CivSpecificKing;
		TileChangeJournal.Record(unit.location);
	}
}

public sealed partial class MapPainter {
	// Why what's on the tile can't go to the player, or null: there's
	// something of someone else's (a city, units, a colony or outpost), and
	// the city could be theirs (City Properties' checks).
	public string CantReassign(Tile tile, Player player, ScenarioInfo info) {
		if (player == null) {
			return "Choose a player first (Map > Set Active Player).";
		}
		bool someoneElses = (tile.cityAtTile != null && tile.cityAtTile.owner != player) || tile.unitsOnTile.Any(u => u.owner != player)
			|| (tile.OwnedHolding() != null && tile.holdingOwner != player);
		if (!someoneElses) {
			return "Nothing here is someone else's.";
		}
		if (tile.cityAtTile is City city) {
			CityFacts facts = FactsOf(city, info) with { Owner = player };
			// Its units go too, so they don't stand in the way.
			if (player.isBarbarians) {
				return "The barbarians have no cities.";
			}
			foreach (Building building in facts.Buildings.Where(b => b.isSmallWonder || b.isCenterOfEmpire)) {
				if (player.cities.FirstOrDefault(c => c != city && c.constructed_buildings.Any(b => b.building == building)) is City mine) {
					return $"{building.name} is already in {mine.name}.";
				}
			}
		}
		return null;
	}

	// Civ3's Reassign: the tile's city, units and colony or outpost become
	// the player's, as one action.
	public EditHistory.Step Reassign(Tile tile, Player player, ScenarioInfo info) {
		if (CantReassign(tile, player, info) != null) {
			return null;
		}
		History.Begin($"Reassign to {ScenarioPlayers.NameOf(player)}");
		History.Touch(tile);
		foreach (MapUnit unit in tile.unitsOnTile.Where(u => u.owner != player).ToList()) {
			UnitFacts before = FactsOf(unit), after = before with { Owner = player };
			Apply(unit, after);
			History.Record(() => Apply(unit, before), () => Apply(unit, after));
		}
		if (tile.cityAtTile is City city && city.owner != player) {
			CityFacts before = FactsOf(city, info), after = before with { Owner = player };
			Apply(city, after, info);
			History.Record(() => Apply(city, before, info), () => Apply(city, after, info), changesCities: true);
		}
		if (tile.OwnedHolding() != null) {
			tile.holdingOwner = player;
		}
		Journal([tile]);
		return History.End();
	}
}

public sealed partial class MapPainter {
	// The scenario's own facts the painter keeps up to date: player starts
	// and the fog (the editor gives it its ScenarioInfo).
	public ScenarioInfo Info { get; set; } = new();

	// The player whose own starting location the tile is, or null.
	public Player PlayerStartAt(Tile tile) {
		PlayerStart start = Info.playerStarts.Find(s => s.x == tile.XCoordinate && s.y == tile.YCoordinate);
		return start == null ? null : gameData.players.Find(p => p.id.ToString() == start.player);
	}

	// Each player's own starting location, for the map and minimap.
	public IEnumerable<(Tile tile, Player player)> PlayerStarts() {
		foreach (PlayerStart start in Info.playerStarts) {
			Tile tile = Map.tileAt(start.x, start.y);
			Player player = gameData.players.Find(p => p.id.ToString() == start.player);
			if (tile != Tile.NONE && player != null) {
				yield return (tile, player);
			}
		}
	}

	// Why the player's starting location can't be on the tile, or null: as
	// an Any one (land, no hut or camp, no other start), and not the
	// barbarians'.
	public string CantPlaceStart(Tile tile, Player player) {
		if (player != null && player.isBarbarians) {
			return "The barbarians have no starting location.";
		}
		if (PlayerStartAt(tile) is Player other && other != player) {
			return $"{ScenarioPlayers.NameOf(other)}'s starting location is here.";
		}
		OverlayBrush start = OverlayBrushes().First(b => b.What == OverlayBrush.Kind.StartingLocation);
		return CantPlace(tile, start);
	}

	// Civ3's player starting location: the player's one start, moved here
	// if it had one. Returns whether it changed.
	public bool PlacePlayerStart(Tile tile, Player player) {
		if (CantPlaceStart(tile, player) != null || PlayerStartAt(tile) == player) {
			return false;
		}
		List<PlayerStart> before = Copy(Info.playerStarts);
		Info.playerStarts.RemoveAll(s => s.player == player.id.ToString());
		Info.playerStarts.Add(new PlayerStart { player = player.id.ToString(), x = tile.XCoordinate, y = tile.YCoordinate });
		List<PlayerStart> after = Copy(Info.playerStarts);
		History.Record(() => SetStarts(before), () => SetStarts(after));
		Journal([tile]);
		return true;
	}

	private void RemovePlayerStart(Tile tile) {
		List<PlayerStart> before = Copy(Info.playerStarts);
		Info.playerStarts.RemoveAll(s => s.x == tile.XCoordinate && s.y == tile.YCoordinate);
		List<PlayerStart> after = Copy(Info.playerStarts);
		History.Record(() => SetStarts(before), () => SetStarts(after));
	}

	private static List<PlayerStart> Copy(List<PlayerStart> starts) {
		return starts.Select(s => new PlayerStart { player = s.player, x = s.x, y = s.y }).ToList();
	}

	private void SetStarts(List<PlayerStart> starts) {
		Info.playerStarts.Clear();
		Info.playerStarts.AddRange(Copy(starts));
	}

	// --- Civ3's Fog of War tool ---

	private HashSet<(int, int)> revealed;
	private List<TileLocation> fogBefore;

	// Whether the tile is painted clear of fog (known to every player when
	// the game starts).
	public bool IsRevealed(Tile tile) {
		revealed ??= Info.revealedTiles.Select(t => (t.X, t.Y)).ToHashSet();
		return revealed.Contains((tile.XCoordinate, tile.YCoordinate));
	}

	// Paints fog on ("Place Fog") or off ("Remove Fog") the tiles a brush of
	// this size covers. Returns the tiles that changed.
	public List<Tile> PaintFog(Tile center, int size, bool reveal) {
		List<Tile> changed = [];
		foreach (Tile tile in TilesUnder(center, size)) {
			if (IsRevealed(tile) != reveal) {
				if (fogBefore == null) {
					// The whole stroke is one record, made with its first tile.
					fogBefore = [.. Info.revealedTiles];
					List<TileLocation> before = fogBefore;
					FogAfter after = new();
					fogAfter = after;
					History.Record(() => SetRevealed(before), () => SetRevealed(after.tiles));
				}
				if (reveal) {
					revealed.Add((tile.XCoordinate, tile.YCoordinate));
					Info.revealedTiles.Add(new TileLocation(tile));
				} else {
					revealed.Remove((tile.XCoordinate, tile.YCoordinate));
					Info.revealedTiles.RemoveAll(t => t.X == tile.XCoordinate && t.Y == tile.YCoordinate);
				}
				changed.Add(tile);
			}
		}
		Journal(changed);
		return changed;
	}

	private sealed class FogAfter {
		public List<TileLocation> tiles = [];
	}

	private FogAfter fogAfter;

	// Called as a stroke ends: what the fog became, for the redo.
	private void EndFog() {
		if (fogAfter != null) {
			fogAfter.tiles = [.. Info.revealedTiles];
		}
		fogBefore = null;
		fogAfter = null;
	}

	private void SetRevealed(List<TileLocation> tiles) {
		Info.revealedTiles.Clear();
		Info.revealedTiles.AddRange(tiles);
		revealed = null;
	}
}

public sealed partial class MapPainter {
	// Gives the scenario the Scenario tab's settings, as one undo step.
	// Returns why it couldn't, or null.
	public string EditScenario(ScenarioSettings settings) {
		if (settings.Problem() is string why) {
			return why;
		}
		ScenarioSettings before = ScenarioSettings.Of(gameData, Info);
		History.Begin("Scenario Properties");
		settings.ApplyTo(gameData, Info);
		History.Record(() => before.ApplyTo(gameData, Info), () => settings.ApplyTo(gameData, Info));
		History.End();
		return null;
	}
}
