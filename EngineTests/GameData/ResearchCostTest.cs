using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Research by difficulty, as Civ3 has it since patch 1.29f (CivFanatics'
// "Research cost formula v1.29f"): the AI pays the neutral cost on every
// level, and the human pays for the AI's advantage, the cost divided by the
// AI cost factor when it's under the neutral 10.
public class ResearchCostTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player human;
	private readonly Player ai;
	private readonly Tech tech;

	public ResearchCostTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		human = gameData.players.First(p => p.isHuman);
		ai = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		// An advance no one knows or is researching, so only the difficulty
		// differs between the two.
		tech = gameData.techs.First(t => t.Cost > 0 && gameData.players.All(p => !p.knownTechs.Contains(t.id) && p.currentlyResearchedTech != t.id));
	}

	private Difficulty Level(int aiCostFactor) {
		return gameData.difficulties.First(d => d.AiCostFactor == aiCostFactor);
	}

	// The formula at the neutral factor: techRate × 10 × cost / (10 × 10).
	private int NeutralCost() => gameData.map.techRate * tech.Cost / 10;

	[Fact]
	public void TheAIPaysTheSameOnEveryLevel() {
		Assert.True(gameData.difficulties.Count >= 6);
		foreach (Difficulty level in gameData.difficulties) {
			gameData.gameDifficulty = level;
			Assert.Equal(NeutralCost(), gameData.TechCostFor(tech, ai));
		}
	}

	[Theory]
	[InlineData(6)] // Deity: 10/6, 67% dearer
	[InlineData(8)] // Emperor: 25%
	[InlineData(9)] // Monarch: 11%
	public void TheHumanPaysForTheAIsAdvantage(int aiCostFactor) {
		gameData.gameDifficulty = Level(aiCostFactor);
		int expected = gameData.map.techRate * 10 * tech.Cost / (aiCostFactor * 10);
		Assert.Equal(expected, gameData.TechCostFor(tech, human));
		Assert.True(gameData.TechCostFor(tech, human) > gameData.TechCostFor(tech, ai));
	}

	// Chieftain's AI cost factor of 20 (and Warlord's 12) doesn't make the
	// human's research cheaper than the neutral cost.
	[Theory]
	[InlineData(20)]
	[InlineData(12)]
	[InlineData(10)]
	public void EasyLevelsChangeNothing(int aiCostFactor) {
		gameData.gameDifficulty = Level(aiCostFactor);
		Assert.Equal(NeutralCost(), gameData.TechCostFor(tech, human));
		Assert.Equal(NeutralCost(), gameData.TechCostFor(tech, ai));
	}

	// A difficulty without an AI cost factor counts as neutral rather than
	// dividing by zero.
	[Fact]
	public void AnUnsetFactorIsNeutral() {
		gameData.gameDifficulty = new Difficulty();
		Assert.Equal(NeutralCost(), gameData.TechCostFor(tech, human));
	}
}
