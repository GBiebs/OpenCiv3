using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Production queues saved under a name (Shift-Q on the city screen) and
// loaded into a city (Q); see SavedProductionQueue.
public class SavedProductionQueueTest : IClassFixture<SaveGameFixture>, System.IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player human;
	private readonly Player ai;
	private readonly ID previousActivePlayer;

	public SavedProductionQueueTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		human = gameData.players.First(p => p.isHuman);
		ai = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		previousActivePlayer = EngineStorage.activePlayerID;
	}

	public void Dispose() {
		EngineStorage.activePlayerID = previousActivePlayer;
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private static bool IsEmptyLand(Tile t) {
		return t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.IsImpassable();
	}

	private City BuildCity(Player owner) {
		Tile tile = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities()
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity())
			&& t.GetTilesWithinRankDistance(3).All(n => n == Tile.NONE || !n.HasCity()));
		return CityInteractions.BuildCity(tile, owner, $"City {owner.cities.Count}");
	}

	// Sends the order as the player, on their turn.
	private static void Send(MessageToEngine message, Player player) {
		player.hasPlayedThisTurn = false;
		EngineStorage.activePlayerID = player.id;
		message.playerID = player.id;
		Assert.True(message.process());
	}

	private (City city, Building building, UnitPrototype unit) CityWithQueue() {
		City city = BuildCity(human);
		List<IProducible> options = city.ListProductionOptions(gameData).ToList();
		Building building = options.OfType<Building>().First(b => b.greatWonderProperties == null);
		UnitPrototype unit = options.OfType<UnitPrototype>().First();
		city.EnqueueProduction(building);
		city.EnqueueProduction(unit);
		return (city, building, unit);
	}

	[Fact]
	public void SavedQueueLoadsIntoAnotherCity() {
		(City city, Building building, UnitPrototype unit) = CityWithQueue();
		Send(new MsgSaveProductionQueue(city.id, "  Core city  "), human);

		Assert.Equal("Core city", Assert.Single(human.savedProductionQueues).name);

		City other = BuildCity(human);
		other.EnqueueProduction(other.ListProductionOptions(gameData).OfType<UnitPrototype>().Last());
		IProducible producing = other.itemBeingProduced;
		Send(new MsgLoadProductionQueue(other.id, "core city"), human);

		// The saved queue replaces the city's own; what it builds stays.
		Assert.Equal([building, unit], other.productionQueue);
		Assert.Same(producing, other.itemBeingProduced);
		// The city whose queue was saved keeps it.
		Assert.Equal([building, unit], city.productionQueue);
	}

	[Fact]
	public void SavingUnderTheSameNameReplacesTheQueue() {
		(City city, Building building, _) = CityWithQueue();
		Send(new MsgSaveProductionQueue(city.id, "Basics"), human);
		city.ClearProductionQueue();
		city.EnqueueProduction(building);
		Send(new MsgSaveProductionQueue(city.id, "BASICS"), human);

		SavedProductionQueue saved = Assert.Single(human.savedProductionQueues);
		Assert.Equal("BASICS", saved.name);
		Assert.Equal([building.name], saved.items.Select(i => i.name));
	}

	[Fact]
	public void EmptyQueueOrBlankNameIsNotSaved() {
		(City city, _, _) = CityWithQueue();
		Send(new MsgSaveProductionQueue(city.id, "   "), human);
		City empty = BuildCity(human);
		Send(new MsgSaveProductionQueue(empty.id, "Nothing"), human);

		Assert.Empty(human.savedProductionQueues);
	}

	[Fact]
	public void ItemsTheCityCannotBuildAreLeftOut() {
		(City city, Building building, UnitPrototype unit) = CityWithQueue();
		Send(new MsgSaveProductionQueue(city.id, "Q"), human);

		City other = BuildCity(human);
		other.AddBuilding(building);
		Send(new MsgLoadProductionQueue(other.id, "Q"), human);

		Assert.Equal([unit], other.productionQueue);
	}

	[Fact]
	public void OnlyTheOwnerSavesAndLoads() {
		(City city, _, _) = CityWithQueue();
		Send(new MsgSaveProductionQueue(city.id, "Theirs"), ai);
		Assert.Empty(ai.savedProductionQueues);
		Assert.Empty(human.savedProductionQueues);

		Send(new MsgSaveProductionQueue(city.id, "Ours"), human);
		City aiCity = BuildCity(ai);
		Send(new MsgLoadProductionQueue(aiCity.id, "Ours"), ai);
		Assert.Empty(aiCity.productionQueue);
	}

	[Fact]
	public void SavedQueuesSurviveSaveAndLoad() {
		(City city, Building building, UnitPrototype unit) = CityWithQueue();
		Send(new MsgSaveProductionQueue(city.id, "Kept"), human);

		string json = System.Text.Encoding.UTF8.GetString(SaveGame.FromGameData(gameData).ToCompactJSON());
		C7GameData.GameData loaded = SaveGame.LoadFromJSON(json).ToGameData(fixture.behaviors);
		Player loadedHuman = loaded.GetPlayer(human.id);

		SavedProductionQueue saved = Assert.Single(loadedHuman.savedProductionQueues);
		Assert.Equal("Kept", saved.name);
		Assert.Equal([(building.name, ProducibleType.BUILDING), (unit.name, ProducibleType.UNIT)],
			saved.items.Select(i => (i.name, i.type)));
	}

	[Fact]
	public void OlderSavesLoadWithoutSavedQueues() {
		string json = System.Text.Encoding.UTF8.GetString(SaveGame.FromGameData(gameData).ToCompactJSON());
		string oldJson = Regex.Replace(json, @"""savedProductionQueues"":\[\],?", "");
		Assert.DoesNotContain("savedProductionQueues", oldJson);

		C7GameData.GameData loaded = SaveGame.LoadFromJSON(oldJson).ToGameData(fixture.behaviors);

		Assert.All(loaded.players, p => Assert.Empty(p.savedProductionQueues));
	}
}
