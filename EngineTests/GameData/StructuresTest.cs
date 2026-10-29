using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7Engine.Pathing;
using C7GameData;
using C7GameData.AIData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;
using static C7GameData.Tile.TileOverlays;

namespace EngineTests.GameData;

// Structures a worker gives itself up to build (Civilopedia): colonies on
// resources outside anyone's borders, outposts that see far, airfields for
// aircraft, radar towers that strengthen nearby units. Each belongs to its
// builder; borders, pillage and other civs' territory take them away.
public class StructuresTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City home;
	private readonly City abroad;

	public StructuresTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		home = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Home");
		abroad = CityInteractions.BuildCity(them.units.First(u => u.unitType.isSettler).location, them, "Abroad");
		foreach (Tech t in gameData.techs) us.knownTechs.Add(t.id);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private Terraform Job(string key) => gameData.Terraforms.Find(t => t.Improvement?.key == key);

	private TerrainImprovement Improvement(string key) => gameData.terrainImprovements.Find(i => i.key == key);

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == type), tile);

	private Resource Strategic => gameData.Resources.First(r => r.Category == ResourceCategory.STRATEGIC
		&& !gameData.map.tiles.Any(t => t.Resource == r && t.OwningPlayer() != null));

	// Empty neutral land at least `distance` from both cities and from the
	// other tiles given, with land around it.
	private Tile Neutral(int distance = 6, params Tile[] awayFrom) {
		return gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.OwningPlayer() == null
			&& t.unitsOnTile.Count == 0 && !t.hasGoodyHut && !t.hasBarbarianCamp && t.Resource == Resource.NONE
			&& t.DistanceTo(home.location) >= distance && t.DistanceTo(abroad.location) >= distance
			&& awayFrom.All(a => t.DistanceTo(a) >= 6)
			&& t.neighbors.Values.Count(n => Tile.IsTileValid(n) && n.IsLand()) >= 6)
			.OrderBy(t => t.DistanceTo(home.location)).First();
	}

	private Tile Ours(int skip = 0) => home.location.neighbors.Values.Where(t => t.IsLand() && !t.HasCity() && t.OwningPlayer() == us).Skip(skip).First();

	private Tile Theirs() => abroad.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity() && t.OwningPlayer() == them);

	private void Place(Tile tile, string key, Player owner) {
		tile.overlays.Add(Improvement(key));
		Structures.OnBuilt(gameData, tile, owner);
	}

	[Fact]
	public void WorkersBuildThem() {
		UnitPrototype worker = gameData.unitPrototypes.Find(u => u.name == "Worker");
		foreach (string key in new[] { COLONY, OUTPOST, AIRFIELD, RADAR_TOWER }) {
			Assert.True(Structures.IsStructure(Improvement(key)), key);
			Assert.Contains(Job(key), worker.terraformActions);
		}
		Assert.False(Structures.IsStructure(Improvement(FORTRESS)));
	}

	[Fact]
	public void WhereEachMayStand() {
		MapUnit worker = Spawn(us, "Worker", home.location);
		bool Can(string key, Tile tile) => worker.CanPerformTerraformAction(Job(key), tile);
		Tile neutral = Neutral(), ours = Ours(), theirs = Theirs();

		// A colony: a luxury or strategic resource outside anyone's borders.
		Assert.False(Can(COLONY, neutral));
		neutral.Resource = Strategic;
		Assert.True(Can(COLONY, neutral));
		ours.Resource = Strategic;
		Assert.False(Can(COLONY, ours));
		neutral.Resource = gameData.Resources.First(r => r.Category == ResourceCategory.BONUS);
		Assert.False(Can(COLONY, neutral));

		// An outpost: neutral or our land. A radar tower: our land.
		Assert.True(Can(OUTPOST, neutral));
		Assert.True(Can(OUTPOST, ours));
		Assert.False(Can(OUTPOST, theirs));
		Assert.True(Can(RADAR_TOWER, ours));
		Assert.False(Can(RADAR_TOWER, neutral));
		Assert.False(Can(RADAR_TOWER, theirs));

		// An airfield: neutral or our land, with nothing but a road.
		Assert.True(Can(AIRFIELD, neutral));
		Assert.False(Can(AIRFIELD, theirs));
		Assert.True(Can(AIRFIELD, ours));
		ours.overlays.Add(Improvement(MINE));
		Assert.False(Can(AIRFIELD, ours));
		Assert.True(Can(OUTPOST, ours));

		// None where a fortress or another structure stands, or in a city.
		neutral.overlays.Add(Improvement(FORTRESS));
		Assert.False(Can(OUTPOST, neutral));
		Assert.False(Can(OUTPOST, home.location));
	}

	[Fact]
	public void BuildingOneUsesUpItsWorker() {
		Tile site = Neutral(), other = Neutral(6, site);
		MapUnit worker = Spawn(us, "Worker", site);
		MapUnit idle = Spawn(us, "Worker", site);
		MapUnit second = Spawn(us, "Worker", other);
		worker.PerformTerraformAction(Job(OUTPOST));
		second.PerformTerraformAction(Job(AIRFIELD));
		Assert.Contains(worker, us.units);

		TurnHandling.OnEndTurn(us);
		Assert.True(Structures.Has(site, OUTPOST, us));
		Assert.True(Structures.Has(other, AIRFIELD, us));
		Assert.DoesNotContain(worker, us.units);
		Assert.DoesNotContain(second, us.units);
		Assert.Contains(idle, us.units);
	}

	[Fact]
	public void TheyAreSaved() {
		Tile site = Neutral();
		Place(site, AIRFIELD, us);
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Tile loaded = reloaded.map.tileAt(site.XCoordinate, site.YCoordinate);
		Assert.Equal(AIRFIELD, Structures.On(loaded)?.key);
		Assert.Equal(us.id, loaded.overlays.structureOwner?.id);
	}

	[Fact]
	public void BordersTakeThemIn() {
		// A colony in our borders is no longer needed; in theirs it's lost.
		Tile ourColony = Neutral(), theirColony = Neutral(6, ourColony);
		ourColony.Resource = theirColony.Resource = Strategic;
		Place(ourColony, COLONY, us);
		Place(theirColony, COLONY, us);
		CityInteractions.BuildCity(ourColony.neighbors.Values.First(t => t.IsLand()), us, "Settled");
		Assert.Null(Structures.On(ourColony));
		Assert.Null(ourColony.overlays.structureOwner);
		CityInteractions.BuildCity(theirColony.neighbors.Values.First(t => t.IsLand()), them, "Rival");
		Assert.Null(Structures.On(theirColony));

		// Outposts and radar towers in another civ's territory are destroyed;
		// neutral land keeps them.
		Tile outpost = Neutral(6, ourColony, theirColony), tower = Ours();
		Place(outpost, OUTPOST, us);
		Place(tower, RADAR_TOWER, us);
		tower.owningCity = null;
		Structures.OnTerritoryChanged(gameData);
		Assert.True(Structures.Has(tower, RADAR_TOWER, us));
		tower.owningCity = abroad;
		Structures.OnTerritoryChanged(gameData);
		Assert.Null(Structures.On(tower));
		CityInteractions.BuildCity(outpost.neighbors.Values.First(t => t.IsLand()), them, "Watch");
		Assert.Null(Structures.On(outpost));

		// An airfield becomes theirs if they know Flight, else it's destroyed.
		Tile field = Neutral(6, ourColony, theirColony, outpost), other = Neutral(6, ourColony, theirColony, outpost, field);
		Place(field, AIRFIELD, us);
		Place(other, AIRFIELD, us);
		ID flight = Job(AIRFIELD).RequiredTech;
		them.knownTechs.Remove(flight);
		CityInteractions.BuildCity(field.neighbors.Values.First(t => t.IsLand()), them, "Field");
		Assert.Null(Structures.On(field));
		them.knownTechs.Add(flight);
		CityInteractions.BuildCity(other.neighbors.Values.First(t => t.IsLand()), them, "Other");
		Assert.True(Structures.Has(other, AIRFIELD, them));
	}

	[Fact]
	public void PillagingDestroysThem() {
		Tile site = Neutral();
		site.overlays.Add(Improvement(ROAD));
		Place(site, OUTPOST, us);
		site.overlays.PillageNext();
		Assert.True(Structures.Has(site, OUTPOST, us));
		site.overlays.PillageNext();
		Assert.Null(Structures.On(site));
		Assert.Null(site.overlays.structureOwner);
	}

	[Fact]
	public void RadarTowersStrengthenOurUnitsNearby() {
		Tile tower = Ours();
		Place(tower, RADAR_TOWER, us);
		MapUnit near = Spawn(us, "Spearman", tower.neighbors.Values.First(t => t.IsLand() && !t.HasCity()));
		Tile beyond = tower.GetTilesWithinTileSquare(3).First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& !tower.GetTilesWithinTileSquare(2).Contains(t));
		MapUnit far = Spawn(us, "Spearman", beyond);
		MapUnit stranger = Spawn(them, "Spearman", near.location);
		int Bonuses(MapUnit u, CombatRole role) => u.ListStrengthBonusesVersus(null, role, null).Count(b => b.description == "Radar tower");

		Assert.Equal(1, Bonuses(near, CombatRole.Attack));
		Assert.Equal(1, Bonuses(near, CombatRole.Defense));
		Assert.Equal(0, Bonuses(far, CombatRole.Defense));
		Assert.Equal(0, Bonuses(stranger, CombatRole.Defense));
		Assert.Equal(0.25, near.ListStrengthBonusesVersus(null, CombatRole.Attack, null).First(b => b.description == "Radar tower").amount);

		// Once, however many towers.
		Place(Ours(1) == tower ? Ours(2) : Ours(1), RADAR_TOWER, us);
		Assert.Equal(1, Bonuses(near, CombatRole.Defense));

		// The rules set the range and the bonus.
		us.rules.structures.radarRange = 3;
		us.rules.structures.radarBonusPercent = 50;
		Assert.Equal(1, Bonuses(far, CombatRole.Defense));
		Assert.Equal(0.5, near.ListStrengthBonusesVersus(null, CombatRole.Attack, null).First(b => b.description == "Radar tower").amount);
	}

	[Fact]
	public void OutpostsSeeAroundThem() {
		Tile site = Neutral(12);
		Assert.All(us.units, u => Assert.True(u.location.DistanceTo(site) > 8));
		site.overlayTerrainType = gameData.terrainTypes.Find(t => t.Key == "grassland");
		List<Tile> view = site.GetTilesWithinTileSquare(2).Where(Tile.IsTileValid).ToList();
		Tile beyond = site.GetTilesWithinTileSquare(3).First(t => Tile.IsTileValid(t) && !view.Contains(t));
		Assert.False(us.tileKnowledge.isActiveTile(site));

		Place(site, OUTPOST, us);
		Assert.All(view, t => Assert.True(us.tileKnowledge.isTileKnown(t) && us.tileKnowledge.isActiveTile(t)));
		Assert.False(us.tileKnowledge.isActiveTile(beyond));

		// Further from hills (and mountains).
		site.overlayTerrainType = gameData.terrainTypes.Find(t => t.Key == "hills");
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.True(us.tileKnowledge.isActiveTile(beyond));

		// Theirs don't show us anything.
		site.overlays.structureOwner = them;
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.False(us.tileKnowledge.isActiveTile(site));
	}

	[Fact]
	public void AColonyGivesItsResourceByRoad() {
		MapUnit walker = Spawn(us, "Worker", home.location);
		PathingAlgorithm pathing = PathingAlgorithmChooser.GetAlgorithm(walker);
		Tile site = gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.OwningPlayer() == null
				&& t.DistanceTo(home.location) is >= 4 and <= 8 && t.DistanceTo(abroad.location) >= 6 && t.Resource == Resource.NONE)
			.First(t => pathing.PathFrom(home.location, t, walker)?.PathLength() > 0);
		Resource resource = Strategic;
		site.Resource = resource;
		TilePath path = pathing.PathFrom(home.location, site, walker);
		while (path.PathLength() > 0) {
			path.Next().overlays.Add(Improvement(ROAD));
		}
		gameData.InvalidateCachedTradeNetwork();
		bool Has() => gameData.GetTradeNetwork().GetResourcesAvailableToCity(us, home).ContainsKey(resource);
		Assert.False(Has());

		Place(site, COLONY, us);
		Assert.True(Has());

		// Not by someone else's colony, and not without the road.
		site.overlays.structureOwner = them;
		gameData.InvalidateCachedTradeNetwork();
		Assert.False(Has());
		site.overlays.structureOwner = us;
		site.overlays.Remove(Improvement(ROAD));
		Assert.False(Has());
	}

	[Fact]
	public async Task AirfieldsAreAirBases() {
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		Tile field = Neutral();
		Assert.False(Air.CanFly(fighter, Air.Mission.Rebase, field));
		Place(field, AIRFIELD, us);
		Assert.True(Air.CanFly(fighter, Air.Mission.Rebase, field));
		await Air.Fly(gameData, fighter, Air.Mission.Rebase, field);
		Assert.Equal(field, fighter.location);
		field.overlays.structureOwner = them;
		Assert.False(Air.IsBase(fighter, field));
	}

	[Fact]
	public void TheAIMovesPlanesToItsAirfields() {
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		us.DeclareWarOn(them, gameData.turn);
		Tile field = gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.OwningPlayer() == null
				&& t.unitsOnTile.Count == 0 && t.DistanceTo(abroad.location) < home.location.DistanceTo(abroad.location) - 2)
			.OrderBy(t => t.DistanceTo(home.location)).First();
		Assert.Null(AirAI.FrontBase(bomber, us, [them]));
		Place(field, AIRFIELD, us);
		Assert.Equal(field, AirAI.FrontBase(bomber, us, [them]));
	}
}

// An AI civ's workers build colonies on resources it lacks.
public class StructuresAITest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City home;
	private readonly Tile site;
	private readonly Resource resource;

	public StructuresAITest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		home = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Home");
		foreach (Tech t in gameData.techs) us.knownTechs.Add(t.id);

		// One resource we lack, 3 tiles out; none other around.
		resource = gameData.Resources.First(r => r.Category == ResourceCategory.STRATEGIC);
		List<Tile> around = home.location.GetTilesWithinTileSquare(WorkerAI.ColonyRange).Where(Tile.IsTileValid).ToList();
		foreach (Tile t in around) {
			t.Resource = Resource.NONE;
		}
		MapUnit walker = Spawn("Worker", home.location);
		PathingAlgorithm pathing = PathingAlgorithmChooser.GetAlgorithm(walker);
		site = around.Where(t => t.IsLand() && !t.HasCity() && t.OwningPlayer() == null && t.unitsOnTile.Count == 0 && t.DistanceTo(home.location) == 3)
			.First(t => pathing.PathFrom(home.location, t, walker)?.PathLength() > 0);
		site.Resource = resource;
		walker.RemoveFromPlay();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private MapUnit Spawn(string type, Tile tile, Player owner = null) => gameData.SpawnUnit(owner ?? us, gameData.unitPrototypes.Find(u => u.name == type), tile);

	[Fact]
	public async Task TheAIColonizesAResourceItLacks() {
		MapUnit worker = Spawn("Worker", home.location);
		Spawn("Worker", home.location);
		WorkerAIData plan = WorkerAI.MakeAiData(worker, us);
		Assert.Equal(COLONY, plan.workerMove.Improvement?.key);
		Assert.Equal(site, plan.destination);

		// Another worker doesn't head for the same site.
		worker.currentAI = new WorkerAI(plan);
		Assert.NotEqual(site, WorkerAI.MakeAiData(Spawn("Worker", home.location), us)?.destination);

		// There, it roads the tile, then builds the colony.
		gameData.TeleportUnit(worker, site);
		worker.movementPoints.reset(worker.unitType.movement);
		await worker.currentAI.PlayTurn(us, worker);
		Assert.True(worker.WorkerJob?.ProvidesRoad());
		for (int turn = 0; turn < 20 && !site.IsRoaded(); turn++) {
			TurnHandling.OnEndTurn(us);
		}
		Assert.True(site.IsRoaded());
		worker.movementPoints.reset(worker.unitType.movement);
		await worker.currentAI.PlayTurn(us, worker);
		TurnHandling.OnEndTurn(us);
		Assert.True(Structures.Has(site, COLONY, us));
		Assert.DoesNotContain(worker, us.units);
	}

	[Fact]
	public void NotWhenItHasTheResourceOrOneWorkerOrForAHuman() {
		foreach (MapUnit start in us.units.Where(u => u.unitType.name == "Worker").ToList()) {
			start.RemoveFromPlay();
		}
		MapUnit worker = Spawn("Worker", home.location);
		Assert.NotEqual(COLONY, WorkerAI.MakeAiData(worker, us)?.workerMove.Improvement?.key);
		MapUnit second = Spawn("Worker", home.location);
		Assert.Equal(COLONY, WorkerAI.MakeAiData(worker, us)?.workerMove.Improvement?.key);

		home.location.Resource = resource;
		gameData.InvalidateCachedTradeNetwork();
		Assert.NotEqual(COLONY, WorkerAI.MakeAiData(worker, us)?.workerMove.Improvement?.key);
		home.location.Resource = Resource.NONE;
		gameData.InvalidateCachedTradeNetwork();

		// A human's automated workers don't give themselves up.
		us.isHuman = true;
		Assert.NotEqual(COLONY, WorkerAI.MakeAiData(worker, us)?.workerMove.Improvement?.key);
	}
}

// Civ3's workers' orders (PRTO) and the structures in the user's saves.
public class StructuresImportTest {
	[SkippableFact]
	public void WorkersGetTheirOrdersFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "9 WWII in the Pacific.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		ID Job(string key) => save.TerraForms.Single(t => t.Improvement == key).Id;
		int workers = 0;
		foreach (QueryCiv3.Biq.PRTO prto in BiqData.LoadFile(scenario).Prto) {
			SaveUnitPrototype unit = save.UnitPrototypes.Find(u => u.name == prto.Name);
			Assert.Equal(prto.BuildColony, unit.terraformActions.Contains(Job(COLONY)));
			Assert.Equal(prto.BuildOutpost, unit.terraformActions.Contains(Job(OUTPOST)));
			Assert.Equal(prto.BuildAirfield, unit.terraformActions.Contains(Job(AIRFIELD)));
			Assert.Equal(prto.BuildRadarTower, unit.terraformActions.Contains(Job(RADAR_TOWER)));
			workers += prto.BuildColony && prto.BuildOutpost && prto.BuildAirfield && prto.BuildRadarTower ? 1 : 0;
		}
		Assert.True(workers > 0);
	}

	// Structures and their owners come from the SAV's records.
	[SkippableTheory]
	[InlineData("claudia of the Japanese, 1942 AD.SAV")]
	[InlineData("GBiebs of the Romans, 148 AD.SAV")]
	public void SavesKeepThem(string name) {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string file = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Saves", name);
		Skip.If(!File.Exists(file), "The user's save isn't here.");
		SavData sav = new(QueryCiv3.Util.ReadFile(file), QueryCiv3.Util.ReadFile(PathUtils.defaultBicPath));
		SaveGame save = ImportCiv3.ImportSav(file, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);

		void Check(string key, IEnumerable<(int x, int y, int player)> records) {
			List<(int x, int y, int player)> list = records.ToList();
			Assert.Equal(list.Count, save.Map.tiles.Count(t => t.overlays.Contains(key)));
			foreach ((int x, int y, int player) in list) {
				SaveTile tile = save.Map.tiles.Find(t => t.X == x && t.Y == y);
				Assert.Contains(key, tile.overlays);
				Assert.Equal(save.Players[player].id, tile.structureOwner);
			}
		}
		Check(AIRFIELD, (sav.Aibs ?? []).Select(a => (a.XCoordinate, a.YCoordinate, a.PlayerID)));
		Check(OUTPOST, (sav.Outp ?? []).Select(a => (a.XCoordinate, a.YCoordinate, a.PlayerID)));
		Check(RADAR_TOWER, (sav.Radt ?? []).Select(a => (a.XCoordinate, a.YCoordinate, a.PlayerID)));
		Check(COLONY, (sav.Clny ?? []).Select(a => (a.XCoordinate, a.YCoordinate, a.PlayerID)));
		Assert.Equal(sav.Tile.Count(t => t.Barricade), save.Map.tiles.Count(t => t.overlays.Contains(BARRICADE)));
		Assert.Equal(sav.Tile.Count(t => t.Fortress && !t.Barricade), save.Map.tiles.Count(t => t.overlays.Contains(FORTRESS)));
		Assert.True(save.Map.tiles.Count(t => t.structureOwner != null) > 0);
	}
}
