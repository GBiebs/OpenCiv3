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

// Civ3's culture flipping (CivFanatics Civ3 FAQ, from Firaxis):
// P = [(F + T) × Cc × H × (Cte / Cty) − G] / D, with F the city's citizens of
// the other civ (resisters double), T the tiles of its 21 that civ owns, Cc 2
// if that civ has more culture in the city, H ½ celebrating or 2 in
// disorder, Cte / Cty the civs' total culture, G the garrison, and D 2000
// times the ratio of the distances to the two capitals, held to ¼..4.
public class CultureFlipTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City ourCapital;
	private readonly City theirCapital;
	private readonly City border;

	public CultureFlipTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		ourCapital = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		theirCapital = CityInteractions.BuildCity(them.units[0].location, them, "Metropolis");
		// Our city close to their capital, far from ours.
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.RankDistanceTo(theirCapital.location) >= 3 && t.RankDistanceTo(theirCapital.location) <= 4
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 2));
		border = CityInteractions.BuildCity(site, us, "Frontier");
		Grow(border, 6);
		ourCapital.perPlayerCulture[us] = 100;
		theirCapital.perPlayerCulture[them] = 300;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private void Grow(City city, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			CityResident resident = new() { city = city, nationality = city.owner.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
	}

	private int TheirTiles() {
		return border.location.GetTilesWithinRankDistance(gameData.rules.MaxRankOfWorkableTiles)
			.Count(t => t != border.location && t.OwningPlayer() == them);
	}

	private double Divisor() {
		double ratio = (double)border.location.RankDistanceTo(theirCapital.location) / border.location.RankDistanceTo(ourCapital.location);
		return 2000 * Math.Clamp(ratio, .25, 4);
	}

	private double Chance() => CultureFlip.Chance(gameData, border, them);

	[Fact]
	public void TheChanceFollowsTheFormula() {
		border.residents[0].nationality = them.civilization;
		border.residents[1].nationality = them.civilization;
		border.residents[1].resisting = true;
		int tiles = TheirTiles();
		// F = 1 + 2 (a resister counts double), Cc = 1, H = 1, Cte / Cty = 300 / 100.
		double expected = (3 + tiles) * 1 * 1 * 3.0 / Divisor();
		Assert.True(expected > 0);
		Assert.Equal(expected, Chance(), 9);

		// More of their culture in the city doubles it.
		border.perPlayerCulture[them] = 50;
		Assert.Equal(2 * expected, Chance(), 9);
		border.perPlayerCulture[them] = 0;

		// Celebrating halves it, disorder doubles it.
		border.celebrating = true;
		Assert.Equal(expected / 2, Chance(), 9);
		border.celebrating = false;
		border.isInCivilDisorder = true;
		Assert.Equal(2 * expected, Chance(), 9);
		border.isInCivilDisorder = false;

		// Each defender takes 1 / D off; a worker doesn't.
		gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Spearman"), border.location);
		gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Worker"), border.location);
		Assert.Equal(expected - 1 / Divisor(), Chance(), 9);
	}

	// The formula's numbers are rules.
	[Fact]
	public void TheFormulasNumbersAreRules() {
		border.residents[0].nationality = them.civilization;
		double expected = Chance();
		Assert.True(expected > 0);
		CultureFlipRules rules = gameData.rules.cultureFlip;
		rules.divisor = 1000;
		Assert.Equal(2 * expected, Chance(), 9);
		rules.celebratingFactor = 3;
		border.celebrating = true;
		Assert.Equal(6 * expected, Chance(), 9);
		border.celebrating = false;
		rules.disorderFactor = 5;
		border.isInCivilDisorder = true;
		Assert.Equal(10 * expected, Chance(), 9);
		border.isInCivilDisorder = false;
		rules.localCultureFactor = 4;
		border.perPlayerCulture[them] = 50;
		Assert.Equal(8 * expected, Chance(), 9);
	}

	[Fact]
	public void NothingDrawsACityWithNoTiesToTheOtherCiv() {
		// No citizens or land of theirs: nothing to multiply.
		foreach (Tile t in border.location.GetTilesWithinRankDistance(2).Where(t => t.OwningPlayer() == them)) {
			t.owningCity = border;
		}
		Assert.Equal(0, Chance());
	}

	[Fact]
	public void CapitalsNeverFlipNorCitiesThatJustChangedHands() {
		ourCapital.residents.ForEach(r => r.nationality = them.civilization);
		Assert.Equal(0, CultureFlip.Chance(gameData, ourCapital, them));

		border.residents.ForEach(r => r.nationality = them.civilization);
		Assert.True(Chance() > 0);
		border.flippedOnTurn = gameData.turn;
		Assert.Equal(0, Chance());
		border.flippedOnTurn = -1;
		border.capturedOnTurn = gameData.turn;
		Assert.Equal(0, Chance());
	}

	[Fact]
	public void NoCityFlipsWhenConversionsAreOff() {
		border.residents.ForEach(r => r.nationality = them.civilization);
		Assert.True(Chance() > 0);
		gameData.rules.AllowCulturalConversions = false;
		Assert.Equal(0, Chance());
	}

	[Fact]
	public void TheDistanceRatioIsHeldToAQuarter() {
		border.residents.ForEach(r => r.nationality = them.civilization);
		int toTheirs = border.location.RankDistanceTo(theirCapital.location);
		// Our capital moves so far away that their capital is less than a
		// quarter as far.
		Tile farSite = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& t.RankDistanceTo(border.location) > 4 * toTheirs + 4 && gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		ourCapital.capital = false;
		City farCapital = CityInteractions.BuildCity(farSite, us, "Far Home");
		farCapital.capital = true;
		Assert.True(2000.0 * toTheirs / border.location.RankDistanceTo(farSite) < 500);

		// F = 6, Cc = 1, H = 1, Cte / Cty = 300 / 100, D = 500.
		Assert.Equal((6 + TheirTiles()) * 3.0 / 500, Chance(), 9);
	}

	[Fact]
	public void AFlipHandsTheCityOverWithoutItsGarrisonOrSmallWonders() {
		border.residents[0].nationality = them.civilization;
		border.residents[0].resisting = true;
		border.AddBuilding(gameData.Buildings.Find(b => b.name == "Heroic Epic"), gameData);
		border.AddBuilding(gameData.Buildings.Find(b => b.name == "The Pyramids"), gameData);
		border.AddBuilding(gameData.Buildings.Find(b => b.name == "Temple"), gameData);
		MapUnit spearman = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Spearman"), border.location);

		CultureFlip.Flip(gameData, border, them);

		Assert.Equal(them, border.owner);
		Assert.Contains(border, them.cities);
		Assert.DoesNotContain(border, us.cities);
		Assert.DoesNotContain(spearman, gameData.mapUnits);
		Assert.Equal(["Temple", "The Pyramids"], border.constructed_buildings.Select(cb => cb.building.name).Order());
		// Citizens keep their nationality; theirs stop resisting.
		Assert.Equal(5, border.residents.Count(r => r.nationality == us.civilization));
		Assert.False(border.residents[0].resisting);
		MsgCityFlipped told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityFlipped>());
		Assert.Equal((us.id, them.id), (told.fromId, told.toId));
		Assert.Equal(["The Pyramids"], told.wonders);
	}

	[Fact]
	public void ACityThatWillSurelyFlipDoesAtTheTurn() {
		border.residents.ForEach(r => r.nationality = them.civilization);
		theirCapital.perPlayerCulture[them] = 100_000_000;
		Assert.Equal(1, Chance());

		CultureFlip.CheckFlips(gameData, us);
		Assert.Equal(them, border.owner);
		Assert.Equal(us, ourCapital.owner);
	}

	[Fact]
	public void TheGainerMayRebuffTheRebels() {
		CultureFlip.Flip(gameData, border, them);
		Assert.True(CultureFlip.Rebuff(gameData, border));

		Assert.Equal(us, border.owner);
		Assert.Contains(border, us.cities);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityRebuffed>());
		// Not again this turn.
		Assert.False(CultureFlip.CanFlip(gameData, border));
		Assert.False(CultureFlip.Rebuff(gameData, border));
	}

	[Fact]
	public async Task AHumansCityCanFlipAtTheEndOfTheTurn() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		Player rival = game.players.First(p => !p.isHuman && !p.isBarbarians);
		City home = CityInteractions.BuildCity(human.units[0].location, human, "Home");
		City pull = CityInteractions.BuildCity(rival.units[0].location, rival, "Lure");
		pull.perPlayerCulture[rival] = 100_000_000;
		Tile site = game.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& game.cities.All(c => c.location.DistanceTo(t) > 3));
		City town = CityInteractions.BuildCity(site, human, "Town");
		town.residents.ForEach(r => r.nationality = rival.civilization);
		EngineStorage.messagesToUI.Clear();

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();

		Assert.Equal(rival, town.owner);
		Assert.Equal(human, home.owner);
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgCityFlipped>(), m => m.city == town && m.fromId == human.id);
	}
}

// Whether a game allows cultural conversions comes from its scenario's
// settings: Napoleonic Europe turns them off, Mesoamerica leaves them on.
public class CultureFlipImportTest {
	private static SaveGame Import(string scenarioFile) {
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", scenarioFile);
		return ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
	}

	[SkippableFact]
	public void TheRuleComesFromTheScenario() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		Assert.False(Import("8 Napoleonic Europe.biq").Rules.AllowCulturalConversions);
		Assert.True(Import("5 Mesoamerica.biq").Rules.AllowCulturalConversions);
	}

	[SkippableFact]
	public void AnOrdinaryGameAllowsThem() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// A random-map Civ3 game on the stock rules (the save tests download it).
		string save = Path.Combine(PathUtils.getDataPath("saves"), "12345.SAV");
		Skip.If(!File.Exists(save), "The sample save isn't downloaded.");

		SaveGame game = ImportCiv3.ImportSav(save, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.True(game.Rules.AllowCulturalConversions);
	}
}
