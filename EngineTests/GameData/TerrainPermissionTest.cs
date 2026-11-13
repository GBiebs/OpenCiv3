using System;
using System.Linq;
using C7Engine;
using C7Engine.Lua;
using C7GameData;
using EngineTests.Utils;
using Xunit;
using static C7GameData.Tile.TileOverlays;

namespace EngineTests.GameData;

// What the terrain allows to be built on it (the BIQ's TERR flags, and roads
// only where the terrain has a road bonus), and what craters and a harbor do
// to a tile's yield.
public class TerrainPermissionTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public TerrainPermissionTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => p.isHuman);
		foreach (Terraform tf in gameData.Terraforms.Where(t => t.RequiredTech != null)) {
			us.knownTechs.Add(tf.RequiredTech);
		}
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private TerrainType Terrain(string key) => gameData.terrainTypes.Single(t => t.Key == key);
	private TerrainImprovement Improvement(string key) => gameData.terrainImprovements.Single(ti => ti.key == key);

	// Open land outside everyone's borders, with nothing on or around it.
	private Tile LonelyLand(string terrain) {
		Tile tile = gameData.map.tiles.First(t => t.IsLand() && !t.IsImpassable() && t.OwningPlayer() == null
			&& !t.hasBarbarianCamp && !t.hasGoodyHut && !t.overlays.HasBeenImproved() && t.Resource == Resource.NONE
			&& t.GetTilesWithinRankDistance(2).All(n => n.unitsOnTile.Count == 0 && !n.HasCity()));
		tile.baseTerrainType = Terrain(terrain);
		tile.overlayTerrainType = Terrain(terrain);
		return tile;
	}

	// conquests.biq: every land terrain allows forts, airfields, radar
	// towers, outposts and colonies but Marsh (outposts only) and Volcano
	// (none); water allows none. The flags survive a save, as the fixture's
	// game has been through one.
	[Fact]
	public void TheTerrainFlagsComeFromTheBiq() {
		foreach (string key in new[] { "desert", "plains", "grassland", "tundra", "flood plain", "hills", "mountains", "forest", "jungle" }) {
			TerrainType t = Terrain(key);
			Assert.True(t.allowForts && t.allowAirfields && t.allowRadarTowers && t.allowOutposts && t.allowColonies, key);
		}
		TerrainType marsh = Terrain("marsh");
		Assert.False(marsh.allowForts || marsh.allowAirfields || marsh.allowRadarTowers || marsh.allowColonies);
		Assert.True(marsh.allowOutposts);
		foreach (string key in new[] { "volcano", "coast", "sea", "ocean" }) {
			TerrainType t = Terrain(key);
			Assert.False(t.allowForts || t.allowAirfields || t.allowRadarTowers || t.allowOutposts || t.allowColonies, key);
		}
	}

	[Fact]
	public void AMarshTakesAnOutpostButNoFortOrAirfield() {
		Tile marsh = LonelyLand("marsh");
		Assert.True(marsh.CanTakeOutpost(us));
		Assert.False(marsh.CanTakeAirfield(us));
		Assert.False(marsh.overlays.CanAdd(marsh, Improvement(FORTRESS)));
		Assert.False(gameData.Terraforms.Single(t => t.Name == "Fortress").MeetsRequirements(us, marsh));
		Assert.True(gameData.Terraforms.Single(t => t.Name == "Outpost").MeetsRequirements(us, marsh));
	}

	[Fact]
	public void AVolcanoTakesNoRoadFortOrHolding() {
		Tile volcano = LonelyLand("volcano");
		Assert.False(volcano.overlays.CanAdd(volcano, Improvement(ROAD)));
		Assert.False(volcano.overlays.CanAdd(volcano, Improvement(FORTRESS)));
		Assert.False(volcano.CanTakeOutpost(us));
		Assert.False(volcano.CanTakeAirfield(us));
		Assert.False(gameData.Terraforms.Single(t => t.Name == "Road").MeetsRequirements(us, volcano));
	}

	[Fact]
	public void GrasslandTakesRoadsAndForts() {
		Tile grassland = LonelyLand("grassland");
		Assert.True(grassland.overlays.CanAdd(grassland, Improvement(ROAD)));
		Assert.True(grassland.overlays.CanAdd(grassland, Improvement(FORTRESS)));
		Assert.True(grassland.CanTakeOutpost(us));
		Assert.True(grassland.CanTakeAirfield(us));
	}

	[Fact]
	public void NoColonyOnAMarsh() {
		Tile marsh = LonelyLand("marsh");
		Resource luxury = gameData.Resources.First(r => r.Category == ResourceCategory.LUXURY && us.KnowsAboutResource(r));
		marsh.Resource = luxury;
		Assert.False(marsh.CanTakeColony(us));

		marsh.baseTerrainType = marsh.overlayTerrainType = Terrain("grassland");
		Assert.True(marsh.CanTakeColony(us));
	}

	// "the presence of craters reduces a square's production, commerce, and
	// food output by one" (GCON_Craters), and Clear Damage fills them in.
	[Fact]
	public void CratersCostAFoodAShieldAndACommerce() {
		Tile tile = LonelyLand("grassland");
		tile.overlays.Add(Improvement(ROAD));
		tile.overlays.Add(Improvement(MINE));
		(int food, int shields, int commerce) before = (tile.FoodYield(us).yield, tile.ProductionYield(us).yield, tile.CommerceYield(us).yield);
		Assert.True(before.food > 0 && before.shields > 0 && before.commerce > 0);

		tile.overlays.Add(Improvement(CRATERS));
		Assert.Equal(before.food - 1, tile.FoodYield(us).yield);
		Assert.Equal(before.shields - 1, tile.ProductionYield(us).yield);
		Assert.Equal(before.commerce - 1, tile.CommerceYield(us).yield);

		Assert.True(tile.HasDamage());
		tile.ClearDamage();
		Assert.False(tile.HasCraters());
		Assert.Equal(before.commerce, tile.CommerceYield(us).yield);
	}

	// Pollution, like craters, costs a tile one of each yield, never below 0,
	// rather than all of them (Civ3 binary,
	// doc/perez-findings/Civ3_Findings.md, Economy).
	[Fact]
	public void PollutionCostsAFoodAShieldAndACommerce() {
		Tile tile = LonelyLand("grassland");
		tile.overlays.Add(Improvement(ROAD));
		tile.overlays.Add(Improvement(MINE));
		(int food, int shields, int commerce) before = (tile.FoodYield(us).yield, tile.ProductionYield(us).yield, tile.CommerceYield(us).yield);
		Assert.True(before.food > 0 && before.shields > 0 && before.commerce > 0);

		tile.overlays.Add(Improvement(POLLUTION));
		Assert.Equal(before.food - 1, tile.FoodYield(us).yield);
		Assert.Equal(before.shields - 1, tile.ProductionYield(us).yield);
		Assert.Equal(before.commerce - 1, tile.CommerceYield(us).yield);
	}

	// A harbor adds a food on every water tile of a body of more than 20
	// tiles, an ocean's none included (Civ3 binary,
	// doc/perez-findings/Civ3_Findings.md, Economy), but not on a lake or a
	// tile under fallout. The Offshore Platform and Commercial Dock are the
	// same.
	[Fact]
	public void AHarborFeedsOceanTilesButNotLakes() {
		Action<Tile.Yield> harbor = NativeBehaviors.Get<Action<Tile.Yield>>("buildings.tile_modifiers.increases_food_in_water");
		Action<Tile.Yield> platform = NativeBehaviors.Get<Action<Tile.Yield>>("buildings.tile_modifiers.increases_shields_in_water");
		Action<Tile.Yield> dock = NativeBehaviors.Get<Action<Tile.Yield>>("buildings.tile_modifiers.increases_trade_in_water");
		Tile ocean = gameData.map.tiles.First(t => t.overlayTerrainType.IsOcean && !t.HasFallout() && !t.isFreshWater);
		Tile.Yield yield = new(ocean, 0, Tile.YieldType.Food);
		harbor(yield);
		Assert.Equal(1, yield.yield);

		ocean.isFreshWater = true;
		Tile.Yield food = new(ocean, 0, Tile.YieldType.Food);
		harbor(food);
		Assert.Equal(0, food.yield);
		Tile.Yield shields = new(ocean, 0, Tile.YieldType.Production);
		platform(shields);
		Assert.Equal(0, shields.yield);
		Tile.Yield commerce = new(ocean, 2, Tile.YieldType.Commerce);
		dock(commerce);
		Assert.Equal(2, commerce.yield);
		ocean.isFreshWater = false;

		ocean.overlays.Add(Improvement(FALLOUT));
		Tile.Yield fallout = new(ocean, 0, Tile.YieldType.Food);
		harbor(fallout);
		Assert.Equal(0, fallout.yield);
	}
}
