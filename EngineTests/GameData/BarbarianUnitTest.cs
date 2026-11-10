using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Barbarians field their advanced unit (Horsemen) only once enough civs
// know the advance it needs (Rules.BarbarianAdvancedUnitCivsWithTech).
public class BarbarianUnitTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Tile inland;
	private readonly Tech needed;

	public BarbarianUnitTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		inland = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.NeighborsWater());
		needed = gameData.barbarianInfo.advancedBarbarian.requiredTech;
		Assert.NotNull(needed);
		foreach (Player p in gameData.players) {
			p.knownTechs.Remove(needed.id);
		}
	}

	private int Advanced(int draws) {
		return Enumerable.Range(0, draws).Count(_ => BarbarianInteractions.SelectBarbarianUnitType(gameData, inland) == gameData.barbarianInfo.advancedBarbarian);
	}

	private void Learn(int civs) {
		foreach (Player p in gameData.players.Where(p => !p.isBarbarians).Take(civs)) {
			p.knownTechs.Add(needed.id);
		}
	}

	[Fact]
	public void NotUntilEnoughCivsKnowTheAdvance() {
		Assert.Equal(2, gameData.rules.BarbarianAdvancedUnitCivsWithTech);
		Assert.Equal(0, Advanced(400));

		Learn(1);
		Assert.False(gameData.barbarianInfo.AdvancedUnitAvailable(gameData));
		Assert.Equal(0, Advanced(400));

		Learn(2);
		Assert.True(gameData.barbarianInfo.AdvancedUnitAvailable(gameData));
		Assert.InRange(Advanced(400), 50, 150);
	}

	// Once it's out, camps still raise the basic unit too: the advanced
	// one only this percent of the time (a rule).
	[Fact]
	public void HowOftenIsARule() {
		Learn(2);
		Assert.Equal(25, gameData.rules.BarbarianAdvancedUnitPercent);
		gameData.rules.BarbarianAdvancedUnitPercent = 0;
		Assert.Equal(0, Advanced(200));
		gameData.rules.BarbarianAdvancedUnitPercent = 100;
		Assert.Equal(200, Advanced(200));
	}

	[Fact]
	public void TheNumberOfCivsIsARule() {
		gameData.rules.BarbarianAdvancedUnitCivsWithTech = 1;
		Learn(1);
		Assert.True(gameData.barbarianInfo.AdvancedUnitAvailable(gameData));
	}
}
