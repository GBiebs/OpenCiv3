using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// What the screens did to the game themselves are orders now (so online,
// every game does them): the city screen's tiles and specialists, the
// units' hold, explore, automate and disband.
public class CityScreenOrdersTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player player;
	private readonly City city;

	public CityScreenOrdersTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		player = gameData.players.First(p => p.isHuman);
		// (Away from the units: disbanding in a city runs Lua the tests can't.)
		Tile start = player.units[0].location;
		Tile site = gameData.map.tiles.Where(t => t.IsLand() && t.cityAtTile == null && t.unitsOnTile.Count == 0 && t.DistanceTo(start) >= 3)
			.OrderBy(t => t.DistanceTo(start)).First();
		city = CityInteractions.BuildCity(site, player, "Uruk");
		for (int i = 0; i < 2; i++) {
			city.AddCitizen(new CityResident { city = city, citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen) });
		}
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
		EngineStorage.animationsEnabled = true;
	}

	private void Process(MessageToEngine order) {
		order.send();
		EngineStorage.ProcessNextMessageToEngine();
	}

	private Tile Unworked() => city.GetWorkableTiles().First(t => t.personWorkingTile == null && t.cityAtTile == null);

	[Fact]
	public void AClickOnATileMovesTheCitizenOnThePoorestOne() {
		Tile tile = Unworked();
		Process(new MsgWorkTile(city.id, tile));
		Assert.NotNull(tile.personWorkingTile);
		Assert.Equal(city, tile.personWorkingTile.city);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityChanged>(), m => m.cityId == city.id);
	}

	[Fact]
	public void AClickOnAWorkedTileMakesASpecialist() {
		Tile worked = city.residents.First(r => r.tileWorked != Tile.NONE).tileWorked;
		CityResident resident = worked.personWorkingTile;
		Process(new MsgWorkTile(city.id, worked));
		Assert.Null(worked.personWorkingTile);
		Assert.False(resident.citizenType.IsDefaultCitizen);

		// And a click on the specialist makes the next kind.
		ID before = resident.citizenType.Id;
		Process(new MsgCycleSpecialist(city.id, city.residents.IndexOf(resident)));
		if (player.GetKnownSpecialists(gameData).Count > 1) {
			Assert.NotEqual(before, resident.citizenType.Id);
		}
		Assert.Equal(2, EngineStorage.messagesToUI.OfType<MsgCityChanged>().Count());
	}

	// Not a tile the city works, nor a citizen: nothing changes.
	[Fact]
	public void WhatTheCityCantWorkIsLeftAlone() {
		Tile far = gameData.map.tiles.First(t => t.DistanceTo(city.location) > 5);
		Assert.False(CityInteractions.WorkTile(gameData, city, far));
		CityResident worker = city.residents.First(r => r.citizenType.IsDefaultCitizen);
		Assert.False(CityInteractions.CycleSpecialist(gameData, city, city.residents.IndexOf(worker)));
		Assert.False(CityInteractions.CycleSpecialist(gameData, city, 99));
		Process(new MsgWorkTile(city.id, far));
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgCityChanged>());
	}

	[Fact]
	public void ChoosingProductionShowsTheCityAgain() {
		string producible = city.ListProductionOptions(gameData).Last().name;
		Process(new MsgChooseProduction(city.id, producible));
		Assert.Equal(producible, city.itemBeingProduced.name);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityChanged>());
	}

	[Fact]
	public async Task UnitOrdersAreOrders() {
		MapUnit unit = player.units.First();
		Process(new MsgUnitOrder(unit.id, MsgUnitOrder.Order.Hold));
		Assert.False(unit.movementPoints.canMove);

		await new MsgUnitOrder(unit.id, MsgUnitOrder.Order.Disband).ProcessAsync();
		Assert.DoesNotContain(unit, player.units);
	}

	// An order whose work goes on: done when it's done.
	[Fact]
	public async Task TheEngineKnowsWhenAnOrdersWorkIsDone() {
		Assert.True(EngineStorage.OrdersDone());
		TaskCompletionSource<bool> going = new();
		new SlowOrder(going.Task).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.False(EngineStorage.OrdersDone());
		going.SetResult(true);
		await Task.Delay(50);
		Assert.True(EngineStorage.OrdersDone());
	}

	private class SlowOrder : MessageToEngine {
		private readonly Task until;

		public SlowOrder(Task until) {
			this.until = until;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() => await until;
	}
}
