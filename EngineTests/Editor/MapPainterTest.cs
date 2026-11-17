using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Editor;

// The map editor's terrain brush and its undo.
public class MapPainterTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public MapPainterTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	// A new scenario (100 by 100 tiles of ocean) as the editor has it.
	private (C7GameData.GameData, MapPainter) NewScenario() {
		SaveGame rules = SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave();
		C7GameData.GameData gameData = ScenarioEditing.Open(ScenarioFiles.NewScenario(rules), fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		return (gameData, new MapPainter(gameData));
	}

	private static TerrainBrush Brush(MapPainter painter, string name) => painter.TerrainBrushes().First(b => b.Name == name);

	private static void Stroke(MapPainter painter, TerrainBrush brush, int size, params Tile[] tiles) {
		painter.BeginStroke("Paint " + brush.Name);
		foreach (Tile tile in tiles) {
			painter.PaintTerrain(tile, size, brush);
		}
		painter.EndStroke();
	}

	// Civ3's editor's list: the rules' terrains, then its three variants.
	[Fact]
	public void TheBrushesAreTheRulesTerrainsAndCiv3sVariants() {
		(_, MapPainter painter) = NewScenario();
		List<string> names = painter.TerrainBrushes().Select(b => b.Name).ToList();
		Assert.Equal(["Desert", "Plains", "Grassland", "Tundra", "Flood Plain", "Hills", "Mountains", "Forest", "Jungle",
			"Marsh", "Volcano", "Coast", "Sea", "Ocean", "Bonus Grassland", "Snow-Capped Mountains", "Pine Forest"], names);
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(3, 9)]
	[InlineData(5, 25)]
	[InlineData(7, 49)]
	public void ABrushCoversItsSize(int size, int tiles) {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Stroke(painter, Brush(painter, "Plains"), size, gameData.map.tileAt(50, 50));
		Assert.Equal(tiles, gameData.map.tiles.Count(t => t.overlayTerrainType.IsPlains));
		Assert.Equal(tiles, MapPainter.TilesUnder(gameData.map.tileAt(50, 50), size).Count);
	}

	// Land painted in the ocean gets a coast around it, as Civ3's maps have
	// (only coast touches land), and is a continent of its own.
	[Fact]
	public void LandInTheOceanGetsACoast() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile island = gameData.map.tileAt(50, 50);
		int oceanContinent = island.continent;
		Stroke(painter, Brush(painter, "Grassland"), 1, island);

		Assert.True(island.overlayTerrainType.IsGrassland);
		Assert.True(island.baseTerrainType.IsGrassland);
		Assert.All(island.neighbors.Values, n => Assert.True(n.overlayTerrainType.IsCoast, $"{n} isn't coast"));
		Assert.Equal(1 + 8, gameData.map.tiles.Count(t => !t.overlayTerrainType.IsOcean));
		Assert.NotEqual(island.neighbors[TileDirection.NORTH].continent, island.continent);
		Assert.Equal(1, gameData.map.continents.Count(c => c.Contains(island) && c.Count == 1));
		Assert.Equal(island.neighbors[TileDirection.NORTH].continent, gameData.map.tileAt(10, 10).continent);
		Assert.NotEqual(-1, oceanContinent);
	}

	// A hill, mountain, forest... keeps the land under it; on water it's on
	// grassland; a flood plain is on desert. The variants set Civ3's flags.
	[Fact]
	public void ATerrainOnLandKeepsTheLandUnderIt() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile a = gameData.map.tileAt(50, 50), b = gameData.map.tileAt(60, 60), c = gameData.map.tileAt(70, 70);
		Stroke(painter, Brush(painter, "Plains"), 1, a);
		Stroke(painter, Brush(painter, "Hills"), 1, a);
		Assert.True(a.overlayTerrainType.IsHills);
		Assert.True(a.baseTerrainType.IsPlains);

		Stroke(painter, Brush(painter, "Pine Forest"), 1, b);
		Assert.True(b.overlayTerrainType.IsForest);
		Assert.True(b.baseTerrainType.IsGrassland);
		Assert.True(b.isPineForest);
		Stroke(painter, Brush(painter, "Forest"), 1, b);
		Assert.False(b.isPineForest);

		Stroke(painter, Brush(painter, "Flood Plain"), 1, c);
		Assert.True(c.baseTerrainType.IsDesert);
		Stroke(painter, Brush(painter, "Bonus Grassland"), 1, c);
		Assert.True(c.isBonusShield);
		Assert.True(c.baseTerrainType.IsGrassland);
		Stroke(painter, Brush(painter, "Snow-Capped Mountains"), 1, c);
		Assert.True(c.isSnowCapped);
		Assert.False(c.isBonusShield);
	}

	// Water takes away what only land has: a river (on both sides of its
	// edge), a hut, a camp, a road, a starting location, and a resource the
	// water can't have.
	[Fact]
	public void WaterTakesAwayWhatOnlyLandHas() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile land = gameData.map.tileAt(50, 50);
		Stroke(painter, Brush(painter, "Grassland"), 5, land);
		Tile east = land.neighbors[TileDirection.NORTHEAST];
		land.riverNortheast = true;
		east.riverSouthwest = true;
		land.hasGoodyHut = true;
		land.hasBarbarianCamp = true;
		land.Resource = gameData.Resources.First(r => r.Key == "Horses");
		land.overlays.Add(gameData.terrainImprovements.First(i => i.key == Tile.TileOverlays.ROAD));
		gameData.map.startingLocations.Add(land);

		Stroke(painter, Brush(painter, "Coast"), 1, land);
		Assert.True(land.overlayTerrainType.IsCoast);
		Assert.False(land.riverNortheast);
		Assert.False(east.riverSouthwest);
		Assert.False(land.hasGoodyHut);
		Assert.False(land.hasBarbarianCamp);
		Assert.Empty(land.overlays.GetImprovements());
		Assert.DoesNotContain(land, gameData.map.startingLocations);
		Assert.Equal(Resource.NONE, land.Resource);

		// Undone, all of it comes back.
		painter.Undo();
		Assert.True(land.overlayTerrainType.IsGrassland);
		Assert.True(land.riverNortheast);
		Assert.True(east.riverSouthwest);
		Assert.True(land.hasGoodyHut);
		Assert.Single(land.overlays.GetImprovements());
		Assert.Contains(land, gameData.map.startingLocations);
		Assert.Equal("Horses", land.Resource.Key);
	}

	// A resource the new terrain allows stays; one it doesn't goes.
	[Fact]
	public void AResourceStaysOnlyWhereItsAllowed() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile tile = gameData.map.tileAt(50, 50);
		Stroke(painter, Brush(painter, "Grassland"), 1, tile);
		tile.Resource = gameData.Resources.First(r => r.Key == "Cattle");
		Stroke(painter, Brush(painter, "Plains"), 1, tile);
		Assert.Equal("Cattle", tile.Resource.Key);
		Stroke(painter, Brush(painter, "Mountains"), 1, tile);
		Assert.Equal(Resource.NONE, tile.Resource);
	}

	// A city keeps land under it, a land unit too, a ship water.
	[Fact]
	public void UnitsKeepTheirGround() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile land = gameData.map.tileAt(50, 50), sea = gameData.map.tileAt(10, 10);
		Stroke(painter, Brush(painter, "Grassland"), 1, land);
		land.unitsOnTile.Add(new MapUnit(ID.FromString("unit-1")) { unitType = gameData.unitPrototypes.First(p => p.name == "Warrior") });
		sea.unitsOnTile.Add(new MapUnit(ID.FromString("unit-2")) { unitType = gameData.unitPrototypes.First(p => p.name == "Galley") });

		Assert.NotNull(MapPainter.CantPaint(land, Brush(painter, "Ocean").Terrain));
		Assert.Null(MapPainter.CantPaint(land, Brush(painter, "Hills").Terrain));
		Assert.NotNull(MapPainter.CantPaint(sea, Brush(painter, "Desert").Terrain));
		Stroke(painter, Brush(painter, "Ocean"), 3, land);
		Assert.True(land.overlayTerrainType.IsGrassland);
		Assert.True(land.neighbors[TileDirection.NORTH].overlayTerrainType.IsCoast);
		Stroke(painter, Brush(painter, "Desert"), 1, sea);
		Assert.True(sea.overlayTerrainType.IsOcean);
	}

	// A stroke, however far it's dragged, is one step to undo; undo puts
	// back the terrain, the coasts and the art; redo does it again.
	[Fact]
	public void AStrokeIsUndoneAndRedoneWhole() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Dictionary<Tile, (TerrainType, TerrainType, Civ3ExtraInfo)> before = gameData.map.tiles.ToDictionary(t => t, t => (t.baseTerrainType, t.overlayTerrainType, t.ExtraInfo));

		Stroke(painter, Brush(painter, "Tundra"), 3, gameData.map.tileAt(50, 50), gameData.map.tileAt(52, 50), gameData.map.tileAt(54, 50));
		Dictionary<Tile, (TerrainType, TerrainType, Civ3ExtraInfo)> after = gameData.map.tiles.ToDictionary(t => t, t => (t.baseTerrainType, t.overlayTerrainType, t.ExtraInfo));
		Assert.Equal(1, painter.History.UndoCount);
		Assert.Equal(2, gameData.map.continents.Count);

		EditHistory.Step undone = painter.Undo();
		Assert.Equal("Paint Tundra", undone.Name);
		Assert.All(gameData.map.tiles, t => Assert.Equal(before[t], (t.baseTerrainType, t.overlayTerrainType, t.ExtraInfo)));
		Assert.Single(gameData.map.continents);
		Assert.False(painter.History.CanUndo);

		painter.Redo();
		Assert.All(gameData.map.tiles, t => Assert.Equal(after[t], (t.baseTerrainType, t.overlayTerrainType, t.ExtraInfo)));
		Assert.Equal(2, gameData.map.continents.Count);
		Assert.Contains(gameData.map.tileAt(55, 49), MapPainter.LookChanged(undone));
	}

	// A stroke that changes nothing (the terrain already there) isn't a step.
	[Fact]
	public void NothingChangedIsNoStep() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Stroke(painter, Brush(painter, "Ocean"), 7, gameData.map.tileAt(50, 50));
		Assert.False(painter.History.CanUndo);
		Assert.False(painter.History.Modified);
	}

	// The plan's measure: undo walks back 1,000 strokes.
	[Fact]
	public void UndoWalksBackAThousandStrokes() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		string[] terrains = ["Grassland", "Plains", "Desert", "Hills"];
		for (int i = 0; i < 1000; ++i) {
			Stroke(painter, Brush(painter, terrains[i % 4]), 1, gameData.map.tileAt(2 * (i % 40) + 10, 10 + 2 * (i / 40 % 30)));
		}
		Assert.Equal(1000, painter.History.UndoCount);
		while (painter.Undo() != null) { }
		Assert.All(gameData.map.tiles, t => Assert.True(t.overlayTerrainType.IsOcean));
		Assert.True(painter.History.CanRedo);
	}

	// What was painted is saved and opened again.
	[Fact]
	public void APaintedMapIsSavedAndOpenedAgain() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Stroke(painter, Brush(painter, "Grassland"), 5, gameData.map.tileAt(50, 50));
		Stroke(painter, Brush(painter, "Pine Forest"), 1, gameData.map.tileAt(50, 50));
		Stroke(painter, Brush(painter, "Hills"), 1, gameData.map.tileAt(51, 51));

		using TempDirectory dir = new();
		string path = dir.File("painted.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, new ScenarioInfo { customMap = true }), path);
		SaveGame opened = ScenarioFiles.ForEditing(SaveGame.Load(path, s => s));
		C7GameData.GameData again = ScenarioEditing.Open(opened, fixture.behaviors);

		Tile forest = again.map.tileAt(50, 50);
		Assert.True(forest.overlayTerrainType.IsForest);
		Assert.True(forest.isPineForest);
		Assert.True(again.map.tileAt(51, 51).overlayTerrainType.IsHills);
		Assert.True(again.map.tileAt(51, 51).baseTerrainType.IsGrassland);
		Assert.Equal(25, again.map.tiles.Count(t => !t.overlayTerrainType.IsWater));
		Assert.Equal(gameData.map.tiles.Count(t => t.overlayTerrainType.IsCoast), again.map.tiles.Count(t => t.overlayTerrainType.IsCoast));
		Assert.All(again.map.tiles, t => Assert.NotNull(t.ExtraInfo));
	}
}
