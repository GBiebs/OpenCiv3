using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's Plant Forest (a Worker's job, with Engineering; 18 points of work
// times the terrain's movement cost, a Worker doing 2 a turn): on
// grassland, plains or tundra, not a city's tile, not over a resource a
// forest can't have. The tile becomes forest (pines on tundra); irrigation
// or a mine, which give nothing there, goes; a road stays.
public class PlantForestTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Tile field;
	private readonly MapUnit worker;
	private readonly Terraform plant;

	public PlantForestTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == "Engineering").id);
		// A land tile with no city or unit near.
		field = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0
			&& t.GetTilesWithinRankDistance(2).All(n => Tile.IsTileValid(n) && !n.HasCity()));
		SetTerrain(field, "grassland");
		worker = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Worker"), field);
		plant = C7Action.ToTerraform(C7Action.UnitPlantForest);
	}

	private TerrainType Terrain(string key) => gameData.terrainTypes.Find(t => t.Key == key);

	private void SetTerrain(Tile tile, string key) {
		tile.baseTerrainType = tile.overlayTerrainType = Terrain(key);
		tile.Resource = Resource.NONE;
		tile.ResourceKey = Resource.NONE.Key;
	}

	private TerrainImprovement Improvement(string key) => gameData.terrainImprovements.First(i => i.key == key);

	[Fact]
	public void AWorkerCanPlantOnGrasslandPlainsAndTundra() {
		Assert.NotNull(plant);
		Assert.Contains(plant, gameData.unitPrototypes.Find(u => u.name == "Worker").terraformActions);
		foreach (string key in new[] { "grassland", "plains", "tundra" }) {
			SetTerrain(field, key);
			Assert.True(worker.CanPerformTerraformAction(plant), key);
		}
	}

	[Fact]
	public void NotOnOtherTerrainOrAlreadyForest() {
		foreach (string key in new[] { "desert", "hills", "mountains", "flood plain" }) {
			SetTerrain(field, key);
			Assert.False(worker.CanPerformTerraformAction(plant), key);
		}
		field.baseTerrainType = Terrain("grassland");
		field.overlayTerrainType = Terrain("forest");
		Assert.False(worker.CanPerformTerraformAction(plant));
	}

	[Fact]
	public void NotWithoutEngineering() {
		us.knownTechs.Remove(gameData.techs.Find(t => t.Name == "Engineering").id);
		Assert.False(worker.CanPerformTerraformAction(plant));
	}

	[Fact]
	public void NotOverAResourceAForestCantHave() {
		Resource wheat = gameData.Resources.First(r => r.Name == "Wheat");
		field.Resource = wheat;
		field.ResourceKey = wheat.Key;
		Assert.False(worker.CanPerformTerraformAction(plant));
		Resource ivory = gameData.Resources.First(r => r.Name == "Ivory");
		SetTerrain(field, "plains");
		field.Resource = ivory;
		field.ResourceKey = ivory.Key;
		Assert.True(worker.CanPerformTerraformAction(plant));
	}

	// The job's end, through its Lua effect.
	[Fact]
	public void TheTileBecomesForestIrrigationGoesARoadStays() {
		field.overlays.Add(Improvement(Tile.TileOverlays.ROAD));
		field.overlays.Add(Improvement(Tile.TileOverlays.IRRIGATION));
		plant.OnComplete(us, field);
		Assert.Equal("forest", field.overlayTerrainType.Key);
		Assert.Equal("grassland", field.baseTerrainType.Key);
		Assert.Null(field.overlays.ImprovementAtLayer(TerrainImprovement.Layer.ResourceDevelopment));
		Assert.True(field.overlays.HasImprovement(Improvement(Tile.TileOverlays.ROAD)));
		Assert.False(worker.CanPerformTerraformAction(plant));
		Assert.True(worker.CanPerformTerraformAction(C7Action.ToTerraform(C7Action.UnitClearForest)));
	}

	[Fact]
	public void PinesOnTundra() {
		SetTerrain(field, "tundra");
		plant.OnComplete(us, field);
		Assert.Equal("forest", field.overlayTerrainType.Key);
		Assert.True(field.isPineForest);
	}

	// One Worker on grassland: 18 points at 2 a turn.
	[Fact]
	public void OneWorkerTakesNineTurnsOnGrassland() {
		Assert.Equal(9, worker.TurnsToCompleteTerraform(plant));
	}

	// A save from before planting worked: the job had no Lua and no unit.
	[Fact]
	public void AnOlderSaveGetsTheJob() {
		SaveGame older = fixture.saveGame.Clone();
		SaveTerraform job = older.TerraForms.Single(t => t.Name == "Plant Forest");
		job.Validators.Clear();
		job.Effects.Clear();
		older.UnitPrototypes.Find(u => u.name == "Worker").terraformActions.Remove(job.Id);

		C7GameData.GameData loaded = older.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(loaded);
		Player them = loaded.players.First(p => !p.isHuman && !p.isBarbarians);
		them.knownTechs.Add(loaded.techs.Find(t => t.Name == "Engineering").id);
		Tile tile = loaded.map.tileAt(field.XCoordinate, field.YCoordinate);
		tile.baseTerrainType = tile.overlayTerrainType = loaded.terrainTypes.Find(t => t.Key == "grassland");
		MapUnit theirs = loaded.SpawnUnit(them, loaded.unitPrototypes.Find(u => u.name == "Worker"), tile);
		Terraform loadedJob = loaded.Terraforms.Single(t => t.Name == "Plant Forest");
		Assert.True(theirs.CanPerformTerraformAction(loadedJob));
		loadedJob.OnComplete(them, tile);
		Assert.Equal("forest", tile.overlayTerrainType.Key);
	}

	[Fact]
	public void APlantedForestIsSaved() {
		SetTerrain(field, "tundra");
		plant.OnComplete(us, field);
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Tile loaded = reloaded.map.tileAt(field.XCoordinate, field.YCoordinate);
		Assert.Equal("forest", loaded.overlayTerrainType.Key);
		Assert.True(loaded.isPineForest);
	}
}
