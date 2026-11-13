using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's future techs: once every advance is known, research goes on to
// Future Tech 1, 2, ..., each counting in the score like a content citizen.
// Adapted from GBiebs/OpenCiv3.
public class FutureTechTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public FutureTechTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => p.isHuman);
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private void LearnEverything() {
		foreach (Tech t in gameData.techs) {
			us.knownTechs.Add(t.id);
		}
		us.SetCurrentlyResearchedTech(null);
	}

	[Fact]
	public void NotWhileAdvancesRemain() {
		us.SetCurrentlyResearchedTech(null);
		us.beakers = 100000;
		us.turnsResearched = 100;
		us.CompletePaidResearch(gameData);
		Assert.Equal(0, us.futureTechs);
	}

	[Fact]
	public void OnceEverythingIsKnownResearchGoesOn() {
		LearnEverything();
		int cost = gameData.FutureTechCostFor(us);
		Assert.True(cost > 0);
		Assert.StartsWith("Future Tech 1 (", us.SummarizeScience(gameData));

		// Not before the least turns of research, even with the beakers.
		us.beakers = cost;
		us.turnsResearched = 0;
		us.CompletePaidResearch(gameData);
		Assert.Equal(0, us.futureTechs);

		us.turnsResearched = gameData.rules.MinimumResearchTime;
		us.CompletePaidResearch(gameData);
		Assert.Equal(1, us.futureTechs);
		Assert.Equal(0, us.beakers);
		Assert.Equal(0, us.turnsResearched);
		MsgShowScienceAdvisorPopup news = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowScienceAdvisorPopup>());
		Assert.Equal("Our scientists have discovered Future Tech 1!", news.message);
		Assert.StartsWith("Future Tech 2 (", us.SummarizeScience(gameData));

		// With no science, no progress.
		Assert.Equal(0, us.BeakersPerTurn());
		Assert.Equal("Future Tech 2 (-- turns)", us.SummarizeScience(gameData));
		// An unfunded turn doesn't count towards the most turns of research,
		// so the clamp can't hand out a future tech for nothing.
		us.turnsResearched = gameData.rules.MaximumResearchTime - 1;
		us.DoPerTurnScienceUpdates(gameData);
		Assert.Equal(gameData.rules.MaximumResearchTime - 1, us.turnsResearched);
		us.CompletePaidResearch(gameData);
		Assert.Equal(1, us.futureTechs);
	}

	[Fact]
	public void TheCostIsTheRulesFutureTechCost() {
		LearnEverything();
		int cost = gameData.FutureTechCostFor(us);
		int rule = gameData.rules.FutureTechCost;
		Assert.True(rule > 0);
		gameData.rules.FutureTechCost = 2 * rule;
		Assert.InRange(gameData.FutureTechCostFor(us), 2 * cost - 1, 2 * cost + 1);
	}

	[Fact]
	public void EachCountsLikeAContentCitizenAndIsSaved() {
		float before = ScoreVictory.ComputeTurnScore(us, gameData);
		us.futureTechs = 3;
		// A point each per turn, times the difficulty.
		Assert.Equal(before + 3 * ScoreVictory.GetDifficultyScoreFactor(gameData), ScoreVictory.ComputeTurnScore(us, gameData), 3);

		C7GameData.GameData reloaded = SaveGame.FromJSON(SaveGame.FromGameData(gameData).ToCompactJSON()).ToGameData(fixture.behaviors);
		Assert.Equal(3, reloaded.GetPlayer(us.id).futureTechs);
	}
}
