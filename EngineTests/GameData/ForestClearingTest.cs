using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Clearing a forest gives its shields (Civ3's 10) to the clearing civ's
// nearest city whose radius reaches it, nearest first in Civ3's order
// around the forest (its saves' spiral: the neighbors from the NE
// clockwise, then the outer ring), once per tile. A city building a wonder
// or Wealth is passed over for the next (user, 2026-11-11); none in reach,
// no shields.
public class ForestClearingTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile forest;

	public ForestClearingTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		them = gameData.players.First(p => !p.isHuman && !p.isBarbarians && p != us);
		// A land tile with land all around it, two tiles out and more,
		// nobody's unit or city near.
		forest = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.GetTilesWithinRankDistance(3).All(n => Tile.IsTileValid(n) && n.IsLand() && n.unitsOnTile.Count == 0)
			&& gameData.cities.All(c => c.location.RankDistanceTo(t) > 6));
		TerrainType grassland = gameData.terrainTypes.Find(t => t.Key == "grassland");
		foreach (Tile t in forest.GetTilesWithinRankDistance(3)) {
			t.baseTerrainType = t.overlayTerrainType = grassland;
		}
		forest.overlayTerrainType = gameData.terrainTypes.Find(t => t.Key == "forest");
	}

	private Tile Spiral(int index) {
		TileLocation at = Tile.Civ3SpiralLocation(new TileLocation(forest), index);
		return gameData.map.tileAt(at.X, at.Y);
	}

	private City CityAt(int spiral, string name, Player owner = null) {
		City city = CityInteractions.BuildCity(Spiral(spiral), owner ?? us, name);
		city.SetItemBeingProduced(gameData.unitPrototypes.Find(u => u.name == "Settler"));
		city.SetStoredShields(0);
		return city;
	}

	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);

	[Fact]
	public void TheNearestCityGetsThem() {
		City far = CityAt(9, "Far");
		City near = CityAt(5, "Near");
		forest.MaybeAwardForestClearingShields(us);
		Assert.Equal(10, near.shieldsStored);
		Assert.Equal(0, far.shieldsStored);
	}

	[Fact]
	public void BetweenNeighborsCiv3sOrderDecides() {
		// Civ3's spiral starts at the NE and ends with the N (our own
		// starts with the N).
		City north = CityAt(8, "North");
		City northeast = CityAt(1, "Northeast");
		forest.MaybeAwardForestClearingShields(us);
		Assert.Equal(10, northeast.shieldsStored);
		Assert.Equal(0, north.shieldsStored);
		Assert.Equal(Spiral(2), forest.TilesInCiv3Order(2)[2]);
		Assert.Equal(Spiral(9), forest.TilesInCiv3Order(2)[9]);
		Assert.Equal(21, forest.TilesInCiv3Order(2).Count);
	}

	[Fact]
	public void AWonderOrWealthIsPassedOverForTheNext() {
		City wonder = CityAt(1, "Wonder");
		wonder.SetItemBeingProduced(Building("The Pyramids"));
		City wealth = CityAt(5, "Wealth");
		wealth.SetItemBeingProduced(gameData.Inflows.First());
		City next = CityAt(12, "Next");
		forest.MaybeAwardForestClearingShields(us);
		Assert.Equal(0, wonder.shieldsStored);
		Assert.Equal(0, wealth.shieldsStored);
		Assert.Equal(10, next.shieldsStored);
	}

	[Fact]
	public void OnlyTheClearingCivsCities() {
		City theirs = CityAt(1, "Theirs", them);
		City ours = CityAt(16, "Ours");
		forest.MaybeAwardForestClearingShields(us);
		Assert.Equal(0, theirs.shieldsStored);
		Assert.Equal(10, ours.shieldsStored);
	}

	[Fact]
	public void NoCityInReachNoShields() {
		// Three tiles out: beyond the city radius.
		Tile beyond = forest.GetTilesWithinRankDistance(3).First(t => forest.RankDistanceTo(t) == 3);
		City city = CityInteractions.BuildCity(beyond, us, "Beyond");
		city.SetItemBeingProduced(gameData.unitPrototypes.Find(u => u.name == "Settler"));
		city.SetStoredShields(0);
		Assert.Null(forest.CityForClearedForest(us));
		forest.MaybeAwardForestClearingShields(us);
		Assert.Equal(0, city.shieldsStored);
	}

	[Fact]
	public void OncePerTileAndNoMoreThanTheBoxHolds() {
		City city = CityAt(4, "Close");
		int cost = us.ShieldCost(city.itemBeingProduced);
		city.SetStoredShields(cost - 4);
		forest.MaybeAwardForestClearingShields(us);
		Assert.Equal(cost, city.shieldsStored);
		city.SetStoredShields(0);
		forest.MaybeAwardForestClearingShields(us);
		Assert.Equal(0, city.shieldsStored);
	}

	// Through a worker's real job: the Lua effect passes the worker's civ,
	// so the forest needn't be in anyone's land (a new city's borders reach
	// a tile out, this one is two).
	[Fact]
	public void AWorkerClearingTheForest() {
		City city = CityAt(14, "Home");
		Assert.Null(forest.OwningPlayer());
		Terraform clear = C7Action.ToTerraform(C7Action.UnitClearForest);
		clear.OnComplete(us, forest);
		Assert.Equal(10, city.shieldsStored);
		Assert.NotEqual("forest", forest.overlayTerrainType.Key);
		Assert.True(forest.hasHadForestCleared);
	}
}
