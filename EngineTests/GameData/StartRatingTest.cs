using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Start position ratings (advanced-statistics branch, phase 4).
public class StartRatingTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public StartRatingTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		EngineStorage.animationsEnabled = false;
	}

	private async Task<C7GameData.GameData> NewGame() {
		await CreateGame.createGame(fixture.saveGame.Clone(), _ => fixture.behaviors);
		return EngineStorage.gameData;
	}

	private static List<Player> Civs(C7GameData.GameData gameData) => gameData.players.Where(p => !p.isBarbarians).ToList();

	// Tiles without a resource hold a placeholder with no category, not null.
	private static bool HasResource(Tile t) => t.Resource != null && t.Resource.Category != ResourceCategory.NONE;

	private static Tile Start(Player player) => player.units.First(u => u.unitType.actions.Contains(UnitAction.BuildCity)).location;

	private static List<(Player, Tile)> Starts(C7GameData.GameData gameData) => Civs(gameData).Select(p => (p, Start(p))).ToList();

	[Fact]
	public async Task ANewGameRatesEveryCivAndTheAverageIs100() {
		C7GameData.GameData gameData = await NewGame();
		List<Player> civs = Civs(gameData);

		Assert.All(civs, p => Assert.NotNull(p.stats.startRating));
		Assert.All(civs, p => Assert.Equal((Start(p).XCoordinate, Start(p).YCoordinate), (p.stats.startRating.x, p.stats.startRating.y)));
		Assert.Null(gameData.players.First(p => p.isBarbarians).stats.startRating);

		List<StartRating> ratings = civs.Select(p => p.stats.startRating).ToList();
		Assert.Equal(100, ratings.Average(r => r.rating), 9);
		Assert.Equal(100, ratings.Average(r => r.siteIndex), 9);
		Assert.Equal(100, ratings.Average(r => r.roomIndex), 9);
		Assert.Equal(100, ratings.Average(r => r.resourceIndex), 9);
		// The factors' weights add up, so the rating is their weighted sum.
		Assert.All(ratings, r => Assert.Equal(0.5 * r.siteIndex + 0.35 * r.roomIndex + 0.15 * r.resourceIndex, r.rating, 9));
	}

	[Fact]
	public async Task RatingsAreOnlyMadeOnceAtTheStart() {
		C7GameData.GameData gameData = await NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		StartRating original = human.stats.startRating;

		StartPositionRating.RateNewGame(gameData);
		Assert.Same(original, human.stats.startRating);

		// A game past turn 0 without ratings (e.g. saved before they existed) gets none.
		foreach (Player p in gameData.players) p.stats.startRating = null;
		gameData.turn = 5;
		StartPositionRating.RateNewGame(gameData);
		Assert.All(gameData.players, p => Assert.Null(p.stats.startRating));
	}

	[Fact]
	public async Task SitePointsWeightTheCapitalTiles() {
		C7GameData.GameData gameData = await NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		Tile start = Start(human);

		int Points(Tile t) => 20 * t.FoodYield(human).yield + 12 * t.ProductionYield(human).yield + 4 * t.CommerceYield(human).yield;
		int expected = start.GetTilesWithinRankDistance(1).Sum(Points) + start.GetTilesWithinRankDistance(2).Sum(Points);

		Assert.Equal(expected, StartPositionRating.SitePoints(start, human));
		Assert.Equal(expected, human.stats.startRating.sitePoints);
	}

	[Fact]
	public async Task ALuxuryNearbyRaisesTheResourceIndex() {
		C7GameData.GameData gameData = await NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		StartRating before = human.stats.startRating;
		Tile start = Start(human);

		// A luxury type the start doesn't have yet, on an empty land tile 3 steps away.
		HashSet<string> present = gameData.map.tiles.Where(t => start.DistanceTo(t) <= StartPositionRating.ResourceRadius && HasResource(t)).Select(t => t.Resource.Name).ToHashSet();
		Resource luxury = gameData.Resources.First(r => r.Category == ResourceCategory.LUXURY && !present.Contains(r.Name));
		Tile tile = gameData.map.tiles.First(t => start.DistanceTo(t) == 3 && t.IsLand() && !HasResource(t));
		tile.Resource = luxury;

		StartPositionRating.Rate(Starts(gameData), gameData);

		Assert.Equal(before.luxuries + 1, human.stats.startRating.luxuries);
		Assert.True(human.stats.startRating.resourceIndex > before.resourceIndex);
	}

	[Fact]
	public async Task BarbarianCampsNearbyCostRoom() {
		C7GameData.GameData gameData = await NewGame();
		Player human = gameData.players.First(p => p.isHuman);
		StartRating before = human.stats.startRating;
		Tile start = Start(human);

		Tile campTile = gameData.map.tiles.First(t => start.DistanceTo(t) == 4 && t.IsLand() && !t.hasBarbarianCamp);
		campTile.hasBarbarianCamp = true;
		gameData.map.barbarianCamps.Add(campTile);

		StartPositionRating.Rate(Starts(gameData), gameData);

		Assert.Equal(before.barbarianCamps + 1, human.stats.startRating.barbarianCamps);
		Assert.Equal(before.landShare, human.stats.startRating.landShare);
		Assert.True(human.stats.startRating.roomIndex < before.roomIndex);
	}

	[Fact]
	public async Task LandIsSharedBetweenTheNearestStarts() {
		C7GameData.GameData gameData = await NewGame();
		List<Tile> starts = Starts(gameData).Select(s => s.Item2).ToList();

		Dictionary<Tile, double> shares = StartPositionRating.LandShares(starts, StartPositionRating.RoomRadius);

		// Every reachable land tile is shared out exactly once.
		int reachable = starts.SelectMany(s => gameData.map.tiles.Where(t => t.IsLand() && LandSteps(s, t) <= StartPositionRating.RoomRadius)).Distinct().Count();
		Assert.Equal(reachable, shares.Values.Sum(), 6);

		// A rival starting right next to a start takes about half of its land.
		Tile mine = starts[0];
		Tile neighbor = mine.neighbors.Values.First(t => Tile.IsTileValid(t) && t.IsLand());
		Dictionary<Tile, double> crowded = StartPositionRating.LandShares([mine, neighbor], StartPositionRating.RoomRadius);
		Dictionary<Tile, double> alone = StartPositionRating.LandShares([mine], StartPositionRating.RoomRadius);
		Assert.InRange(crowded[mine] / alone[mine], 0.3, 0.7);
	}

	// Steps over land between two tiles (independent of the code under test).
	private static int LandSteps(Tile from, Tile to) {
		Dictionary<Tile, int> seen = new() { [from] = 0 };
		Queue<Tile> queue = new([from]);
		while (queue.Count > 0) {
			Tile t = queue.Dequeue();
			if (t == to) return seen[t];
			if (seen[t] >= StartPositionRating.RoomRadius) continue;
			foreach (Tile n in t.neighbors.Values.Where(n => Tile.IsTileValid(n) && n.IsLand() && !seen.ContainsKey(n))) {
				seen[n] = seen[t] + 1;
				queue.Enqueue(n);
			}
		}
		return int.MaxValue;
	}

	[Fact]
	public async Task RatingsAreSaved() {
		C7GameData.GameData gameData = await NewGame();
		Player human = gameData.players.First(p => p.isHuman);

		string path = Path.Combine(Path.GetTempPath(), "openciv3-start-rating.json");
		SaveGame.FromGameData(gameData).Save(path);
		SaveGame loaded = SaveGame.Load(path, s => s, s => s);
		File.Delete(path);

		StartRating saved = loaded.Players.First(p => p.id == human.id).stats.startRating;
		Assert.Equal(human.stats.startRating.rating, saved.rating, 9);
		Assert.Equal(human.stats.startRating.landShare, saved.landShare);
	}
}
