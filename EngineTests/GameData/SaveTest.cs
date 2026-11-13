using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Newtonsoft.Json.Linq;
using QueryCiv3;
using Xunit;
using Serilog;

namespace EngineTests.GameData;

public class SaveTests : IClassFixture<SaveGameFixture> {
	SaveGameFixture fixture;

	public enum SaveType {
		basic,
		standalone
	}

	public SaveTests(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	// A fresh copy of the generated save, which the test may change freely.
	private SaveGame GetSave(SaveType type) {
		return type switch {
			SaveType.basic => fixture.saveGame,
			SaveType.standalone => fixture.standaloneSaveGame,
			_ => throw new ArgumentOutOfRangeException()
		};
	}

	[Theory]
	[InlineData(SaveType.basic, "basic")]
	[InlineData(SaveType.standalone, "standalone")]
	public void SimpleSave(SaveType saveType, string outputFilePostfix) {
		using TempDirectory output = new();

		// The baseline: the save as generated, written straight to a file
		// before anything else can touch it.
		string outputNeverGameDataPath = output.File($"static-save-never-game-data-{outputFilePostfix}.json");
		GetSave(saveType).Save(outputNeverGameDataPath);

		// An independent copy of the save, converted to GameData and back
		// before being written. A GameData shares objects with the save it was
		// made from, so converting the very same object would compare it with
		// itself.
		string outputWasGameDataPath = output.File($"static-save-was-game-data-{outputFilePostfix}.json");
		C7GameData.GameData gameData = ToGameData(GetSave(saveType));
		SaveGame saveWasGameData = SaveGame.FromGameData(gameData);
		saveWasGameData.Save(outputWasGameDataPath);

		string[] savedNeverGameData = File.ReadAllLines(outputNeverGameDataPath);
		string[] savedWasGameData = File.ReadAllLines(outputWasGameDataPath);

		// saved files should not be empty
		Assert.NotEmpty(savedNeverGameData);
		Assert.NotEmpty(savedWasGameData);

		string neverGameDataText = File.ReadAllText(outputNeverGameDataPath);
		string wasGameDataText = File.ReadAllText(outputWasGameDataPath);

		JObject neverGameDataJson = JObject.Parse(neverGameDataText);
		JObject wasGameDataJson = JObject.Parse(wasGameDataText);

		// Building a game starts an empty history for each player that has
		// none yet, which is the one difference converting may make.
		if (wasGameDataJson["history"] is JObject wasHistory) {
			JObject neverHistory = neverGameDataJson["history"] as JObject;
			foreach (JProperty entry in wasHistory.Properties().ToList()) {
				if (neverHistory?[entry.Name] == null) {
					Assert.Empty(Assert.IsType<JArray>(entry.Value));
					entry.Remove();
				}
			}
			if (neverHistory == null && !wasHistory.HasValues) {
				wasGameDataJson.Remove("history");
			}
		}

		// A game always has its random numbers' state and its difficulty's
		// index, so a save written without them has them once it's been a
		// game.
		foreach (string derived in new[] { "rngState", "gameDifficultyIndex" }) {
			if (neverGameDataJson[derived] == null) {
				wasGameDataJson.Remove(derived);
			}
		}

		// Nor does a save from before players' receivedStartUnits was saved
		// have it, which loading works out (see SaveGame.ConvertCities).
		if (neverGameDataJson["players"] is JArray neverPlayers && wasGameDataJson["players"] is JArray wasPlayers) {
			for (int i = 0; i < neverPlayers.Count && i < wasPlayers.Count; ++i) {
				if (neverPlayers[i]["receivedStartUnits"] == null && wasPlayers[i] is JObject wasPlayer) {
					wasPlayer.Remove("receivedStartUnits");
				}
			}
		}

		// saved files should be the same as the original
		Assert.True(JToken.DeepEquals(wasGameDataJson, neverGameDataJson),
			$"The save changed when converted to GameData and back; see {output.Path} (set C7_KEEP_TEST_OUTPUT to keep it)");
	}

	// The messages to the UI a headless game may send while the AIs play.
	private static void IgnoreExpectedMessages(MessageToUI msg) {
		switch (msg) {
			case MsgWarDeclaration:
			case MsgShowTemporaryPopup:
			case MsgCityDestroyed:
			case MsgCityCaptured:
			case MsgCivilizationDestroyed:
			case MsgVictory:
			case MsgCityChanged:
			case MsgCityProductionCompleted:
			// The human's palace may have earned upgrades in the first turn.
			case MsgShowPalaceUpgrade:
			// A scenario within twenty turns of its turn limit warns the
			// human that the game will end (TimeLimitVictory.MaybeWarn).
			case MsgShowDomesticAdvisorPopup:
			// The AIs that play before the human player may already hit the
			// human's units, as AI bombers do in the WWII scenarios, before
			// the human is made an AI too.
			case MsgUnitMoved:
				return;
			default:
				throw new Exception($"Unexpected message to the UI: {msg}");
		}
	}

	private static void WaitForStartTurnMessage() {
		EngineWaits.WaitForStartTurnMessage(IgnoreExpectedMessages);
	}

	private async Task<Player> CreateHeadlessGame(string path, string biqPath, Func<string, string> getPediaIconsPath) {
		GameParams options = new(biqPath) {
			GetPediaIconsPath = getPediaIconsPath,
			GameModeLoader = (_) => { return fixture.behaviors; },
		};
		Player player = await CreateGame.createGame(path, options);
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		return player;
	}

	private async Task<Player> CreateHeadlessGame(SaveGame game) {
		Player player = await CreateGame.createGame(game, (_) => { return fixture.behaviors; });
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		return player;
	}

	private C7GameData.GameData ToGameData(SaveGame game) {
		return game.ToGameData(fixture.behaviors);
	}

	private void CheckAiInvariants() {
		EngineStorage.ReadGameData((C7GameData.GameData gameData) => {
			C7GameData.GameData game = gameData;

			foreach (Player p in game.players) {
				foreach (MapUnit u in p.units) {
					if (u.unitType.name != "Settler") {
						continue;
					}

					// We don't require an escort if the settler is in a city.
					if (u.location.cityAtTile != null) {
						continue;
					}

					// This is a settler not in a city - make sure it has an escort.
					if (u.currentAI is SettlerAI settlerAi) {
						if (settlerAi.data.escort != null) {
							// A settler may take a soldier up to three tiles away as
							// its escort, which catches up with it on the way (see
							// SettlerAI.TryToTakeEscort), so it need not be on the
							// settler's tile yet, only close by.
							MapUnit escort = settlerAi.data.escort;
							Assert.True(escort.location.DistanceTo(u.location) <= 4, $"{u} at {u.location} is far from its escort {escort} at {escort.location}");
						} else {
							// This assertion is tempting, but will sometimes fire
							// if the escort is disbanded due to unit support costs.
							//
							// Assert.True(u.location.unitsOnTile.Count > 1, $"{u} {u.location}");
						}
					}
				}
			}
		});
	}

	[Theory]
	[InlineData(SaveType.basic, "basic")]
	[InlineData(SaveType.standalone, "standalone")]
	public async Task SimpleGame(SaveType saveType, string outputFilePostfix) {
		// Log to the console while the game plays, and put back whatever
		// logger the other tests use afterwards.
		ILogger previousLogger = Log.Logger;
		Serilog.Core.Logger consoleLogger = new LoggerConfiguration()
			.WriteTo.Console(outputTemplate: "[{Level:u3}] {Timestamp:HH:mm:ss} {SourceContext}: {Message:lj} {NewLine}{Exception}")
			.MinimumLevel.Information()
			.CreateLogger();
		Log.Logger = consoleLogger;
		try {
			await PlaySimpleGame(saveType, outputFilePostfix);
		} finally {
			Log.Logger = previousLogger;
			consoleLogger.Dispose();
		}
	}

	private async Task PlaySimpleGame(SaveType saveType, string outputFilePostfix) {
		SaveGame developerSave = GetSave(saveType);

		new MsgSetAnimationsEnabled(false).send();

		Player human = await CreateHeadlessGame(developerSave);

		// Make all the players AI players while we run the game in headless mode.
		human.isHuman = false;

		// Play out 50 turns.
		for (int i = 0; i < 50; ++i) {
			WaitForStartTurnMessage();
			CheckAiInvariants();
			new MsgEndTurn().send();
		}
		WaitForStartTurnMessage();

		// Make the player a human again so we can save and load the game.
		human.isHuman = true;
		C7GameData.GameData game = null;
		EngineStorage.ReadGameData((C7GameData.GameData gameData) => {
			game = gameData;
		});
		Assert.Equal(50, game.turn);

		Assert.True(game.players.Count(p => p.isIncludedInGame) == 9);
		Assert.True(game.players.Count(p => p.canBePicked) == 8);
		Assert.True(game.players.Count == 9);
		Assert.True(game.civilizations.Count == 32);

		using TempDirectory output = new();

		// Save the game.
		string outputDirectSavePath = output.File($"headless-game-direct-save-{outputFilePostfix}.json");
		SaveGame.FromGameData(game).Save(outputDirectSavePath);

		// Load the saved game and save it again.
		string roundTrippedSavePath = output.File($"headless-game-round-tripped-save-{outputFilePostfix}.json");
		C7GameData.GameData roundTrippedGameData = ToGameData(SaveGame.Load(outputDirectSavePath, (string unused) => { return unused; }));
		SaveGame.FromGameData(roundTrippedGameData).Save(roundTrippedSavePath);

		string[] directSaveLines = File.ReadAllLines(outputDirectSavePath);
		string[] roundTrippedSaveLines = File.ReadAllLines(roundTrippedSavePath);

		// The saved files should not be empty
		Assert.NotEmpty(directSaveLines);
		Assert.NotEmpty(roundTrippedSaveLines);

		// And they should be the same, despite round-tripping through the GameData format.
		Assert.Equal(directSaveLines, roundTrippedSaveLines);
	}

	// Saving in the background writes just what saving straight away does,
	// however the game changes meanwhile, and loading reads a file with or
	// without a byte order mark, or in UTF-16, alike.
	[Fact]
	public async Task SaveInBackgroundAndLoad() {
		using TempDirectory output = new();
		C7GameData.GameData gameData = ToGameData(GetSave(SaveType.basic));
		string direct = output.File("direct.json");
		SaveGame.FromGameData(gameData).Save(direct);
		byte[] expected = File.ReadAllBytes(direct);

		string background = output.File("background.json");
		System.Threading.Tasks.Task saving = SaveManager.SaveInBackground(gameData, background);
		gameData.history.Values.First().Add(new HistTurnRecord { Score = 1 });
		gameData.players[0].gold += 100;
		await saving;
		Assert.Equal(expected, File.ReadAllBytes(background));

		byte[] compact = SaveGame.Load(direct, _ => "").ToCompactJSON();
		string bom = output.File("bom.json");
		File.WriteAllBytes(bom, [0xEF, 0xBB, 0xBF, .. expected]);
		Assert.Equal(compact, SaveGame.Load(bom, _ => "").ToCompactJSON());
		string utf16 = output.File("utf16.json");
		File.WriteAllText(utf16, File.ReadAllText(direct), System.Text.Encoding.Unicode);
		Assert.Equal(compact, SaveGame.Load(utf16, _ => "").ToCompactJSON());

		Assert.ThrowsAny<Exception>(() => SaveManager.SaveInBackground(gameData, output.File("missing/dir/x.json")).Wait());
	}

	[Fact]
	public async Task WorldWrapDetails() {
		Player human = await CreateHeadlessGame(fixture.saveGame);
		WaitForStartTurnMessage();

		C7GameData.GameData gd = null;
		EngineStorage.ReadGameData((C7GameData.GameData gameData) => { gd = gameData; });

		Tile t0 = gd.map.tileAt(97, 33);
		Tile t1 = gd.map.tileAt(99, 33);
		Tile t2 = gd.map.tileAt(1, 33);

		Assert.Equal(1, t0.DistanceTo(t1));
		Assert.Equal(1, t1.DistanceTo(t0));

		Assert.Equal(1, t1.DistanceTo(t2));
		Assert.Equal(1, t2.DistanceTo(t1));

		Assert.Equal(2, t0.DistanceTo(t2));
		Assert.Equal(2, t2.DistanceTo(t0));
	}


	[Fact]
	public async Task TurnTimeCalculations() {
		Player human = await CreateHeadlessGame(fixture.saveGame);
		WaitForStartTurnMessage();

		C7GameData.GameData gd = null;
		EngineStorage.ReadGameData((C7GameData.GameData gameData) => { gd = gameData; });

		// YEARS

		// --------------- 50 turn interval for 25 turns ---------------
		Assert.Equal(-4000, gd.timeOptions.GetRawNumber(0));
		Assert.Equal(0, gd.timeOptions.GetTurnFromRaw(-4000));
		Assert.Equal(-3950, gd.timeOptions.GetRawNumber(1));
		Assert.Equal(1, gd.timeOptions.GetTurnFromRaw(-3950));
		Assert.Equal(2, gd.timeOptions.GetTurnFromRaw(-3900));
		Assert.Equal(-2800, gd.timeOptions.GetRawNumber(24));
		Assert.Equal(-2750, gd.timeOptions.GetRawNumber(25));

		// --------------- 40 turn interval for 25 turns ---------------
		Assert.Equal(-2710, gd.timeOptions.GetRawNumber(26));
		Assert.Equal(26, gd.timeOptions.GetTurnFromRaw(-2710));
		Assert.Equal(-2670, gd.timeOptions.GetRawNumber(27));
		Assert.Equal(-1790, gd.timeOptions.GetRawNumber(49));
		Assert.Equal(-1750, gd.timeOptions.GetRawNumber(50));

		// --------------- 25 turn interval for 40 turns ---------------
		Assert.Equal(-1725, gd.timeOptions.GetRawNumber(51));
		Assert.Equal(52, gd.timeOptions.GetTurnFromRaw(-1700));
		Assert.Equal(-1700, gd.timeOptions.GetRawNumber(52));
		Assert.Equal(-1675, gd.timeOptions.GetRawNumber(53));
		Assert.Equal(-1525, gd.timeOptions.GetRawNumber(59));
		Assert.Equal(-775, gd.timeOptions.GetRawNumber(89));
		Assert.Equal(-750, gd.timeOptions.GetRawNumber(90));
		Assert.Equal(90, gd.timeOptions.GetTurnFromRaw(-750));

		// --------------- 20 turn interval for 50 turns ---------------
		Assert.Equal(-730, gd.timeOptions.GetRawNumber(91));
		Assert.Equal(91, gd.timeOptions.GetTurnFromRaw(-730));
		Assert.Equal(-710, gd.timeOptions.GetRawNumber(92));
		Assert.Equal(-570, gd.timeOptions.GetRawNumber(99));
		Assert.Equal(-470, gd.timeOptions.GetRawNumber(104));
		Assert.Equal(230, gd.timeOptions.GetRawNumber(139));
		Assert.Equal(250, gd.timeOptions.GetRawNumber(140));
		Assert.Equal(140, gd.timeOptions.GetTurnFromRaw(250));

		// --------------- 10 turn interval for 100 turns ---------------
		Assert.Equal(260, gd.timeOptions.GetRawNumber(141));
		Assert.Equal(141, gd.timeOptions.GetTurnFromRaw(260));
		Assert.Equal(270, gd.timeOptions.GetRawNumber(142));
		Assert.Equal(280, gd.timeOptions.GetRawNumber(143));
		Assert.Equal(600, gd.timeOptions.GetRawNumber(175));
		//...
		Assert.Equal(1230, gd.timeOptions.GetRawNumber(238));
		Assert.Equal(1240, gd.timeOptions.GetRawNumber(239));
		Assert.Equal(1250, gd.timeOptions.GetRawNumber(240));

		// --------------- 5  turn interval for 100 turns ---------------
		Assert.Equal(1255, gd.timeOptions.GetRawNumber(241));
		Assert.Equal(241, gd.timeOptions.GetTurnFromRaw(1255));
		Assert.Equal(1260, gd.timeOptions.GetRawNumber(242));
		Assert.Equal(1560, gd.timeOptions.GetRawNumber(302));
		Assert.Equal(1745, gd.timeOptions.GetRawNumber(339));
		Assert.Equal(1750, gd.timeOptions.GetRawNumber(340));

		// --------------- 2  turn interval for 100 turns ---------------
		Assert.Equal(1752, gd.timeOptions.GetRawNumber(341));
		Assert.Equal(1754, gd.timeOptions.GetRawNumber(342));
		Assert.Equal(342, gd.timeOptions.GetTurnFromRaw(1754));
		// ...
		Assert.Equal(1948, gd.timeOptions.GetRawNumber(439));
		Assert.Equal(1950, gd.timeOptions.GetRawNumber(440));

		// ---------------   1  turn interval forEVER     ---------------
		Assert.Equal(1951, gd.timeOptions.GetRawNumber(441));
		Assert.Equal(1955, gd.timeOptions.GetRawNumber(445));
		Assert.Equal(445, gd.timeOptions.GetTurnFromRaw(1955));
		Assert.Equal(2000, gd.timeOptions.GetRawNumber(490));
		Assert.Equal(2500, gd.timeOptions.GetRawNumber(990));



		// MONTHS

		gd.timeOptions = new TimeOptions() {
			baseUnit = TimeUnit.Months,
			startMonth = 0, // to simplify the math
			timeScale = new int[,] { { 25, 35, 40, 50, 100, 100, 100, 1000 }, { 12, 6, 4, 3, 2, 2, 1, 1 } }
		};

		// 12 moths every turn for 25 turns
		Assert.Equal(0, gd.timeOptions.GetRawNumber(0));

		Assert.Equal(12, gd.timeOptions.GetRawNumber(1));
		Assert.Equal(1, gd.timeOptions.GetTurnFromRaw(12));

		Assert.Equal(24, gd.timeOptions.GetRawNumber(2));
		Assert.Equal(2, gd.timeOptions.GetTurnFromRaw(24));
		Assert.Equal(0, gd.timeOptions.GetTurnFromRaw(0));

		// 6 moths every turn for 35 turns
		Assert.Equal(306, gd.timeOptions.GetRawNumber(26));
		Assert.Equal(26, gd.timeOptions.GetTurnFromRaw(306));

		// 4 moths every turn for 40 turns
		Assert.Equal(514, gd.timeOptions.GetRawNumber(61));
		Assert.Equal(61, gd.timeOptions.GetTurnFromRaw(514));

		// WEEKS

		gd.timeOptions = new TimeOptions() {
			baseUnit = TimeUnit.Weeks,
			startWeek = 0, // to simplify the math
			timeScale = new int[,] { { 25, 35, 40, 50, 100, 100, 100, 1000 }, { 8, 4, 2, 2, 2, 2, 1, 1 } }
		};

		// 8 weeks every turn for 25 turns
		Assert.Equal(0, gd.timeOptions.GetRawNumber(0));
		Assert.Equal(0, gd.timeOptions.GetTurnFromRaw(0));

		Assert.Equal(8, gd.timeOptions.GetRawNumber(1));
		Assert.Equal(1, gd.timeOptions.GetTurnFromRaw(8));

		Assert.Equal(16, gd.timeOptions.GetRawNumber(2));
		Assert.Equal(2, gd.timeOptions.GetTurnFromRaw(16));

		// 4 weeks every turn for 35 turns
		Assert.Equal(220, gd.timeOptions.GetRawNumber(30));
		Assert.Equal(30, gd.timeOptions.GetTurnFromRaw(220));

		// 2 weeks every turn for 40 turns
		Assert.Equal(342, gd.timeOptions.GetRawNumber(61));
		Assert.Equal(61, gd.timeOptions.GetTurnFromRaw(342));
	}


	[SkippableFact]
	public async Task LoadSampleSaves() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");

		// The sample save, plus any .SAV files a developer has put in
		// EngineTests/data/saves to try.
		List<string> savePaths = [await RemoteFileCache.GetAsync(RemoteSaves.SampleGotmSave)];
		string localSavesPath = PathUtils.getDataPath("saves");
		if (Directory.Exists(localSavesPath)) {
			savePaths.AddRange(new DirectoryInfo(localSavesPath).EnumerateFiles("*.SAV")
				.Where(f => f.Name != RemoteSaves.SampleGotmSave.Name)
				.Select(f => f.FullName));
		}

		using TempDirectory output = new();
		int i = 0;
		foreach (string savePath in savePaths) {
			SaveGame game = null;
			C7GameData.GameData gd = null;
			Console.WriteLine(savePath);
			Exception ex = Record.Exception(() => {
				game = ImportCiv3.ImportSav(savePath, PathUtils.defaultBicPath, (relativeModePath) => {
					return PathUtils.defaultPediaIconsPath;
				});
			});
			Assert.True(ex == null, savePath + ": " + ex);
			ex = Record.Exception(() => {
				gd = ToGameData(game);
			});
			Assert.True(ex == null, savePath + ": " + ex);
			Assert.NotNull(game);
			Assert.NotNull(gd);
			game.Save(output.File($"gotm_save_{i}.json"));
			i++;
		}
		Assert.True(i > 0);
	}

	[SkippableFact]
	public async Task LoadAllConquestScenarios() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");

		string[] singleplayerScenarios = {
			"1 Mesopotamia.biq",
			// "2 Rise of Rome.biq" is covered by LoadRiseOfRomeScenario below
			"3 Fall of Rome.biq",
			"4 Middle Ages.biq",
			"5 Mesoamerica.biq",
			"6 Age of Discovery.biq",
			// skip for now because BIQ parsing fails
			// "7 Sengoku - Sword of the Shogun.biq",
			"8 Napoleonic Europe.biq",
			"9 WWII in the Pacific.biq",
		};
		string[] multiplayerScenarios = {
			"1 MP Mesopotamia.biq",
			"2 MP Rise of Rome.biq",
			"3 MP Fall of Rome.biq",
			"4 MP Middle Ages.biq",
			"5 MP Mesoamerica.biq",
			"6 MP Age of Discovery.biq",
			// skip for now because BIQ parsing fails
			// "7 MP Sengoku - Sword of the Shogun.biq",
			"8 MP Napoleonic Europe.biq",
			"9 MP WWII in the Pacific.biq",
		};
		// Only bother running one turn of the newer scenarios, just to keep the
		// tests faster.
		await CheckScenariosInCiv3Subfolder("Conquests/Conquests", singleplayerScenarios, runOneTurn: true, "singleplayer");
		await CheckScenariosInCiv3Subfolder("Conquests/Scenarios", multiplayerScenarios, runOneTurn: false, "multiplayer");
	}

	// "2 Rise of Rome.biq" is missing from the Conquests folder in some
	// recent releases of the game. We test it separately from the rest of the
	// scenarios so that its absence only skips this one test, instead of
	// failing (or requiring us to silently tolerate missing files in)
	// LoadAllConquestScenarios as a whole.
	private const string RiseOfRomeScenarioName = "2 Rise of Rome.biq";

	[SkippableFact]
	public async Task LoadRiseOfRomeScenario() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");

		string scenarioPath = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests/Conquests", RiseOfRomeScenarioName);
		Skip.If(!File.Exists(scenarioPath), $"{RiseOfRomeScenarioName} not present in this Civ3 installation.");

		await CheckScenariosInCiv3Subfolder("Conquests/Conquests", new[] { RiseOfRomeScenarioName }, runOneTurn: true, "singleplayer");
	}

	private async Task CheckScenariosInCiv3Subfolder(string subfolder, string[] scenarioNamesToTest, bool runOneTurn, string basename) {
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), subfolder);
		DirectoryInfo directoryInfo = new DirectoryInfo(conquests);
		IEnumerable<FileInfo> saveFiles = directoryInfo.EnumerateFiles().Where(fi => scenarioNamesToTest.Contains(fi.Name));
		Assert.True(
			scenarioNamesToTest.Count() == saveFiles.Count(),
			$"Expected {scenarioNamesToTest.Count()} files but got {saveFiles.Count()}"
		);
		foreach (FileInfo saveFileInfo in saveFiles) {
			string name = saveFileInfo.Name;
			SaveGame game = null;
			C7GameData.GameData gd = null;

			Func<string, string> getPediaIconsPath = (relativeModPath) => {
				if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					relativeModPath = relativeModPath.Replace("\\conquests\\", "/Conquests/");
				}

				return Path.GetFullPath(Path.Combine(Civ3Location.GetCiv3Path(), subfolder, relativeModPath, "Text", "PediaIcons.txt"));
			};

			EngineStorage.animationsEnabled = false;

			Exception ex = Record.Exception(() => {
				game = ImportCiv3.ImportBiq(saveFileInfo.FullName, PathUtils.defaultBicPath, getPediaIconsPath);
			});
			Assert.True(ex == null, name + ": " + ex?.ToString());
			ex = Record.Exception(() => {
				gd = ToGameData(game);
			});
			Assert.True(ex == null, name + ":" + ex?.ToString());
			Assert.NotNull(game);
			Assert.NotNull(gd);
			Assert.NotEqual(-1, gd.seed);

			CheckPlayableCivs(name, game);
			CheckAlliances(name, gd);

			// Check that the human player has at least one settler or city in
			// each scenario, when looking at the SaveGame.
			foreach (SavePlayer player in game.Players) {
				int settlerCount = 0;
				int totalUnitCount = 0;
				foreach (SaveUnit su in game.Units) {
					if (su.owner == player.id) {
						++totalUnitCount;
						if (su.prototype == "Settler") {
							++settlerCount;
						}
					}
				}

				int cityCount = 0;
				foreach (SaveCity sc in game.Cities) {
					if (sc.owner == player.id) {
						++cityCount;
					}
				}

				// The human player should always have either a city or a settler.
				if (player.human) {
					Assert.True(cityCount + settlerCount > 0,
						name + " : " + player.civilization);
				}
			}

			// And check again, looking at the GameData.
			foreach (Player player in gd.players) {
				int settlerCount = 0;
				int totalUnitCount = 0;
				foreach (MapUnit mu in player.units) {
					++totalUnitCount;
					if (mu.unitType.name == "Settler") {
						++settlerCount;
					}
				}
				int cityCount = player.cities.Count;

				// The human player should always have either a city or a settler.
				if (player.isHuman) {
					Assert.True(cityCount + settlerCount > 0,
						name + " : " + player.civilization.name);
				}
			}

			using (TempDirectory output = new()) {
				game.Save(output.File($"{basename}_{name[0]}.json"));
			}

			// Finally, ensure we can run the first turn of the scenario.
			if (runOneTurn) {
				Player human = await CreateHeadlessGame(saveFileInfo.FullName, PathUtils.defaultBicPath, getPediaIconsPath);

				// Make all the players AI players while we run the game in headless mode.
				human.isHuman = false;

				WaitForStartTurnMessage();
				new MsgEndTurn().send();
				WaitForStartTurnMessage();
			}
		}
	}

	private void CheckPlayableCivs(string name, SaveGame game) {
		Console.WriteLine($"Running playability test for {name}");

		// Playable checks
		// 4 Middle Ages.biq
		if (name.Equals("4 Middle Ages.biq")) {
			Assert.True(game.Players.Count(p => p.isIncludedInGame) == 19);
			Assert.True(game.Players.Count(p => p.canBePicked) == 13);
			Assert.True(game.Players.Count == 19);
			Assert.True(game.Civilizations.Count == 20);

			Assert.DoesNotContain(game.Players, p => p.civilization == "Mongols");

			Assert.Contains(game.Civilizations, c => c.name == "Mongols");
			Assert.Contains(game.Civilizations, c => c.name == "Bulgars");
			Assert.Contains(game.Civilizations, c => c.name == "Poland");

			foreach (var gamePlayer in game.Players) {
				if (gamePlayer.civilization == "Turks") {
					Assert.True(gamePlayer.primaryColorIndex == 14);
				}
			}

			// make sure barbarian save units are assigned correctly to the barbarian civ
			foreach (var gameUnit in game.Units) {
				// there is a horseman barb + barb camp unit in this location
				if (gameUnit.currentLocation is { X: 7, Y: 103 }) {
					Assert.Contains("barbarianCamp", game.Map.tiles.Find(t => t.X == 7 && t.Y == 103).features);
					Assert.True(game.Players.Find(p => p.id == gameUnit.owner).civilization == "A Barbarian Chiefdom");
				}
			}
		}
		// 4 MP Middle Ages.biq
		if (name.Equals("4 MP Middle Ages.biq")) {
			Assert.True(game.Players.Count(p => p.isIncludedInGame) == 9);
			Assert.True(game.Players.Count(p => p.canBePicked) == 8);
			Assert.True(game.Players.Count == 9);
			Assert.True(game.Civilizations.Count == 20);

			Assert.DoesNotContain(game.Players, p => p.civilization == "Mongols");
			Assert.DoesNotContain(game.Players, p => p.civilization == "Bulgars");
			Assert.DoesNotContain(game.Players, p => p.civilization == "Poland");

			Assert.Contains(game.Civilizations, c => c.name == "Mongols");
			Assert.Contains(game.Civilizations, c => c.name == "Bulgars");
			Assert.Contains(game.Civilizations, c => c.name == "Poland");
		}
		// 6 Age of Discovery.biq
		if (name.Equals("6 Age of Discovery.biq")) {
			Assert.True(game.Players.Count(p => p.isIncludedInGame) == 10);
			Assert.True(game.Players.Count(p => p.canBePicked) == 8);
			Assert.True(game.Players.Count == 10);
			Assert.True(game.Civilizations.Count == 10);

			Assert.Contains(game.Players, p => p.civilization == "Iroquois");
			Assert.Contains(game.Players, p => p.civilization == "France");
			Assert.Contains(game.Players, p => p.civilization == "Maya");

			Assert.Contains(game.Civilizations, c => c.name == "Iroquois");
			Assert.Contains(game.Civilizations, c => c.name == "France");
			Assert.Contains(game.Civilizations, c => c.name == "Maya");
		}
		// 6 MP Age of Discovery.biq
		if (name.Equals("6 MP Age of Discovery.biq")) {
			Assert.True(game.Players.Count(p => p.isIncludedInGame) == 9);
			Assert.True(game.Players.Count(p => p.canBePicked) == 8);
			Assert.True(game.Players.Count == 9);
			Assert.True(game.Civilizations.Count == 10);

			Assert.DoesNotContain(game.Players, p => p.civilization == "Iroquois");
			Assert.Contains(game.Players, p => p.civilization == "France");
			Assert.Contains(game.Players, p => p.civilization == "Maya");

			Assert.Contains(game.Civilizations, c => c.name == "Iroquois");
			Assert.Contains(game.Civilizations, c => c.name == "France");
			Assert.Contains(game.Civilizations, c => c.name == "Maya");
		}
		// 8 Napoleonic Europe.biq
		if (name.Equals("8 Napoleonic Europe.biq")) {
			Assert.True(game.Players.Count(p => p.isIncludedInGame) == 13);
			Assert.True(game.Players.Count(p => p.canBePicked) == 7);
			Assert.True(game.Players.Count == 13);
			Assert.True(game.Civilizations.Count == 13);

			Assert.Contains(game.Players, p => p.civilization == "Denmark");
			Assert.Contains(game.Players, p => p.civilization == "Portugal");
			Assert.Contains(game.Players, p => p.civilization == "Netherlands");
			Assert.Contains(game.Players, p => p.civilization == "Kingdom of Naples");

			Assert.Contains(game.Civilizations, c => c.name == "Denmark");
			Assert.Contains(game.Civilizations, c => c.name == "Portugal");
			Assert.Contains(game.Civilizations, c => c.name == "Netherlands");
			Assert.Contains(game.Civilizations, c => c.name == "Kingdom of Naples");
		}
		// 8 MP Napoleonic Europe.biq
		if (name.Equals("8 MP Napoleonic Europe.biq")) {
			Assert.True(game.Players.Count(p => p.isIncludedInGame) == 9);
			Assert.True(game.Players.Count(p => p.canBePicked) == 7);
			Assert.True(game.Players.Count == 9);
			Assert.True(game.Civilizations.Count == 13);

			Assert.DoesNotContain(game.Players, p => p.civilization == "Denmark");
			Assert.DoesNotContain(game.Players, p => p.civilization == "Portugal");
			Assert.DoesNotContain(game.Players, p => p.civilization == "Netherlands");
			Assert.DoesNotContain(game.Players, p => p.civilization == "Kingdom of Naples");

			Assert.Contains(game.Civilizations, c => c.name == "Denmark");
			Assert.Contains(game.Civilizations, c => c.name == "Portugal");
			Assert.Contains(game.Civilizations, c => c.name == "Netherlands");
			Assert.Contains(game.Civilizations, c => c.name == "Kingdom of Naples");
		}
	}

	private void CheckAlliances(string name, C7GameData.GameData gd) {
		var players = gd.players;

		if (name.Equals("8 Napoleonic Europe.biq")) {
			var france = players.First(p => p.civilization.name == "France");
			var denmark = players.First(p => p.civilization.name == "Denmark");
			var britain = players.First(p => p.civilization.name == "Britain");
			var netherlands = players.First(p => p.civilization.name == "Netherlands");
			var portugal = players.First(p => p.civilization.name == "Portugal");
			var naples = players.First(p => p.civilization.name == "Kingdom of Naples");
			var austria = players.First(p => p.civilization.name == "Austria");
			var spain = players.First(p => p.civilization.name == "Spain");

			Assert.True(france.alliance.name == "French Coalition");
			Assert.True(denmark.alliance.name == "French Coalition");

			Assert.True(austria.alliance == null);
			Assert.True(spain.alliance == null);

			Assert.False(gd.AreInLockedPeace(austria, spain));
			Assert.False(gd.AreInLockedWar(austria, spain));

			Assert.True(britain.alliance.name == "English Coalition");
			Assert.True(netherlands.alliance.name == "English Coalition");
			Assert.True(portugal.alliance.name == "English Coalition");
			Assert.True(naples.alliance.name == "English Coalition");

			Assert.True(gd.AreInLockedPeace(france, denmark));
			Assert.True(gd.AreInLockedPeace(britain, portugal));
			Assert.True(gd.AreInLockedWar(france, britain));
			Assert.True(gd.AreInLockedWar(denmark, naples));

			Assert.True(france.playerRelationships[britain.id].AtWar());
			Assert.True(denmark.playerRelationships[naples.id].AtWar());
		}

		if (name.Equals("9 WWII in the Pacific.biq")) {
			var usa = players.First(p => p.civilization.name == "United States");
			var china = players.First(p => p.civilization.name == "China");
			var commonwealth = players.First(p => p.civilization.name == "Commonwealth");
			var netherlands = players.First(p => p.civilization.name == "Netherlands");
			var japan = players.First(p => p.civilization.name == "Japan");

			Assert.True(usa.alliance.name == "Allies");
			Assert.True(china.alliance.name == "Allies");
			Assert.True(commonwealth.alliance.name == "Allies");
			Assert.True(netherlands.alliance.name == "Allies");

			Assert.True(japan.alliance.name == "Japanese Empire");

			Assert.True(gd.AreInLockedPeace(usa, china));
			Assert.True(gd.AreInLockedPeace(netherlands, china));
			Assert.True(gd.AreInLockedPeace(usa, commonwealth));

			Assert.True(gd.AreInLockedWar(netherlands, japan));
			Assert.True(gd.AreInLockedWar(china, japan));
			Assert.True(gd.AreInLockedWar(usa, japan));

			Assert.True(usa.playerRelationships[japan.id].AtWar());
			Assert.True(china.playerRelationships[japan.id].AtWar());
		}
	}
}
