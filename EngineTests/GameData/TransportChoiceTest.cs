using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Choosing which transport a unit boards (Civ3's "What Transport?",
// script.txt's SELECT_TRANSPORT) and which units a transport unloads (its
// "Disembark?" popup, DISEMBARK).
public class TransportChoiceTest : IClassFixture<SaveGameFixture>, System.IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public TransportChoiceTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler));
	}

	public void Dispose() {
		EngineStorage.activePlayerID = null;
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private MapUnit Spawn(string prototype, Tile tile) =>
		gameData.SpawnUnit(us, gameData.unitPrototypes.Single(p => p.name == prototype), tile);

	// One of our cities by the sea.
	private Tile Port() {
		Tile tile = gameData.map.tiles.First(t => t.IsLand() && !t.HasCity() && !t.hasBarbarianCamp && t.unitsOnTile.Count == 0
			&& t.IsAllowCities() && t.OwningPlayer() == null && t.neighbors.Values.Any(n => Tile.IsTileValid(n) && n.IsWater()));
		return CityInteractions.BuildCity(tile, us, us.GetNextCityName()).location;
	}

	private void Send(MessageToEngine message) {
		EngineStorage.pendingMessages.Clear();
		EngineStorage.activePlayerID = us.id;
		message.playerID = us.id;
		message.send();
		while (EngineStorage.HasPendingMessagesToEngine()) {
			EngineStorage.ProcessNextMessageToEngine();
		}
	}

	[Fact]
	public void TheTransportsAUnitCouldBoardAreListedForItToChoose() {
		Tile port = Port();
		MapUnit empty = Spawn("Galley", port);
		MapUnit carrying = Spawn("Galley", port);
		MapUnit full = Spawn("Galley", port);
		Spawn("Warrior", port).loadedOnUnitId = carrying.id;
		for (int i = full.FreeCapacity(); i > 0; i--) {
			Spawn("Warrior", port).loadedOnUnitId = full.id;
		}
		MapUnit warrior = Spawn("Warrior", port);

		// The one it would board unasked comes first: one already carrying
		// units. A full one isn't offered.
		Assert.Equal([carrying, empty], warrior.TransportsToBoard(port, explicitLoad: true));

		// It boards the one chosen.
		Send(new MsgLoadToTransport(warrior.id, empty.id));
		Assert.True(warrior.IsLoadedIn(empty));
		Assert.DoesNotContain(UnitAction.Load, warrior.GetAvailableActions());
	}

	[Fact]
	public void ATransportUnloadsOneUnitOrAll() {
		Tile port = Port();
		MapUnit galley = Spawn("Galley", port);
		MapUnit first = Spawn("Warrior", port);
		MapUnit second = Spawn("Warrior", port);
		first.loadedOnUnitId = galley.id;
		second.loadedOnUnitId = galley.id;

		Send(new MsgUnloadTransport(galley.id, second.id));
		Assert.True(first.IsLoadedIn(galley));
		Assert.False(second.IsLoaded());

		second.loadedOnUnitId = galley.id;
		Send(new MsgUnloadTransport(galley.id));
		Assert.Empty(galley.Passengers());
	}
}
