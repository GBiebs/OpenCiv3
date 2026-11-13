using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The statistics screen's records (PlayerStats): production, growth,
// finances, battle luck, game actions, units built and start ratings.
// Adapted from GBiebs/OpenCiv3.
public class StatisticsTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public StatisticsTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
	}

	private C7GameData.GameData NewGame() {
		C7GameData.GameData gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		return gameData;
	}

	private static MapUnit Spawn(C7GameData.GameData gameData, Player owner, string unit, Tile tile) {
		return gameData.SpawnUnit(owner, gameData.unitPrototypes.First(u => u.name == unit), tile);
	}

	// ---------------------------------------------------------------- the ledger

	[Fact]
	public void SeriesStayAlignedAndRecordingOutsideATurnDoesNothing() {
		PlayerStats stats = new();
		stats.RecordProduction(10, 1, 0, 0, 0, 0);
		stats.RecordGrowth(3, 1);
		Assert.Equal(0, stats.TurnCount);

		stats.StartTurn(5);
		stats.RecordProduction(10, 2, 1, 0, 3, 0);
		stats.RecordProduction(5, 0, 0, 0, 0, 2);
		stats.RecordGrowth(4, 1);
		Assert.Equal([15], stats.shieldsProduced);
		Assert.Equal([2], stats.shieldsCorrupted);
		Assert.Equal([3], stats.shieldsOverflowed);
		Assert.Equal([2], stats.shieldsCapped);
		Assert.All(stats.AllSeries(), s => Assert.Single(s));

		// Usable = 15 - 2 corrupted; wasted = 1 disorder + 3 overflow + 2 capped.
		Assert.Equal(1 - 6.0 / 13, stats.ProductionEfficiency().Value, 9);
		Assert.Equal(0.75, stats.GrowthEfficiency().Value, 9);
		Assert.Null(stats.Luck());
	}

	[Fact]
	public void ACityRecordsOverflowAFullBoxAndFood() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		City city = CityInteractions.BuildCity(human.units.First(u => u.unitType.isSettler).location, human, "Statsville");
		human.stats.StartTurn(gameData.turn);

		// A warrior with a nearly full box: what's beyond its cost is thrown away.
		UnitPrototype warrior = gameData.unitPrototypes.First(u => u.name == "Warrior");
		city.SetItemBeingProduced(warrior);
		int cost = human.ShieldCost(warrior);
		int yield = city.CurrentProductionYield().useful;
		Assert.True(yield > 0);
		city.SetStoredShields(cost - 1);
		Assert.Same(warrior, city.ComputeTurnProduction());
		Assert.Equal(yield - 1, human.stats.shieldsOverflowed.Last());
		Assert.Equal(0, human.stats.shieldsCapped.Last());

		// A settler in a size 1 city can't finish: the shields beyond a full box are lost.
		UnitPrototype settler = gameData.unitPrototypes.First(u => u.isSettler);
		city.SetItemBeingProduced(settler);
		city.SetStoredShields(human.ShieldCost(settler));
		Assert.Null(city.ComputeTurnProduction());
		Assert.Equal(yield, human.stats.shieldsCapped.Last());
		Assert.Equal(2 * (yield + city.CurrentProductionYield().corrupt), human.stats.shieldsProduced.Last());

		// Food beyond the box is wasted.
		int surplus = city.FoodGrowthPerTurn();
		Assert.True(surplus > 0);
		city.foodStored = city.FoodNeededToGrow() - 1;
		int sizeBefore = city.residents.Count;
		city.HandleCityGrowth(gameData);
		Assert.Equal(surplus, human.stats.foodSurplus.Last());
		Assert.Equal(surplus - 1, human.stats.foodWasted.Last());
		Assert.Equal(sizeBefore + 1, city.residents.Count);
	}

	[Fact]
	public void UnitsBuiltInCitiesAreCountedByType() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		City city = CityInteractions.BuildCity(human.units.First(u => u.unitType.isSettler).location, human, "Statsville");
		UnitPrototype warrior = gameData.unitPrototypes.First(u => u.name == "Warrior");
		city.SetItemBeingProduced(warrior);
		for (int i = 0; i < 2; i++) {
			city.SetStoredShields(human.ShieldCost(warrior));
			city.HandleCityProduction(gameData);
		}
		// A free unit isn't built.
		Spawn(gameData, human, "Warrior", city.location);
		Assert.Equal(new Dictionary<string, int> { ["Warrior"] = 2 }, human.stats.unitsBuilt);
	}

	// ---------------------------------------------------------------- battle odds and luck

	// The attacker wins if it scores d round wins before losing a: a negative binomial sum.
	private static double ClosedForm(double q, int a, int d) {
		double sum = 0;
		for (int k = 0; k < a; k++) {
			sum += Choose(d - 1 + k, k) * Math.Pow(q, d) * Math.Pow(1 - q, k);
		}
		return sum;
	}

	private static double Choose(int n, int k) {
		double result = 1;
		for (int i = 1; i <= k; i++) result = result * (n - k + i) / i;
		return result;
	}

	[Theory]
	[InlineData(0.5, 1, 1)]
	[InlineData(0.3, 3, 3)]
	[InlineData(0.7, 2, 4)]
	[InlineData(0.45, 5, 3)]
	[InlineData(0.9, 1, 5)]
	public void OddsMatchTheClosedForm(double roundWinChance, int attackerHp, int defenderHp) {
		Assert.Equal(ClosedForm(roundWinChance, attackerHp, defenderHp), BattleOdds.AttackerWinChance(roundWinChance, attackerHp, defenderHp), 12);
	}

	[Fact]
	public void OddsWithABombardAndRetreats() {
		// A defensive bombard that always hits is the same as starting a hit point down.
		Assert.Equal(BattleOdds.AttackerWinChance(0.6, 2, 3), BattleOdds.AttackerWinChance(0.6, 3, 3, defensiveBombardHitChance: 1), 12);
		// Two hit points each. An attacker that always withdraws from 2 to 1
		// loses if it loses the first round; after winning it, the defender is
		// down to 1, so it fights on to the end.
		Assert.Equal(0.6 * (0.6 + 0.4 * 0.6), BattleOdds.AttackerWinChance(0.6, 2, 2, attackerRetreatChance: 1), 12);
		// A defender that always withdraws has lost if it loses the first
		// round; after winning it, the attacker is down to 1, so it stays.
		Assert.Equal(0.6 + 0.4 * 0.6 * 0.6, BattleOdds.AttackerWinChance(0.6, 2, 2, defenderRetreatChance: 1), 12);
		// Nobody withdraws against an opponent with 1 hit point left.
		Assert.Equal(BattleOdds.AttackerWinChance(0.6, 2, 1), BattleOdds.AttackerWinChance(0.6, 2, 1, attackerRetreatChance: 1), 12);
	}

	// Two adjacent empty land tiles near the human's start, with an empty land
	// tile behind the second one (where a defender withdraws to).
	private static (Tile from, Tile to) BattleTiles(C7GameData.GameData gameData) {
		Tile start = gameData.players.First(p => p.isHuman).units[0].location;
		static bool IsFree(Tile t) => t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity();
		foreach (Tile from in start.GetTilesWithinRankDistance(3).Where(IsFree)) {
			foreach (KeyValuePair<TileDirection, Tile> n in from.neighbors) {
				if (IsFree(n.Value) && n.Value.neighbors.TryGetValue(n.Key, out Tile behind) && Tile.IsTileValid(behind) && IsFree(behind))
					return (from, n.Value);
			}
		}
		throw new Exception("no room for a battle near the human start");
	}

	// Fights `count` fresh battles of a human attacking or defending against
	// the barbarians, and returns the human's stats.
	private static async Task<PlayerStats> Fight(C7GameData.GameData gameData, bool humanAttacks, string attackerUnit, string defenderUnit,
			int count, bool catapultWithDefender = false) {
		Player human = gameData.players.First(p => p.isHuman);
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		(Player attackerOwner, Player defenderOwner) = humanAttacks ? (human, barbarians) : (barbarians, human);
		(Tile from, Tile to) = BattleTiles(gameData);
		for (int i = 0; i < count; i++) {
			MapUnit attacker = Spawn(gameData, attackerOwner, attackerUnit, from);
			MapUnit defender = Spawn(gameData, defenderOwner, defenderUnit, to);
			MapUnit catapult = null;
			if (catapultWithDefender) {
				catapult = Spawn(gameData, defenderOwner, "Catapult", to);
				catapult.defensiveBombardsRemaining = 1;
			}

			await attacker.Fight(defender);

			foreach (MapUnit unit in new[] { attacker, defender, catapult }) {
				if (unit != null && unit.hitPointsRemaining > 0 && gameData.mapUnits.Contains(unit)) {
					unit.RemoveFromPlay();
				}
			}
		}
		return human.stats;
	}

	// The recorded wins must agree with the recorded expectations (within 4
	// standard deviations): leaving out the bombard or a retreat would fail.
	private static void AssertLuckIsPlausible(PlayerStats stats, int battles) {
		LuckRating luck = stats.Luck().Value;
		Assert.Equal(battles, luck.battles);
		Assert.True(Math.Abs(luck.WinsAboveExpected) < 4 * Math.Sqrt(luck.variance),
			$"won {luck.won} of {luck.battles}, expected {luck.expectedWins:F1} (sd {Math.Sqrt(luck.variance):F1})");
	}

	[Theory]
	[InlineData(true, "Warrior", "Spearman", false)] // plain rounds
	[InlineData(true, "Warrior", "Spearman", true)]  // a defensive bombard first
	[InlineData(true, "Horseman", "Spearman", false)] // the attacker can withdraw
	[InlineData(true, "Warrior", "Horseman", false)]  // the defender can withdraw
	[InlineData(false, "Warrior", "Spearman", false)] // defending
	public async Task RecordedOddsMatchManyRealBattles(bool humanAttacks, string attackerUnit, string defenderUnit, bool catapult) {
		C7GameData.GameData gameData = NewGame();
		PlayerStats stats = await Fight(gameData, humanAttacks, attackerUnit, defenderUnit, 3000, catapult);

		AssertLuckIsPlausible(stats, 3000);
		NotableBattle notable = stats.luckiestWin ?? stats.unluckiestLoss;
		Assert.Equal(humanAttacks, notable.attacking);
		// Barbarians keep no statistics.
		Assert.Equal(0, gameData.players.First(p => p.isBarbarians).stats.TurnCount);
	}

	[Fact]
	public async Task BothCivsRecordTheSameBattle() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		Player ai = gameData.players.First(p => !p.isHuman && !p.IsIndependent);
		(Tile from, Tile to) = BattleTiles(gameData);
		await Spawn(gameData, ai, "Warrior", from).Fight(Spawn(gameData, human, "Warrior", to));

		Assert.Equal(1, ai.stats.battles.Single());
		Assert.Equal(1, human.stats.battles.Single());
		Assert.Equal(1, ai.stats.battlesWon.Single() + human.stats.battlesWon.Single());
		Assert.InRange(ai.stats.battleExpectedWins10k.Single() + human.stats.battleExpectedWins10k.Single(), 9_999, 10_001);
	}

	[Fact]
	public void NotableBattlesKeepTheLeastLikelyWinAndMostLikelyLoss() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		MapUnit ours = Spawn(gameData, human, "Warrior", human.units[0].location);
		MapUnit theirs = Spawn(gameData, barbarians, "Spearman", human.units[0].location);

		foreach ((bool won, double chance) in new[] { (true, 0.6), (true, 0.2), (true, 0.4), (false, 0.3), (false, 0.8), (false, 0.5) }) {
			human.stats.RecordBattle(human, gameData, won, chance, ours, theirs, attacking: true);
		}

		Assert.Equal(0.2, human.stats.luckiestWin.winChance);
		Assert.Equal(0.8, human.stats.unluckiestLoss.winChance);
		Assert.Equal("Spearman", human.stats.luckiestWin.enemyUnit);
		Assert.Equal([6], human.stats.battles);
		Assert.Equal([3], human.stats.battlesWon);
	}

	[Fact]
	public void LuckRatingMath() {
		LuckRating luck = new(battles: 10, won: 7, expectedWins: 5, variance: 2.5);
		Assert.Equal(2, luck.WinsAboveExpected, 12);
		Assert.Equal(2 / Math.Sqrt(2.5), luck.ZScore.Value, 12);
		Assert.Equal(0.5, LuckRating.NormalCdf(0), 6);
		Assert.Equal(0.975, LuckRating.NormalCdf(1.959964), 6);
		Assert.Equal(0.158655254, LuckRating.NormalCdf(-1), 6);
		// A certain battle tells nothing about luck.
		Assert.Null(new LuckRating(1, 1, 1, 0).ZScore);
	}

	// ---------------------------------------------------------------- game actions

	private static void Process(MessageToEngine message) {
		message.send();
		EngineStorage.ProcessNextMessageToEngine();
	}

	[Fact]
	public async Task OrdersCountAsActionsAndHousekeepingDoesNot() {
		Player human = await CreateGame.createGame(fixture.saveGame, _ => fixture.behaviors);
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		MapUnit unit = human.units.First();

		Process(new MsgSetFortification(unit.id, true));
		Process(new MsgChangeSliders(MsgChangeSliders.DomesticPolicyChoice.MoreScience));
		Process(new MsgSetAnimationsEnabled(false));
		Process(new MsgSelectUnit(unit.id));
		Process(new MsgToggleAnimationsEnabled());
		Process(new MsgToggleAnimationsEnabled());

		Assert.Equal([2], human.stats.actions);
		Assert.All(EngineStorage.gameData.players.Where(p => p != human), p => Assert.Empty(p.stats.actions));
	}

	[Fact]
	public void OnlyTheEnginesOwnMessagesAreNotActions() {
		HashSet<Type> notActions = [typeof(MsgShutdownEngine), typeof(MsgEndTurn), typeof(MsgSetAnimationsEnabled), typeof(MsgToggleAnimationsEnabled),
			typeof(MsgDiplomacyCompleted), typeof(MsgSelectUnit), typeof(MsgAskWhatToResearch), typeof(MsgPickDefaultResearch)];
		List<Type> messages = typeof(MessageToEngine).Assembly.GetTypes()
			.Where(t => t.IsSubclassOf(typeof(MessageToEngine)) && !t.IsAbstract && !t.IsSubclassOf(typeof(DebugMessage))).ToList();
		Assert.True(messages.Count > 40);
		foreach (Type type in messages) {
			MessageToEngine message = (MessageToEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
			Assert.True(message.IsGameAction != notActions.Contains(type), type.Name);
		}
	}

	[Fact]
	public void ActionsDuringATurnGoIntoThatTurnsEntry() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		City city = CityInteractions.BuildCity(human.units.First(u => u.unitType.isSettler).location, human, "Statsville");

		// Turn 0 has no entry from turn processing: the first action makes it, with a snapshot.
		human.stats.RecordAction(human, gameData);
		human.stats.RecordAction(human, gameData);
		Assert.Equal([gameData.turn], human.stats.turns);
		Assert.Equal([2], human.stats.actions);
		Assert.Equal([1], human.stats.cities);
		Assert.Equal([city.residents.Count], human.stats.population);

		// The next turn's processing opens its own entry, and actions during it go there.
		gameData.turn++;
		human.stats.StartTurn(gameData.turn);
		human.stats.EndTurn(human, default);
		human.stats.RecordAction(human, gameData);
		Assert.Equal([2, 1], human.stats.actions);
	}

	// ---------------------------------------------------------------- real turns, saves

	[Fact]
	public async Task RealTurnsRecordEveryCivAndSurviveASave() {
		await CreateGame.createGame(fixture.saveGame, _ => fixture.behaviors);
		C7GameData.GameData gameData = EngineStorage.gameData;
		gameData.observerMode = true;
		foreach (Player player in gameData.players) {
			player.isHuman = false;
		}
		const int turns = 3;
		try {
			TurnHandling.OnBeginTurn();
			TurnHandling.InitTurnData();
			Task firstTurn = TurnHandling.AdvanceTurn();
			EngineWaits.WaitForStartTurnMessage(_ => { });
			await firstTurn;
			for (int i = 0; i < turns; i++) {
				new MsgEndTurn { turn = gameData.turn }.send();
				EngineWaits.WaitForStartTurnMessage(_ => { });
			}
		} finally {
			gameData.observerMode = false;
		}

		List<Player> civs = gameData.players.Where(p => !p.IsIndependent && !p.defeated).ToList();
		Dictionary<Player, PlayerStats.LandCount> land = PlayerStats.CountLand(gameData.map);
		foreach (Player civ in civs) {
			PlayerStats stats = civ.stats;
			Assert.True(stats.TurnCount >= turns, $"{civ}: {stats.TurnCount} turns");
			Assert.All(stats.AllSeries(), s => Assert.Equal(stats.TurnCount, s.Count));
			Assert.Equal(civ.cities.Count, stats.cities.Last());
			Assert.Equal(civ.cities.Sum(c => c.residents.Count), stats.population.Last());
			Assert.Equal(land.GetValueOrDefault(civ).owned, stats.tilesOwned.Last());
		}
		Assert.Contains(civs, c => PlayerStats.Sum(c.stats.shieldsProduced) > 0);
		Assert.Contains(civs, c => PlayerStats.Sum(c.stats.foodSurplus) > 0);
		Assert.All(gameData.players.Where(p => p.IsIndependent), p => Assert.Equal(0, p.stats.TurnCount));

		// Saved and loaded, one line per series.
		Player first = civs[0];
		first.stats.unitsBuilt["Warrior"] = 4;
		string path = System.IO.Path.GetTempFileName();
		try {
			SaveGame.FromGameData(gameData).Save(path);
			string json = System.IO.File.ReadAllText(path);
			Assert.Matches(@"""shieldsProduced"": \[[-0-9,]*\]", json);
		} finally {
			System.IO.File.Delete(path);
		}
		C7GameData.GameData reloaded = SaveGame.FromJSON(SaveGame.FromGameData(gameData).ToCompactJSON()).ToGameData(fixture.behaviors);
		PlayerStats again = reloaded.GetPlayer(first.id).stats;
		Assert.Equal(first.stats.turns, again.turns);
		Assert.Equal(first.stats.shieldsProduced, again.shieldsProduced);
		Assert.Equal(first.stats.population, again.population);
		Assert.Equal(4, again.unitsBuilt["Warrior"]);
		Assert.Equal(first.stats.startRating.rating, again.startRating.rating, 9);
	}

	[Fact]
	public void OlderSavesLoadWithNoStatistics() {
		C7GameData.GameData gameData = NewGame();
		SaveGame save = SaveGame.FromGameData(gameData);
		foreach (SavePlayer player in save.Players) {
			player.stats = null;
		}
		C7GameData.GameData loaded = SaveGame.FromJSON(save.ToCompactJSON()).ToGameData(fixture.behaviors);
		Assert.All(loaded.players, p => Assert.Equal(0, p.stats.TurnCount));
		Assert.All(loaded.players, p => Assert.Null(p.stats.startRating));
	}

	// ---------------------------------------------------------------- start ratings

	[Fact]
	public async Task ANewGameRatesEveryCivAndTheAverageIs100() {
		await CreateGame.createGame(fixture.saveGame, _ => fixture.behaviors);
		C7GameData.GameData gameData = EngineStorage.gameData;
		List<Player> civs = gameData.players.Where(p => !p.IsIndependent).ToList();

		Assert.All(civs, p => Assert.NotNull(p.stats.startRating));
		Assert.Null(gameData.players.First(p => p.isBarbarians).stats.startRating);
		Tile start = civs[0].units.First(u => u.unitType.isSettler).location;
		Assert.Equal((start.XCoordinate, start.YCoordinate), (civs[0].stats.startRating.x, civs[0].stats.startRating.y));

		List<StartRating> ratings = civs.Select(p => p.stats.startRating).ToList();
		Assert.Equal(100, ratings.Average(r => r.rating), 9);
		Assert.Equal(100, ratings.Average(r => r.siteIndex), 9);
		Assert.Equal(100, ratings.Average(r => r.roomIndex), 9);
		Assert.Equal(100, ratings.Average(r => r.resourceIndex), 9);
		Assert.All(ratings, r => Assert.Equal(StartPositionRating.SiteWeight * r.siteIndex + StartPositionRating.RoomWeight * r.roomIndex
			+ StartPositionRating.ResourceWeight * r.resourceIndex, r.rating, 9));
		Assert.All(ratings, r => Assert.True(r.sitePoints > 0 && r.landShare > 0));

		// Only once: a later call (a reload on turn 0) keeps the ratings.
		StartRating original = civs[0].stats.startRating;
		StartPositionRating.RateNewGame(gameData);
		Assert.Same(original, civs[0].stats.startRating);
	}

	[Fact]
	public void LandIsSharedBetweenEquallyNearStarts() {
		C7GameData.GameData gameData = NewGame();
		Tile start = gameData.players.First(p => p.isHuman).units[0].location;
		Tile other = start.GetTilesWithinRankDistance(4).First(t => t.IsLand() && t.DistanceTo(start) == 4);
		Dictionary<Tile, double> alone = StartPositionRating.LandShares([start], 3);
		Dictionary<Tile, double> shared = StartPositionRating.LandShares([start, other], 3);
		Assert.True(shared[start] < alone[start]);
		// Every tile either start reaches is counted once in all.
		Dictionary<Tile, double> otherAlone = StartPositionRating.LandShares([other], 3);
		Assert.True(shared[start] + shared[other] <= alone[start] + otherAlone[other] + 1e-9);
	}
}
