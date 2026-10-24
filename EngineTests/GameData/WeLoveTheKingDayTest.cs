using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Civ3's We Love the King Day (Civilopedia "We Love the __ Day"): a city that
// isn't starving, has no resisters and no unhappy citizens, more happy than
// content citizens, and is at least size 6 (RULE) celebrates, and wastes
// fewer shields: for waste only, its distance to the palace counts half and
// its optimal city number grows by a quarter of the map's (Firaxis via
// alexman, "Everything About Corruption: C3C Edition").
public class WeLoveTheKingDayTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City capital;
	private readonly City city;

	public WeLoveTheKingDayTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		capital = CityInteractions.BuildCity(us.units[0].location, us, "Capital");
		// A city on grassland some way from the palace, so it can feed six
		// citizens and has waste to lose, but not so far that it hits the cap.
		Tile site = GrasslandSite(t => t.DistanceTo(capital.location) >= 8 && t.DistanceTo(capital.location) <= 14);
		city = CityInteractions.BuildCity(site, us, "Province");
		Grow(city, 6);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	// An empty land tile with grassland nearly all around, where a city of six
	// is fed, away from other cities.
	private Tile GrasslandSite(Func<Tile, bool> also) => GrasslandSite(gameData, also);

	private static Tile GrasslandSite(C7GameData.GameData game, Func<Tile, bool> also) {
		return game.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.neighbors.Values.Count(n => n.overlayTerrainType.Key == "grassland") >= 7
			&& game.cities.All(c => c.location.DistanceTo(t) > 3) && also(t));
	}

	private void Grow(City c, int size) => Grow(gameData, c, size);

	private static void Grow(C7GameData.GameData game, City c, int size) {
		CitizenType laborer = game.citizenTypes.Find(t => t.IsDefaultCitizen);
		while (c.residents.Count < size) {
			CityResident resident = new() { city = c, nationality = c.owner.civilization, citizenType = laborer };
			c.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(game, resident);
		}
	}

	// Gives the city's laborers these moods, in order (the rest content).
	private void Moods(params CityResident.Mood[] moods) {
		int i = 0;
		foreach (CityResident r in city.residents.Where(r => r.citizenType.IsDefaultCitizen && !r.resisting)) {
			r.mood = i < moods.Length ? moods[i] : CityResident.Mood.Content;
			++i;
		}
	}

	private static readonly CityResident.Mood Happy = CityResident.Mood.Happy;
	private static readonly CityResident.Mood Unhappy = CityResident.Mood.Unhappy;

	[Fact]
	public void TheRulesetNeedsSizeSixAndNamesEachCivsLeader() {
		Assert.Equal(6, gameData.rules.MinimumPopulationForWeLoveTheKing);
		Assert.Equal("Emperor", gameData.civilizations.Find(c => c.name == "Rome").leaderTitle);
		Assert.Equal("Queen", gameData.civilizations.Find(c => c.name == "Egypt").leaderTitle);
	}

	[Fact]
	public void AHappyEnoughCityOfSixCelebrates() {
		Assert.True(city.residents.All(r => r.citizenType.IsDefaultCitizen));
		Assert.True(city.FoodGrowthPerTurn() >= 0);
		Moods(Happy, Happy, Happy, Happy);
		Assert.True(city.QualifiesForCelebration(gameData));

		// More happy than content citizens.
		Moods(Happy, Happy, Happy);
		Assert.False(city.QualifiesForCelebration(gameData));

		// No unhappy citizens.
		Moods(Happy, Happy, Happy, Happy, Happy, Unhappy);
		Assert.False(city.QualifiesForCelebration(gameData));
	}

	[Fact]
	public void TooSmallACityDoesnt() {
		city.RemoveCitizen(city.residents.Last());
		Moods(Happy, Happy, Happy, Happy, Happy);
		Assert.False(city.QualifiesForCelebration(gameData));
	}

	[Fact]
	public void ACityWithResistersDoesnt() {
		Moods(Happy, Happy, Happy, Happy, Happy, Happy);
		Assert.True(city.QualifiesForCelebration(gameData));
		CityResident resister = city.residents.Last();
		resister.resisting = true;
		resister.tileWorked.personWorkingTile = null;
		resister.tileWorked = Tile.NONE;
		Assert.False(city.QualifiesForCelebration(gameData));
	}

	[Fact]
	public void AStarvingCityDoesnt() {
		Moods(Happy, Happy, Happy, Happy, Happy, Happy);
		Assert.True(city.QualifiesForCelebration(gameData));
		// Specialists eat but grow nothing.
		CitizenType specialist = us.GetKnownSpecialists(gameData).First();
		foreach (CityResident r in city.residents.Skip(2)) {
			r.tileWorked.personWorkingTile = null;
			r.tileWorked = Tile.NONE;
			r.citizenType = specialist;
		}
		Assert.True(city.FoodGrowthPerTurn() < 0);
		Assert.False(city.QualifiesForCelebration(gameData));
	}

	[Fact]
	public void TheCelebrationStartsAndEndsWithTheMoodsAndAHumanIsTold() {
		Player human = gameData.players.First(p => p.isHuman);
		Tile site = GrasslandSite(t => gameData.cities.All(c => c.location.DistanceTo(t) > 6));
		City theirs = CityInteractions.BuildCity(site, human, "Festival");
		Grow(theirs, 6);
		foreach (CityResident r in theirs.residents) {
			r.mood = Happy;
		}

		human.UpdateCelebrations(gameData);
		Assert.True(theirs.celebrating);
		MsgCelebration started = Assert.Single(EngineStorage.messagesToUI.OfType<MsgCelebration>());
		Assert.True(started.started);
		Assert.Equal(human.id, started.playerId);
		Assert.Equal($"'We love the {human.civilization.leaderTitle}' day celebrated in Festival.", started.Text());

		// Nothing new while it goes on.
		human.UpdateCelebrations(gameData);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgCelebration>());

		theirs.residents[0].mood = Unhappy;
		human.UpdateCelebrations(gameData);
		Assert.False(theirs.celebrating);
		MsgCelebration ended = EngineStorage.messagesToUI.OfType<MsgCelebration>().Last();
		Assert.False(ended.started);
		Assert.Equal($"'We love the {human.civilization.leaderTitle}' celebration has ended in Festival.", ended.Text());
	}

	[Fact]
	public async Task ACityCelebratesThroughARealTurn() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		Tile site = GrasslandSite(game, t => game.mapUnits.All(u => u.owner == human || u.location.DistanceTo(t) > 6));
		City festival = CityInteractions.BuildCity(site, human, "Festival");
		Grow(game, festival, 6);
		// The buildings make everyone content, and four civs declaring war
		// on us each rally a quarter of the city: four happy, two content.
		festival.AddBuilding(game.Buildings.Find(b => b.name == "Temple"));
		festival.AddBuilding(game.Buildings.Find(b => b.name == "Colosseum"));
		foreach (Player ai in game.players.Where(p => !p.isHuman && !p.isBarbarians).Take(4)) {
			ai.DeclareWarOn(human, game.turn);
		}
		EngineStorage.messagesToUI.Clear();

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();

		Assert.True(festival.celebrating);
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgCelebration>(), m => m.city == festival && m.started);
	}

	[Fact]
	public void TheAIIsntTold() {
		Moods(Happy, Happy, Happy, Happy, Happy, Happy);
		us.UpdateCelebrations(gameData);
		Assert.True(city.celebrating);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgCelebration>());
	}

	[Fact]
	public void ACelebratingCityWastesLessButIsNoLessCorrupt() {
		// Forest under three workers, for shields to waste.
		TerrainType forest = gameData.terrainTypes.Find(t => t.Key == "forest");
		foreach (CityResident r in city.residents.Take(3)) {
			r.tileWorked.overlayTerrainType = forest;
		}
		us.DoCorruptionCalculations(gameData);
		float corruption = city.corruption;
		Assert.True(corruption > 0);
		Assert.Equal(corruption, city.waste);
		int wasted = city.CurrentProductionYield().corrupt;
		Assert.True(wasted >= 2);

		city.celebrating = true;
		us.DoCorruptionCalculations(gameData);
		Assert.Equal(corruption, city.corruption);
		Assert.True(city.waste < corruption);
		Assert.True(city.CurrentProductionYield().corrupt < wasted);
	}

	[Fact]
	public void TheWasteFollowsTheArticlesFormula() {
		// Rank 1 of 2 cities, distance d from the palace: the celebrating
		// city's distance part halves and its optimal number grows by OCN / 4.
		us.DoCorruptionCalculations(gameData);
		float normalWaste = city.waste;
		city.celebrating = true;
		us.DoCorruptionCalculations(gameData);

		float rank = city.rankIndex;
		float nOpt = Math.Max(1, us.GetAdjustedOptimalCityNumber(gameData));
		float celebratingNOpt = nOpt + .25f * gameData.map.optimalNumberOfCities;
		float Rank(float n) => rank < n ? rank / (2 * n) : (2 * rank - n) / (2 * n);
		float normalDistance = normalWaste - Rank(nOpt);
		Assert.True(normalDistance > 0 && normalDistance < 1);
		Assert.Equal(normalDistance / 2 + Rank(celebratingNOpt), city.waste, 4);
	}

	[Fact]
	public void TheCelebrationIsSaved() {
		city.celebrating = true;

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.True(reloaded.cities.Find(c => c.id == city.id).celebrating);
		Assert.False(reloaded.cities.Find(c => c.id == capital.id).celebrating);
	}
}

// Celebrations and leader titles from a real Civ3 save: a turn into the
// Napoleonic Europe scenario, 20 cities celebrate, London among them.
public class WeLoveTheKingDayImportTest : RemoteSaveLoader {
	[SkippableFact]
	public async Task CelebrationsAndLeaderTitlesComeFromCiv3Saves() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenarioBiqPath = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Scenarios", "8 MP Napoleonic Europe.biq");
		string scenarioPediaPath = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "Napoleonic Europe", "Text", "PediaIcons.txt");
		string uri = "https://www.dropbox.com/scl/fi/l8bm8dhacd85cwyxn3nn7/MultiTurnDeal_Save_F.SAV?rlkey=mr7itejsucesk14h859jwjffu&st=fuo8n4d9&dl=1";

		(SaveGame game, Exception ex, string _) = await LoadGameAndData("MultiTurnDeal_Save_F.SAV", "saves/multi-turn-deals", uri, scenarioBiqPath, scenarioPediaPath);
		Assert.Null(ex);

		Assert.Equal(20, game.Cities.Count(c => c.celebrating));
		Assert.True(game.Cities.Find(c => c.name == "London").celebrating);
		Assert.False(game.Cities.Find(c => c.name == "Paris").celebrating);
		Assert.Equal("Emperor", game.Civilizations.Find(c => c.name == "France").leaderTitle);
		Assert.Equal("Czar", game.Civilizations.Find(c => c.name == "Russia").leaderTitle);
	}
}
