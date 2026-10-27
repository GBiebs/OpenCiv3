using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's pollution (CivFanatics' Civ3 FAQ, "How does pollution work?"):
// buildings' pollution values, at most 1 with a Recycling Center, plus one
// per citizen above 12, at most 1 with Mass Transit; each point a chance per
// turn that one of the city's tiles is polluted; workers clear the damage.
public class PollutionTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public PollutionTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Smokestack");
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private void Add(string name) => city.AddBuilding(gameData.Buildings.Find(b => b.name == name), gameData);

	private void Size(int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = laborer });
		}
	}

	private Terraform ClearDamage => gameData.Terraforms.Find(t => t.Name == "Clear Damage");

	[Fact]
	public void BuildingsAndCrowdsMakePollutionPoints() {
		Assert.Equal(0, Pollution.Points(city, gameData));
		Add("Factory");
		Add("Coal Plant");
		Add("Research Lab");
		Assert.Equal(5, Pollution.BuildingPoints(city));

		Size(12);
		Assert.Equal(0, Pollution.PopulationPoints(city, gameData));
		Size(15);
		Assert.Equal(3, Pollution.PopulationPoints(city, gameData));
		Assert.Equal(8, Pollution.Points(city, gameData));

		Add("Recycling Center");
		Assert.Equal(1, Pollution.BuildingPoints(city));
		Add("Mass Transit System");
		Assert.Equal(1, Pollution.PopulationPoints(city, gameData));
		// Civ3 leaves this 1 + 1 (a later community patch made it 0).
		Assert.Equal(2, Pollution.Points(city, gameData));
		gameData.rules.PollutionLeftByReducingBuildings = 0;
		Assert.Equal(0, Pollution.Points(city, gameData));
	}

	[Fact]
	public void EachPointIsAChancePerTurn() {
		Add("Factory");
		Assert.Equal(2 * gameData.rules.PollutionChancePerPoint / 100.0, Pollution.Chance(city, gameData), 6);
		gameData.rules.PollutionChancePerPoint = 60;
		Assert.Equal(1, Pollution.Chance(city, gameData));
	}

	[Fact]
	public void PollutionStrikesOneOfTheCitysLandTiles() {
		gameData.rules.PollutionChancePerPoint = 100;
		Pollution.UpdateForNewTurn(gameData, us);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgPollution>());

		Add("Factory");
		// Water in the city's radius, which pollution leaves alone.
		Tile water = city.location.neighbors[TileDirection.NORTH];
		TerrainType coast = gameData.terrainTypes.Find(t => t.Key == "coast");
		water.baseTerrainType = coast;
		water.overlayTerrainType = coast;
		List<Tile> atRisk = Pollution.TilesAtRisk(city, gameData);
		Assert.NotEmpty(atRisk);
		Assert.All(atRisk, t => Assert.True(t.IsLand() && t != city.location));
		Pollution.UpdateForNewTurn(gameData, us);
		Tile struck = Assert.Single(atRisk, t => t.HasPollution());
		Assert.Equal(city, Assert.Single(EngineStorage.messagesToUI.OfType<MsgPollution>()).city);
		Assert.DoesNotContain(struck, Pollution.TilesAtRisk(city, gameData));
		// A polluted tile makes nothing.
		Assert.Equal(0, struck.FoodYield(us).yield + struck.ProductionYield(us).yield + struck.CommerceYield(us).yield);

		// Once every tile is polluted, nothing more happens.
		foreach (Tile t in atRisk) {
			Pollution.UpdateForNewTurn(gameData, us);
		}
		Assert.All(atRisk, t => Assert.True(t.HasPollution()));
		Pollution.UpdateForNewTurn(gameData, us);
		Assert.False(city.location.HasPollution());
	}

	[Fact]
	public void WorkersClearTheDamage() {
		Tile tile = Pollution.TilesAtRisk(city, gameData).First();
		Assert.False(ClearDamage.MeetsRequirements(us, tile));
		tile.overlays.Add(gameData.terrainImprovements.Find(i => i.key == Tile.TileOverlays.POLLUTION));
		Assert.True(ClearDamage.MeetsRequirements(us, tile));
		// Before any other job there.
		Assert.All(gameData.Terraforms.Where(t => t != ClearDamage && t.MeetsRequirements(us, tile)),
			t => Assert.True(t.CalculateAIScore(us, tile) < ClearDamage.CalculateAIScore(us, tile)));

		ClearDamage.OnComplete(us, tile);
		Assert.False(tile.HasPollution());
		Assert.False(ClearDamage.MeetsRequirements(us, tile));
	}

	[Fact]
	public async Task CitiesPolluteAtTheEndOfTheTurn() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		Player owner = game.players.First(p => !p.isHuman && !p.isBarbarians);
		City mill = CityInteractions.BuildCity(owner.units[0].location, owner, "Mill Town");
		mill.AddBuilding(game.Buildings.Find(b => b.name == "Factory"), game);
		game.rules.PollutionChancePerPoint = 100;
		EngineStorage.messagesToUI.Clear();

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();

		Assert.Contains(mill.location.GetTilesWithinRankDistance(game.rules.MaxRankOfWorkableTiles), t => t.HasPollution());
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgPollution>(), m => m.city == mill);
	}
}
