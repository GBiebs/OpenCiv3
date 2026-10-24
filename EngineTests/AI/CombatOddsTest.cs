using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI;

// The battle odds the AI uses to choose its armies' targets.
public class CombatOddsTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public CombatOddsTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	[Theory]
	[InlineData(0.5, 1, 1, 0.5)]
	[InlineData(0.5, 2, 1, 0.75)]
	[InlineData(0.5, 1, 2, 0.25)]
	[InlineData(1.0, 1, 5, 1.0)]
	[InlineData(0.0, 5, 1, 0.0)]
	[InlineData(0.6, 3, 3, 0.68256)]
	public void WinChanceCountsTheWaysToWinTheRoundsNeeded(double p, int lives, int needed, double expected) {
		Assert.Equal(expected, CombatOdds.WinChance(p, lives, needed), 5);
	}

	[Fact]
	public void OneSideAlwaysWins() {
		foreach (double p in new[] { 0.1, 0.37, 0.5, 0.83 }) {
			for (int a = 1; a <= 6; a++) {
				for (int d = 1; d <= 6; d++) {
					Assert.Equal(1.0, CombatOdds.WinChance(p, a, d) + CombatOdds.WinChance(1 - p, d, a), 9);
				}
			}
		}
	}

	[Fact]
	public async Task ItMatchesRealBattles() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		// Two empty land tiles with no river between them (the odds leave out
		// the river-crossing bonus), far from anyone.
		Tile from = gameData.map.tiles.First(t => IsOpen(t) && gameData.mapUnits.All(u => u.location.DistanceTo(t) > 3)
			&& t.neighbors.TryGetValue(TileDirection.EAST, out Tile east) && IsOpen(east) && !t.HasRiverCrossing(TileDirection.EAST));
		Tile to = from.neighbors[TileDirection.EAST];
		UnitPrototype swordsman = gameData.unitPrototypes.Find(u => u.name == "Swordsman");
		UnitPrototype spearman = gameData.unitPrototypes.Find(u => u.name == "Spearman");
		MapUnit attacker = gameData.SpawnUnit(ais[0], swordsman, from);

		double expected = CombatOdds.AttackerWinChance(attacker, gameData.SpawnUnit(ais[1], spearman, to));
		to.unitsOnTile.Single().RemoveFromPlay();
		const int battles = 4000;
		int wins = 0;
		for (int i = 0; i < battles; i++) {
			attacker.hitPointsRemaining = 3;
			attacker.experienceLevel = gameData.defaultExperienceLevel;
			MapUnit defender = gameData.SpawnUnit(ais[1], spearman, to);
			if (await attacker.Fight(defender) == CombatResult.DefenderKilled) {
				wins++;
			} else {
				defender.RemoveFromPlay();
			}
			if (attacker.hitPointsRemaining <= 0) {
				attacker = gameData.SpawnUnit(ais[0], swordsman, from);
			}
		}

		double sigma = Math.Sqrt(battles * expected * (1 - expected));
		Assert.InRange(wins, battles * expected - 4 * sigma, battles * expected + 4 * sigma);
	}

	private static bool IsOpen(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}
}
