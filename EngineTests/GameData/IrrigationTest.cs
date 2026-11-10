using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Irrigation needs fresh water next to the tile (a river, a lake, an
// irrigated tile), until Civ3's Electricity, whose "enables irrigation
// everywhere" flag lifts that.
public class IrrigationTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly TerrainImprovement irrigation;

	public IrrigationTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isBarbarians);
		irrigation = gameData.terrainImprovements.Find(i => i.key == "irrigation");
	}

	// Dry grassland: no river, lake, city or irrigation anywhere near.
	private Tile DryGrassland() {
		return gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.overlayTerrainType.Key == "grassland" && !t.BordersRiver()
			&& !t.HasCity() && t.overlays.CanAdd(t, irrigation)
			&& t.GetTilesWithinRankDistance(2).All(n => !n.BordersRiver() && !n.NeighborsFreshWater() && !n.HasCity()
				&& !n.overlays.HasImprovement(irrigation) && !n.baseTerrainType.isWater()));
	}

	[Fact]
	public void OnlyWithFreshWaterUntilElectricity() {
		Tech electricity = gameData.techs.Single(t => t.HasFlag(SaveTech.Flag.EnablesIrrigationEverywhere));
		Assert.Equal("Electricity", electricity.Name);
		us.knownTechs.Remove(electricity.id);

		Tile dry = DryGrassland();
		Assert.False(dry.CanBeIrrigated(irrigation, us));

		us.knownTechs.Add(electricity.id);
		Assert.True(dry.CanBeIrrigated(irrigation, us));
	}
}
