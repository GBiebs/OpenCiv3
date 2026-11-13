using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Automated workers and explorers stay automated through a save and load
// (play by email loads every turn): the kind of automation is saved, and the
// unit makes a new plan of that kind on its next turn. Adapted from
// GBiebs/OpenCiv3.
public class AutomationSaveTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public AutomationSaveTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler));
		CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Home");
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private MapUnit Spawn(string type) {
		return gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == type), us.cities[0].location);
	}

	private MapUnit Reloaded(MapUnit unit, SaveGame save = null) {
		C7GameData.GameData reloaded = (save ?? SaveGame.FromGameData(gameData)).Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(reloaded);
		MapUnit again = reloaded.GetPlayer(us.id).units.Single(u => u.id == unit.id);
		again.movementPoints.reset(again.MaxMovementPoints());
		return again;
	}

	[Theory]
	[InlineData("Worker", "work")]
	[InlineData("Warrior", "explore")]
	[InlineData("Worker", "explore")]
	public async Task AutomationCarriesOnAfterALoad(string type, string kind) {
		MapUnit unit = Spawn(type);
		if (kind == "work") {
			unit.Automate();
		} else {
			unit.Explore();
		}
		Assert.True(unit.isAutomated);
		Assert.Equal(kind, unit.AutomationKind());

		MapUnit again = Reloaded(unit);
		Assert.True(again.isAutomated);
		Assert.Null(again.currentAI);

		await again.PerformBusyAction();
		Assert.True(again.isAutomated);
		Assert.Equal(kind, again.AutomationKind());
		Assert.IsType(kind == "work" ? typeof(WorkerAI) : typeof(ExplorerAI), again.currentAI);
	}

	// A save from before the kind was saved: a worker goes back to work.
	[Fact]
	public async Task AnOlderSavesAutomatedWorkerWorks() {
		MapUnit worker = Spawn("Worker");
		worker.Automate();
		SaveGame save = SaveGame.FromGameData(gameData);
		save.Units.Single(u => u.id == worker.id).automation = null;

		MapUnit again = Reloaded(worker, save);
		await again.PerformBusyAction();
		Assert.Equal("work", again.AutomationKind());
	}

	[Fact]
	public void AUnitNotAutomatedStaysSo() {
		MapUnit warrior = Spawn("Warrior");
		Assert.Null(warrior.AutomationKind());
		MapUnit again = Reloaded(warrior);
		Assert.False(again.isAutomated);
		Assert.Null(again.resumeAutomation);
	}
}
