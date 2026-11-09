using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's production queue (ProductionQueue): what's queued starts when the
// build finishes, without asking; what the city can no longer build is
// skipped; queues are saved, and can be kept under a name.
public class ProductionQueueTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public ProductionQueueTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.isHuman = true;
		city = CityInteractions.BuildCity(us.units[0].location, us, "Seat");
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Warrior => gameData.unitPrototypes.Find(u => u.name == "Warrior");
	private Building Barracks => gameData.Buildings.Find(b => b.name == "Barracks");

	private void Finish(IProducible item) {
		city.SetItemBeingProduced(item);
		city.SetStoredShields(us.ShieldCost(item));
		city.HandleCityProduction(gameData);
	}

	[Fact]
	public void TheQueuedItemStartsWithoutAsking() {
		new MsgSetProductionQueue(city.id, [ProductionQueue.Entry(Barracks)]).process();
		Assert.Equal([Barracks], city.productionQueue);

		Finish(Warrior);
		Assert.Same(Barracks, city.itemBeingProduced);
		Assert.Empty(city.productionQueue);
		Assert.False(Assert.Single(EngineStorage.messagesToUI.OfType<MsgProductionComplete>()).askWhatNext);
	}

	[Fact]
	public void WhatCanNoLongerBeBuiltIsSkipped() {
		new MsgSetProductionQueue(city.id, [ProductionQueue.Entry(Barracks), ProductionQueue.Entry(Warrior)]).process();

		Finish(Barracks);
		Assert.Same(Warrior, city.itemBeingProduced);
		Assert.Empty(city.productionQueue);
	}

	[Fact]
	public void TheQueueIsSaved() {
		new MsgSetProductionQueue(city.id, [ProductionQueue.Entry(Barracks), ProductionQueue.Entry(Warrior), "X:nothing"]).process();
		Assert.Equal(2, city.productionQueue.Count);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(["Barracks", "Warrior"], reloaded.cities.Single(c => c.id == city.id).productionQueue.Select(p => p.name));
	}

	[Fact]
	public void AQueueIsKeptUnderAName() {
		new MsgSaveProductionQueue(us.id, " Defense ", [ProductionQueue.Entry(Barracks), ProductionQueue.Entry(Warrior)]).process();
		Assert.Equal(["B:Barracks", "U:Warrior"], us.savedQueues["Defense"]);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(["B:Barracks", "U:Warrior"], reloaded.GetPlayer(us.id).savedQueues["Defense"]);

		// (Loading made the reloaded game the engine's: back to ours.)
		EngineStorage.InitializeGameDataForTests(gameData);
		new MsgSaveProductionQueue(us.id, "Defense", []).process();
		Assert.Empty(us.savedQueues);
	}
}
