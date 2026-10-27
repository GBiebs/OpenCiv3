using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI.UnitAI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Conquests' scientific great leaders (Civilopedia "Leaders", "Age of
// Science"): a chance, 3% (5% for a Scientific civ, CivFanatics), each time a
// civ is first to discover an advance; he appears in the capital, and either
// starts a 20-turn Age of Science (research 25% more productive) there or
// hurries any improvement, great wonders included.
public class ScientificLeaderTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City capital;

	public ScientificLeaderTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.isHuman = true; // so free advances wait for us to pick them
		us.civilization.traits.Clear();
		capital = CityInteractions.BuildCity(us.units[0].location, us, "Capital");
		Assert.True(capital.IsCapital());
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private Tech UnknownToAll() => us.GetAvailableTechsToResearch(gameData.techs)
		.First(t => gameData.players.All(p => !p.knownTechs.Contains(t.id)));

	private MapUnit[] ScientificLeaders() => us.units.Where(u => u.scientificLeader).ToArray();

	private MapUnit Leader(bool scientific, Tile where) {
		MapUnit leader = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == gameData.rules.BattleCreatedUnitType), where);
		leader.scientificLeader = scientific;
		return leader;
	}

	[Fact]
	public void BeingFirstToAnAdvanceCanBringOne() {
		gameData.rules.ScientificLeaderChance = 100;
		us.freeTechsRemaining = 1;
		Assert.True(us.ClaimFreeTech(gameData, UnknownToAll()));
		MapUnit leader = Assert.Single(ScientificLeaders());
		Assert.Equal(capital.location, leader.location);
		Assert.Equal(us.civilization.scientificLeaderNames[0], leader.name);
		MsgLeaderBorn told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgLeaderBorn>());
		Assert.True(told.scientific);

		// Researched, too.
		Tech next = UnknownToAll();
		us.SetCurrentlyResearchedTech(next.id);
		us.beakers = 1_000_000;
		us.turnsResearched = 100;
		us.DoPerTurnScienceUpdates(gameData);
		Assert.Contains(next.id, us.knownTechs);
		Assert.Equal(2, ScientificLeaders().Length);
		Assert.Equal(us.civilization.scientificLeaderNames[1], ScientificLeaders()[1].name);
	}

	[Fact]
	public void NotForWhatOthersKnowOrWhatWasGiven() {
		gameData.rules.ScientificLeaderChance = 100;
		Player other = gameData.players.First(p => p != us && !p.isBarbarians);
		Tech known = UnknownToAll();
		other.knownTechs.Add(known.id);
		us.freeTechsRemaining = 1;
		us.ClaimFreeTech(gameData, known);
		Assert.Empty(ScientificLeaders());

		// Traded, or a goody hut's.
		us.AcquireTechs(gameData, [UnknownToAll()]);
		Assert.Empty(ScientificLeaders());
	}

	[Fact]
	public void AScientificCivHasTheBetterChance() {
		gameData.rules.ScientificLeaderChance = 0;
		gameData.rules.ScientificTraitLeaderChance = 100;
		us.freeTechsRemaining = 2;
		us.ClaimFreeTech(gameData, UnknownToAll());
		Assert.Empty(ScientificLeaders());
		us.civilization.traits.Add(Civilization.Trait.Scientific);
		us.ClaimFreeTech(gameData, UnknownToAll());
		Assert.Single(ScientificLeaders());
	}

	// "Move the scientific leader to your capital and press the 'Increase
	// Scientific Output' button."
	[Fact]
	public void InTheCapitalHeStartsAnAgeOfScience() {
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsAllowCities() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		City town = CityInteractions.BuildCity(site, us, "Town");
		Assert.False(Leader(scientific: true, town.location).CanStartAgeOfScience());
		Assert.False(Leader(scientific: false, capital.location).CanStartAgeOfScience());
		MapUnit leader = Leader(scientific: true, capital.location);
		Assert.Contains(UnitAction.StartAgeOfScience, leader.GetAvailableActions());

		// Enough beakers for a quarter more to show: gold under the capital,
		// no despotism penalty.
		us.government = gameData.governments.Find(g => g.name == "Monarchy");
		capital.location.Resource = gameData.Resources.Find(r => r.Name == "Gold");
		us.knownTechs.UnionWith(gameData.techs.Select(t => t.id));
		us.scienceRate = 10;
		us.luxuryRate = 0;
		int beakers = capital.CurrentCommerceYield().beakers;
		Assert.True(beakers >= 4);

		Assert.True(leader.StartAgeOfScience());
		Assert.DoesNotContain(leader, us.units);
		Assert.Equal(gameData.rules.AgeOfScienceDuration, us.ageOfScienceTurnsLeft);
		Assert.True(Assert.Single(EngineStorage.messagesToUI.OfType<MsgAgeOfScience>()).started);
		Assert.Equal((int)Math.Floor(beakers * (1 + gameData.rules.AgeOfScienceResearchBonus / 100.0)), capital.CurrentCommerceYield().beakers);
		// One at a time.
		Assert.False(Leader(scientific: true, capital.location).CanStartAgeOfScience());

		for (int turn = 0; turn < gameData.rules.AgeOfScienceDuration; ++turn) {
			us.CountDownAgeOfScience();
		}
		Assert.False(us.InAgeOfScience());
		Assert.Equal(beakers, capital.CurrentCommerceYield().beakers);
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgAgeOfScience>(), m => !m.started);
	}

	// He can hurry "any improvement (including a Great Wonder)"; a military
	// leader can't hurry great wonders. He leads no army.
	[Fact]
	public void HeHurriesGreatWondersButLeadsNoArmy() {
		capital.SetItemBeingProduced(gameData.Buildings.Find(b => b.name == "The Pyramids"));
		Assert.False(Leader(scientific: false, capital.location).CanHurryProduction());
		MapUnit scientist = Leader(scientific: true, capital.location);
		Assert.True(scientist.CanHurryProduction());

		// With cities enough for an army, a military leader could lead one.
		foreach (Tile site in gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsAllowCities() && t.unitsOnTile.Count == 0).ToList()) {
			if (us.cities.Count >= gameData.rules.CitiesNeededToSupportAnArmy) {
				break;
			}
			if (gameData.cities.All(c => c.location.DistanceTo(site) > 3)) {
				CityInteractions.BuildCity(site, us, us.GetNextCityName());
			}
		}
		Assert.True(us.CanSupportAnotherArmy());
		Assert.True(Leader(scientific: false, capital.location).CanBuildArmy());
		Assert.False(scientist.CanBuildArmy());
	}

	[Fact]
	public void SavesKeepItAll() {
		MapUnit leader = Leader(scientific: true, capital.location);
		us.ageOfScienceTurnsLeft = 7;
		us.scientificLeadersCreated = 3;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Player them = reloaded.players.Find(p => p.id == us.id);
		Assert.True(reloaded.mapUnits.Find(u => u.id == leader.id).scientificLeader);
		Assert.Equal((7, 3), (them.ageOfScienceTurnsLeft, them.scientificLeadersCreated));
		Assert.Equal(us.civilization.scientificLeaderNames, them.civilization.scientificLeaderNames);
		Assert.NotEmpty(us.civilization.scientificLeaderNames);
	}

	[SkippableFact]
	public void TheNamesComeFromCiv3sRules() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "2 Rise of Rome.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.Contains(save.Civilizations, c => c.scientificLeaderNames.Count > 0);
		Assert.DoesNotContain(save.Civilizations.SelectMany(c => c.scientificLeaderNames), n => n.Contains(' '));
	}

	// The AI starts an Age of Science when it's worth more than hurrying.
	[Fact]
	public void TheAIWeighsTheAgeOfScience() {
		MapUnit leader = Leader(scientific: true, capital.location);
		us.scienceRate = 10;
		us.luxuryRate = 0;
		Assert.True(LeaderAI.AgeOfScienceValue(us, leader) > 0);
		Assert.Equal(0, LeaderAI.AgeOfScienceValue(us, Leader(scientific: false, capital.location)));

		// A cheap improvement to hurry is worth less than the age.
		capital.SetItemBeingProduced(gameData.Buildings.Find(b => b.name == "Barracks"));
		capital.SetStoredShields(us.ShieldCost(capital.itemBeingProduced) - 1);
		new LeaderAI().PlayTurnImpl(us, leader);
		Assert.True(us.InAgeOfScience());
	}

	[Fact]
	public async Task TheAgeCountsDownEachTurn() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		Player owner = game.players.First(p => !p.isHuman && !p.isBarbarians);
		owner.ageOfScienceTurnsLeft = 3;

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();
		Assert.Equal(2, owner.ageOfScienceTurnsLeft);
	}
}
