using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;
using static C7GameData.CultureReport;

namespace EngineTests.GameData;

// The cultural advisor: each city's culture and the culture its borders need
// next, culture a turn from improvements and from wonders, our wonders, how
// other civs see our culture, and the advice.
public class CultureReportTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City home;
	private readonly City abroad;

	public CultureReportTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		home = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		abroad = CityInteractions.BuildCity(them.units[0].location, them, "Abroad");
		// No palace culture: only what each test builds.
		home.constructed_buildings.Clear();
		abroad.constructed_buildings.Clear();
	}

	private Building Improvement => gameData.Buildings.First(b => !b.IsWonder() && b.culturePerTurn > 0);
	private Building Wonder => gameData.Buildings.First(b => b.IsGreatWonder() && b.culturePerTurn > 0);

	[Fact]
	public void CultureByCityImprovementsAndWonders() {
		home.AddBuilding(Improvement, gameData);
		home.AddBuilding(Wonder, gameData);
		int perTurn = Improvement.culturePerTurn + Wonder.culturePerTurn;

		CityCulture city = Assert.Single(Cities(us));
		Assert.Equal(new CityCulture(home, 0, perTurn, 10), city);
		Assert.Equal((10 + perTurn - 1) / perTurn, TurnsToExpand(city));
		Assert.Equal((Improvement.culturePerTurn, Wonder.culturePerTurn), PerTurn(us));
		Assert.Equal([new WonderCulture(Wonder, home, Wonder.culturePerTurn)], Wonders(us));

		// Past 10 culture, the next border needs 100.
		home.perPlayerCulture[us] = 15;
		Assert.Equal(100, Cities(us)[0].nextExpansion);
		Assert.Null(TurnsToExpand(new CityCulture(home, 15, 0, 100)));
	}

	[Fact]
	public void HowTheyThinkOfOurCulture() {
		us.EnsureRelationshipExists(them);
		home.perPlayerCulture[us] = 400;
		abroad.perPlayerCulture[them] = 100;
		(Player civ, CultureOpinion opinion) = Assert.Single(Opinions(gameData, us));
		Assert.Equal(them, civ);
		// Four times their culture: the best level (Civ3's "in awe of", 300%).
		Assert.Equal(gameData.cultureOpinions.MaxBy(o => o.cultureRatioPercent), opinion);
	}

	[Fact]
	public void TheCulturalAdvisorsAdvice() {
		us.EnsureRelationshipExists(them);
		AdvisorAdvice.Advice none = AdvisorAdvice.Culture(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Sad, none.mood);
		Assert.StartsWith("Our cities make no culture.", none.text);

		// A little culture, far less than theirs: the worst opinion.
		home.AddBuilding(Improvement, gameData);
		home.perPlayerCulture[us] = 1;
		abroad.perPlayerCulture[them] = 1000;
		CultureOpinion worst = gameData.cultureOpinions.MinBy(o => o.cultureRatioPercent);
		Assert.Equal($"The {them.civilization.noun} are {worst.name} our culture.", AdvisorAdvice.Culture(gameData, us).text);

		// Level with them: borders about to grow come first.
		abroad.perPlayerCulture[them] = 1;
		home.perPlayerCulture[us] = 10 - Improvement.culturePerTurn;
		AdvisorAdvice.Advice soon = AdvisorAdvice.Culture(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Surprised, soon.mood);
		Assert.Equal("The borders of Home will grow in 1 turn.", soon.text);

		// Otherwise their opinion of us.
		home.perPlayerCulture[us] = 50;
		abroad.perPlayerCulture[them] = 10;
		AdvisorAdvice.Advice best = AdvisorAdvice.Culture(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Happy, best.mood);
		Assert.Equal($"The {them.civilization.noun} are {gameData.cultureOpinions.MaxBy(o => o.cultureRatioPercent).name} our culture.", best.text);
	}
}
