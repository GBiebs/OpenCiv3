using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.AIData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's kinds of worker automation (WorkerAutomation, planned by WorkerAI):
// road to a tile, clear only one thing, one city only, no alterations,
// connect the trade network; and the kind is saved.
public class WorkerAutomationTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City seat;

	public WorkerAutomationTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.isHuman = true;
		seat = CityInteractions.BuildCity(us.units[0].location, us, "Seat");
	}

	private TerrainType Terrain(string key) => gameData.terrainTypes.Find(t => t.Key == key);
	private TerrainImprovement Improvement(string key) => gameData.terrainImprovements.Find(i => i.key == key);

	private MapUnit Worker(Tile at) => gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Worker"), at);

	private WorkerAIData PlanFor(MapUnit worker, WorkerAutomation how) {
		worker.workerAutomation = how;
		return WorkerAI.MakeAiData(worker, us);
	}

	private static void Make(Tile tile, TerrainType terrain, TerrainType overlay = null) {
		tile.baseTerrainType = terrain;
		tile.overlayTerrainType = overlay ?? terrain;
		tile.Resource = Resource.NONE;
		tile.ResourceKey = Resource.NONE.Key;
		tile.overlays.Clear();
	}

	// Plain land tiles out from our city, in a line, for routes.
	private List<Tile> Line(int length) {
		Tile at = seat.location;
		List<Tile> line = [];
		TileDirection dir = seat.location.neighbors.First(n => Tile.IsTileValid(n.Value) && n.Value.IsLand()).Key;
		for (int i = 0; i < length && at.neighbors.TryGetValue(dir, out Tile next) && Tile.IsTileValid(next); i++) {
			Make(next, Terrain("grassland"));
			line.Add(next);
			at = next;
		}
		return line;
	}

	[Fact]
	public void RoadToRoadsEachTileOnTheWayThenHandsTheWorkerBack() {
		List<Tile> line = Line(4);
		Assert.Equal(4, line.Count);
		MapUnit worker = Worker(line[0]);
		WorkerAutomation how = new() { kind = WorkerAutomation.Kind.RoadTo, target = line[3] };

		List<Tile> roaded = [];
		for (WorkerAIData plan = PlanFor(worker, how); plan != null; plan = PlanFor(worker, how)) {
			Assert.True(plan.workerMove.ProvidesRoad());
			Assert.DoesNotContain(plan.destination, roaded);
			plan.destination.overlays.Add(Improvement("road"));
			roaded.Add(plan.destination);
			Assert.True(roaded.Count <= 4);
		}
		Assert.Equal(line, roaded);

		worker.Automate(how);
		Assert.False(worker.isAutomated);
		Assert.Null(worker.workerAutomation);
	}

	[Fact]
	public void ClearingOnlyClearsThatInOurLand() {
		Tile forest = seat.location.neighbors.Values.First(t => Tile.IsTileValid(t) && t.IsLand());
		Make(forest, Terrain("plains"), Terrain("forest"));
		WorkerAutomation how = new() { kind = WorkerAutomation.Kind.ClearOnly, job = C7Action.UnitClearForest };

		WorkerAIData plan = PlanFor(Worker(seat.location), how);
		Assert.NotNull(plan);
		Assert.Equal(C7Action.UnitClearForest, plan.workerMove.UIAction);
		Assert.Equal("forest", plan.destination.overlayTerrainType.Key);
		Assert.Equal(us, plan.destination.owningCity?.owner);
	}

	[Fact]
	public void OneCityOnlyStaysInThatCity() {
		Tile far = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && t.continent == seat.location.continent
			&& t.DistanceTo(seat.location) >= 8 && !t.HasCity() && t.unitsOnTile.Count == 0);
		City other = CityInteractions.BuildCity(far, us, "Other");
		WorkerAutomation how = new() { kind = WorkerAutomation.Kind.ThisCityOnly, target = other.location };

		WorkerAIData plan = PlanFor(Worker(seat.location), how);
		Assert.NotNull(plan);
		Assert.Contains(plan.destination, other.GetTilesWithinBorders().Concat(other.residents.Select(r => r.tileWorked)));
		Assert.DoesNotContain(plan.destination, seat.GetTilesWithinBorders());
	}

	[Fact]
	public void NoAlteringNeverReplacesAnImprovement() {
		// Mined grassland next to the city (the city is a water source for
		// irrigation, which would replace the mine).
		foreach (Tile t in seat.location.neighbors.Values.Where(t => Tile.IsTileValid(t) && t.IsLand())) {
			Make(t, Terrain("grassland"));
			t.overlays.Add(Improvement("road"));
			t.overlays.Add(Improvement("mine"));
		}
		MapUnit worker = Worker(seat.location);
		WorkerAutomation how = new() { kind = WorkerAutomation.Kind.NoAltering };
		for (int i = 0; i < 10 && PlanFor(worker, how) is WorkerAIData plan; i++) {
			Assert.Null(plan.destination.overlays.GetReplacementTarget(plan.workerMove));
			plan.destination.overlays.Add(plan.workerMove.Improvement);
		}
	}

	[Fact]
	public void TheTradeNetworkJoinsACityCutOff() {
		List<Tile> line = Line(5);
		City cutOff = CityInteractions.BuildCity(line[4], us, "Cut Off");
		Assert.False(gameData.GetTradeNetwork().ConnectedToCapital(us, cutOff));

		WorkerAIData plan = PlanFor(Worker(seat.location), new() { kind = WorkerAutomation.Kind.TradeNetwork });
		Assert.NotNull(plan);
		Assert.True(plan.workerMove.ProvidesRoad());
		Assert.Contains(plan.destination, line);
	}

	[Fact]
	public void TheKindIsSavedAndResumed() {
		List<Tile> line = Line(4);
		MapUnit worker = Worker(line[0]);
		worker.Automate(new WorkerAutomation() { kind = WorkerAutomation.Kind.RoadTo, target = line[3] });
		Assert.True(worker.isAutomated);
		Assert.Equal($"work:RoadTo@{line[3].XCoordinate},{line[3].YCoordinate}", worker.AutomationKind());

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(reloaded);
		MapUnit again = reloaded.GetPlayer(us.id).units.Single(u => u.id == worker.id);
		again.PlayAutomatedTurn();
		Assert.Equal(WorkerAutomation.Kind.RoadTo, again.workerAutomation?.kind);
		Assert.Equal(line[3].XCoordinate, again.workerAutomation.target.XCoordinate);
		Assert.Equal(line[3].YCoordinate, again.workerAutomation.target.YCoordinate);
	}
}
