using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.AIData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI.UnitAI;

// Civ3's kinds of worker automation (see WorkerAutomation and
// WorkerAutomationAI): each plans only its own jobs, ends when they're done,
// and is saved with the worker.
public class WorkerAutomationTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public WorkerAutomationTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians && !p.isHuman && p.units.Any(u => u.unitType.isSettler));
		foreach (MapUnit u in us.units.Where(u => u.unitType.isWorker).ToList()) {
			u.RemoveFromPlay();
		}
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private UnitPrototype WorkerType => gameData.unitPrototypes.Single(p => p.name == "Worker");
	private TerrainType Terrain(string key) => gameData.terrainTypes.First(t => t.Key == key);
	private TerrainImprovement Improvement(string key) => gameData.terrainImprovements.First(i => i.key == key);

	private MapUnit Worker(Tile at) => gameData.SpawnUnit(us, WorkerType, at);

	private WorkerAIData PlanFor(MapUnit worker, WorkerAutomation how) {
		worker.workerAutomation = how;
		return WorkerAI.MakeAiData(worker, us);
	}

	// Plain grassland, with nothing on it.
	private void Make(Tile tile, string terrain, string overlay = null) {
		tile.baseTerrainType = Terrain(terrain);
		tile.overlayTerrainType = Terrain(overlay ?? terrain);
		tile.Resource = Resource.NONE;
		tile.ResourceKey = Resource.NONE.Key;
		tile.overlays.Clear();
	}

	// Our city, alone on its land, and a line of grassland tiles leading
	// north-east away from it.
	private (City city, List<Tile> line) CityWithLine(int length) {
		foreach (Tile site in gameData.map.tiles) {
			if (!site.IsLand() || !site.IsAllowCities() || site.OwningPlayer() != null
				|| site.GetTilesWithinRankDistance(length + 1).Any(n => n.HasCity() || n.OwningPlayer() != null || n.unitsOnTile.Count > 0
					|| n.hasGoodyHut || n.hasBarbarianCamp)) {
				continue;
			}
			List<Tile> line = new();
			Tile t = site;
			for (int i = 0; i < length; ++i) {
				t = t.neighbors[TileDirection.NORTHEAST];
				if (t == Tile.NONE || !t.IsLand() || t.IsImpassable()) {
					break;
				}
				line.Add(t);
			}
			if (line.Count < length) {
				continue;
			}
			// Plain grassland all around, so what there is to do is known.
			foreach (Tile l in site.GetTilesWithinRankDistance(length + 1).Where(n => n.IsLand() && n != site)) {
				Make(l, "grassland");
			}
			foreach (Tile known in site.GetTilesWithinRankDistance(length + 1)) {
				us.tileKnowledge.knownTiles.Add(known);
			}
			City city = CityInteractions.BuildCity(site, us, "Seat");
			gameData.InvalidateCachedTradeNetwork();
			return (city, line);
		}
		throw new InvalidOperationException("no site for a city with a line of land");
	}

	// Plans the kind's jobs one after the other, finishing each at once,
	// until there are none left (at most `max`).
	private List<WorkerAIData> PlanAll(MapUnit worker, WorkerAutomation how, int max = 20) {
		List<WorkerAIData> plans = new();
		for (WorkerAIData plan = PlanFor(worker, how); plan != null; plan = PlanFor(worker, how)) {
			plans.Add(plan);
			plan.workerMove.OnComplete(us, plan.destination);
			gameData.InvalidateCachedTradeNetwork();
			Assert.True(plans.Count <= max, $"more than {max} jobs");
		}
		return plans;
	}

	[Fact]
	public void RoadToRoadsEachTileOnTheWayAndThenHandsTheWorkerBack() {
		(City _, List<Tile> line) = CityWithLine(4);
		MapUnit worker = Worker(line[0]);
		WorkerAutomation how = new(WorkerAutomation.Kind.RoadTo, line[3]);

		List<WorkerAIData> plans = PlanAll(worker, how);
		Assert.All(plans, p => Assert.True(p.workerMove.ProvidesRoad()));
		Assert.Equal(line[0], plans[0].destination);
		Assert.Equal(line[3], plans[^1].destination);
		Assert.Equal(4, plans.Count);
		for (int i = 1; i < plans.Count; ++i) {
			Assert.Equal(1, plans[i].destination.DistanceTo(plans[i - 1].destination));
		}

		// With the road built, the order ends and the player has the worker.
		worker.Automate(new WorkerAutomation(WorkerAutomation.Kind.RoadTo, line[3]));
		Assert.False(worker.isAutomated);
		Assert.Null(worker.workerAutomation);
	}

	[Fact]
	public void RailToRoadsATileBeforeLayingTheRailroad() {
		(City _, List<Tile> line) = CityWithLine(3);
		Terraform rail = C7Action.ToTerraform(C7Action.UnitBuildRailroad);
		us.knownTechs.Add(rail.RequiredTech);
		rail.RequiredResources.Clear();
		MapUnit worker = Worker(line[0]);

		List<WorkerAIData> plans = PlanAll(worker, new WorkerAutomation(WorkerAutomation.Kind.RailTo, line[2]));
		Assert.Equal(6, plans.Count);
		for (int i = 0; i < plans.Count; i += 2) {
			Assert.True(plans[i].workerMove.ProvidesRoad());
			Assert.Equal(C7Action.UnitBuildRailroad, plans[i + 1].workerMove.UIAction);
			Assert.Equal(plans[i].destination, plans[i + 1].destination);
		}
		Assert.All(plans, p => Assert.True(p.destination.HasRailroad()));
	}

	[Theory]
	[InlineData(WorkerAutomation.Kind.ClearForest, "forest")]
	[InlineData(WorkerAutomation.Kind.ClearWetlands, "jungle")]
	[InlineData(WorkerAutomation.Kind.ClearWetlands, "marsh")]
	public void ClearingClearsOnlyThatAndOnlyInOurLand(WorkerAutomation.Kind kind, string overlay) {
		(City city, List<Tile> line) = CityWithLine(6);
		Assert.Null(line[5].OwningPlayer());
		Tile inside = line[0];
		Tile outside = line[5];
		Assert.Equal(us, inside.OwningPlayer());
		Assert.Null(outside.OwningPlayer());
		Make(inside, "grassland", overlay);
		Make(outside, "grassland", overlay);

		List<WorkerAIData> plans = PlanAll(Worker(city.location), new WorkerAutomation(kind));
		WorkerAIData plan = Assert.Single(plans);
		Assert.Equal(inside, plan.destination);
		Assert.Equal(new WorkerAutomation(kind).ClearingJob, plan.workerMove.UIAction);
		Assert.Equal(overlay, outside.overlayTerrainType.Key);
	}

	[Fact]
	public void ClearDamageCleansUpPollution() {
		(City city, List<Tile> line) = CityWithLine(2);
		line[1].overlays.Add(Improvement(Tile.TileOverlays.POLLUTION));
		Assert.True(line[1].HasDamage());
		// Pollution beyond our borders is left.
		line[0].overlays.Add(Improvement(Tile.TileOverlays.POLLUTION));
		Assert.Null(line[1].OwningPlayer());

		List<WorkerAIData> plans = PlanAll(Worker(city.location), new WorkerAutomation(WorkerAutomation.Kind.ClearDamage));
		WorkerAIData plan = Assert.Single(plans);
		Assert.Equal(line[0], plan.destination);
		Assert.False(line[0].HasDamage());
		Assert.True(line[1].HasDamage());
	}

	[Fact]
	public void ThisCityOnlyImprovesOnlyTheNearestCity() {
		(City seat, List<Tile> line) = CityWithLine(6);
		City other = CityInteractions.BuildCity(line[5], us, "Other");
		Assert.Same(other, WorkerAutomation.NearestCity(Worker(line[4])));
		// Given the order near the other city, and then sent home.
		MapUnit worker = Worker(seat.location);
		WorkerAutomation how = new(WorkerAutomation.Kind.ThisCityOnly, other.location);

		List<WorkerAIData> plans = PlanAll(worker, how, max: 100);
		Assert.NotEmpty(plans);
		Assert.All(plans, p => Assert.Same(other, p.destination.owningCity));
	}

	[Fact]
	public void NoAlteringNeverReplacesAnImprovement() {
		(City city, List<Tile> _) = CityWithLine(2);
		foreach (Tile t in city.location.neighbors.Values.Where(t => t.IsLand())) {
			Make(t, "grassland");
			t.overlays.Add(Improvement(Tile.TileOverlays.ROAD));
			t.overlays.Add(Improvement(Tile.TileOverlays.MINE));
		}
		MapUnit worker = Worker(city.location);
		WorkerAutomation how = new(WorkerAutomation.Kind.NoAltering);
		foreach (WorkerAIData plan in PlanAll(worker, how, max: 100)) {
			Assert.True(plan.destination.overlays.ImprovementAtLayer(TerrainImprovement.Layer.ResourceDevelopment)?.key
				is null or Tile.TileOverlays.MINE);
		}
		Assert.All(city.location.neighbors.Values.Where(t => t.IsLand()), t => Assert.True(t.overlays.HasImprovementWithKey(Tile.TileOverlays.MINE,
			TerrainImprovement.Layer.ResourceDevelopment)));
	}

	[Fact]
	public void PlainAutomationDoesReplaceThem() {
		// The other side of the test above: plain automation irrigates over
		// a mine, so No Altering is what keeps them.
		(City city, List<Tile> _) = CityWithLine(2);
		Tile mined = city.location.neighbors.Values.First(t => t.IsLand());
		foreach (Tile t in city.location.GetTilesWithinRankDistance(2).Where(t => t.IsLand() && !t.HasCity())) {
			Make(t, "grassland");
			t.overlays.Add(Improvement(Tile.TileOverlays.ROAD));
			t.overlays.Add(Improvement(Tile.TileOverlays.IRRIGATION));
		}
		mined.overlays.Add(Improvement(Tile.TileOverlays.MINE));
		Assert.NotNull(WorkerAI.MakeAiData(Worker(mined), us));
		Assert.Null(PlanFor(Worker(mined), new WorkerAutomation(WorkerAutomation.Kind.NoAltering)));
	}

	[Fact]
	public void BuildTradeJoinsACityCutOffFromTheCapital() {
		(City seat, List<Tile> line) = CityWithLine(5);
		Assert.True(seat.IsCapital());
		City cutOff = CityInteractions.BuildCity(line[4], us, "Cut Off");
		gameData.InvalidateCachedTradeNetwork();
		Assert.False(gameData.GetTradeNetwork().ConnectedToCapital(us, cutOff));

		List<WorkerAIData> plans = PlanAll(Worker(seat.location), new WorkerAutomation(WorkerAutomation.Kind.TradeNetwork));
		Assert.NotEmpty(plans);
		Assert.All(plans, p => Assert.True(p.workerMove.ProvidesRoad()));
		Assert.True(gameData.GetTradeNetwork().ConnectedToCapital(us, cutOff));
	}

	[Fact]
	public void IrrigateToCityBringsWaterToTheNearestCity() {
		(City city, List<Tile> line) = CityWithLine(5);
		// The water: an irrigated tile beyond the worker.
		line[4].overlays.Add(Improvement(Tile.TileOverlays.IRRIGATION));
		MapUnit worker = Worker(line[3]);
		Assert.Same(city, WorkerAutomation.NearestCity(worker));
		WorkerAutomation how = new(WorkerAutomation.Kind.IrrigateToCity, city.location);

		List<WorkerAIData> plans = PlanAll(worker, how);
		Assert.Equal(line[3], plans[0].destination);
		Assert.All(plans, p => Assert.Equal(C7Action.UnitIrrigate, p.workerMove.UIAction));
		Assert.Equal(1, plans[^1].destination.DistanceTo(city.location));
	}

	[Fact]
	public void TheKindIsSavedWithTheWorker() {
		(City _, List<Tile> line) = CityWithLine(4);
		MapUnit worker = Worker(line[0]);
		worker.Automate(new WorkerAutomation(WorkerAutomation.Kind.RoadTo, line[3]));
		Assert.True(worker.isAutomated);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).ToGameData(fixture.behaviors);
		MapUnit again = reloaded.GetUnit(worker.id);
		Assert.True(again.isAutomated);
		Assert.Equal(WorkerAutomation.Kind.RoadTo, again.workerAutomation?.kind);
		Assert.Equal(line[3].XCoordinate, again.workerAutomation.target.XCoordinate);
		Assert.Equal(line[3].YCoordinate, again.workerAutomation.target.YCoordinate);
	}

	[Fact]
	public void TakingTheWorkerBackOrPlainAutomationEndsTheKind() {
		(City city, List<Tile> line) = CityWithLine(4);
		line[0].overlays.Add(Improvement(Tile.TileOverlays.POLLUTION));
		MapUnit worker = Worker(city.location);
		worker.Automate(new WorkerAutomation(WorkerAutomation.Kind.ClearDamage));
		Assert.NotNull(worker.workerAutomation);
		worker.isAutomated = false;
		Assert.Null(worker.workerAutomation);

		worker.Automate(new WorkerAutomation(WorkerAutomation.Kind.ClearDamage));
		worker.Automate(null);
		Assert.Null(worker.workerAutomation);

		// A save from before the kinds has plain automation.
		SaveGame save = SaveGame.FromGameData(gameData);
		SaveUnit saved = save.Units.Single(u => u.id == worker.id);
		Assert.Null(saved.workerAutomation);
	}
}
