using C7Engine;
using C7GameData;
using Xunit;

namespace EngineTests.GameData.Victory;

public class ConquestDominationVictoryTest {
	[Fact]
	public void ConquestIsWonWhenNoRivalsRemain() {
		ConquestVictory conquest = new();
		Assert.True(conquest.HasVictory(new VictoryStatus() { RivalsRemaining = 0 }));
		Assert.False(conquest.HasVictory(new VictoryStatus() { RivalsRemaining = 1 }));
	}

	[Fact]
	public void DominationNeedsBothTerritoryAndPopulation() {
		DominationVictory domination = new(territoryPercentNeeded: 66, populationPercentNeeded: 66);
		Assert.True(domination.HasVictory(new VictoryStatus() { TerritoryPercent = 70, PopulationPercent = 66.5f }));
		// Strictly above both thresholds (Perez's decompile, Round 2).
		Assert.False(domination.HasVictory(new VictoryStatus() { TerritoryPercent = 70, PopulationPercent = 66 }));
		Assert.False(domination.HasVictory(new VictoryStatus() { TerritoryPercent = 66, PopulationPercent = 70 }));
		Assert.False(domination.HasVictory(new VictoryStatus() { TerritoryPercent = 70, PopulationPercent = 50 }));
		Assert.False(domination.HasVictory(new VictoryStatus() { TerritoryPercent = 40, PopulationPercent = 90 }));
	}

	[Fact]
	public void NewGamesAllowConquestAndDomination() {
		VictoryConditions defaults = VictoryConditions.NewGameDefaults();
		Assert.True(defaults.AllowConquestVictory);
		Assert.True(defaults.AllowDominationVictory);
	}

	[Fact]
	public void NewGamesAllowCulturalVictory() {
		Assert.True(VictoryConditions.NewGameDefaults().AllowCulturalVictory);
	}
}
