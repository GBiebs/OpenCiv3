using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace EngineTests.Soak;

// Soak test: plays a whole all-AI game and checks that nothing crashes, hangs or
// leaves the game in an impossible state. Long AI games find bugs that short
// tests don't (three crashes were found this way). It runs only when SOAK_SEED
// is set, one game per process because a hang ends the process:
//
//   SOAK_SEED=7 SOAK_OUT=<dir> [SOAK_TURNS=300] [SOAK_TURN_LIMIT=120] \
//     dotnet test EngineTests --no-build --filter "FullyQualifiedName~SoakTest"
//
// It writes <dir>/seed_7.txt, whose first line starts with OK, CRASH, HANG or
// INVALID, followed by details and the end of the engine log. The seed picks the
// map, the civs and every AI and combat roll, so a seed replays the same game.
public class SoakTest {
	private class RingSink : ILogEventSink {
		public readonly ConcurrentQueue<string> lines = new();

		public void Emit(LogEvent e) {
			lines.Enqueue($"{e.Level} {e.RenderMessage()}");
			while (lines.Count > 300) {
				lines.TryDequeue(out _);
			}
		}
	}

	private static int Env(string name, int fallback) {
		return int.TryParse(Environment.GetEnvironmentVariable(name), out int v) ? v : fallback;
	}

	// A mid-sized map of 8 civs. The landform and barbarian activity vary with
	// the seed, so a run of seeds covers more of the game.
	private static SaveGame NewSave(GameMode gameMode, int seed) {
		SaveGame save = gameMode.GetSave();
		WorldSize worldSize = new() {
			width = 100,
			height = 100,
			numberOfCivs = 8,
			distanceBetweenCivs = 12,
			techRate = 240,
			optimalNumberOfCities = 20,
		};
		WorldCharacteristics.Landform[] landforms = [WorldCharacteristics.Landform.Pangaea, WorldCharacteristics.Landform.Continents, WorldCharacteristics.Landform.Archipelago];
		BarbarianActivity[] barbarians = [BarbarianActivity.Roaming, BarbarianActivity.Restless, BarbarianActivity.Sedentary, BarbarianActivity.Raging];
		WorldCharacteristics wc = new(save) {
			landform = landforms[seed % landforms.Length],
			oceanCoverage = WorldCharacteristics.OceanCoverage.Percent_70,
			age = WorldCharacteristics.Age.Billion_4,
			climate = WorldCharacteristics.Climate.Normal,
			temperature = WorldCharacteristics.Temperature.Temperate,
			barbarianActivity = barbarians[seed % barbarians.Length],
			worldSize = worldSize,
			mapSeed = seed,
		};
		// The neutral difficulty (AI cost factor 10): the AI plays at normal costs.
		Difficulty difficulty = save.Difficulties.FirstOrDefault(d => d.AiCostFactor == 10) ?? save.Difficulties[save.Difficulties.Count / 2];
		GameSetup gameSetup = new() {
			playerCivilization = save.Civilizations.Where(c => !c.isBarbarian).ElementAt(seed % 8),
			difficulty = difficulty,
			worldCharacteristics = wc,
			opponents = Enumerable.Repeat(new SelectedOpponent() { isRandom = true }, worldSize.numberOfCivs - 1).ToList(),
			victoryConditions = new VictoryConditions()
		};
		gameSetup.Populate(save);
		return save;
	}

	// Things that must always be true between turns. Returns what's wrong.
	internal static List<string> CheckInvariants(C7GameData.GameData gameData) {
		List<string> problems = new();
		HashSet<City> liveCities = gameData.cities.ToHashSet();
		foreach (City city in gameData.cities) {
			if (city.location.cityAtTile != city) {
				problems.Add($"{city.name} at {city.location} isn't the city on its own tile ({city.location.cityAtTile?.name ?? "none"})");
			}
			if (city.location.owningCity != city) {
				problems.Add($"{city.name} at {city.location} doesn't own its own tile ({city.location.owningCity?.name ?? "nobody"} does)");
			}
			if (!city.owner.cities.Contains(city)) {
				problems.Add($"{city.name} isn't in its owner's ({city.owner}) list of cities");
			}
			if (city.owner.defeated) {
				problems.Add($"{city.name} belongs to {city.owner}, who has been defeated");
			}
			if (city.residents.Count == 0) {
				problems.Add($"{city.name} has no citizens but is still in the game");
			}
		}
		foreach (Player player in gameData.players) {
			if (!player.isBarbarians && player.gold < 0) {
				problems.Add($"{player} has {player.gold} gold");
			}
			foreach (City city in player.cities) {
				if (!liveCities.Contains(city)) {
					problems.Add($"{player} still lists {city.name}, which is no longer in the game");
				}
			}
			foreach (MapUnit unit in player.units) {
				if (unit.owner != player) {
					problems.Add($"{unit} is in {player}'s list of units but belongs to {unit.owner}");
				}
			}
		}
		foreach (MapUnit unit in gameData.mapUnits) {
			if (unit.hitPointsRemaining <= 0) {
				problems.Add($"{unit} at {unit.location} has {unit.hitPointsRemaining} hit points but is still in the game");
			}
			if (!unit.location.unitsOnTile.Contains(unit) && !unit.IsLoaded()) {
				problems.Add($"{unit} thinks it's at {unit.location}, but that tile doesn't list it");
			}
		}
		foreach (Tile tile in gameData.map.tiles) {
			if (tile.owningCity != null && !liveCities.Contains(tile.owningCity)) {
				problems.Add($"{tile} is owned by {tile.owningCity.name}, which is no longer in the game");
			}
			// As in Civ3, units of different players never share a tile:
			// enemies are fought, captured or destroyed.
			if (tile.unitsOnTile.Select(u => u.owner).Distinct().Count() > 1) {
				problems.Add($"{tile} holds units of {string.Join(" and ", tile.unitsOnTile.Select(u => u.owner).Distinct())}");
			}
		}
		return problems;
	}

	[Fact]
	public async Task PlayAWholeGame() {
		if (!int.TryParse(Environment.GetEnvironmentVariable("SOAK_SEED"), out int seed)) {
			return;
		}
		string outDir = Environment.GetEnvironmentVariable("SOAK_OUT") ?? Path.GetTempPath();
		int turns = Env("SOAK_TURNS", 300);
		int turnLimitSeconds = Env("SOAK_TURN_LIMIT", 120);
		Directory.CreateDirectory(outDir);
		string resultFile = Path.Combine(outDir, $"seed_{seed}.txt");

		// The engine's loggers are created on first use, so set ours up first.
		RingSink ring = new();
		Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(ring).CreateLogger();
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();

		void Report(string firstLine, IEnumerable<string> details) {
			List<string> lines = [firstLine, .. details, "", "Last engine log lines:", .. ring.lines];
			File.WriteAllLines(resultFile, lines);
		}

		GameMode gameMode = GameMode.Load(PathUtils.GameModesDir, new GameMode.Config("civ3"));
		Stopwatch clock = Stopwatch.StartNew();
		Player human = await CreateGame.createGame(NewSave(gameMode, seed), _ => gameMode.behaviors);
		C7GameData.GameData gameData = EngineStorage.gameData;
		// Every civ is an AI. The engine hands control back at the UI controller's
		// turn, so the barbarians (who are never eliminated) are the controller.
		human.isHuman = false;
		Player observer = gameData.players.First(p => p.isBarbarians);
		EngineStorage.uiControllerID = observer.id;

		Stopwatch turnClock = Stopwatch.StartNew();
		using System.Threading.Timer watchdog = new(_ => {
			if (turnClock.Elapsed.TotalSeconds < turnLimitSeconds) {
				return;
			}
			Report($"HANG seed {seed} on turn {gameData.turn}: no progress for {turnLimitSeconds} s", []);
			Environment.Exit(3);
		}, null, 5000, 5000);

		try {
			TurnHandling.OnBeginTurn();
			TurnHandling.InitTurnData();
			await TurnHandling.AdvanceTurn();
			while (gameData.turn < turns && !gameData.gameOver) {
				turnClock.Restart();
				TurnHandling.OnEndTurn(observer);
				observer.hasPlayedThisTurn = true;
				await TurnHandling.AdvanceTurn();

				List<string> problems = CheckInvariants(gameData);
				if (problems.Count > 0) {
					Report($"INVALID seed {seed} on turn {gameData.turn}: {problems[0]}", problems.Take(20));
					Assert.Fail($"seed {seed}, turn {gameData.turn}: {problems[0]}");
				}
			}
		} catch (Exception e) when (e is not Xunit.Sdk.XunitException) {
			Report($"CRASH seed {seed} on turn {gameData.turn}: {e.GetType().Name}: {e.Message}", [e.StackTrace ?? ""]);
			throw;
		}

		int cities = gameData.cities.Count;
		int civsLeft = gameData.players.Count(p => !p.isBarbarians && !p.defeated);
		string ending = gameData.gameOver ? $", won by {gameData.winner?.civilization.name ?? "nobody"}" : "";
		Report($"OK seed {seed}: {gameData.turn} turns in {clock.Elapsed.TotalSeconds:F0} s, {civsLeft} civs and {cities} cities left{ending}", []);
	}
}
