using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Editor;

// Phase 8: a scenario's own folder (art copied in, packaged, installed),
// its own Civilopedia articles, Check Scenario and Import.
public class ScenarioContentTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public ScenarioContentTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private SaveGame Scenario() {
		SaveGame s = ScenarioFiles.NewScenario(fixture.saveGame.Clone());
		s.Scenario ??= new ScenarioInfo();
		return s;
	}

	[Fact]
	public void TheSearchFolderIsTheScenariosOwn() {
		Assert.Equal("Knights", ScenarioContent.SearchFolder(null, "C:/x/Scenarios/Knights.json"));
		Assert.Equal("Art Pack", ScenarioContent.SearchFolder("Art Pack", "C:/x/Scenarios/Knights.json"));
		// A path into Civ3's folders isn't the scenario's own.
		Assert.Equal("Knights", ScenarioContent.SearchFolder("..\\conquests\\Rise of Rome", "C:/x/Scenarios/Knights.json"));
		Assert.Equal("Knights;..\\conquests\\Rise of Rome", ScenarioContent.WithSearchFolder("..\\conquests\\Rise of Rome", "Knights"));
		Assert.Equal("Knights", ScenarioContent.WithSearchFolder("Knights", "Knights"));
	}

	[Fact]
	public void ArtIsCopiedIntoTheScenariosFolderPackagedAndInstalled() {
		using TempDirectory dir = new("scenario-content");
		string scenarios = Path.Combine(dir.Path, "Scenarios");
		Directory.CreateDirectory(scenarios);
		string scenarioPath = Path.Combine(scenarios, "Knights.json");
		SaveGame s = Scenario();
		ScenarioFiles.Save(s, scenarioPath);

		// A unit's animation folder and a Civilopedia picture from elsewhere.
		string source = Path.Combine(dir.Path, "source", "Knight");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "Knight.INI"), "[Animations]");
		File.WriteAllText(Path.Combine(source, "KnightRun.flc"), "flc");
		string picture = Path.Combine(dir.Path, "source", "knight_lg.png");
		File.WriteAllText(picture, "png");

		Assert.Equal("Knight", ScenarioContent.ImportUnitArt(scenarioPath, "Knights", Path.Combine(source, "Knight.INI")));
		Assert.Equal("Art/Civilopedia/Icons/Units/knight_lg.png", ScenarioContent.ImportFile(scenarioPath, "Knights", picture, "Art/Civilopedia/Icons/Units"));
		string folder = Path.Combine(scenarios, "Knights");
		Assert.True(File.Exists(Path.Combine(folder, "Art", "Units", "Knight", "Knight.INI")));
		Assert.True(File.Exists(Path.Combine(folder, "Art", "Units", "Knight", "KnightRun.flc")));
		Assert.True(File.Exists(Path.Combine(folder, "Art", "Civilopedia", "Icons", "Units", "knight_lg.png")));

		// One .zip with the scenario and its folder at its top.
		string zip = dir.File("Knights.zip");
		ScenarioContent.Package(scenarioPath, "Knights", zip);
		using (ZipArchive archive = ZipFile.OpenRead(zip)) {
			List<string> entries = archive.Entries.Select(e => e.FullName).ToList();
			Assert.Contains("Knights.json", entries);
			Assert.Contains("Knights/Art/Units/Knight/Knight.INI", entries);
			Assert.Contains("Knights/Art/Civilopedia/Icons/Units/knight_lg.png", entries);
		}

		// A friend's Scenarios folder.
		string theirs = Path.Combine(dir.Path, "Friend", "Scenarios");
		Directory.CreateDirectory(theirs);
		string installed = ScenarioContent.Install(zip, theirs);
		Assert.Equal(Path.Combine(theirs, "Knights.json"), installed);
		Assert.True(File.Exists(Path.Combine(theirs, "Knights", "Art", "Units", "Knight", "KnightRun.flc")));
		Assert.NotNull(SaveGame.Load(installed, x => x));
	}

	[Fact]
	public void APackageCantWriteOutsideTheScenariosFolder() {
		using TempDirectory dir = new("scenario-content");
		string zip = dir.File("evil.zip");
		using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
			using StreamWriter w = new(archive.CreateEntry("../outside.json").Open());
			w.Write("{}");
		}
		string scenarios = Path.Combine(dir.Path, "Scenarios");
		Directory.CreateDirectory(scenarios);
		Assert.Throws<InvalidDataException>(() => ScenarioContent.Install(zip, scenarios));
		Assert.False(File.Exists(dir.File("outside.json")));
	}

	[Fact]
	public void TheCheckFindsCiv3sLimitsAndMissingArt() {
		SaveGame s = Scenario();
		Assert.Empty(ScenarioContent.Check(s));

		s.Buildings.First(b => b.flags.Contains(SaveBuilding.Flag.IsCenterOfEmpire)).flags.Remove(SaveBuilding.Flag.IsCenterOfEmpire);
		Assert.Contains(ScenarioContent.Check(s), p => p.Contains("palace"));

		SaveGame few = Scenario();
		few.Civilizations.RemoveAll(c => !c.isBarbarian && c != few.Civilizations.First(x => !x.isBarbarian));
		Assert.Contains(ScenarioContent.Check(few), p => p.Contains("at least 2"));

		// Art: the animation each unit names, and its pictures.
		SaveGame art = Scenario();
		SaveUnitPrototype knight = art.UnitPrototypes.First();
		Art a = knight.art;
		a.mainArt.defaultName = "Nowhere";
		knight.art = a;
		List<string> problems = ScenarioContent.Check(art, name => name != "Nowhere", _ => true);
		Assert.Contains(problems, p => p.Contains("Art/Units/Nowhere/Nowhere.INI"));
		Assert.Single(problems);
		a.pediaArt.large = "Art/Civilopedia/Icons/Units/gone.pcx";
		knight.art = a;
		Assert.Contains(ScenarioContent.Check(art, _ => true, p => !p.Contains("gone")), p => p.Contains("gone.pcx"));
	}

	[Fact]
	public void AScenariosCivilopediaArticlesLieOverTheGames() {
		CivilopediaText game = CivilopediaText.Parse(["#PRTO_Warrior", "^A warrior.", "#DESC_PRTO_Warrior", "^Old history."]);
		CivilopediaText merged = game.With([
			new CivilopediaEntryRule { Key = "PRTO_Knight", Text = ["A {knight}."], History = ["Brave."] },
			new CivilopediaEntryRule { Key = "PRTO_Warrior", Text = ["A stronger warrior."] },
		]);
		Assert.Equal(["A {knight}."], merged.Get("PRTO_Knight").Lines);
		Assert.Equal(["Brave."], merged.Get("PRTO_Knight").DescriptionLines);
		Assert.Equal(["A stronger warrior."], merged.Get("PRTO_Warrior").Lines);
		// Its history wasn't given: the game's stays.
		Assert.Equal(["Old history."], merged.Get("PRTO_Warrior").DescriptionLines);
		// The game's own text is left as it was.
		Assert.Equal(["A warrior."], game.Get("PRTO_Warrior").Lines);
		Assert.False(game.Has("PRTO_Knight"));
	}

	[Fact]
	public void TheUnitsPageWritesItsCivilopediaArticle() {
		SaveGame s = Scenario();
		RulePage units = RulePages.Find("Units");
		SaveUnitPrototype unit = s.UnitPrototypes.First();
		RuleField text = units.Fields.First(f => f.Group == "Civilopedia" && f.Label == "Text:");
		RuleField history = units.Fields.First(f => f.Group == "Civilopedia" && f.Label == "History:");
		Assert.Null(RuleEditing.SetValue(s, text, unit, new List<string> { "A new unit.", "Strong." }));
		Assert.Null(RuleEditing.SetValue(s, history, unit, new List<string> { "Made for this scenario." }));
		CivilopediaEntryRule entry = s.Rules.CivilopediaEntries.Single(e => e.Key == unit.civilopediaEntry);
		Assert.Equal(["A new unit.", "Strong."], entry.Text);

		// Saved and loaded with the scenario.
		using TempDirectory dir = new();
		ScenarioFiles.Save(s, dir.File("pedia.json"));
		SaveGame loaded = SaveGame.Load(dir.File("pedia.json"), x => x);
		Assert.Equal(["Made for this scenario."], loaded.Rules.CivilopediaEntries.Single().History);

		// Emptied, the article goes.
		RuleEditing.SetValue(s, text, unit, new List<string>());
		RuleEditing.SetValue(s, history, unit, new List<string>());
		Assert.Empty(s.Rules.CivilopediaEntries);
	}

	[Fact]
	public void ImportTakesTheChosenParts() {
		SaveGame target = Scenario();
		SaveGame source = Scenario();
		source.UnitPrototypes.First().attack = 77;
		string firstUnit = source.UnitPrototypes.First().name;
		source.Map.tiles.First().overlayTerrain = "hills";
		source.Map.tiles.First().baseTerrain = "grassland";

		SaveGame rules = ScenarioImport.Import(target, source, rules: true, map: false, playerData: false, out string problem);
		Assert.Null(problem);
		Assert.Equal(77, rules.UnitPrototypes.First(u => u.name == firstUnit).attack);
		Assert.NotEqual("hills", rules.Map.tiles.First().overlayTerrain);

		SaveGame map = ScenarioImport.Import(target, source, rules: false, map: true, playerData: false, out problem);
		Assert.Null(problem);
		Assert.Equal("hills", map.Map.tiles.First().overlayTerrain);
		Assert.NotEqual(77, map.UnitPrototypes.First(u => u.name == firstUnit).attack);

		Assert.Null(ScenarioImport.Import(target, source, rules: false, map: false, playerData: true, out problem));
		Assert.Contains("map too", problem);
		Assert.Null(ScenarioImport.Import(target, source, false, false, false, out problem));
		// The target is left as it was.
		Assert.NotEqual(77, target.UnitPrototypes.First(u => u.name == firstUnit).attack);
	}

	[Fact]
	public void ImportedPlayersNeedTheirCivilizationsInTheRules() {
		SaveGame target = Scenario();
		SaveGame source = Scenario();
		ScenarioPlayers.Add(source, source.Civilizations.First(c => c.name == "Rome"), source.Scenario);
		target.Civilizations.RemoveAll(c => c.name == "Rome");
		Assert.Null(ScenarioImport.Import(target, source, false, true, true, out string problem));
		Assert.Contains("Rome", problem);
		Assert.NotNull(ScenarioImport.Import(target, source, true, true, true, out problem));
	}
}
