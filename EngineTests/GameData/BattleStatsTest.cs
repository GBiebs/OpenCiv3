using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Battle odds, the luck rating and game actions (advanced-statistics branch).
public class BattleStatsTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public BattleStatsTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		EngineStorage.animationsEnabled = false;
		// Engine message queue is static; parallelization is disabled repo-wide (XunitSettings.cs)
		EngineStorage.messagesToUI.Clear();
	}

	// ---------------------------------------------------------------- exact odds

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
		Assert.Equal(ClosedForm(roundWinChance, attackerHp, defenderHp), CombatOdds.AttackerWinChance(roundWinChance, attackerHp, defenderHp), 12);
	}

	[Fact]
	public void OddsBasics() {
		Assert.Equal(0.25, CombatOdds.AttackerWinChance(0.25, 1, 1), 12);
		Assert.Equal(0.5, CombatOdds.AttackerWinChance(0.5, 4, 4), 12);
		// A defensive bombard that always hits is the same as starting a hit point down.
		Assert.Equal(CombatOdds.AttackerWinChance(0.6, 2, 3), CombatOdds.AttackerWinChance(0.6, 3, 3, defensiveBombardHitChance: 1), 12);
		Assert.Equal(CombatOdds.AttackerWinChance(0.6, 3, 3), CombatOdds.AttackerWinChance(0.6, 3, 3, defensiveBombardHitChance: 0), 12);
	}

	// ---------------------------------------------------------------- real battles

	private C7GameData.GameData NewGame() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		return gameData;
	}

	// Two adjacent empty land tiles next to the human's start, with an empty land
	// tile behind the second one (where a defender retreats to).
	private static (Tile from, Tile to) BattleTiles(C7GameData.GameData gameData) {
		Tile start = gameData.players.First(p => p.isHuman).units[0].location;
		static bool IsFree(Tile t) => t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity();
		foreach (Tile from in start.neighbors.Values.Where(IsFree)) {
			foreach (KeyValuePair<TileDirection, Tile> n in from.neighbors) {
				if (IsFree(n.Value) && n.Value != start && n.Value.neighbors.TryGetValue(n.Key, out Tile behind) && IsFree(behind))
					return (from, n.Value);
			}
		}
		throw new Exception("no room for a battle near the human start");
	}

	private static MapUnit Spawn(C7GameData.GameData gameData, Player owner, string unit, Tile tile) {
		gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == unit), tile);
		return owner.units.Last();
	}

	// Fights `count` fresh battles and returns the recording side's stats.
	private async Task<PlayerStats> Fight(C7GameData.GameData gameData, Player attackerOwner, string attackerUnit,
			Player defenderOwner, string defenderUnit, int count, bool catapultWithDefender = false) {
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
				if (unit != null && unit.hitPointsRemaining > 0) unit.RemoveFromPlay();
			}
		}
		Player recorder = attackerOwner.isBarbarians ? defenderOwner : attackerOwner;
		return recorder.stats;
	}

	// The recorded wins must agree with the recorded expectations (within 4 standard deviations).
	private static void AssertLuckIsPlausible(PlayerStats stats, int battles) {
		LuckRating luck = stats.Luck().Value;
		Assert.Equal(battles, luck.battles);
		Assert.True(Math.Abs(luck.WinsAboveExpected) < 4 * Math.Sqrt(luck.variance),
			$"won {luck.won} of {luck.battles}, expected {luck.expectedWins:F1} (sd {Math.Sqrt(luck.variance):F1})");
	}

	[Theory]
	[InlineData("Warrior", "Spearman", false)] // plain rounds
	[InlineData("Warrior", "Spearman", true)]  // a defensive bombard first
	[InlineData("Horseman", "Spearman", false)] // the attacker can retreat
	[InlineData("Warrior", "Horseman", false)]  // the defender can retreat
	public async Task RecordedOddsMatchManyRealBattles(string attackerUnit, string defenderUnit, bool catapult) {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		Player barbarians = gameData.players.First(p => p.isBarbarians);

		PlayerStats stats = await Fight(gameData, human, attackerUnit, barbarians, defenderUnit, 4000, catapult);

		AssertLuckIsPlausible(stats, 4000);
		Assert.Equal(0, barbarians.stats.TurnCount); // barbarians keep no statistics
	}

	[Fact]
	public async Task DefendingBattlesAreRecordedFromTheDefendersSide() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		Player barbarians = gameData.players.First(p => p.isBarbarians);

		PlayerStats stats = await Fight(gameData, barbarians, "Warrior", human, "Spearman", 2000);

		AssertLuckIsPlausible(stats, 2000);
		NotableBattle notable = stats.luckiestWin ?? stats.unluckiestLoss;
		Assert.False(notable.attacking);
		Assert.Equal("Spearman", notable.unit);
	}

	[Fact]
	public async Task BothCivsRecordTheSameBattle() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		Player ai = gameData.players.First(p => !p.isHuman && !p.isBarbarians);

		await Fight(gameData, ai, "Warrior", human, "Warrior", 1);

		Assert.Equal(1, ai.stats.battles.Single());
		Assert.Equal(1, human.stats.battles.Single());
		Assert.Equal(1, ai.stats.battlesWon.Single() + human.stats.battlesWon.Single());
		Assert.InRange(ai.stats.battleExpectedWins10k.Single() + human.stats.battleExpectedWins10k.Single(), 9_999, 10_001);
	}

	[Fact]
	public async Task LuckIsSplitIntoCivsAndBarbarians() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		Player ai = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		Player barbarians = gameData.players.First(p => p.isBarbarians);

		await Fight(gameData, human, "Warrior", barbarians, "Warrior", 300);
		await Fight(gameData, barbarians, "Warrior", human, "Spearman", 100);
		await Fight(gameData, ai, "Warrior", human, "Warrior", 200);

		LuckRating all = human.stats.Luck().Value;
		LuckRating againstBarbarians = human.stats.LuckAgainstBarbarians().Value;
		LuckRating againstCivs = human.stats.LuckAgainstCivs().Value;
		Assert.Equal(400, againstBarbarians.battles);
		Assert.Equal(200, againstCivs.battles);

		// The two parts make up the whole.
		Assert.Equal(all.battles, againstCivs.battles + againstBarbarians.battles);
		Assert.Equal(all.won, againstCivs.won + againstBarbarians.won);
		Assert.Equal(all.expectedWins, againstCivs.expectedWins + againstBarbarians.expectedWins, 6);
		Assert.Equal(all.variance, againstCivs.variance + againstBarbarians.variance, 6);

		// Against civs, one side's luck is the other's bad luck.
		LuckRating theirs = ai.stats.LuckAgainstCivs().Value;
		Assert.Null(ai.stats.LuckAgainstBarbarians());
		Assert.Equal(0, againstCivs.WinsAboveExpected + theirs.WinsAboveExpected, 1);
	}

	[Fact]
	public void SeriesMissingFromAnOlderSaveAreFilledIn() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		MapUnit ours = Spawn(gameData, human, "Warrior", human.units[0].location);
		MapUnit theirs = Spawn(gameData, barbarians, "Warrior", human.units[0].location);

		// Three recorded turns, from before the barbarian series existed.
		PlayerStats stats = human.stats;
		for (int turn = gameData.turn - 2; turn <= gameData.turn; turn++) {
			stats.StartTurn(turn);
			stats.EndTurn(human, default);
		}
		foreach (List<int> series in new[] { stats.barbarianBattles, stats.barbarianBattlesWon, stats.barbarianExpectedWins10k, stats.barbarianVariance10k }) {
			series.Clear();
		}

		// A battle in the current turn's entry, then the next turn.
		stats.RecordBattle(human, gameData, won: true, 0.5, ours, theirs, attacking: true);
		Assert.Equal([0, 0, 1], stats.barbarianBattles);
		stats.StartTurn(gameData.turn + 1);
		Assert.All(stats.AllSeries(), series => Assert.Equal(4, series.Count));
	}

	// ---------------------------------------------------------------- the luck rating

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
		Assert.Equal(barbarians.civilization.adjective, human.stats.luckiestWin.enemyCivilization);
	}

	[Fact]
	public void LuckRatingMath() {
		LuckRating luck = new(battles: 10, won: 7, expectedWins: 5, variance: 2.5);
		Assert.Equal(2, luck.WinsAboveExpected, 12);
		Assert.Equal(2 / Math.Sqrt(2.5), luck.ZScore.Value, 12);

		Assert.Equal(0.5, LuckRating.NormalCdf(0), tolerance: 1e-6);
		Assert.Equal(0.975, LuckRating.NormalCdf(1.959964), tolerance: 1e-6);
		Assert.Equal(0.158655254, LuckRating.NormalCdf(-1), tolerance: 1e-6);
		Assert.Null(new LuckRating(1, 1, 1, 0).ZScore); // a certain battle tells nothing about luck
	}

	// ---------------------------------------------------------------- game actions

	private static void Process(MessageToEngine message) {
		message.send();
		EngineStorage.ProcessNextMessageToEngine();
	}

	[Fact]
	public async Task PlayerCommandsCountAsActionsAndHousekeepingDoesNot() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), _ => fixture.behaviors);
		MapUnit worker = human.units.First(u => u.unitType.name == "Worker");

		Process(new MsgSetFortification(worker.id, true));
		Process(new MsgChangeSliders(human.id, MsgChangeSliders.DomesticPolicyChoice.MoreScience));
		Process(new MsgSetAnimationsEnabled(false));
		Process(new MsgDiplomacyCompleted());

		Assert.Equal([2], human.stats.actions);
		Assert.All(EngineStorage.gameData.players.Where(p => p != human), p => Assert.Equal(0, p.stats.TurnCount));
	}

	[Fact]
	public void ActionsDuringATurnGoIntoThatTurnsEntry() {
		C7GameData.GameData gameData = NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		City city = CityInteractions.BuildCity(human.units[0].location, human, "Statsville");

		// Turn 0 has no entry from turn processing: the first action creates it, with a snapshot.
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
		Assert.Equal([0, 1], human.stats.turns.Select(t => t - human.stats.turns[0]));
		Assert.Equal([2, 1], human.stats.actions);
	}
}
