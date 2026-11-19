using System.IO;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.Editor;

// The user's requests of 2026-11-18: a scenario's world for a new game, a
// Civ3 file's playable civilizations, and Civ3's food per citizen.
public class EditorQualityOfLifeTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public EditorQualityOfLifeTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	[Fact]
	public void CitizensEatTheRulesFood() {
		// A copy: the rules are changed.
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		Player owner = gameData.players.First(p => !p.isBarbarians);
		Tile tile = gameData.map.tiles.First(t => t.IsLand() && t.IsAllowCities() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity()));
		City city = CityInteractions.BuildCity(tile, owner, "Granary Test");
		city.AddCitizen(new CityResident { city = city, nationality = owner.civilization, citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen) });
		Assert.Equal(2, city.residents.Count);

		owner.rules.FoodConsumptionPerCitizen = 2;
		Assert.Equal(4, city.FoodConsumedPerTurn());
		// QC's: 4 food a citizen.
		owner.rules.FoodConsumptionPerCitizen = 4;
		Assert.Equal(8, city.FoodConsumedPerTurn());

		// The center feeds a citizen, whatever it stands on: 4 in QC, 2 in
		// Civ3's rules (as Civ3's saves show).
		bool agricultural = owner.civilization.traits.Contains(Civilization.Trait.Agricultural);
		owner.government.hasTilePenalty = false;
		Assert.Equal(4 + (agricultural ? 1 : 0), tile.FoodYield(city).yield);
		owner.rules.FoodConsumptionPerCitizen = 2;
		Assert.Equal(2 + (agricultural ? 1 : 0), tile.FoodYield(city).yield);
		// Rising Tide's richer center (two citizens' food).
		owner.rules.CityCenterFood = 4;
		Assert.Equal(4 + (agricultural ? 1 : 0), tile.FoodYield(city).yield);
	}

	[Fact]
	public void ACiv3FilesPlayableCivilizationsReachTheScenario() {
		SaveGame civ3 = fixture.saveGame.Clone();
		civ3.Scenario = null;
		string[] civs = civ3.Civilizations.Where(c => !c.isBarbarian).Take(3).Select(c => c.name).ToArray();
		civ3.PlayableCivilizations = [.. civs];
		// Played as it is: the file's own list.
		Assert.True(ScenarioRules.IsPlayable(civ3, civs[0]));
		string other = civ3.Civilizations.First(c => !c.isBarbarian && !civs.Contains(c.name)).name;
		Assert.False(ScenarioRules.IsPlayable(civ3, other));

		// Opened in the editor: the scenario's list, the save's let go.
		SaveGame edited = ScenarioFiles.ForEditing(civ3, "QC");
		Assert.Equal(civs, edited.Scenario.playableCivs);
		Assert.Null(edited.PlayableCivilizations);
		Assert.False(ScenarioRules.IsPlayable(edited, other));
	}

	// QC 1.95's own list, when Civ3 is here.
	[SkippableFact]
	public void QcsPlayableCivilizationsAreImported() {
		string file = Path.Join(Civ3Location.GetCiv3Path() ?? "", "Conquests", "Scenarios", "QC_1.95H1n2.biq");
		Skip.IfNot(File.Exists(file), "QC 1.95 isn't in Civ3's Scenarios folder");
		string conquests = Path.GetDirectoryName(Path.GetDirectoryName(file));
		SaveGame opened = ScenarioFiles.Open(file, PathUtils.defaultBicPath, ScenarioCorpusTest.PediaIconsFor(conquests, Path.GetDirectoryName(file)));
		Assert.Equal(19, opened.Scenario.playableCivs.Count);
		Assert.True(opened.Rules.AcceleratedProduction);
		Assert.Equal(4, opened.Rules.FoodConsumptionPerCitizen);
		// Its Wealth-type marker needs the last advance, as in Civ3.
		SaveInflow marker = opened.Inflows.Single(i => i.name.StartsWith("Game Ends"));
		Assert.Equal("End Of Game", opened.Techs.Single(t => t.id == marker.requiredTech).Name);
		Assert.Null(opened.Inflows.Single(i => i.name == "Wealth").requiredTech);
	}

	// A Wealth-type item with an advance is offered only once it's known,
	// and keeps its advance through a save.
	[Fact]
	public void AnInflowWaitsForItsAdvance() {
		SaveGame s = fixture.saveGame.Clone();
		SaveTech tech = s.Techs.Last();
		s.Inflows.Add(new SaveInflow {
			name = "Marker",
			localYield = [new SaveLocalYield(InflowYield.commerce, "inflows.result.wealth.commerce")],
			requiredTech = tech.id,
		});
		using TempDirectory dir = new();
		s.Save(dir.File("inflow.json"));
		SaveGame loaded = SaveGame.Load(dir.File("inflow.json"), x => x);
		Assert.Equal(tech.id, loaded.Inflows.Single(i => i.name == "Marker").requiredTech);

		C7GameData.GameData gameData = loaded.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		Player owner = gameData.players.First(p => !p.isBarbarians);
		Tile tile = gameData.map.tiles.First(t => t.IsLand() && t.IsAllowCities() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity()));
		City city = CityInteractions.BuildCity(tile, owner, "Marker Test");
		Inflow marker = gameData.Inflows.Single(i => i.name == "Marker");
		Assert.DoesNotContain(marker, city.ListProductionOptions(gameData));
		Assert.Contains(gameData.Inflows.First(i => i.requiredTech == null), city.ListProductionOptions(gameData));
		owner.knownTechs.Add(tech.id);
		Assert.Contains(marker, city.ListProductionOptions(gameData));
	}

	[Fact]
	public void TheWorldSettingsAreSavedWithTheScenario() {
		SaveGame s = ScenarioFiles.NewScenario(fixture.saveGame.Clone());
		C7GameData.GameData gameData = ScenarioEditing.Open(s, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		ScenarioSettings settings = ScenarioSettings.Of(gameData, s.Scenario).Copy();
		settings.World.landform = new WorldChoice { value = "Pangaea", only = true };
		settings.World.age = new WorldChoice { value = "Billion_5" };
		settings.World.worldSize = new WorldChoice { value = gameData.worldSizes[0].name, only = true };
		settings.ApplyTo(gameData, s.Scenario);

		using TempDirectory dir = new();
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, s.Scenario), dir.File("world.json"));
		ScenarioWorldSetup world = SaveGame.Load(dir.File("world.json"), x => x).Scenario.world;
		Assert.Equal(WorldCharacteristics.Landform.Pangaea, ScenarioWorld.Value<WorldCharacteristics.Landform>(world.landform));
		Assert.True(world.landform.only);
		Assert.Equal(WorldCharacteristics.Age.Billion_5, ScenarioWorld.Value<WorldCharacteristics.Age>(world.age));
		Assert.False(world.age.only);
		Assert.Null(ScenarioWorld.Value<WorldCharacteristics.Climate>(world.climate));
		Assert.Equal(gameData.worldSizes[0].name, ScenarioWorld.Size(world.worldSize, gameData.worldSizes).name);
	}

	// A game in the other mode (a Civ3 scenario in Civ3's) plays in it
	// without changing the player's choice.
	[Fact]
	public void AGamesModeDoesntChangeThePlayersChoice() {
		bool preferred = C7Settings.PrefersStandaloneMode();
		try {
			C7Settings.SetMode(!preferred, remember: false);
			Assert.Equal(!preferred, C7Settings.UseStandaloneMode());
			Assert.Equal(preferred, C7Settings.PrefersStandaloneMode());
			C7Settings.SetMode(preferred, remember: false);
			Assert.Equal(preferred, C7Settings.UseStandaloneMode());
		} finally {
			C7Settings.SetMode(preferred, remember: false);
		}
	}

	[Fact]
	public void OnlyTheScenariosChoiceIsAllowedWhenItSaysSo() {
		WorldChoice open = new();
		WorldChoice preferred = new() { value = "Percent_60" };
		WorldChoice only = new() { value = "Percent_60", only = true };
		Assert.True(ScenarioWorld.Allows(open, "Percent_80"));
		Assert.True(ScenarioWorld.Allows(preferred, "Percent_80"));
		Assert.False(ScenarioWorld.Allows(only, "Percent_80"));
		Assert.True(ScenarioWorld.Allows(only, "Percent_60"));
		Assert.Equal("This scenario allows only 60% water (Water Coverage).", ScenarioWorld.OnlyNote(ScenarioWorld.Setting.Water, only, []));
		// A value the game doesn't know is the player's choice.
		Assert.Null(ScenarioWorld.Value<WorldCharacteristics.Age>(new WorldChoice { value = "Billion_9" }));
		Assert.Contains(ScenarioWorld.Options(ScenarioWorld.Setting.Barbarians, []), o => o.Label == "No Barbarians");
	}
}
