using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Pathing;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Outposts and colonies, the holdings a worker builds for its civ (see
// Tile_Holdings): an outpost lets its owner see around it and is used up by
// borders, a colony gives its owner the resource under it when a road joins
// it to a city, and is taken in by borders.
public class OutpostAndColonyTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Terraform outpost;
	private readonly TerrainImprovement road;

	public OutpostAndColonyTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler)).ToArray();
		us = civs[0];
		them = civs[1];
		outpost = gameData.Terraforms.Single(t => t.Name == "Outpost");
		road = gameData.terrainImprovements.Single(ti => ti.key == Tile.TileOverlays.ROAD);
		// Building an outpost needs Masonry.
		us.knownTechs.Add(outpost.RequiredTech);
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private UnitPrototype Worker => gameData.unitPrototypes.Single(p => p.name == "Worker");

	// Flat open land far from every unit and city, inside no one's borders.
	private Tile LonelyFlatLand(Func<Tile, bool> also = null) {
		return gameData.map.tiles.First(t => t.IsLand() && !t.IsImpassable() && t.overlayTerrainType.height < 2
			&& t.OwningPlayer() == null && !t.hasBarbarianCamp && !t.hasGoodyHut && !t.overlays.HasBeenImproved()
			&& t.GetTilesWithinRankDistance(5).All(n => n.unitsOnTile.Count == 0 && !n.HasCity() && n.OwningPlayer() == null)
			&& (also == null || also(t)));
	}

	private Tile BuildOutpostWithAWorker(Tile site) {
		MapUnit worker = gameData.SpawnUnit(us, Worker, site);
		Assert.Contains(outpost, worker.GetAvailableTerraforms());
		worker.PerformTerraformAction(outpost);
		if (worker.WorkerJob != null) {
			worker.PerformEndOfTurnAction();
		}
		// "Construction of the Outpost consumes the worker."
		Assert.DoesNotContain(worker, us.units);
		Assert.Empty(site.unitsOnTile);
		return site;
	}

	// The tiles exactly two steps from the site, in the square the outpost
	// sees.
	private static List<Tile> TwoAway(Tile site) {
		return site.GetTilesWithinTileSquare(2).Where(t => t.RankDistanceTo(site) == 2).ToList();
	}

	[Fact]
	public void AnOutpostShowsItsOwnerTheTilesTwoAway() {
		Tile site = LonelyFlatLand();
		List<Tile> twoAway = TwoAway(site);
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.All(twoAway, t => Assert.False(us.tileKnowledge.isActiveTile(t)));

		BuildOutpostWithAWorker(site);

		Assert.True(site.HasOutpost());
		Assert.Same(us, site.OutpostOwner());
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.All(twoAway, t => {
			Assert.True(us.tileKnowledge.isTileKnown(t));
			Assert.True(us.tileKnowledge.isActiveTile(t));
		});
		// The incremental bookkeeping agrees with working it out afresh.
		Assert.Equal(us.tileKnowledge.ComputeActiveTilesFromScratch().OrderBy(t => t.Id.ToString()),
			us.tileKnowledge.ActiveTiles().OrderBy(t => t.Id.ToString()));
		// "Outposts can only be used by the player who created them."
		them.tileKnowledge.RecomputeActiveTiles();
		Assert.All(twoAway, t => Assert.False(them.tileKnowledge.isActiveTile(t)));
	}

	[Fact]
	public void AnOutpostOnAMountainSeesFourTilesAway() {
		Tile site = LonelyFlatLand();
		site.overlayTerrainType = gameData.terrainTypes.Single(tt => tt.Key == "mountains");
		Assert.Equal(4, site.OutpostSightRange());
		site.overlayTerrainType = gameData.terrainTypes.Single(tt => tt.Key == "hills");
		Assert.Equal(3, site.OutpostSightRange());
		site.overlayTerrainType = gameData.terrainTypes.Single(tt => tt.Key == "grassland");
		Assert.Equal(2, site.OutpostSightRange());
	}

	[Fact]
	public void AnOutpostCantGoInAnotherCivsTerritoryOrOnAFortress() {
		City theirs = them.cities.FirstOrDefault() ?? CityInteractions.BuildCity(them.units.First(u => u.unitType.isSettler).location, them, "Theirs");
		Tile inTheirBorders = theirs.location.neighbors.Values.First(t => t.IsLand() && t.owningCity == theirs);
		Assert.False(inTheirBorders.CanTakeOutpost(us));
		Assert.True(inTheirBorders.CanTakeOutpost(them));

		Tile site = LonelyFlatLand();
		Assert.True(site.CanTakeOutpost(us));
		site.overlays.Add(gameData.terrainImprovements.Single(ti => ti.key == Tile.TileOverlays.FORTRESS));
		Assert.False(site.CanTakeOutpost(us));
	}

	[Fact]
	public void ARivalsBordersDestroyAnOutpost() {
		Tile site = BuildOutpostWithAWorker(LonelyFlatLand(t => t.neighbors.Values.Any(n => n.IsLand() && n.IsAllowCities())));
		Tile twoAway = TwoAway(site).First();
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.True(us.tileKnowledge.isActiveTile(twoAway));

		// "If an Outpost falls within the territory of any other
		// civilization, it will be destroyed."
		Tile citySite = site.neighbors.Values.First(n => n.IsLand() && n.IsAllowCities());
		CityInteractions.BuildCity(citySite, them, "Neighbor");

		Assert.False(site.HasOutpost());
		Assert.Null(site.holdingOwner);
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.False(us.tileKnowledge.isActiveTile(twoAway));
	}

	[Fact]
	public void OurOwnBordersKeepOurOutpost() {
		Tile site = BuildOutpostWithAWorker(LonelyFlatLand(t => t.neighbors.Values.Any(n => n.IsLand() && n.IsAllowCities())));
		Tile citySite = site.neighbors.Values.First(n => n.IsLand() && n.IsAllowCities());
		CityInteractions.BuildCity(citySite, us, "Home");

		Assert.Same(us, site.OutpostOwner());
	}

	[Fact]
	public void PillagingAnOutpostTakesAwayItsSight() {
		Tile site = BuildOutpostWithAWorker(LonelyFlatLand());
		Tile twoAway = TwoAway(site).First();

		site.overlays.Remove(site.overlays.ImprovementAtLayer(TerrainImprovement.Layer.Holdings));

		Assert.Null(site.holdingOwner);
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.False(us.tileKnowledge.isActiveTile(twoAway));
	}

	private TerrainImprovement Holding(string key) => gameData.terrainImprovements.Single(ti => ti.key == key);

	// A colony sees the 3x3 around it and a radar tower the 5x5, as Civ3's
	// saves mark them seen by structures (see Tile.HoldingSightRange); an
	// airfield sees nothing.
	[Theory]
	[InlineData(Tile.TileOverlays.COLONY, 1)]
	[InlineData(Tile.TileOverlays.RADAR_TOWER, 2)]
	[InlineData(Tile.TileOverlays.AIRFIELD, -1)]
	public void ColoniesAndRadarTowersSeeAroundThem(string key, int range) {
		Tile site = LonelyFlatLand();
		// On a hill too, unlike an outpost.
		site.overlayTerrainType = gameData.terrainTypes.Single(tt => tt.Key == "hills");
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.False(us.tileKnowledge.isActiveTile(site));

		site.PlaceHolding(Holding(key), us);
		us.tileKnowledge.RecomputeActiveTiles();

		Assert.Equal(range, site.HoldingSightRange());
		HashSet<Tile> seen = range < 0 ? [] : site.GetTilesWithinTileSquare(range).ToHashSet();
		foreach (Tile t in site.GetTilesWithinTileSquare(3).Where(Tile.IsTileValid)) {
			Assert.Equal(seen.Contains(t), us.tileKnowledge.isActiveTile(t));
		}
		Assert.Equal(us.tileKnowledge.ComputeActiveTilesFromScratch().OrderBy(t => t.Id.ToString()),
			us.tileKnowledge.ActiveTiles().OrderBy(t => t.Id.ToString()));
		// Only its owner sees from it.
		them.tileKnowledge.RecomputeActiveTiles();
		Assert.False(them.tileKnowledge.isActiveTile(site));
	}

	[Fact]
	public void PillagingAColonyTakesAwayItsSight() {
		Tile site = LonelyFlatLand();
		site.PlaceHolding(Holding(Tile.TileOverlays.COLONY), us);
		Tile next = site.neighbors.Values.First(Tile.IsTileValid);
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.True(us.tileKnowledge.isActiveTile(next));

		site.overlays.Remove(site.overlays.ImprovementAtLayer(TerrainImprovement.Layer.Colonies));
		us.tileKnowledge.RecomputeActiveTiles();

		Assert.False(us.tileKnowledge.isActiveTile(next));
	}

	// A city of ours, a line of three land tiles leading away from it, and
	// iron on the last, outside the city's borders.
	private (City city, List<Tile> line, Resource iron) CityWithIronThreeAway() {
		Resource iron = gameData.Resources.Single(r => r.Key == "Iron");
		us.knownTechs.Add(iron.Prerequisite);
		foreach (Tile site in gameData.map.tiles) {
			if (!site.IsLand() || !site.IsAllowCities() || site.OwningPlayer() != null
				|| site.GetTilesWithinRankDistance(6).Any(n => n.HasCity() || n.OwningPlayer() != null || n.unitsOnTile.Count > 0)) {
				continue;
			}
			List<Tile> line = new();
			Tile t = site;
			for (int i = 0; i < 3; ++i) {
				t = t.neighbors[TileDirection.NORTHEAST];
				if (t == Tile.NONE || !t.IsLand() || t.IsImpassable()) {
					break;
				}
				line.Add(t);
			}
			if (line.Count < 3 || line[2].hasGoodyHut || line[2].hasBarbarianCamp) {
				continue;
			}
			City city = CityInteractions.BuildCity(site, us, "Ironton");
			if (line[2].OwningPlayer() != null) {
				continue;
			}
			line[2].Resource = iron;
			line[2].ResourceKey = iron.Key;
			gameData.InvalidateCachedTradeNetwork();
			return (city, line, iron);
		}
		throw new InvalidOperationException("no site for a city with iron three tiles away");
	}

	private bool CityHas(City city, Resource resource) {
		return gameData.GetTradeNetwork().GetResourcesAvailableToCity(us, city).ContainsKey(resource);
	}

	[Fact]
	public void ARoadedColonyGivesItsOwnerTheResource() {
		(City city, List<Tile> line, Resource iron) = CityWithIronThreeAway();
		Tile ironTile = line[2];
		foreach (Tile t in line) {
			t.overlays.Add(road);
		}
		// Outside our borders, the roaded iron isn't ours.
		Assert.False(CityHas(city, iron));

		MapUnit worker = gameData.SpawnUnit(us, Worker, ironTile);
		Assert.Contains(UnitAction.BuildColony, worker.GetAvailableActions());
		Assert.True(worker.BuildColony());

		// "The worker is consumed by building a colony."
		Assert.DoesNotContain(worker, us.units);
		Assert.Same(us, ironTile.ColonyOwner());
		Assert.True(CityHas(city, iron));
		Assert.True(gameData.GetTradeNetwork().HasResourceAccess(gameData, city.location, us, iron));
	}

	[Fact]
	public void AColonyWithoutARoadGivesNothing() {
		(City city, List<Tile> line, Resource iron) = CityWithIronThreeAway();
		line[0].overlays.Add(road);
		line[1].overlays.Add(road);

		MapUnit worker = gameData.SpawnUnit(us, Worker, line[2]);
		Assert.True(worker.BuildColony());
		Assert.False(CityHas(city, iron));

		// Roading the colony's tile joins it up.
		line[2].overlays.Add(road);
		Assert.True(CityHas(city, iron));
	}

	[Fact]
	public void AColonyNeedsATradableResourceOutsideEveryonesBorders() {
		(City city, List<Tile> line, Resource iron) = CityWithIronThreeAway();
		MapUnit worker = gameData.SpawnUnit(us, Worker, line[1]);
		// No resource.
		Assert.False(worker.CanBuildColony());
		// A bonus resource isn't tradable.
		line[1].Resource = gameData.Resources.First(r => r.Category == ResourceCategory.BONUS);
		Assert.False(worker.CanBuildColony());
		// Iron inside our borders needs no colony.
		line[0].Resource = iron;
		Assert.NotNull(line[0].OwningPlayer());
		worker.location.unitsOnTile.Remove(worker);
		worker.location = line[0];
		line[0].unitsOnTile.Add(worker);
		Assert.False(worker.CanBuildColony());
	}

	[Fact]
	public void BordersTakeInAColony() {
		(City city, List<Tile> line, Resource iron) = CityWithIronThreeAway();
		Tile ironTile = line[2];
		foreach (Tile t in line) {
			t.overlays.Add(road);
		}
		Assert.True(gameData.SpawnUnit(us, Worker, ironTile).BuildColony());
		Assert.True(CityHas(city, iron));

		// "When a city's borders expand to include a colony, the colony
		// disappears because it is no longer needed."
		Tile rivalSite = ironTile.neighbors.Values.First(n => n.IsLand() && n.IsAllowCities() && !line.Contains(n) && n.RankDistanceTo(city.location) > 1);
		CityInteractions.BuildCity(rivalSite, them, "Claimjumper");

		Assert.False(ironTile.HasColony());
		Assert.Null(ironTile.holdingOwner);
		Assert.False(CityHas(city, iron));
	}

	[Fact]
	public void OutpostsAndColoniesKeepTheirOwnersThroughASave() {
		Tile outpostSite = BuildOutpostWithAWorker(LonelyFlatLand());
		(City city, List<Tile> line, Resource iron) = CityWithIronThreeAway();
		Assert.True(gameData.SpawnUnit(us, Worker, line[2]).BuildColony());

		C7GameData.GameData reloaded = SaveGame.FromJSON(SaveGame.FromGameData(gameData).ToCompactJSON()).ToGameData(fixture.behaviors);
		Tile loadedOutpost = reloaded.map.tileAt(outpostSite.XCoordinate, outpostSite.YCoordinate);
		Tile loadedColony = reloaded.map.tileAt(line[2].XCoordinate, line[2].YCoordinate);
		Assert.True(loadedOutpost.HasOutpost());
		Assert.Equal(us.id, loadedOutpost.OutpostOwner()?.id);
		Assert.True(loadedColony.HasColony());
		Assert.Equal(us.id, loadedColony.ColonyOwner()?.id);
	}
}
