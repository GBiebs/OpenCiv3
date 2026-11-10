using System;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Unloading a transport: all its units, or only the one chosen; and
// Civ3's "Wake Transported", waking the units aboard.
public class UnloadTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly MapUnit galley;
	private readonly MapUnit[] aboard;

	public UnloadTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians);
		Tile port = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsAllowCities() && t.NeighborsWater() && !t.HasCity()
			&& t.unitsOnTile.Count == 0 && gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		CityInteractions.BuildCity(port, us, "Port");
		galley = Spawn("Galley", port);
		aboard = [Spawn("Warrior", port), Spawn("Warrior", port), Spawn("Archer", port)];
		foreach (MapUnit unit in aboard) {
			unit.BoardTransport(galley);
		}
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private MapUnit Spawn(string type, Tile tile) => gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == type), tile);

	[Fact]
	public void OnlyTheOneChosen() {
		Assert.Equal(3, galley.Passengers().Count);
		new MsgUnloadTransport(galley.id, only: aboard[2].id).process();
		Assert.Null(aboard[2].loadedOnUnitId);
		Assert.Equal([aboard[0], aboard[1]], galley.Passengers());
	}

	[Fact]
	public void AllOfThem() {
		new MsgUnloadTransport(galley.id).process();
		Assert.Empty(galley.Passengers());
		Assert.All(aboard, u => Assert.Null(u.loadedOnUnitId));
	}

	[Fact]
	public void WakeTransported() {
		aboard[0].Fortify();
		aboard[1].StandSentry(MapUnit.SentryMode.AnyUnit);
		new MsgWakeTransported(galley.id).process();
		Assert.All(aboard, u => Assert.False(u.isFortified || u.sentry != MapUnit.SentryMode.None));
		Assert.Equal(3, galley.Passengers().Count);
	}
}
