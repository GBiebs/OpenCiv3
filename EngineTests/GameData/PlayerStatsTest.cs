using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The per-turn statistics ledger (advanced-statistics branch).
public class PlayerStatsTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public PlayerStatsTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		EngineStorage.animationsEnabled = false;
		// Engine message queue is static; parallelization is disabled repo-wide (XunitSettings.cs)
		EngineStorage.messagesToUI.Clear();
	}

	private static List<List<int>> PublicSeries(PlayerStats stats) =>
		typeof(PlayerStats).GetFields(BindingFlags.Public | BindingFlags.Instance)
			.Where(f => f.FieldType == typeof(List<int>))
			.Select(f => (List<int>)f.GetValue(stats))
			.ToList();

	// ---------------------------------------------------------------- the ledger itself

	[Fact]
	public void EverySeriesGetsOneEntryPerTurn() {
		PlayerStats stats = new();
		stats.StartTurn(5);
		stats.StartTurn(6);
		stats.StartTurn(7);

		// AllSeries must list every series, or a new one would fall out of step.
		Assert.Equal(PublicSeries(stats).Count - 1, stats.AllSeries().Count());
		Assert.All(PublicSeries(stats), series => Assert.Equal(3, series.Count));
		Assert.Equal([5, 6, 7], stats.turns);
	}

	[Fact]
	public void RecordingOutsideATurnDoesNothing() {
		PlayerStats stats = new();
		stats.RecordProduction(10, 1, 1, 1, 1, 1);
		stats.RecordGrowth(5, 1);
		Assert.All(PublicSeries(stats), Assert.Empty);

		stats.StartTurn(1);
		stats.RecordGrowth(5, 1);
		stats.EndTurn(new Player(), default);
		stats.RecordGrowth(100, 100); // the turn is closed

		Assert.Equal([5], stats.foodSurplus);
		Assert.Equal([1], stats.foodWasted);
	}

	[Fact]
	public void EfficienciesCountOnlyControllableLosses() {
		PlayerStats stats = new();
		Assert.Null(stats.ProductionEfficiency());
		Assert.Null(stats.GrowthEfficiency());

		stats.StartTurn(1);
		// 100 shields, 20 corrupt and 0 anarchy leave 80 usable; 5 disorder + 3 overflow + 2 capped are wasted.
		stats.RecordProduction(produced: 100, corrupted: 20, lostToDisorder: 5, lostToAnarchy: 0, overflowed: 3, capped: 2);
		stats.RecordGrowth(surplus: 40, wasted: 10);
		stats.StartTurn(2);
		// Anarchy, like corruption, isn't held against the player: 50 - 10 - 30 = 10 usable, 1 overflow.
		stats.RecordProduction(produced: 50, corrupted: 10, lostToDisorder: 0, lostToAnarchy: 30, overflowed: 1, capped: 0);

		Assert.Equal(1 - 10.0 / 80, stats.ProductionEfficiency(0, 0).Value, 6);
		Assert.Equal(1 - 1.0 / 10, stats.ProductionEfficiency(1, 1).Value, 6);
		Assert.Equal(1 - 11.0 / 90, stats.ProductionEfficiency().Value, 6);
		Assert.Equal(0.75, stats.GrowthEfficiency().Value, 6);
	}

	// ---------------------------------------------------------------- what cities record

	private (C7GameData.GameData gameData, Player human, City city) GameWithACity() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		Player human = gameData.players.First(p => p.isHuman);
		City city = CityInteractions.BuildCity(human.units[0].location, human, "Statsville");
		human.stats.StartTurn(gameData.turn);
		return (gameData, human, city);
	}

	private static UnitPrototype Unit(C7GameData.GameData gameData, string name) => gameData.unitPrototypes.Find(u => u.name == name);

	[Fact]
	public void ShieldsBeyondTheCostOfAFinishedBuildAreOverflow() {
		(C7GameData.GameData gameData, Player human, City city) = GameWithACity();
		city.SetItemBeingProduced(Unit(gameData, "Warrior"));
		city.SetStoredShields(human.ShieldCost(city.itemBeingProduced));
		CorruptableValue production = city.CurrentProductionYield();
		Assert.True(production.useful > 0);

		Assert.NotNull(city.ComputeTurnProduction());

		PlayerStats stats = human.stats;
		Assert.Equal(production.useful + production.corrupt, stats.shieldsProduced[0]);
		Assert.Equal(production.corrupt, stats.shieldsCorrupted[0]);
		Assert.Equal(production.useful, stats.shieldsOverflowed[0]);
		Assert.Equal(0, stats.shieldsCapped[0]);
	}

	[Fact]
	public void ShieldsThatDontFitAFullBoxAreCapped() {
		(C7GameData.GameData gameData, Player human, City city) = GameWithACity();
		// A Settler costs 2 citizens, so a size 1 city can't finish it.
		city.SetItemBeingProduced(Unit(gameData, "Settler"));
		int cost = human.ShieldCost(city.itemBeingProduced);
		city.SetStoredShields(cost);
		CorruptableValue production = city.CurrentProductionYield();

		Assert.Null(city.ComputeTurnProduction());

		Assert.Equal(cost, city.shieldsStored);
		Assert.Equal(production.useful, human.stats.shieldsCapped[0]);
		Assert.Equal(0, human.stats.shieldsOverflowed[0]);
	}

	[Fact]
	public void InflowsUseAllTheirShields() {
		(C7GameData.GameData gameData, Player human, City city) = GameWithACity();
		city.SetItemBeingProduced(gameData.Inflows.Find(i => i.name == "Wealth"));

		city.ComputeTurnProduction();

		Assert.True(human.stats.shieldsProduced[0] > 0);
		Assert.Equal(0, human.stats.shieldsOverflowed[0]);
		Assert.Equal(0, human.stats.shieldsCapped[0]);
	}

	[Fact]
	public void DisorderLosesTheUsableShields() {
		(C7GameData.GameData gameData, Player human, City city) = GameWithACity();
		CorruptableValue normal = city.CurrentProductionYield();
		city.isInCivilDisorder = true;

		city.ComputeTurnProduction();

		Assert.Equal(normal.useful, human.stats.shieldsLostToDisorder[0]);
		Assert.Equal(normal.corrupt, human.stats.shieldsCorrupted[0]);
		Assert.Equal(0, human.stats.shieldsLostToAnarchy[0]);
	}

	[Fact]
	public void AnarchyLosesTheUsableShields() {
		(C7GameData.GameData gameData, Player human, City city) = GameWithACity();
		CorruptableValue normal = city.CurrentProductionYield();
		human.government = gameData.governments.First(g => g.transitionType);

		city.ComputeTurnProduction();

		Assert.Equal(normal.useful, human.stats.shieldsLostToAnarchy[0]);
		Assert.Equal(0, human.stats.shieldsLostToDisorder[0]);
	}

	[Fact]
	public void FoodBeyondTheBoxIsWasted() {
		(C7GameData.GameData gameData, Player human, City city) = GameWithACity();
		int surplus = city.FoodGrowthPerTurn();
		Assert.True(surplus > 0);

		city.foodStored = 0;
		city.HandleCityGrowth(gameData);
		Assert.Equal(surplus, human.stats.foodSurplus[0]);
		Assert.Equal(0, human.stats.foodWasted[0]);

		// With a full box, the whole surplus is beyond it.
		city.foodStored = city.FoodNeededToGrow();
		surplus = city.FoodGrowthPerTurn();
		city.HandleCityGrowth(gameData);
		Assert.Equal(surplus, human.stats.foodWasted[0]);
	}

	// ---------------------------------------------------------------- through real turns

	private static async Task EndTurn(Player human) {
		// What End Turn (MsgEndTurn) does.
		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();
	}

	[Fact]
	public async Task RealTurnsRecordEveryPlayer() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), _ => fixture.behaviors);
		C7GameData.GameData gameData = EngineStorage.gameData;
		City city = CityInteractions.BuildCity(human.units[0].location, human, "Statsville");
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();

		for (int i = 0; i < 3; i++) {
			await EndTurn(human);
		}

		foreach (Player player in gameData.players) {
			PlayerStats stats = player.stats;
			if (player.isBarbarians) {
				Assert.Equal(0, stats.TurnCount);
				continue;
			}
			Assert.Equal(3, stats.TurnCount);
			Assert.All(PublicSeries(stats), series => Assert.Equal(3, series.Count));
			Assert.Equal(Enumerable.Range(stats.turns[0], 3), stats.turns);

			// The end-of-turn snapshot matches the game.
			Assert.Equal(player.cities.Sum(c => c.residents.Count), stats.population[^1]);
			Assert.Equal(player.cities.Count, stats.cities[^1]);
			List<Tile> owned = gameData.map.tiles.Where(t => t.owningCity?.owner == player).ToList();
			Assert.Equal(owned.Count, stats.tilesOwned[^1]);
			Assert.Equal(owned.Count(t => t.overlays.HasBeenImproved()), stats.tilesImproved[^1]);
		}

		Assert.All(human.stats.shieldsProduced, shields => Assert.True(shields > 0));
		Assert.All(human.stats.population, size => Assert.True(size > 0));
	}

	[Fact]
	public async Task StatsSurviveSavingAndLoadingCompactly() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), _ => fixture.behaviors);
		CityInteractions.BuildCity(human.units[0].location, human, "Statsville");
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		await EndTurn(human);
		await EndTurn(human);

		string path = Path.Combine(Path.GetTempPath(), "openciv3-stats-roundtrip.json");
		SaveGame.FromGameData(EngineStorage.gameData).Save(path);
		string json = File.ReadAllText(path);
		SaveGame loaded = SaveGame.Load(path, s => s, s => s);
		File.Delete(path);

		// Each series is written on one line.
		Assert.Contains($"\"population\": [{string.Join(",", human.stats.population)}]", json);

		PlayerStats reloaded = loaded.Players.First(p => p.id == human.id).stats;
		Assert.Equal(PublicSeries(human.stats), PublicSeries(reloaded));
	}

	[Fact]
	public void SavesWithoutStatsLoadWithEmptyStats() {
		string path = Path.Combine(Path.GetTempPath(), "openciv3-stats-old-save.json");
		fixture.saveGame.Save(path);
		JsonNode save = JsonNode.Parse(File.ReadAllText(path));
		foreach (JsonNode player in save["players"].AsArray()) {
			Assert.True(player.AsObject().Remove("stats"));
		}
		File.WriteAllText(path, save.ToJsonString());

		SaveGame oldSave = SaveGame.Load(path, s => s, s => s);
		File.Delete(path);
		C7GameData.GameData gameData = oldSave.ToGameData(fixture.behaviors);

		Assert.All(gameData.players, p => Assert.Equal(0, p.stats.TurnCount));
		// Recording starts normally from the next turn.
		PlayerStats stats = gameData.players[0].stats;
		stats.StartTurn(1);
		Assert.All(PublicSeries(stats), series => Assert.Single(series));
	}
}
