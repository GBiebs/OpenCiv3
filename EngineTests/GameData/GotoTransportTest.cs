using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Pathing;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// A land unit's go-to can end on a ship off the shore and board it there
// (issue #943), instead of stopping on the shore for a last move by hand.
// Only as the last step: ships are never a bridge on the way somewhere else.
public class GotoTransportTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Tile shore;
	private readonly Tile water;
	private readonly Tile start;

	public GotoTransportTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		foreach (Tile t in gameData.map.tiles) {
			t.hasGoodyHut = false;
		}

		// A shore with open water beside it, and a land tile three steps
		// from that water, from where a warrior walks to the shore in two.
		(Tile s, Tile w, Tile t)[] sites = gameData.map.tiles.Where(IsEmptyLand)
			.SelectMany(s => s.neighbors.Values.Where(w => Tile.IsTileValid(w) && w.IsWater() && w.unitsOnTile.Count == 0).Select(w => (s, w)))
			.SelectMany(sw => sw.s.GetTilesWithinTileSquare(2).Where(t => IsEmptyLand(t) && t.DistanceTo(sw.w) == 3 && t.DistanceTo(sw.s) == 2)
				.Select(t => (sw.s, sw.w, t)))
			.Take(50).ToArray();
		MapUnit probe = Spawn("Warrior", sites[0].t);
		(shore, water, start) = sites.First(site => {
			gameData.TeleportUnit(probe, site.t);
			return PathFor(probe, site.s).PathLength() == 2;
		});
		probe.RemoveFromPlay();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	private MapUnit Spawn(string type, Tile tile, Player owner = null) {
		return gameData.SpawnUnit(owner ?? us, gameData.unitPrototypes.Find(u => u.name == type), tile);
	}

	private static TilePath PathFor(MapUnit unit, Tile destination) {
		return PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, destination, unit);
	}

	[Fact]
	public async Task AGoToOntoAShipBoardsIt() {
		MapUnit galley = Spawn("Galley", water);
		MapUnit warrior = Spawn("Warrior", start);

		TilePath path = PathFor(warrior, water);
		Assert.Equal(3, path.PathLength());
		Assert.Equal(water, path.path.Last());
		Assert.All(path.path.Take(2), t => Assert.True(t.IsLand()));

		// A warrior walks a tile a turn, and boards on the third.
		await warrior.SetUnitPath(path);
		for (int turn = 1; turn < 3; turn++) {
			Assert.False(warrior.IsLoaded());
			warrior.movementPoints.reset(warrior.MovementPerTurn());
			await warrior.PerformBusyAction();
		}
		Assert.Equal(water, warrior.location);
		Assert.True(warrior.IsLoadedIn(galley));
	}

	// Open water, a full ship and another civ's ship take no go-to.
	[Fact]
	public void OnlyOntoAShipItCanBoard() {
		MapUnit warrior = Spawn("Warrior", start);
		Assert.Equal(0, PathFor(warrior, water).PathLength());

		Player them = gameData.players.First(p => p != us && !p.isHuman && !p.isBarbarians);
		MapUnit theirs = Spawn("Galley", water, them);
		Assert.Equal(0, PathFor(warrior, water).PathLength());
		theirs.RemoveFromPlay();

		MapUnit galley = Spawn("Galley", water);
		while (galley.FreeCapacity() > 0) {
			Spawn("Warrior", water).loadedOnUnitId = galley.id;
		}
		Assert.Equal(0, PathFor(warrior, water).PathLength());
	}

	// Only a go-to ending on the ship takes the water: the shore's edges lead
	// out to it then, and otherwise not.
	[Fact]
	public void AShipIsNoBridge() {
		Spawn("Galley", water);
		MapUnit warrior = Spawn("Warrior", start);
		UnitWalker walker = new(warrior);
		Assert.Contains(walker.getEdges(shore, water), e => e.current == water);
		Assert.DoesNotContain(walker.getEdges(shore), e => e.current == water);
		Assert.DoesNotContain(walker.getEdges(shore, start), e => e.current == water);
	}
}
