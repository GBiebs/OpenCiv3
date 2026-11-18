using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Editor;
using QueryCiv3;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;
using Xunit.Abstractions;

namespace EngineTests.Editor;

// Every Civ3 scenario as the editor would take it: opened, saved as a
// scenario, opened again from that file, then played a few turns with the
// computer playing every civ. A scenario with a map starts as Load Scenario
// starts it (the first civ a human may pick plays); a rules mod gets a map
// made for it, as a new game on its rules would.
//
// Slow (some of Civ3's maps hold hundreds of units), so it runs only when
// asked: C7_EDITOR_CORPUS gives the number of turns to play (0 only opens
// and saves), C7_EDITOR_CORPUS_DIR the folder of .biq files (Civ3's
// Conquests/Scenarios by default; conquests.biq is added to Civ3's), and
// C7_EDITOR_CORPUS_OUT a file for the report, C7_EDITOR_CORPUS_ONLY a part
// of the names of the files to try.
//   C7_EDITOR_CORPUS=3 dotnet test EngineTests --filter "FullyQualifiedName~ScenarioCorpusTest"
public class ScenarioCorpusTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly ITestOutputHelper output;

	public ScenarioCorpusTest(SaveGameFixture fixture, ITestOutputHelper output) {
		this.fixture = fixture;
		this.output = output;
		EngineStorage.messagesToUI.Clear();
		EngineStorage.pendingMessages.Clear();
	}

	[SkippableFact]
	public async Task EveryScenarioOpensSavesAndPlays() {
		string turnsSetting = Environment.GetEnvironmentVariable("C7_EDITOR_CORPUS");
		Skip.If(!int.TryParse(turnsSetting, out int turns), "Set C7_EDITOR_CORPUS to the number of turns to play");
		string conquests = Path.Join(Civ3Location.GetCiv3Path() ?? "", "Conquests");
		string folder = Environment.GetEnvironmentVariable("C7_EDITOR_CORPUS_DIR") ?? Path.Join(conquests, "Scenarios");
		Skip.If(!Directory.Exists(folder), $"No scenarios at {folder}");
		List<string> files = Directory.GetFiles(folder, "*.biq").Order().ToList();
		if (Environment.GetEnvironmentVariable("C7_EDITOR_CORPUS_DIR") == null && File.Exists(PathUtils.defaultBicPath)) {
			files.Insert(0, PathUtils.defaultBicPath);
		}
		string only = Environment.GetEnvironmentVariable("C7_EDITOR_CORPUS_ONLY");
		if (!string.IsNullOrEmpty(only)) {
			files = files.Where(f => Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		new MsgSetAnimationsEnabled(false).send();
		EngineStorage.ProcessNextMessageToEngine();

		List<string> report = [];
		List<string> failures = [];
		using TempDirectory dir = new("scenario-corpus");
		foreach (string file in files) {
			string name = Path.GetFileName(file);
			Stopwatch watch = Stopwatch.StartNew();
			string step = "open";
			try {
				SaveGame opened = ScenarioFiles.Open(file, PathUtils.defaultBicPath, PediaIconsFor(conquests, Path.GetDirectoryName(file)));
				step = "edit";
				C7GameData.GameData edited = ScenarioEditing.Open(opened, fixture.behaviors);
				EngineStorage.InitializeGameDataForTests(edited);
				step = "save";
				string path = dir.File(Path.GetFileNameWithoutExtension(file) + ".json");
				ScenarioFiles.Save(ScenarioFiles.ToScenario(edited, opened.Scenario), path);
				step = "load";
				SaveGame scenario = SaveGame.Load(path, s => s);
				string kind = opened.Scenario.customMap
					? $"map {scenario.Map.tilesWide}x{scenario.Map.tilesTall}, {scenario.Players.Count} players, {scenario.Cities.Count} cities, {scenario.Units.Count} units"
					: "rules";
				if (turns > 0) {
					step = "start";
					await Start(scenario);
					step = "play";
					Play(turns);
				}
				string played = turns > 0 ? $", reached turn {EngineStorage.gameData.turn}" : "";
				string line = $"OK    {name}: {kind}{played}, {watch.ElapsedMilliseconds} ms";
				report.Add(line);
				output.WriteLine(line);
			} catch (Exception e) {
				string line = $"FAIL  {name} ({step}): {e.GetType().Name}: {e.Message.Split('\n')[0]}";
				report.Add(line);
				failures.Add(line);
				output.WriteLine(line);
				output.WriteLine(e.ToString());
			} finally {
				EngineStorage.messagesToUI.Clear();
				EngineStorage.pendingMessages.Clear();
				EngineStorage.frameBudget = TimeSpan.Zero;
			}
		}
		report.Add($"{files.Count - failures.Count} of {files.Count} ok, {turns} turns each");
		string outPath = Environment.GetEnvironmentVariable("C7_EDITOR_CORPUS_OUT");
		if (!string.IsNullOrEmpty(outPath)) {
			File.WriteAllLines(outPath, report);
		}
		Assert.Empty(failures);
	}

	// The scenario's own PediaIcons file, else Civ3's (as the editor does
	// without Civ3's mode, see ScenarioEditorSession).
	internal static Func<string, string> PediaIconsFor(string conquests, string scenarios) {
		string civ3s = Path.GetFullPath(Path.Combine(conquests, "Text", "PediaIcons.txt"));
		return modPath => {
			if (string.IsNullOrEmpty(modPath)) {
				return civ3s;
			}
			foreach (string folder in modPath.Split(';')) {
				string relative = OperatingSystem.IsWindows() ? folder : folder.Replace("\\conquests\\", "/Conquests/");
				string own = Path.GetFullPath(Path.Combine(scenarios, relative, "Text", "PediaIcons.txt"));
				if (File.Exists(own)) {
					return own;
				}
			}
			return civ3s;
		};
	}

	// Starts the game as the game's setup screens would: Load Scenario's for
	// a scenario with a map, a new game's for a rules mod. Then the computer
	// plays everyone, as in Watch AI Game.
	private async Task Start(SaveGame scenario) {
		Difficulty difficulty = scenario.Difficulties.First();
		if (scenario.Map.tiles.Count > 0 && scenario.Players.Count > 0) {
			SavePlayer human = scenario.Players.FirstOrDefault(p => p.canBePicked && !p.isBarbarian)
				?? scenario.Players.First(p => !p.isBarbarian);
			foreach (SavePlayer player in scenario.Players) {
				player.human = player == human;
			}
			scenario.GameDifficulty = difficulty;
			GameSetup.PlaceScenarioKings(scenario);
		} else {
			// The smallest world, to keep it quick.
			WorldSize size = scenario.WorldSizes.OrderBy(w => w.width * w.height).First();
			new GameSetup {
				playerCivilization = scenario.Civilizations.First(c => !c.isBarbarian),
				difficulty = difficulty,
				worldCharacteristics = new WorldCharacteristics(scenario) { worldSize = size, mapSeed = 1 },
				opponents = Enumerable.Repeat(new SelectedOpponent { isRandom = true }, Math.Max(1, size.numberOfCivs - 1)).ToList(),
				victoryConditions = scenario.VictoryConditions ?? new VictoryConditions(),
			}.Populate(scenario);
		}
		await CreateGame.createGame(scenario, _ => fixture.behaviors);
		EngineStorage.gameData.observerMode = true;
		foreach (Player player in EngineStorage.gameData.players) {
			player.isHuman = false;
		}
	}

	private static void Play(int turns) {
		C7GameData.GameData gameData = EngineStorage.gameData;
		try {
			TurnHandling.OnBeginTurn();
			TurnHandling.InitTurnData();
			Task firstTurn = TurnHandling.AdvanceTurn();
			EngineWaits.WaitForStartTurnMessage(_ => { });
			firstTurn.GetAwaiter().GetResult();
			for (int turn = 0; turn < turns && !gameData.gameOver; ++turn) {
				new MsgEndTurn { turn = gameData.turn }.send();
				EngineWaits.WaitForStartTurnMessage(_ => { });
			}
		} finally {
			gameData.observerMode = false;
		}
	}
}
