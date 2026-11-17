using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
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
	private static Resource ResourceNamed(MapPainter painter, string name) => painter.ResourceBrushes().First(r => r.Name == name);

	private static EditHistory.Step Place(MapPainter painter, Resource resource, Tile tile) {
		painter.BeginStroke("Place " + resource.Name);
		painter.PlaceResource(tile, resource);
		return painter.EndStroke();
	}

	// Civ3's resource brush lists the rules' resources: Civ3's 22 (its
	// help's list), then any the rules add.
	[Fact]
	public void TheResourceBrushesAreTheRulesResources() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		List<string> names = painter.ResourceBrushes().Select(r => r.Name).ToList();
		Assert.Equal(gameData.Resources.Where(r => r.Category != ResourceCategory.NONE).Select(r => r.Name), names);
		Assert.Equal(["Horses", "Iron", "Saltpeter", "Coal", "Oil", "Rubber", "Aluminum", "Uranium",
			"Wines", "Furs", "Dyes", "Incense", "Spices", "Ivory", "Silks", "Gems",
			"Whales", "Game", "Fish", "Cattle", "Wheat", "Gold"], names.Take(22));
	}

	// A resource goes only on terrain the rules allow it on: fish on the
	// coast, not on grassland; cattle on grassland, not in the ocean.
	[Fact]
	public void AResourceIsPlacedOnlyWhereItsTerrainAllowsIt() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile land = gameData.map.tileAt(50, 50), ocean = gameData.map.tileAt(10, 10);
		Stroke(painter, Brush(painter, "Grassland"), 1, land);
		Tile coast = land.neighbors[TileDirection.NORTH];

		Assert.NotNull(MapPainter.CantPlace(land, ResourceNamed(painter, "Fish")));
		Assert.Null(Place(painter, ResourceNamed(painter, "Fish"), land));
		Assert.Equal(Resource.NONE, land.Resource);
		Assert.Null(Place(painter, ResourceNamed(painter, "Cattle"), ocean));

		Assert.NotNull(Place(painter, ResourceNamed(painter, "Cattle"), land));
		Assert.Equal("Cattle", land.Resource.Name);
		Assert.Equal(land.Resource.Key, land.ResourceKey);
		Assert.NotNull(Place(painter, ResourceNamed(painter, "Fish"), coast));
		Assert.Equal("Fish", coast.Resource.Name);
	}

	// A resource takes the place of another; undo brings the old one back;
	// the same resource again is no step.
	[Fact]
	public void AResourceReplacesAnotherAndIsUndone() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile land = gameData.map.tileAt(50, 50);
		Stroke(painter, Brush(painter, "Grassland"), 1, land);
		Place(painter, ResourceNamed(painter, "Cattle"), land);
		Assert.Null(Place(painter, ResourceNamed(painter, "Cattle"), land));
		Assert.Equal("Place Horses", Place(painter, ResourceNamed(painter, "Horses"), land).Name);
		Assert.Equal("Horses", land.Resource.Name);

		painter.Undo();
		Assert.Equal("Cattle", land.Resource.Name);
		Assert.Equal(land.Resource.Key, land.ResourceKey);
		painter.Undo();
		Assert.Equal(Resource.NONE, land.Resource);
		painter.Redo();
		painter.Redo();
		Assert.Equal("Horses", land.Resource.Name);
	}

	// Placed resources are saved and opened again.
	[Fact]
	public void PlacedResourcesAreSavedAndOpenedAgain() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Stroke(painter, Brush(painter, "Grassland"), 3, gameData.map.tileAt(50, 50));
		Place(painter, ResourceNamed(painter, "Wines"), gameData.map.tileAt(50, 50));
		Place(painter, ResourceNamed(painter, "Fish"), gameData.map.tileAt(50, 46));

		using TempDirectory dir = new();
		string path = dir.File("resources.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, new ScenarioInfo { customMap = true }), path);
		C7GameData.GameData again = ScenarioEditing.Open(ScenarioFiles.ForEditing(SaveGame.Load(path, s => s)), fixture.behaviors);

		Assert.Equal("Wines", again.map.tileAt(50, 50).Resource.Name);
		Assert.Equal("Fish", again.map.tileAt(50, 46).Resource.Name);
		Assert.Equal(2, again.map.tiles.Count(t => t.Resource != Resource.NONE && t.Resource.Category != ResourceCategory.NONE));
	}
	private static OverlayBrush OverlayNamed(MapPainter painter, string name) => painter.OverlayBrushes().First(b => b.Name == name);

	private static EditHistory.Step Overlay(MapPainter painter, string name, params Tile[] tiles) {
		OverlayBrush brush = OverlayNamed(painter, name);
		painter.BeginStroke("Place " + brush.Name);
		foreach (Tile tile in tiles) {
			painter.PlaceOverlay(tile, brush);
		}
		return painter.EndStroke();
	}

	// Civ3's overlay pop-up, its help's order.
	[Fact]
	public void TheOverlayBrushesAreCiv3s() {
		(_, MapPainter painter) = NewScenario();
		Assert.Equal(["River", "Goody Hut", "Barbarian Camp", "Fortress", "Colony", "Irrigation", "Mine", "Railroad", "Road", "Pollution",
			"Starting Location", "Airfield", "Radar Tower", "Outpost", "Victory Point Location", "Ruins"],
			painter.OverlayBrushes().Select(b => b.Name).ToList());
	}

	// Improvements go where the terrain allows them, each taking the place of
	// the one on its layer; nothing goes on water.
	[Fact]
	public void ImprovementsFollowTheTerrain() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile grass = gameData.map.tileAt(50, 50), mountain = gameData.map.tileAt(52, 50), forest = gameData.map.tileAt(48, 50);
		Stroke(painter, Brush(painter, "Grassland"), 5, grass);
		Stroke(painter, Brush(painter, "Mountains"), 1, mountain);
		Stroke(painter, Brush(painter, "Forest"), 1, forest);
		Tile ocean = gameData.map.tileAt(10, 10);

		Overlay(painter, "Road", grass);
		Assert.True(grass.HasRoad());
		Overlay(painter, "Railroad", grass);
		Assert.True(grass.HasRailroad());
		Overlay(painter, "Irrigation", grass);
		Assert.True(grass.HasIrrigation());
		Overlay(painter, "Mine", grass);
		Assert.False(grass.HasIrrigation());
		Assert.True(grass.HasRailroad());
		Overlay(painter, "Pollution", grass);
		Overlay(painter, "Ruins", grass);
		Assert.True(grass.HasPollution() && grass.HasRuins());

		Assert.NotNull(MapPainter.CantPlace(mountain, OverlayNamed(painter, "Irrigation")));
		Assert.Null(MapPainter.CantPlace(mountain, OverlayNamed(painter, "Mine")));
		Assert.NotNull(MapPainter.CantPlace(forest, OverlayNamed(painter, "Mine")));
		Assert.Null(Overlay(painter, "Irrigation", mountain));
		Assert.Null(Overlay(painter, "Road", ocean));
		Assert.Null(Overlay(painter, "Goody Hut", ocean));
		Assert.Null(Overlay(painter, "Road", grass));
	}

	// A colony and an outpost: a colony shares its tile with a fortress, an
	// outpost takes a fortress's place; placed, they belong to no one.
	[Fact]
	public void StructuresShareOrTakeTheirLayer() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile tile = gameData.map.tileAt(50, 50);
		Stroke(painter, Brush(painter, "Plains"), 3, tile);
		Overlay(painter, "Fortress", tile);
		Overlay(painter, "Colony", tile);
		Assert.True(tile.HasColony());
		Assert.Null(tile.holdingOwner);
		Overlay(painter, "Outpost", tile);
		Assert.True(tile.HasOutpost());
		Assert.True(tile.HasColony());
		Assert.Equal(2, tile.overlays.GetImprovements().Count);

		// Water takes the colony and its owner; undo gives both back.
		Player owner = new() { id = ID.FromString("player-1") };
		tile.holdingOwner = owner;
		Stroke(painter, Brush(painter, "Coast"), 1, tile);
		Assert.False(tile.HasColony());
		Assert.Null(tile.holdingOwner);
		painter.Undo();
		Assert.True(tile.HasColony());
		Assert.Equal(owner, tile.holdingOwner);
	}

	// A goody hut, a barbarian camp and a starting location exclude one
	// another, as in Civ3's Tile Properties.
	[Fact]
	public void HutsCampsAndStartsExcludeOneAnother() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile a = gameData.map.tileAt(50, 50), b = gameData.map.tileAt(52, 50), c = gameData.map.tileAt(51, 51);
		Stroke(painter, Brush(painter, "Grassland"), 5, a);
		Overlay(painter, "Goody Hut", a);
		Overlay(painter, "Barbarian Camp", b);
		Overlay(painter, "Starting Location", c);
		foreach (string name in new[] { "Barbarian Camp", "Starting Location" }) {
			Assert.Null(Overlay(painter, name, a));
		}
		foreach (string name in new[] { "Goody Hut", "Starting Location" }) {
			Assert.Null(Overlay(painter, name, b));
		}
		foreach (string name in new[] { "Goody Hut", "Barbarian Camp" }) {
			Assert.Null(Overlay(painter, name, c));
		}
		Assert.True(a.hasGoodyHut && !a.hasBarbarianCamp);
		Assert.True(b.hasBarbarianCamp && !b.hasGoodyHut);
		Assert.Equal([c], gameData.map.startingLocations);
		Assert.NotNull(Overlay(painter, "Victory Point Location", c));
		Assert.True(c.isVictoryLocation);
	}

	// The starting locations are in the player slots' order; undoing a
	// stroke that took one away puts it back in its place.
	[Fact]
	public void StartingLocationsKeepTheirOrderThroughUndo() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile a = gameData.map.tileAt(40, 50), b = gameData.map.tileAt(50, 50), c = gameData.map.tileAt(60, 50);
		Stroke(painter, Brush(painter, "Grassland"), 1, a, b, c);
		Overlay(painter, "Starting Location", a);
		Overlay(painter, "Starting Location", b);
		Overlay(painter, "Starting Location", c);
		Assert.Equal([a, b, c], gameData.map.startingLocations);

		Stroke(painter, Brush(painter, "Coast"), 1, b);
		Assert.Equal([a, c], gameData.map.startingLocations);
		painter.Undo();
		Assert.Equal([a, b, c], gameData.map.startingLocations);
		painter.Redo();
		Assert.Equal([a, c], gameData.map.startingLocations);
		painter.Undo();
		painter.Undo();
		Assert.Equal([a, b], gameData.map.startingLocations);
	}

	// Overlays are saved and opened again.
	[Fact]
	public void PlacedOverlaysAreSavedAndOpenedAgain() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Stroke(painter, Brush(painter, "Grassland"), 5, gameData.map.tileAt(50, 50));
		Overlay(painter, "Goody Hut", gameData.map.tileAt(50, 50));
		Overlay(painter, "Barbarian Camp", gameData.map.tileAt(52, 50));
		Overlay(painter, "Starting Location", gameData.map.tileAt(48, 50));
		Overlay(painter, "Victory Point Location", gameData.map.tileAt(50, 52));
		Overlay(painter, "Road", gameData.map.tileAt(50, 52), gameData.map.tileAt(51, 51));
		Overlay(painter, "Mine", gameData.map.tileAt(50, 48));
		Overlay(painter, "Colony", gameData.map.tileAt(49, 49));

		using TempDirectory dir = new();
		string path = dir.File("overlays.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, new ScenarioInfo { customMap = true }), path);
		C7GameData.GameData again = ScenarioEditing.Open(ScenarioFiles.ForEditing(SaveGame.Load(path, s => s)), fixture.behaviors);

		Assert.True(again.map.tileAt(50, 50).hasGoodyHut);
		Assert.True(again.map.tileAt(52, 50).hasBarbarianCamp);
		Assert.Equal([again.map.tileAt(48, 50)], again.map.startingLocations);
		Assert.True(again.map.tileAt(50, 52).isVictoryLocation);
		Assert.True(again.map.tileAt(50, 52).HasRoad() && again.map.tileAt(51, 51).HasRoad());
		Assert.Contains(again.map.tileAt(50, 48).overlays.GetImprovements(), i => i.key == Tile.TileOverlays.MINE);
		Assert.True(again.map.tileAt(49, 49).HasColony());
	}
	private static EditHistory.Step River(MapPainter painter, params (Tile tile, TileDirection edge)[] edges) {
		painter.BeginStroke("Place River");
		foreach ((Tile tile, TileDirection edge) in edges) {
			painter.PlaceRiver(tile, edge);
		}
		return painter.EndStroke();
	}

	private static int CornerFlags(GameMap map) {
		return map.tiles.Sum(t => (t.riverNorth ? 1 : 0) + (t.riverEast ? 1 : 0) + (t.riverSouth ? 1 : 0) + (t.riverWest ? 1 : 0));
	}

	// A river along one edge is on both tiles; one edge alone sets no
	// corner (Civ3's saves).
	[Fact]
	public void ARiverRunsAlongAnEdgeOnBothTiles() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile tile = gameData.map.tileAt(50, 50);
		Stroke(painter, Brush(painter, "Grassland"), 5, tile);
		Assert.Equal("River", painter.OverlayBrushes()[0].Name);

		River(painter, (tile, TileDirection.NORTHEAST));
		Assert.True(tile.riverNortheast);
		Assert.True(tile.neighbors[TileDirection.NORTHEAST].riverSouthwest);
		Assert.Equal(0, CornerFlags(gameData.map));
		Assert.Null(River(painter, (tile.neighbors[TileDirection.NORTHEAST], TileDirection.SOUTHWEST)));
	}

	// A river straight through a corner cuts the tiles across it off from
	// each other; one bending round a tile cuts only that tile off from the
	// one across from it (the two tiles beside the bend stay joined).
	[Fact]
	public void CornerFlagsFollowTheEdges() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile tile = gameData.map.tileAt(50, 50);
		Stroke(painter, Brush(painter, "Grassland"), 7, tile);
		Tile north = tile.neighbors[TileDirection.NORTH], northwest = tile.neighbors[TileDirection.NORTHWEST];

		// Bending round the northeast tile: its two edges at the corner.
		River(painter, (tile, TileDirection.NORTHEAST), (north, TileDirection.SOUTHEAST));
		Assert.False(tile.riverNorth);
		Assert.False(north.riverSouth);
		Assert.True(northwest.riverEast);
		Assert.True(tile.neighbors[TileDirection.NORTHEAST].riverWest);

		// On through the corner: tile|northeast and north|northwest.
		River(painter, (north, TileDirection.SOUTHWEST));
		Assert.True(tile.riverNorth);
		Assert.True(north.riverSouth);
		Assert.True(northwest.riverEast);
		Assert.True(tile.neighbors[TileDirection.NORTHEAST].riverWest);
		Assert.True(tile.HasRiverCrossing(TileDirection.NORTH));

		painter.Undo();
		Assert.False(tile.riverNorth);
		Assert.False(north.riverSouthwest);
		Assert.True(tile.riverNortheast);
	}

	// Rivers run between land tiles; water painted over one takes its
	// rivers off both sides and the corners with them, and undo puts them
	// back.
	[Fact]
	public void RiversKeepToLand() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile tile = gameData.map.tileAt(50, 50);
		Stroke(painter, Brush(painter, "Grassland"), 3, tile);
		Tile coast = gameData.map.tileAt(50, 46);
		Assert.NotNull(MapPainter.CantPlaceRiver(tile.neighbors[TileDirection.NORTH], TileDirection.NORTHEAST));
		Assert.Null(River(painter, (coast, TileDirection.SOUTHEAST)));

		Tile north = tile.neighbors[TileDirection.NORTH];
		River(painter, (tile, TileDirection.NORTHEAST), (north, TileDirection.SOUTHWEST));
		Assert.True(tile.riverNorth);
		Stroke(painter, Brush(painter, "Coast"), 1, north);
		Assert.False(north.riverSouthwest);
		Assert.False(north.neighbors[TileDirection.SOUTHWEST].riverNortheast);
		Assert.False(tile.riverNorth);
		Assert.True(tile.riverNortheast);
		painter.Undo();
		Assert.True(north.riverSouthwest && tile.riverNorth);
	}

	// The corner rule gives back every corner flag of a real Civ3 game: a
	// save holds what Civ3 works out while playing. (A scenario's .biq holds
	// other corner bits, which agree with neither.)
	[SkippableFact]
	public void CornerFlagsMatchACiv3Game() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		string saves = Path.Join(conquests, "Saves");
		Skip.IfNot(Directory.Exists(saves), "No Civ3 saves");
		C7GameData.GameData gameData = null;
		foreach (string path in Directory.GetFiles(saves, "*.SAV")) {
			try {
				SaveGame save = ScenarioFiles.Open(path, PathUtils.defaultBicPath,
					_ => Path.GetFullPath(Path.Combine(conquests, "Text", "PediaIcons.txt")));
				gameData = ScenarioEditing.Open(save, fixture.behaviors);
				if (CornerFlags(gameData.map) > 50) {
					break;
				}
			} catch (System.Exception) {
				// Saves from before Conquests can't be read.
			}
			gameData = null;
		}
		Skip.If(gameData == null, "No Civ3 save with rivers");
		var civ3 = gameData.map.tiles.ToDictionary(t => t, t => (t.riverNorth, t.riverEast, t.riverSouth, t.riverWest));

		Rivers.SetCorners(gameData.map.tiles);
		Assert.All(gameData.map.tiles, t => Assert.Equal(civ3[t], (t.riverNorth, t.riverEast, t.riverSouth, t.riverWest)));
	}
}
