using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using QueryCiv3.Biq;
using Xunit;

namespace EngineTests.AI;

// The AI changes government as Civ3's does: it weighs what each government
// would earn and save it, keeps order at what cost, avoids a government its
// war weariness would overthrow, revolts only for a clear gain, and picks a
// government when anarchy ends.
public class GovernmentAITest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public GovernmentAITest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.civilization.favoriteGovernment = null;
		us.civilization.shunnedGovernment = null;
		us.civilization.traits.Remove(Civilization.Trait.Religious);
		City capital = CityInteractions.BuildCity(us.units[0].location, us, "Capital");
		Grow(capital, 5);
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& t.DistanceTo(capital.location) >= 5 && t.DistanceTo(capital.location) <= 8);
		Grow(CityInteractions.BuildCity(site, us, "Second"), 4);
	}

	private void Grow(City city, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			CityResident resident = new() { city = city, nationality = us.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
	}

	private Government Govt(string name) => gameData.governments.Find(g => g.name == name);

	private void Learn(string tech) {
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == tech).id);
	}

	// A government better than any in every way, open to everyone.
	private Government AddUtopia() {
		Government utopia = new() {
			id = gameData.ids.CreateID("government"),
			name = "Utopia",
			corruptionType = Government.CorruptionType.Minimal,
			hasTradeBonus = true,
			militaryPoliceLimit = 4,
			freeUnitsPerTown = 20,
			freeUnitsPerCity = 20,
			freeUnitsPerMetropolis = 20,
			unitCost = 1,
			workerRate = 3,
		};
		gameData.governments.Add(utopia);
		return utopia;
	}

	// The next turn on which we review our government.
	private int ReviewTurn() {
		int turn = gameData.turn + 1;
		while ((turn + gameData.players.IndexOf(us)) % GovernmentAI.TurnsBetweenReviews != 0) {
			++turn;
		}
		return turn;
	}

	[Fact]
	public void ARevolutionNeedsAClearGain() {
		Assert.False(GovernmentAI.WorthARevolution(us, 100, 110));
		Assert.True(GovernmentAI.WorthARevolution(us, 100, 115));
		// Not for a point or so, however small the empire.
		Assert.False(GovernmentAI.WorthARevolution(us, 5, 6.5));
		Assert.True(GovernmentAI.WorthARevolution(us, -10, -7));

		// A religious civ's anarchy is short.
		us.civilization.traits.Add(Civilization.Trait.Religious);
		Assert.True(GovernmentAI.WorthARevolution(us, 100, 105));
	}

	[Fact]
	public void TheAIRevoltsForAClearlyBetterGovernmentOnItsReviewTurn() {
		Government utopia = AddUtopia();
		Assert.Equal(utopia, GovernmentAI.BestGovernment(us, gameData, out _));

		gameData.turn = ReviewTurn() - 1;
		GovernmentAI.ManageGovernment(us, gameData);
		Assert.Equal("Despotism", us.government.name);

		gameData.turn = ReviewTurn();
		GovernmentAI.ManageGovernment(us, gameData);
		Assert.True(us.government.transitionType);
		Assert.True(us.inAnarchyUntilTurn > gameData.turn);
	}

	[Fact]
	public void AnAIInAnarchyPicksTheBestGovernmentWhenItEnds() {
		Government utopia = AddUtopia();
		us.StartRevolution(gameData);
		int end = us.inAnarchyUntilTurn;

		gameData.turn = end - 1;
		GovernmentAI.ManageGovernment(us, gameData);
		Assert.True(us.government.transitionType);

		gameData.turn = end;
		GovernmentAI.ManageGovernment(us, gameData);
		Assert.Equal(utopia, us.government);
	}

	[Fact]
	public void ItWontRevoltForNothing() {
		gameData.turn = ReviewTurn();
		GovernmentAI.ManageGovernment(us, gameData);
		// Only Despotism is known.
		Assert.Equal("Despotism", us.government.name);
	}

	[Fact]
	public void AGovernmentOurWarsWouldOverthrowIsNeverChosen() {
		Learn("Democracy");
		Government democracy = Govt("Democracy");
		us.DeclareWarOn(them, gameData.turn);
		us.playerRelationships[them.id].warWearinessPoints = 0;
		Assert.True(GovernmentAI.Score(us, democracy, gameData) > -1e6);

		us.playerRelationships[them.id].warWearinessPoints = 95;
		Assert.True(GovernmentAI.Score(us, democracy, gameData) < -1e6);
		Assert.NotEqual(democracy, GovernmentAI.BestGovernment(us, gameData, out _));
	}

	[Fact]
	public void FavoriteAndShunnedGovernmentsTipTheBalance() {
		Learn("Monarchy");
		Government monarchy = Govt("Monarchy");
		double plain = GovernmentAI.Score(us, monarchy, gameData);
		Assert.True(plain > 0);

		us.civilization.favoriteGovernment = "Monarchy";
		Assert.Equal(plain * GovernmentAI.FavoriteGovernmentFactor, GovernmentAI.Score(us, monarchy, gameData), 6);
		us.civilization.favoriteGovernment = null;
		us.civilization.shunnedGovernment = "Monarchy";
		Assert.Equal(plain * GovernmentAI.ShunnedGovernmentFactor, GovernmentAI.Score(us, monarchy, gameData), 6);
	}

	[Fact]
	public void UnitsBeyondTheFreeOnesCostAGovernmentItsSupport() {
		Government utopia = AddUtopia();
		double before = GovernmentAI.Score(us, utopia, gameData);
		utopia.freeUnitsPerTown = utopia.freeUnitsPerCity = utopia.freeUnitsPerMetropolis = 0;
		utopia.unitCost = 3;
		// More than the AI's free units for its difficulty, outside the city
		// so they don't police it.
		Tile camp = us.cities[0].location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		foreach (int _ in Enumerable.Range(0, 30)) {
			gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Warrior"), camp);
		}
		Government actual = us.government;
		us.government = utopia;
		int support = us.TotalUnitsAllowedUnitsAndSupportCost().Item3;
		us.government = actual;
		Assert.True(support > 0);
		Assert.True(GovernmentAI.Score(us, utopia, gameData) <= before - support);
	}

	[Fact]
	public void TheRulesetGivesEachCivItsFavoriteAndShunnedGovernment() {
		Civilization rome = gameData.civilizations.Find(c => c.name == "Rome");
		Assert.Equal(("Republic", "Communism"), (rome.favoriteGovernment, rome.shunnedGovernment));
		Civilization mongols = gameData.civilizations.Find(c => c.name == "Mongols");
		Assert.Equal(("Despotism", "Democracy"), (mongols.favoriteGovernment, mongols.shunnedGovernment));
	}

	[SkippableFact]
	public void FavoriteAndShunnedGovernmentsAreImportedFromCiv3() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// The stock rules have no map to import, so use a scenario with them.
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "5 Mesoamerica.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);

		BiqData scenarioBiq = BiqData.LoadFile(scenario);
		BiqData defaultBiq = BiqData.LoadFile(PathUtils.defaultBicPath);
		BiqData races = scenarioBiq.Race is null ? defaultBiq : scenarioBiq;
		BiqData governments = scenarioBiq.Govt is null ? defaultBiq : scenarioBiq;
		string Name(int index) => index >= 0 ? governments.Govt[index].Name : null;
		foreach (RACE race in races.Race.Skip(1)) {
			Civilization civ = save.Civilizations.Find(c => c.name == race.Name);
			Assert.Equal(Name(race.FavoriteGovernment), civ.favoriteGovernment);
			Assert.Equal(Name(race.ShunnedGovernment), civ.shunnedGovernment);
		}
		Assert.Contains(save.Civilizations, c => c.favoriteGovernment != null);
	}

	[Fact]
	public void KeepingOrderCostsALuxuryPerUnhappyFace() {
		Government utopia = AddUtopia();
		City capital = us.cities[0];
		foreach (int _ in Enumerable.Range(0, 4)) {
			gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Spearman"), capital.location);
		}
		double policed = GovernmentAI.Score(us, utopia, gameData);
		Government actual = us.government;
		us.government = utopia;
		capital.RecalculateCitizenMoods(gameData);
		int happy = capital.residents.Count(r => r.mood == CityResident.Mood.Happy);
		utopia.militaryPoliceLimit = 0;
		capital.RecalculateCitizenMoods(gameData);
		int unpolicedDeficit = capital.residents.Count(r => r.mood == CityResident.Mood.Unhappy) - capital.residents.Count(r => r.mood == CityResident.Mood.Happy);
		us.government = actual;
		Assert.True(unpolicedDeficit > 0 && happy == 0);

		Assert.Equal(policed - unpolicedDeficit, GovernmentAI.Score(us, utopia, gameData), 6);
	}

	[Fact]
	public void TheTilePenaltyCostsTheFoodItTakes() {
		Government utopia = AddUtopia();
		// Irrigated grassland makes 3 food, which the tile penalty cuts to 2.
		TerrainImprovement irrigation = gameData.terrainImprovements.Find(i => i.key == "irrigation");
		TerrainType grassland = gameData.terrainTypes.Find(t => t.Key == "grassland");
		List<Tile> worked = us.cities.SelectMany(c => c.residents).Select(r => r.tileWorked).Where(t => t != Tile.NONE).ToList();
		foreach (Tile t in worked) {
			t.overlayTerrainType = grassland;
			t.Resource = Resource.NONE;
			t.overlays.Add(irrigation);
		}
		double free = GovernmentAI.Score(us, utopia, gameData);

		utopia.hasTilePenalty = true;
		Assert.True(GovernmentAI.Score(us, utopia, gameData) <= free - worked.Count);
	}

	[Fact]
	public async Task AnAIsTurnEndsItsAnarchyWithAGovernment() {
		Government utopia = AddUtopia();
		us.StartRevolution(gameData);
		gameData.turn = us.inAnarchyUntilTurn;

		await PlayerAI.PlayTurn(us, gameData);

		Assert.Equal(utopia, us.government);
	}

	[Fact]
	public void ScoringLeavesTheGameAsItWas() {
		Learn("Monarchy");
		AddUtopia();
		us.DoCorruptionCalculations(gameData);
		foreach (City c in us.cities) {
			c.RecalculateCitizenMoods(gameData);
		}
		Government government = us.government;
		List<float> corruption = us.cities.Select(c => c.corruption).ToList();
		List<CityResident.Mood> moods = us.cities.SelectMany(c => c.residents).Select(r => r.mood).ToList();

		GovernmentAI.BestGovernment(us, gameData, out _);

		Assert.Equal(government, us.government);
		Assert.Equal(corruption, us.cities.Select(c => c.corruption));
		Assert.Equal(moods, us.cities.SelectMany(c => c.residents).Select(r => r.mood));
	}
}
