using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Editor;

// Phase 5: the rules editor's pages (RulePages), generated from the rule
// schema: every field of every page round-trips through the scenario file
// and reaches a started game; Rename, Add and Delete keep the scenario's
// references right; Restore Defaults, Clear Rules and Custom Rules.
public class RulesEditorTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public RulesEditorTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private static SaveGame Defaults() => SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave();

	private static SaveGame Scenario() {
		SaveGame scenario = ScenarioFiles.NewScenario(Defaults());
		scenario.Scenario ??= new ScenarioInfo();
		ScenarioPlayers.Add(scenario, scenario.Civilizations.First(c => c.name == "Rome"), scenario.Scenario);
		ScenarioPlayers.Add(scenario, scenario.Civilizations.First(c => c.name == "Greece"), scenario.Scenario);
		return scenario;
	}

	private static SaveGame SavedAndLoaded(SaveGame scenario, TempDirectory dir, string name = "rules.json") {
		string path = dir.File(name);
		ScenarioFiles.Save(scenario, path);
		return SaveGame.Load(path, s => s);
	}

	// The item a page's test changes: the third, where there is one (the
	// first ones are special: the default citizen, the top opinion, Anarchy
	// and Despotism...). A page without items (the standard rules have no
	// flavors) gets one.
	private static object ItemOf(SaveGame s, RulePage page) {
		IList items = page.Items(s);
		if (items.Count == 0) {
			page.Add(s, null);
			items = page.Items(s);
		}
		return items[Math.Min(2, items.Count - 1)];
	}

	private static string Show(object value) => value switch {
		null => "None",
		IEnumerable<string> list => string.Join("|", list),
		IEnumerable<int> list => string.Join("|", list),
		_ => value.ToString(),
	};

	// A valid value other than the field's.
	private static object Other(SaveGame s, RuleField f, object current) {
		switch (f.Kind) {
			case RuleFieldKind.Number:
				int n = (int)current;
				return n < f.Max ? n + 1 : n - 1;
			case RuleFieldKind.Text:
				return (string)current + " changed";
			case RuleFieldKind.Check:
				return !(bool)current;
			case RuleFieldKind.Choice:
				return f.Choices(s).First(c => c != (string)current);
			case RuleFieldKind.Choices:
				List<string> choices = f.Choices(s);
				List<string> now = (List<string>)current;
				return choices.Where(c => !now.Contains(c)).Take(1).ToList();
			case RuleFieldKind.Lines:
				return new List<string> { "Alpha", "Beta", "Gamma" };
			case RuleFieldKind.Numbers:
				List<int> numbers = [.. (List<int>)current];
				if (numbers.Count == 0) {
					return new List<int> { 2 };
				}
				numbers[0] = numbers[0] < f.Max ? numbers[0] + 1 : numbers[0] - 1;
				return numbers;
		}
		throw new ArgumentException(f.Kind.ToString());
	}

	// Every field of every page set to another value, then the expected
	// values by page and field.
	private static Dictionary<(string, string), string> ChangeEverything(SaveGame s) {
		Dictionary<(string, string), string> want = [];
		foreach (RulePage page in RulePages.All) {
			object item = ItemOf(s, page);
			foreach (RuleField f in page.Fields.Where(f => !f.ReadOnly)) {
				object value = Other(s, f, f.Get(s, item));
				string why = RuleEditing.SetValue(s, f, item, value);
				// A field may refuse an item (a wonder's field on an
				// improvement); only those.
				if (why != null && f.Refuse != null) {
					continue;
				}
				Assert.Null(why);
				Assert.True(Show(value) == Show(f.Get(s, item)), $"{page.Name} > {f.Label}: set {Show(value)}, reads {Show(f.Get(s, item))}");
			}
		}
		// Pages share some data (an improvement's flavors are the Flavors
		// page's), so what's expected is every field as it reads now.
		return Snapshot(s);
	}

	private static Dictionary<(string, string), string> Snapshot(SaveGame s) {
		Dictionary<(string, string), string> now = [];
		foreach (RulePage page in RulePages.All) {
			object item = ItemOf(s, page);
			foreach (RuleField f in page.Fields.Where(f => !f.ReadOnly)) {
				now[(page.Name, f.Group + " > " + f.Label)] = Show(f.Get(s, item));
			}
		}
		return now;
	}

	private static void AssertHas(SaveGame s, Dictionary<(string, string), string> want) {
		foreach (RulePage page in RulePages.All) {
			object item = ItemOf(s, page);
			foreach (RuleField f in page.Fields.Where(f => !f.ReadOnly)) {
				Assert.True(want[(page.Name, f.Group + " > " + f.Label)] == Show(f.Get(s, item)),
					$"{page.Name} > {f.Label}: wanted {want[(page.Name, f.Group + " > " + f.Label)]}, got {Show(f.Get(s, item))}");
			}
		}
	}

	[Fact]
	public void EveryFieldOfEveryPageRoundTrips() {
		SaveGame s = Scenario();
		Dictionary<(string, string), string> want = ChangeEverything(s);
		Assert.Null(RuleEditing.Problem(s));
		Assert.True(want.Count > 80, $"{want.Count} fields");
		using TempDirectory dir = new();
		AssertHas(SavedAndLoaded(s, dir), want);
	}

	// The game holds every value once a game starts from the scenario (read
	// back from the started game's data), and uses the ones it reads: an
	// espionage mission's price and name, an era's name.
	[Fact]
	public void EveryFieldReachesAStartedGame() {
		SaveGame s = Scenario();
		Dictionary<(string, string), string> want = ChangeEverything(s);
		RulePage espionage = RulePages.Find("Diplomats and Spies");
		EspionageMissionRule investigate = s.Rules.EspionageMissionRuleFor("InvestigateCity");
		investigate.BaseCost = 33;
		Assert.Null(espionage.Rename(s, investigate, "Snoop Around"));
		Assert.Null(RulePages.Find("Eras").Rename(s, EraUtils.MIDDLE_AGES_CVLPD, "Feudal Times"));
		want = Snapshot(s);
		using TempDirectory dir = new();
		SaveGame loaded = SavedAndLoaded(s, dir);
		loaded.Players.First(p => p.civilization == "Rome").human = true;

		CreateGame.createGame(loaded, _ => fixture.behaviors).GetAwaiter().GetResult();
		C7GameData.GameData game = EngineStorage.gameData;
		SaveGame played = SaveGame.FromGameData(game);
		played.Scenario = loaded.Scenario;
		AssertHas(played, want);

		Assert.Equal(investigate.BaseCost, C7Engine.Espionage.BaseCost(EspionageMission.InvestigateCity));
		Assert.Equal("Snoop Around", C7Engine.Espionage.Describe(EspionageMission.InvestigateCity));
		Assert.Equal("Feudal Times", EraUtils.GetNiceEraName(EraUtils.MIDDLE_AGES_CVLPD));
		Assert.Equal(EraUtils.ANCIENT_TIMES, EraUtils.GetNiceEraName(EraUtils.ANCIENT_TIMES_CVLPD));
	}

	[Fact]
	public void RenameAddAndDeleteKeepTheScenarioRight() {
		SaveGame s = Scenario();
		RulePage difficulty = RulePages.Find("Difficulty Levels");
		Difficulty monarch = s.Difficulties.First(d => d.Name == "Monarch");
		string rome = s.Players.First(p => p.civilization == "Rome").id.ToString();
		s.Scenario.players.Add(new C7Engine.Editor.PlayerSlot { player = rome, difficulty = "Monarch" });

		// Rename: the player's difficulty follows; a name in use is refused.
		Assert.NotNull(difficulty.Rename(s, monarch, "Regent"));
		Assert.Null(difficulty.Rename(s, monarch, "King"));
		Assert.Equal("King", s.Scenario.players[0].difficulty);
		// Add: a copy, after it, with a name and ID of its own.
		Difficulty added = (Difficulty)difficulty.Add(s, monarch);
		Assert.Equal(s.Difficulties.IndexOf(monarch) + 1, s.Difficulties.IndexOf(added));
		Assert.Equal(monarch.NumberOfCitizensBornContent, added.NumberOfCitizensBornContent);
		Assert.Equal(s.Difficulties.Count, s.Difficulties.Select(d => d.id.ToString()).Distinct().Count());
		// Delete: what used it is named first, then cleared.
		Assert.Equal(["the difficulty of Rome"], difficulty.UsedBy(s, monarch));
		Assert.Null(difficulty.Delete(s, monarch));
		Assert.Null(s.Scenario.players[0].difficulty);

		// A specialist's citizens become laborers when it goes; the laborer
		// can't go.
		RulePage citizens = RulePages.Find("Citizens");
		CitizenType laborer = s.CitizenTypes.First(c => c.IsDefaultCitizen);
		CitizenType entertainer = s.CitizenTypes.First(c => !c.IsDefaultCitizen);
		Assert.NotNull(citizens.Delete(s, laborer));
		CitizenType extra = (CitizenType)citizens.Add(s, entertainer);
		Assert.False(extra.IsDefaultCitizen);
		Assert.NotEqual(entertainer.Id.ToString(), extra.Id.ToString());
		s.Cities.Add(new SaveCity { name = "Testa", residents = [new SaveCityResident { citizenType = extra.Id }] });
		Assert.Equal(["citizens of Testa"], citizens.UsedBy(s, extra));
		Assert.Null(citizens.Delete(s, extra));
		Assert.Equal(laborer.Id.ToString(), s.Cities.Last().residents[0].citizenType.ToString());
		s.Cities.RemoveAt(s.Cities.Count - 1);

		// A wonder's field: a wonder takes it, an improvement refuses it.
		RulePage improvements = RulePages.Find("Improvements and Wonders");
		RuleField gains = improvements.Fields.First(f => f.Label == "Gains in Every City:");
		SaveBuilding pyramids = s.Buildings.First(b => b.greatWonderProperties != null);
		Assert.Null(RuleEditing.SetValue(s, gains, pyramids, "Granary"));
		Assert.Equal("Granary", pyramids.greatWonderProperties.buildingGainedInEveryCity);
		Assert.NotNull(RuleEditing.SetValue(s, gains, s.Buildings.First(b => b.name == "Temple"), "Granary"));
		// Renaming an improvement follows it into the wonder's field.
		Assert.Null(improvements.Rename(s, s.Buildings.First(b => b.name == "Granary"), "Silo"));
		Assert.Equal("Silo", pyramids.greatWonderProperties.buildingGainedInEveryCity);

		// Pages without Add or Delete in Civ3 haven't them here.
		Assert.Null(RulePages.Find("Combat Experience").Add);
		Assert.Null(RulePages.Find("World Sizes").Delete);
		Assert.Null(RulePages.Find("General Settings").Rename);

		using TempDirectory dir = new();
		SaveGame loaded = SavedAndLoaded(s, dir);
		Assert.Contains(loaded.Difficulties, d => d.Name == "New Level");
		Assert.DoesNotContain(loaded.Difficulties, d => d.Name == "King");
		Assert.Equal(s.CitizenTypes.Count, loaded.CitizenTypes.Count);
	}

	// Phase 6's pages keep the scenario right as items are renamed and
	// deleted: unit types, civilizations, advances, resources, governments.
	[Fact]
	public void BigPagesKeepTheScenarioRight() {
		SaveGame s = Scenario();
		SavePlayer rome = s.Players.First(p => p.civilization == "Rome");
		s.Units.Add(new SaveUnit {
			id = ID.FromString("Unit-900"),
			name = "Warrior",
			prototype = "Warrior",
			owner = rome.id,
			nationality = "Rome",
			currentLocation = new TileLocation(0, 0),
			previousLocation = new TileLocation(-1, -1)
		});

		// A unit type renamed: units on the map, upgrades, default units.
		RulePage units = RulePages.Find("Units");
		SaveUnitPrototype warrior = s.UnitPrototypes.First(u => u.name == "Warrior");
		string barbarian = s.BarbarianInfo.basicBarbarianUnit;
		Assert.Null(units.Rename(s, warrior, "Brave"));
		Assert.Equal("Brave", s.Units.Last().prototype);
		Assert.Contains(s.UnitPrototypes, u => u.upgradesTo?.Contains("Brave") == true || u.name == "Brave");
		if (barbarian == "Warrior") {
			Assert.Equal("Brave", s.BarbarianInfo.basicBarbarianUnit);
		}
		// Deleted: its uses named first (the unit on the map goes with it).
		Assert.Contains(units.UsedBy(s, warrior), u => u.Contains("on the map"));
		Assert.Null(units.Delete(s, warrior));
		Assert.DoesNotContain(s.Units, u => u.prototype == "Brave");
		Assert.DoesNotContain(s.UnitPrototypes, u => u.upgradesTo?.Contains("Brave") == true);
		// A copy is its own type.
		SaveUnitPrototype copy = (SaveUnitPrototype)units.Add(s, s.UnitPrototypes.First(u => u.name == "Spearman"));
		Assert.Equal("Spearman (2)", copy.name);
		copy.attack = 9;
		Assert.NotEqual(9, s.UnitPrototypes.First(u => u.name == "Spearman").attack);

		// A civilization renamed follows into its player and units; a
		// player's civilization can't go.
		RulePage civs = RulePages.Find("Civilizations");
		Civilization romans = s.Civilizations.First(c => c.name == "Rome");
		Assert.Null(civs.Rename(s, romans, "Latium"));
		Assert.Equal("Latium", rome.civilization);
		Assert.All(s.UnitPrototypes.Where(u => u.producibleBy.Count > 0), u => Assert.DoesNotContain("Rome", u.producibleBy));
		Assert.NotNull(civs.Delete(s, romans));
		Assert.Null(civs.Delete(s, s.Civilizations.First(c => c.name == "Inca")));

		// An advance gone: prerequisites and players' knowledge cleared.
		RulePage advances = RulePages.Find("Civilization Advances");
		SaveTech bronze = s.Techs.First(t => t.Name == "Bronze Working");
		rome.knownTechs.Add(bronze.id);
		Assert.NotEmpty(advances.UsedBy(s, bronze));
		Assert.Null(advances.Delete(s, bronze));
		Assert.DoesNotContain(s.Techs, t => t.Prerequisites.Any(p => p.ToString() == bronze.id.ToString()));
		Assert.DoesNotContain(rome.knownTechs, t => t.ToString() == bronze.id.ToString());
		// No advance may need itself.
		SaveTech alphabet = s.Techs.First(t => t.Name == "Alphabet");
		SaveTech writing = s.Techs.First(t => t.Name == "Writing");
		alphabet.Prerequisites.Add(writing.id);
		Assert.NotNull(RuleEditing.Problem(s));
		alphabet.Prerequisites.Remove(writing.id);

		// A resource gone comes off the map, terrain and units.
		RulePage resources = RulePages.Find("Natural Resources");
		Resource iron = s.Resources.First(r => r.Name == "Iron");
		s.Map.tiles[0].resource = iron.Key;
		Assert.Null(resources.Delete(s, iron));
		Assert.Null(s.Map.tiles[0].resource);
		Assert.All(s.TerrainTypes, t => Assert.DoesNotContain(iron.Key, t.allowedResources));
		Assert.All(s.UnitPrototypes, u => Assert.DoesNotContain(iron.Key, u.requiredResources));

		// A government renamed follows into favourites; one gone puts its
		// players under the starting government.
		RulePage governments = RulePages.Find("Governments");
		Government monarchy = s.Governments.First(g => g.name == "Monarchy");
		Civilization fond = s.Civilizations.First(c => c.favoriteGovernment == "Monarchy");
		Assert.Null(governments.Rename(s, monarchy, "Kingdom"));
		Assert.Equal("Kingdom", fond.favoriteGovernment);
		rome.governmentId = monarchy.id;
		Assert.Null(governments.Delete(s, monarchy));
		Assert.Equal(s.Governments.First(g => g.defaultType).id.ToString(), rome.governmentId.ToString());
		Assert.NotNull(governments.Delete(s, s.Governments.First(g => g.transitionType)));

		// Flavors: a new one adds its column to the others.
		RulePage flavors = RulePages.Find("Flavors");
		flavors.Add(s, null);
		flavors.Add(s, null);
		Assert.All(s.Rules.Flavors, f => Assert.Equal(2, f.Relationships.Count));
		Assert.Null(flavors.Delete(s, s.Rules.Flavors[0]));
		Assert.All(s.Rules.Flavors, f => Assert.Single(f.Relationships));

		using TempDirectory dir = new();
		SaveGame loaded = SavedAndLoaded(s, dir);
		Assert.Contains(loaded.Civilizations, c => c.name == "Latium");
		Assert.Contains(loaded.UnitPrototypes, u => u.name == "Spearman (2)" && u.attack == 9);
		CreateGame.createGame(loaded.Also(l => l.Players.First(p => p.civilization == "Latium").human = true), _ => fixture.behaviors).GetAwaiter().GetResult();
		Assert.Contains(EngineStorage.gameData.players, p => p.civilization.name == "Latium");
	}

	[Fact]
	public void BadValuesAreRefused() {
		SaveGame s = Scenario();
		RulePage general = RulePages.Find("General Settings");
		RuleField roads = general.Fields.First(f => f.Label.StartsWith("Movement Rate"));
		Assert.NotNull(RuleEditing.SetValue(s, roads, s, 0));
		Assert.NotNull(RuleEditing.SetValue(s, roads, s, 100));
		RuleField start = general.Fields.First(f => f.Label == "Start Unit 1:");
		Assert.NotNull(RuleEditing.SetValue(s, start, s, "No Such Unit"));
		RulePage jobs = RulePages.Find("Worker Jobs");
		RuleField resources = jobs.Fields.First(f => f.Kind == RuleFieldKind.Choices);
		Assert.NotNull(RuleEditing.SetValue(s, resources, s.TerraForms[0], RulePages.ResourceNames(s).Take(3).ToList()));
		Assert.NotNull(RuleEditing.SetValue(s, RulePages.Find("Eras").Fields[0], EraUtils.ANCIENT_TIMES_CVLPD, "x"));

		s.Rules.MaximumLevel1CitySize = 20;
		Assert.NotNull(RuleEditing.Problem(s));
		s.Rules.MaximumLevel1CitySize = 6;
		s.CitizenTypes.ForEach(c => c.IsDefaultCitizen = false);
		Assert.NotNull(RuleEditing.Problem(s));
		// Ticking a default citizen unticks the others.
		RuleField isDefault = RulePages.Find("Citizens").Fields.First(f => f.Label == "Default Citizen");
		Assert.Null(RuleEditing.SetValue(s, isDefault, s.CitizenTypes[0], true));
		Assert.Null(RuleEditing.SetValue(s, isDefault, s.CitizenTypes[1], true));
		Assert.Equal(1, s.CitizenTypes.Count(c => c.IsDefaultCitizen));
		Assert.True(s.CitizenTypes[1].IsDefaultCitizen);
	}

	// Restore Defaults puts every page back; Clear Rules the one page; a
	// scenario without Custom Rules is given the standard rules as it
	// starts.
	[Fact]
	public void RestoreDefaultsClearRulesAndCustomRules() {
		SaveGame s = Scenario();
		SaveGame defaults = Defaults();
		Dictionary<(string, string), string> changed = ChangeEverything(s);
		RuleEditing.ClearPage(s, defaults, RulePages.Find("General Settings"));
		Assert.Equal(defaults.Rules.MovementAlongRoads, s.Rules.MovementAlongRoads);
		Assert.NotEqual(defaults.WorldSizes[2].techRate, s.WorldSizes[2].techRate);

		RuleEditing.RestoreDefaults(s, defaults);
		Assert.Equal(defaults.WorldSizes[2].techRate, s.WorldSizes[2].techRate);
		Assert.Equal(defaults.ExperienceLevels[2].baseHitPoints, s.ExperienceLevels[2].baseHitPoints);
		Assert.Equal(Show(defaults.Rules.CultureLevelNames), Show(s.Rules.CultureLevelNames));
		Assert.Null(RuleEditing.Problem(s));

		ChangeEverything(s);
		s.Scenario.customRules = true;
		RuleEditing.UseStandardRulesIfNotCustom(s, defaults);
		Assert.NotEqual(defaults.WorldSizes[2].techRate, s.WorldSizes[2].techRate);
		s.Scenario.customRules = false;
		using TempDirectory dir = new();
		SaveGame loaded = SavedAndLoaded(s, dir);
		RuleEditing.UseStandardRulesIfNotCustom(loaded, defaults);
		Assert.Equal(defaults.WorldSizes[2].techRate, loaded.WorldSizes[2].techRate);
	}
}

internal static class AlsoExtension {
	public static T Also<T>(this T value, Action<T> act) {
		act(value);
		return value;
	}
}
