using System;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Border disputes after a city is destroyed. Destroying a city clears the
// owner of every tile inside its borders, including a neighboring city's own
// tile, and then reassigns them all. A tie between two other cities must still
// be settled by the cities themselves (Law VI of the eight laws of border
// dynamics), not by whoever owned the winning city's tile a moment ago.
public class BorderDisputeTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;

	public BorderDisputeTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
	}

	// `count` land tiles in a row, going east one tile at a time.
	private Tile[] LandRow(int count) {
		foreach (Tile start in gameData.map.tiles) {
			Tile[] row = Enumerable.Range(0, count)
				.Select(i => gameData.map.tileAt(start.XCoordinate + 2 * i, start.YCoordinate))
				.ToArray();
			if (row.All(t => Tile.IsTileValid(t) && t.IsLand() && t.cityAtTile == null)) {
				return row;
			}
		}
		throw new Exception("The test map has no row of land tiles");
	}

	[Fact]
	public void ATieBesideADestroyedCityStillGoesToACity() {
		Player[] players = gameData.players.Where(p => !p.isBarbarians).Take(2).ToArray();
		Tile[] row = LandRow(4);
		// Two capitals (equal culture) one tile either side of the disputed
		// tile, so they tie, and a third city right beside the western one.
		City east = CityInteractions.BuildCity(row[3], players[0], "East");
		City west = CityInteractions.BuildCity(row[1], players[1], "West");
		City besideWest = CityInteractions.BuildCity(row[0], players[1], "Beside West");
		Tile disputed = row[2];
		// Law VI searches counter-clockwise from the north, so the western
		// city wins the tie.
		Assert.Equal(west, disputed.owningCity);

		// The western city's own tile is inside the destroyed city's borders.
		// The eastern city is reassigned first and meets the tie again.
		CityInteractions.DestroyCity(besideWest);

		Assert.Equal(west, disputed.owningCity);
		Assert.Equal(west, west.location.owningCity);
		Assert.Equal(east, east.location.owningCity);
	}
}
