using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's future techs: once every advance is known, research goes on to
// Future Tech 1, 2, ..., each costing as an advance of the rules' future
// tech cost would, and each counting in the score.
public class FutureTechTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public FutureTechTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians);
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
		us.DoPerTurnScienceUpdates(gameData);
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
		us.DoPerTurnScienceUpdates(gameData);
		Assert.Equal(0, us.futureTechs);

		us.turnsResearched = gameData.rules.MinimumResearchTime - 1;
		us.DoPerTurnScienceUpdates(gameData);
		Assert.Equal(1, us.futureTechs);
		Assert.Equal(0, us.beakers);
		MsgFutureTech news = Assert.Single(EngineStorage.messagesToUI.OfType<MsgFutureTech>());
		Assert.Equal("Our scientists have discovered Future Tech 1!", news.Text());

		// Without the beakers, at the most turns of research.
		us.turnsResearched = gameData.rules.MaximumResearchTime - 1;
		us.DoPerTurnScienceUpdates(gameData);
		Assert.Equal(2, us.futureTechs);
	}

	[Fact]
	public void TheCostIsARule() {
		LearnEverything();
		int cost = gameData.FutureTechCostFor(us);
		Assert.Equal(400, gameData.rules.FutureTechCost);
		gameData.rules.FutureTechCost = 800;
		Assert.InRange(gameData.FutureTechCostFor(us), 2 * cost - 1, 2 * cost + 1);
	}

	[Fact]
	public void TheyCountInTheScoreAndAreSaved() {
		float before = ScoreVictory.ComputeTurnScoreParts(us, gameData).futureTech;
		Assert.Equal(0, before);
		// A point each, times the difficulty (as everything in the score).
		us.futureTechs = 1;
		float one = ScoreVictory.ComputeTurnScoreParts(us, gameData).futureTech;
		Assert.True(one >= 1);
		us.futureTechs = 3;
		Assert.Equal(3 * one, ScoreVictory.ComputeTurnScoreParts(us, gameData).futureTech, 3);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(3, reloaded.GetPlayer(us.id).futureTechs);
	}
}
