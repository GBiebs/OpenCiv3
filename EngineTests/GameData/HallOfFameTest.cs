using System;
using System.Collections.Generic;
using System.IO;
using C7Engine;
using C7GameData;
using Xunit;

namespace EngineTests.GameData;

// Civ3's Hall of Fame: finished games recorded to a file and listed best
// score first.
public class HallOfFameTest : IDisposable {
	private readonly string directory = Path.Combine(Path.GetTempPath(), $"c7-hof-{Guid.NewGuid():N}");
	private string FilePath => Path.Combine(directory, HallOfFame.FileName);

	private readonly C7GameData.GameData gameData = new() {
		gameDifficulty = new Difficulty() { Name = "Monarch" },
		history = [],
		turn = 300,
	};
	private readonly Player us;
	private readonly Player them;
	private static readonly DateTime Day = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

	public HallOfFameTest() {
		EngineStorage.InitializeGameDataForTests(gameData);
		us = MakeCiv("player-1", "Romans", "Caesar");
		them = MakeCiv("player-2", "Greeks", "Alexander");
		gameData.history[us.id.ToString()] = [new HistTurnRecord { Score = 120 }, new HistTurnRecord { Score = 345 }];
		gameData.history[them.id.ToString()] = [new HistTurnRecord { Score = 500 }];
	}

	public void Dispose() {
		if (Directory.Exists(directory)) {
			Directory.Delete(directory, true);
		}
	}

	private Player MakeCiv(string id, string noun, string leader) {
		Player player = new() {
			id = ID.FromString(id),
			civilization = new Civilization(noun) { noun = noun, leader = leader },
		};
		gameData.players.Add(player);
		return player;
	}

	[Fact]
	public void AWinIsRecordedWithItsVictoryTypeAndScore() {
		HallOfFameEntry entry = HallOfFame.ForVictory(gameData, us, us, new DominationVictory(60, 66), "1850 AD", Day);

		Assert.Equal("Caesar", entry.Leader);
		Assert.Equal("Romans", entry.Civilization);
		Assert.Equal("Monarch", entry.Difficulty);
		Assert.Equal(345, entry.Score);
		Assert.Equal("Domination", entry.VictoryType);
		Assert.True(entry.Won);
		Assert.Equal("Romans", entry.Winner);
		Assert.Equal(300, entry.Turn);
		Assert.Equal("1850 AD", entry.GameDate);
		Assert.Equal("Domination (Won)", entry.Outcome);
	}

	[Fact]
	public void SomeoneElsesWinIsALoss() {
		HallOfFameEntry entry = HallOfFame.ForVictory(gameData, us, them, new SpaceRaceVictory(), "1900 AD", Day);
		Assert.False(entry.Won);
		Assert.Equal("Greeks", entry.Winner);
		Assert.Equal("Space Race (Lost)", entry.Outcome);
	}

	[Fact]
	public void VictoriesAreNamedAsCiv3sHallOfFameNamesThem() {
		Assert.Equal("Military", HallOfFame.VictoryTypeName(new ConquestVictory()));
		Assert.Equal("Cultural", HallOfFame.VictoryTypeName(new CulturalVictory(20000, 100000)));
		Assert.Equal("Diplomatic", HallOfFame.VictoryTypeName(new DiplomaticVictory()));
		Assert.Equal("None", HallOfFame.VictoryTypeName(new ScoreVictory()));
		Assert.Equal("None", HallOfFame.VictoryTypeName(null));
	}

	[Fact]
	public void RetiringAndDefeatAreRecordedToo() {
		Assert.Equal("Retired", HallOfFame.ForRetirement(gameData, us, "", Day).Outcome);
		Assert.Equal("None (Lost)", HallOfFame.ForDefeat(gameData, us, "", Day).Outcome);
	}

	[Fact]
	public void EntriesAreSavedAndLoadedBack() {
		Assert.Empty(HallOfFame.Load(FilePath));

		HallOfFameEntry first = HallOfFame.ForVictory(gameData, us, us, new DominationVictory(60, 66), "1850 AD", Day);
		HallOfFameEntry second = HallOfFame.ForRetirement(gameData, them, "1000 BC", Day.AddDays(1));
		Assert.True(HallOfFame.Add(FilePath, first));
		Assert.True(HallOfFame.Add(FilePath, second));

		Assert.Equal([first, second], HallOfFame.Load(FilePath));
	}

	// The owner's Cultural win was listed three times, 2026-11-12: the same
	// save loaded and won three times.
	[Fact]
	public void TheSameGameEndingTheSameWayIsRecordedOnce() {
		HallOfFameEntry win = HallOfFame.ForVictory(gameData, us, us, new CulturalVictory(20000, 100000), "1868 AD", Day);
		HallOfFameEntry again = HallOfFame.ForVictory(gameData, us, us, new CulturalVictory(20000, 100000), "1868 AD",
			Day.AddHours(3));
		Assert.True(HallOfFame.Add(FilePath, win));
		Assert.True(HallOfFame.Add(FilePath, again));
		Assert.Equal([win], HallOfFame.Load(FilePath));

		// A game that ended otherwise, or later, is another entry.
		gameData.turn = 301;
		HallOfFameEntry later = HallOfFame.ForVictory(gameData, us, us, new CulturalVictory(20000, 100000), "1870 AD", Day);
		HallOfFameEntry retired = HallOfFame.ForRetirement(gameData, us, "1870 AD", Day);
		Assert.True(HallOfFame.Add(FilePath, later));
		Assert.True(HallOfFame.Add(FilePath, retired));
		Assert.Equal([win, later, retired], HallOfFame.Load(FilePath));
	}

	[Fact]
	public void AnUnreadableFileHasNoEntries() {
		Directory.CreateDirectory(directory);
		File.WriteAllText(FilePath, "not json");
		Assert.Empty(HallOfFame.Load(FilePath));

		// Adding starts the file over rather than failing.
		HallOfFameEntry entry = HallOfFame.ForRetirement(gameData, us, "", Day);
		Assert.True(HallOfFame.Add(FilePath, entry));
		Assert.Equal([entry], HallOfFame.Load(FilePath));
	}

	[Fact]
	public void EntriesAreRankedBestScoreFirstAndFilteredByLeader() {
		HallOfFameEntry low = new() { Leader = "Caesar", Score = 100, RecordedAt = Day };
		HallOfFameEntry high = new() { Leader = "Alexander", Score = 900, RecordedAt = Day };
		HallOfFameEntry tieEarlier = new() { Leader = "Caesar", Score = 500, RecordedAt = Day };
		HallOfFameEntry tieLater = new() { Leader = "Caesar", Score = 500, RecordedAt = Day.AddDays(2) };
		List<HallOfFameEntry> entries = [low, tieLater, high, tieEarlier];

		Assert.Equal([(1, high), (2, tieEarlier), (3, tieLater), (4, low)], HallOfFame.Ranked(entries));
		Assert.Equal([(1, high), (2, tieEarlier), (3, tieLater), (4, low)], HallOfFame.Ranked(entries, HallOfFame.Everyone));
		Assert.Equal([(1, tieEarlier), (2, tieLater), (3, low)], HallOfFame.Ranked(entries, "Caesar"));
		Assert.Equal(["Alexander", "Caesar"], HallOfFame.Leaders(entries));
	}
}
