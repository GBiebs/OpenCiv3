using System.IO;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData.Victory;

// Civ3's cultural victory: a city with 20,000 culture points, or a civ with
// 100,000 on a standard map (60,000 on a tiny one to 160,000 on a huge one)
// and twice the culture of any other civ.
public class CulturalVictoryTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City home;
	private readonly City abroad;

	public CulturalVictoryTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		SaveGame save = fixture.saveGame.Clone();
		save.VictoryConditions = new VictoryConditions { AllowCulturalVictory = true };
		gameData = save.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		Player[] civs = gameData.players.Where(p => !p.isBarbarians).Take(2).ToArray();
		(us, them) = (civs[0], civs[1]);
		home = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		abroad = CityInteractions.BuildCity(them.units[0].location, them, "Abroad");
		// A standard map.
		WorldSize standard = gameData.worldSizes.Find(w => w.name == "Standard");
		(gameData.map.numTilesWide, gameData.map.numTilesTall) = (standard.width, standard.height);
	}

	private CulturalVictory Victory() => gameData.victories.OfType<CulturalVictory>().Single();

	[Fact]
	public void TheGameHasItWhenItsAllowed() {
		Assert.Single(gameData.victories.OfType<CulturalVictory>());

		SaveGame save = fixture.saveGame.Clone();
		save.VictoryConditions = new VictoryConditions();
		Assert.Empty(save.ToGameData(fixture.behaviors).victories.OfType<CulturalVictory>());
	}

	[Fact]
	public void ACityWith20000CulturePointsWins() {
		CulturalVictory victory = CulturalVictory.FromRules(gameData);
		home.perPlayerCulture[us] = 19999;
		Assert.False(victory.HasVictory(victory.Evaluate(us, gameData)));

		home.perPlayerCulture[us] = 20000;
		Assert.True(victory.HasVictory(victory.Evaluate(us, gameData)));

		// Whatever the rest of the world has.
		abroad.perPlayerCulture[them] = 50000;
		Assert.True(victory.HasVictory(victory.Evaluate(us, gameData)));
	}

	[Fact]
	public void ACivNeeds100000AndTwiceAnyOther() {
		// Leave the one-city victory out of it.
		gameData.rules.victory.cultureCityPoints = int.MaxValue;
		CulturalVictory victory = CulturalVictory.FromRules(gameData);
		City second = CityInteractions.BuildCity(
			gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && gameData.cities.All(c => c.location.DistanceTo(t) > 3)),
			us, "Second");
		home.perPlayerCulture[us] = 19000;
		second.perPlayerCulture[us] = 80999;
		Assert.False(victory.HasVictory(victory.Evaluate(us, gameData)));

		second.perPlayerCulture[us] = 81000;
		Assert.True(victory.HasVictory(victory.Evaluate(us, gameData)));

		// No other civ may have more than half as much.
		abroad.perPlayerCulture[them] = 50000;
		Assert.True(victory.HasVictory(victory.Evaluate(us, gameData)));
		abroad.perPlayerCulture[them] = 50001;
		Assert.False(victory.HasVictory(victory.Evaluate(us, gameData)));
	}

	[Theory]
	[InlineData("Tiny", 60000)]
	[InlineData("Small", 80000)]
	[InlineData("Standard", 100000)]
	[InlineData("Large", 130000)]
	[InlineData("Huge", 160000)]
	public void TheCivsCultureNeededGrowsWithTheMap(string size, int needed) {
		WorldSize worldSize = gameData.worldSizes.Find(w => w.name == size);
		(gameData.map.numTilesWide, gameData.map.numTilesTall) = (worldSize.width, worldSize.height);
		Assert.Equal(needed, CulturalVictory.CivPointsNeeded(gameData));

		gameData.rules.victory.cultureCivPoints = 50000;
		Assert.Equal(needed / 2, CulturalVictory.CivPointsNeeded(gameData));
	}

	[Fact]
	public void TheEndOfTheTurnDeclaresIt() {
		home.perPlayerCulture[us] = 20000;
		TurnHandling.CheckVictory(gameData);

		Assert.True(gameData.gameOver);
		Assert.Equal(us, gameData.winner);
		MsgVictory message = Assert.Single(EngineStorage.messagesToUI.OfType<MsgVictory>());
		Assert.IsType<CulturalVictory>(message.victory);
	}

	[Fact]
	public void TheLimitsComeFromTheRules() {
		gameData.rules.victory.cultureCityPoints = 500;
		home.perPlayerCulture[us] = 500;
		Assert.True(CulturalVictory.FromRules(gameData).HasVictory(Victory().Evaluate(us, gameData)));

		gameData.rules.victory.cultureCityPoints = 20000;
		gameData.rules.victory.cultureCivPoints = 1000;
		gameData.rules.victory.cultureCivLead = 3;
		home.perPlayerCulture[us] = 1000;
		abroad.perPlayerCulture[them] = 333;
		Assert.True(CulturalVictory.FromRules(gameData).HasVictory(Victory().Evaluate(us, gameData)));
		abroad.perPlayerCulture[them] = 334;
		Assert.False(CulturalVictory.FromRules(gameData).HasVictory(Victory().Evaluate(us, gameData)));
	}
}

// The limits come from Civ3's GAME section ("game limits").
public class VictoryLimitsImportTest {
	[SkippableFact]
	public void TheGameLimitsComeFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "5 Mesoamerica.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		QueryCiv3.Biq.GAME game = BiqData.LoadFile(scenario).Game[0];

		Assert.Equal(game.DominationTerrain, save.Rules.victory.dominationTerrainPercent);
		Assert.Equal(game.DominationPopulation, save.Rules.victory.dominationPopulationPercent);
		Assert.Equal(game.OneCityCultureWin, save.Rules.victory.cultureCityPoints);
		Assert.Equal(game.AllCitiesCultureWin, save.Rules.victory.cultureCivPoints);
	}
}

// A Civ3 save keeps the limits its game was set up with.
public class VictoryLimitsSavImportTest {
	[SkippableFact]
	public void TheGameLimitsComeFromTheSave() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// One of the user's saves, from a mod with its own limits.
		string file = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Saves", "Suede of the Dwarves, 1545 AD.SAV");
		Skip.If(!File.Exists(file), "The user's save isn't there.");
		SaveGame save = ImportCiv3.ImportSav(file, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.Equal(50000, save.Rules.victory.cultureCityPoints);
		Assert.Equal(100000, save.Rules.victory.cultureCivPoints);
	}
}
