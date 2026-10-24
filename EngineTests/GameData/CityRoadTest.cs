using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3 puts a road on every city's tile, so a road running from one city to
// another connects them (Civilopedia "Trade Route Connections"). Before, a
// founded city had no road, and a road reaching it didn't join it to the
// network.
public class CityRoadTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City capital;

	public CityRoadTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		capital = CityInteractions.BuildCity(us.units[0].location, us, "Capital");
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && !t.hasBarbarianCamp;
	}

	[Fact]
	public void AFoundedCityHasARoadOnItsTile() {
		Assert.True(capital.location.HasRoad());
		Assert.True(gameData.GetTradeNetwork().ConnectedToCapital(us, capital));
	}

	[Fact]
	public void ARoadBetweenTwoCitiesSharesTheirLuxuries() {
		// A town four tiles east of the capital, over land, with wine beside it.
		List<Tile> between = [];
		Tile tile = capital.location;
		for (int i = 0; i < 3; i++) {
			tile = tile.neighbors[TileDirection.EAST];
			between.Add(tile);
		}
		Tile site = tile.neighbors[TileDirection.EAST];
		Assert.True(between.All(IsEmptyLand) && IsEmptyLand(site), "the fixture's capital needs land to its east");
		City town = CityInteractions.BuildCity(site, us, "Town");
		Resource wines = gameData.Resources.Find(r => r.Name == "Wines");
		Tile vineyard = site.neighbors[TileDirection.EAST];
		Assert.Equal(us, vineyard.OwningPlayer());
		vineyard.Resource = wines;
		TerrainImprovement road = gameData.terrainImprovements.Find(i => i.key == "road");
		vineyard.overlays.Add(road);
		gameData.InvalidateCachedTradeNetwork();

		Assert.Contains(wines, town.GetLuxuries(gameData).Keys);
		Assert.DoesNotContain(wines, capital.GetLuxuries(gameData).Keys);

		foreach (Tile t in between) {
			t.overlays.Add(road);
		}

		Assert.True(gameData.GetTradeNetwork().ConnectedToCapital(us, town));
		Assert.Contains(wines, capital.GetLuxuries(gameData).Keys);
	}
}
