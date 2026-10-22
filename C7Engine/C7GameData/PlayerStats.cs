using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace C7GameData;

/// <summary>
/// Statistics recorded for one player once per turn, for the statistics screens.
///
/// Each series holds one value per recorded turn, in the same order as
/// <see cref="turns"/>. A turn's entry is opened by <see cref="StartTurn"/> while
/// the turn is processed (finances, city growth and production), the Record
/// methods add to it, and <see cref="EndTurn"/> takes the end-of-turn snapshot and
/// closes it. Recording without an open entry does nothing, so code paths outside
/// turn processing (and unit tests) don't need to know about statistics.
///
/// The series are written on one line each in saves, so a long game stays small.
/// </summary>
public class PlayerStats {
	// The game turn of each entry.
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> turns = new();

	// Production, in shields. Produced = everything the city tiles yield;
	// corrupted = lost to corruption (waste); lost to disorder / anarchy = what
	// would have been usable but was lost because a city was in civil disorder or
	// the government in anarchy; overflowed = shields beyond the cost when a build
	// finished (thrown away, as in Civ3); capped = shields that didn't fit in a
	// full box because the build couldn't finish (e.g. a Settler in a size 1 city).
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> shieldsProduced = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> shieldsCorrupted = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> shieldsLostToDisorder = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> shieldsLostToAnarchy = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> shieldsOverflowed = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> shieldsCapped = new();

	// Food: the cities' positive food surplus, and the part of it that didn't fit
	// in the food box (overflow when a city grows, or a full box at a size cap).
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> foodSurplus = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> foodWasted = new();

	// Snapshots at the end of the turn.
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> population = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> cities = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> tilesOwned = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> tilesImproved = new();

	// Money, from the turn's finances. Gold income = taxes, tax collectors, gold
	// from other civs, interest and wealth; expenses = building maintenance, unit
	// support and gold paid to other civs; commerce = all commerce the cities
	// made, before corruption.
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> goldIncome = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> goldExpenses = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> netGold = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> commerce = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> commerceCorrupted = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> beakers = new();

	// Recorded while the turn is played (after turn processing), into that turn's entry.
	// Actions = the player's commands (moves, orders, builds, research, sliders,
	// city tile and specialist changes), not UI-only clicks. Battles = fights the
	// player's units took part in, attacking or defending; expected wins and the
	// variance are in ten-thousandths of a battle (so even thousands of battles in
	// one turn fit in an int), from each battle's exact win chance.
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> actions = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> battles = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> battlesWon = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> battleExpectedWins10k = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> battleVariance10k = new();
	// The part of the battles above that was against barbarians (who keep no
	// statistics, so their side of these battles is never recorded); the rest
	// were against other civs.
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> barbarianBattles = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> barbarianBattlesWon = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> barbarianExpectedWins10k = new();
	[JsonConverter(typeof(CompactIntListConverter))] public List<int> barbarianVariance10k = new();

	// The least likely battle the player won, and the most likely one it lost.
	public NotableBattle luckiestWin;
	public NotableBattle unluckiestLoss;

	// The start position, rated when the game began (null for games started
	// before ratings existed, and for Civ3 saves).
	public StartRating startRating;

	/// Every per-turn series except <see cref="turns"/>.
	public IEnumerable<List<int>> AllSeries() => [
		shieldsProduced, shieldsCorrupted, shieldsLostToDisorder, shieldsLostToAnarchy, shieldsOverflowed, shieldsCapped,
		foodSurplus, foodWasted,
		population, cities, tilesOwned, tilesImproved,
		goldIncome, goldExpenses, netGold, commerce, commerceCorrupted, beakers,
		actions, battles, battlesWon, battleExpectedWins10k, battleVariance10k,
		barbarianBattles, barbarianBattlesWon, barbarianExpectedWins10k, barbarianVariance10k,
	];

	public int TurnCount => turns.Count;

	// Whether the last entry is still being recorded (not saved: a turn is
	// opened and closed while it is processed).
	private bool turnOpen = false;

	/// Opens the entry for <paramref name="turn"/>; the Record methods add to it.
	public void StartTurn(int turn) {
		Align();
		turns.Add(turn);
		foreach (List<int> series in AllSeries()) {
			series.Add(0);
		}
		turnOpen = true;
	}

	// A series added after a game began (missing from its save) gets zeros for
	// the turns before it, so every series has one entry per recorded turn.
	private void Align() {
		foreach (List<int> series in AllSeries()) {
			if (series.Count < turns.Count) {
				series.InsertRange(0, new int[turns.Count - series.Count]);
			}
		}
	}

	private bool HasOpenTurn => turnOpen;

	private static void Add(List<int> series, int amount) {
		series[^1] += amount;
	}

	public void RecordProduction(int produced, int corrupted, int lostToDisorder, int lostToAnarchy, int overflowed, int capped) {
		if (!HasOpenTurn) return;
		Add(shieldsProduced, produced);
		Add(shieldsCorrupted, corrupted);
		Add(shieldsLostToDisorder, lostToDisorder);
		Add(shieldsLostToAnarchy, lostToAnarchy);
		Add(shieldsOverflowed, overflowed);
		Add(shieldsCapped, capped);
	}

	public void RecordGrowth(int surplus, int wasted) {
		if (!HasOpenTurn) return;
		Add(foodSurplus, surplus);
		Add(foodWasted, wasted);
	}

	public void RecordFinances(PlayerCommerceBreakdown flows) {
		if (!HasOpenTurn) return;
		Add(goldIncome, flows.taxes + flows.taxmenTaxes + flows.fromOtherCivs + flows.interest + flows.wealthProduction);
		Add(goldExpenses, flows.maintenance + flows.unitSupport + flows.toOtherCivs);
		Add(netGold, flows.Netflows());
		Add(commerce, flows.corrupted + flows.taxes + flows.taxmenTaxes + flows.beakers + flows.happiness);
		Add(commerceCorrupted, flows.corrupted);
		Add(beakers, flows.beakers);
	}

	/// Records the end-of-turn snapshot and closes the entry.
	public void EndTurn(Player player, LandCount land) {
		if (!HasOpenTurn) return;
		population[^1] = player.cities.Sum(c => c.residents.Count);
		cities[^1] = player.cities.Count;
		tilesOwned[^1] = land.owned;
		tilesImproved[^1] = land.improved;
		turnOpen = false;
	}

	// Actions and battles happen while a turn is played, after turn processing
	// opened and closed its entry, so they go into the current turn's entry. If
	// there is none (turn 0, or a game saved before statistics were recorded), it
	// is created here with its end-of-turn snapshot.
	private void UseCurrentTurnEntry(Player player, GameData gameData) {
		if (turns.Count > 0 && turns[^1] == gameData.turn) {
			// The entry may come from a save made before some series existed.
			Align();
			return;
		}
		StartTurn(gameData.turn);
		EndTurn(player, CountLand(gameData.map).GetValueOrDefault(player));
	}

	public void RecordAction(Player player, GameData gameData) {
		if (player.isBarbarians) return;
		UseCurrentTurnEntry(player, gameData);
		Add(actions, 1);
	}

	/// Records a battle from <paramref name="player"/>'s side: whether it won, and
	/// its chance of winning before the dice were rolled.
	public void RecordBattle(Player player, GameData gameData, bool won, double winChance, MapUnit unit, MapUnit enemy, bool attacking) {
		if (player.isBarbarians) return;
		UseCurrentTurnEntry(player, gameData);
		Add(battles, 1);
		Add(battlesWon, won ? 1 : 0);
		Add(battleExpectedWins10k, (int)Math.Round(winChance * 1e4));
		Add(battleVariance10k, (int)Math.Round(winChance * (1 - winChance) * 1e4));
		if (enemy.owner.isBarbarians) {
			Add(barbarianBattles, 1);
			Add(barbarianBattlesWon, won ? 1 : 0);
			Add(barbarianExpectedWins10k, (int)Math.Round(winChance * 1e4));
			Add(barbarianVariance10k, (int)Math.Round(winChance * (1 - winChance) * 1e4));
		}

		NotableBattle battle = new() {
			turn = gameData.turn,
			unit = unit.unitType.name,
			enemyUnit = enemy.unitType.name,
			enemyCivilization = enemy.owner.civilization?.adjective,
			attacking = attacking,
			winChance = winChance,
		};
		if (won && (luckiestWin == null || winChance < luckiestWin.winChance)) luckiestWin = battle;
		if (!won && (unluckiestLoss == null || winChance > unluckiestLoss.winChance)) unluckiestLoss = battle;
	}

	public record struct LandCount(int owned, int improved);

	/// Counts each player's tiles and improved tiles in one pass over the map.
	public static Dictionary<Player, LandCount> CountLand(GameMap map) {
		Dictionary<Player, LandCount> result = new();
		foreach (Tile tile in map.tiles) {
			Player owner = tile.owningCity?.owner;
			if (owner == null) continue;
			LandCount count = result.GetValueOrDefault(owner);
			count.owned++;
			if (tile.overlays.HasBeenImproved()) count.improved++;
			result[owner] = count;
		}
		return result;
	}

	// Ratings over a range of turns (by default the whole game). They are null
	// when there's nothing to rate yet.

	/// Share of the usable shields that went into builds. Corruption and anarchy
	/// are not counted against it: it rates the losses the player controls
	/// (overflow, a full box, civil disorder).
	public double? ProductionEfficiency(int fromIndex = 0, int toIndex = int.MaxValue) {
		long usable = Sum(shieldsProduced, fromIndex, toIndex) - Sum(shieldsCorrupted, fromIndex, toIndex) - Sum(shieldsLostToAnarchy, fromIndex, toIndex);
		if (usable <= 0) return null;
		long wasted = Sum(shieldsLostToDisorder, fromIndex, toIndex) + Sum(shieldsOverflowed, fromIndex, toIndex) + Sum(shieldsCapped, fromIndex, toIndex);
		return 1.0 - (double)wasted / usable;
	}

	/// Share of the food surplus that went into growing the cities.
	public double? GrowthEfficiency(int fromIndex = 0, int toIndex = int.MaxValue) {
		long surplus = Sum(foodSurplus, fromIndex, toIndex);
		if (surplus <= 0) return null;
		return 1.0 - (double)Sum(foodWasted, fromIndex, toIndex) / surplus;
	}

	/// How lucky the player's battles were, or null before the first battle.
	public LuckRating? Luck(int fromIndex = 0, int toIndex = int.MaxValue) => Rating(Totals(fromIndex, toIndex));

	/// Luck in battles against barbarians only.
	public LuckRating? LuckAgainstBarbarians(int fromIndex = 0, int toIndex = int.MaxValue) => Rating(BarbarianTotals(fromIndex, toIndex));

	/// Luck in battles against other civs only. These are zero-sum: both sides
	/// record each battle, so all civs' luck against civs adds up to zero.
	public LuckRating? LuckAgainstCivs(int fromIndex = 0, int toIndex = int.MaxValue) {
		(long battles, long won, long expected10k, long variance10k) all = Totals(fromIndex, toIndex), barbarian = BarbarianTotals(fromIndex, toIndex);
		return Rating((all.battles - barbarian.battles, all.won - barbarian.won, all.expected10k - barbarian.expected10k, all.variance10k - barbarian.variance10k));
	}

	private (long, long, long, long) Totals(int from, int to) =>
		(Sum(battles, from, to), Sum(battlesWon, from, to), Sum(battleExpectedWins10k, from, to), Sum(battleVariance10k, from, to));

	private (long, long, long, long) BarbarianTotals(int from, int to) =>
		(Sum(barbarianBattles, from, to), Sum(barbarianBattlesWon, from, to), Sum(barbarianExpectedWins10k, from, to), Sum(barbarianVariance10k, from, to));

	private static LuckRating? Rating((long battles, long won, long expected10k, long variance10k) t) =>
		t.battles == 0 ? null : new LuckRating(t.battles, t.won, t.expected10k / 1e4, t.variance10k / 1e4);

	public static long Sum(List<int> series, int fromIndex = 0, int toIndex = int.MaxValue) {
		long sum = 0;
		for (int i = Math.Max(fromIndex, 0); i <= Math.Min(toIndex, series.Count - 1); i++) {
			sum += series[i];
		}
		return sum;
	}
}

/// A battle worth remembering, from the recording player's side.
public class NotableBattle {
	public int turn;
	public string unit;
	public string enemyUnit;
	public string enemyCivilization; // the adjective, e.g. "Barbarian" or "Celtic"
	public bool attacking;
	public double winChance;
}

/// Battles won against the number expected from each battle's win chance.
/// The z-score says how unusual the difference is (each battle is a weighted coin
/// flip, so the spread is the square root of the summed variances); the
/// percentile is the share of possible outcomes the player did better than.
public readonly record struct LuckRating(long battles, long won, double expectedWins, double variance) {
	public double WinsAboveExpected => won - expectedWins;

	public double? ZScore => variance > 0 ? WinsAboveExpected / Math.Sqrt(variance) : null;

	public double? Percentile => ZScore is double z ? NormalCdf(z) : null;

	// Standard normal cumulative distribution, via the Abramowitz and Stegun
	// 7.1.26 approximation of erf (error below 1.5e-7).
	public static double NormalCdf(double z) {
		double x = Math.Abs(z) / Math.Sqrt(2);
		double t = 1 / (1 + 0.3275911 * x);
		double erf = 1 - t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429)))) * Math.Exp(-x * x);
		return z >= 0 ? (1 + erf) / 2 : (1 - erf) / 2;
	}
}

/// Writes a list of ints on one line ([1,2,3]) even in indented saves, so long
/// per-turn statistics stay compact. Reads any JSON array of ints.
public class CompactIntListConverter : JsonConverter<List<int>> {
	public override List<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.StartArray) {
			throw new JsonException($"Expected an array of ints, got {reader.TokenType}");
		}
		List<int> result = new();
		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) {
			result.Add(reader.GetInt32());
		}
		return result;
	}

	public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options) {
		writer.WriteRawValue("[" + string.Join(",", value) + "]", skipInputValidation: true);
	}
}
