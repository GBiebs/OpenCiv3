using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.AIData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI;

// The AI's explorers visit the goody huts they know of nearby.
public class GoodyHutAITest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly MapUnit explorer;
	private readonly Tile hut;

	public GoodyHutAITest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		foreach (Tile t in gameData.map.tiles) {
			t.hasGoodyHut = false;
		}
		explorer = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Warrior"), us.units[0].location);
		// Three steps away over land.
		hut = explorer.location;
		for (int i = 0; i < 3; ++i) {
			hut = hut.neighbors.Values.First(n => n.IsLand() && !n.overlayTerrainType.impassable && n.unitsOnTile.Count == 0
				&& n.DistanceTo(explorer.location) > hut.DistanceTo(explorer.location));
		}
		hut.hasGoodyHut = true;
		us.tileKnowledge.AddTilesToKnown(hut);
	}

	[Fact]
	public void ExplorersHeadForAKnownHutNearby() {
		ExplorerAIData plan = ExplorerAI.MaybeMakeAiData(explorer, us);
		Assert.Equal(hut, plan.destination);
		Assert.True(plan.goodyHut);
	}

	[Fact]
	public void NotForHutsTooFarOrUnknown() {
		us.tileKnowledge.knownTiles.Remove(hut);
		Assert.NotEqual(hut, ExplorerAI.MaybeMakeAiData(explorer, us)?.destination);

		us.tileKnowledge.AddTilesToKnown(hut);
		gameData.SpawnUnit(gameData.players.Find(p => p.isBarbarians), gameData.unitPrototypes.Find(u => u.name == "Warrior"), hut);
		Assert.NotEqual(hut, ExplorerAI.MaybeMakeAiData(explorer, us)?.destination);
	}

	[Fact]
	public async Task TheExplorerFindsWhatsInIt() {
		ExplorerAI ai = new(ExplorerAI.MaybeMakeAiData(explorer, us));
		for (int turn = 0; turn < 5 && hut.hasGoodyHut; ++turn) {
			explorer.OnBeginTurn();
			await ((C7GameData.UnitAI)ai).PlayTurn(us, explorer);
		}
		Assert.False(hut.hasGoodyHut);
		// And it's done with that plan.
		explorer.OnBeginTurn();
		Assert.Equal(C7GameData.UnitAI.Result.Done, await ((C7GameData.UnitAI)ai).PlayTurn(us, explorer));
	}
}
