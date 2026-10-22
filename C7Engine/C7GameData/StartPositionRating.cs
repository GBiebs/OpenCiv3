using System;
using System.Collections.Generic;
using System.Linq;

namespace C7GameData;

/// <summary>
/// A civ's start position as measured at the start of the game, and its rating
/// against the other starts in the same game (100 = the average start).
/// </summary>
public class StartRating {
	// The start tile (the first settler's tile).
	public int x;
	public int y;

	// Weighted food, shields and commerce of the tiles a capital there could work.
	public int sitePoints;
	// Distinct luxury and strategic resource types near the start.
	public int luxuries;
	public int strategics;
	// Land tiles nearer (walking over land) to this start than to any rival's.
	public int landShare;
	// Barbarian camps near the start.
	public int barbarianCamps;
	// Walking distance to the nearest rival start (0 if there is none).
	public int nearestRival;

	// Indexes against the game's average start (100), and the weighted rating.
	public double siteIndex;
	public double roomIndex;
	public double resourceIndex;
	public double rating;
}

/// <summary>
/// Rates every civ's start position once, at the start of a new game. Each
/// factor is turned into an index against the game's average start, and the
/// indexes are weighted into one rating, so the average start rates exactly 100:
/// - the capital site: food, shields and commerce of the tiles a capital there
///   could work, weighted like the map generator's start scoring, with the inner
///   ring counted twice;
/// - expansion room: land tiles within <see cref="RoomRadius"/> that are nearer
///   to this start than to any rival's (ties shared), less
///   <see cref="CampPenalty"/> tiles for each barbarian camp nearby;
/// - resources: distinct luxury (counted twice) and strategic types nearby.
/// </summary>
public static class StartPositionRating {
	public const double SiteWeight = 0.5;
	public const double RoomWeight = 0.35;
	public const double ResourceWeight = 0.15;

	public const int ResourceRadius = 6;
	public const int RoomRadius = 12;
	public const int CampRadius = 6;
	public const int CampPenalty = 8;

	// The map generator's weights for scoring a start (MapGenerator.ScorePossibleCityLocation).
	private const int FoodPoints = 20;
	private const int ShieldPoints = 12;
	private const int CommercePoints = 4;

	/// Rates the starts of a game that is just beginning: on turn 0, if no civ
	/// has a rating yet. Civs without a settler (e.g. in some scenarios) are skipped.
	public static void RateNewGame(GameData gameData) {
		if (gameData.turn != 0) return;
		List<(Player player, Tile start)> starts = gameData.players
			.Where(p => !p.isBarbarians && !p.defeated)
			.Select(p => (player: p, start: p.units.FirstOrDefault(u => u.unitType.actions.Contains(UnitAction.BuildCity))?.location))
			.Where(s => Tile.IsTileValid(s.start))
			.ToList();
		if (starts.Count == 0 || starts.Any(s => s.player.stats.startRating != null)) return;
		Rate(starts, gameData);
	}

	public static void Rate(List<(Player player, Tile start)> starts, GameData gameData) {
		Dictionary<Tile, double> shares = LandShares(starts.Select(s => s.start).ToList(), RoomRadius);

		List<(Player player, StartRating rating)> rated = new();
		foreach ((Player player, Tile start) in starts) {
			List<Tile> nearby = gameData.map.tiles.Where(t => start.DistanceTo(t) <= ResourceRadius).ToList();
			List<Tile> rivals = starts.Select(s => s.start).Where(t => t != start).ToList();
			rated.Add((player, new StartRating {
				x = start.XCoordinate,
				y = start.YCoordinate,
				sitePoints = SitePoints(start, player),
				luxuries = DistinctResources(nearby, ResourceCategory.LUXURY),
				strategics = DistinctResources(nearby, ResourceCategory.STRATEGIC),
				landShare = (int)Math.Round(shares[start]),
				barbarianCamps = gameData.map.barbarianCamps.Count(c => start.DistanceTo(c) <= CampRadius),
				nearestRival = rivals.Count > 0 ? rivals.Min(start.DistanceTo) : 0,
			}));
		}

		double averageSite = rated.Average(r => SiteValue(r.rating));
		double averageRoom = rated.Average(r => RoomValue(r.rating));
		double averageResources = rated.Average(r => ResourceValue(r.rating));
		foreach ((Player player, StartRating rating) in rated) {
			rating.siteIndex = Index(SiteValue(rating), averageSite);
			rating.roomIndex = Index(RoomValue(rating), averageRoom);
			rating.resourceIndex = Index(ResourceValue(rating), averageResources);
			rating.rating = SiteWeight * rating.siteIndex + RoomWeight * rating.roomIndex + ResourceWeight * rating.resourceIndex;
			player.stats.startRating = rating;
		}
	}

	private static double SiteValue(StartRating r) => r.sitePoints;
	private static double RoomValue(StartRating r) => Math.Max(r.landShare - CampPenalty * r.barbarianCamps, 0);
	private static double ResourceValue(StartRating r) => 2 * r.luxuries + r.strategics;

	// 100 means average; when every start has none of something, all rate 100 on it.
	private static double Index(double value, double average) => average > 0 ? 100 * value / average : 100;

	public static int SitePoints(Tile start, Player player) {
		int points = 0;
		// The inner ring counts twice: it's in both loops.
		foreach (Tile t in start.GetTilesWithinRankDistance(1).Where(Tile.IsTileValid)) {
			points += TilePoints(t, player);
		}
		foreach (Tile t in start.GetTilesWithinRankDistance(player.rules.MaxRankOfWorkableTiles).Where(Tile.IsTileValid)) {
			points += TilePoints(t, player);
		}
		return points;
	}

	private static int TilePoints(Tile t, Player player) =>
		FoodPoints * t.FoodYield(player).yield + ShieldPoints * t.ProductionYield(player).yield + CommercePoints * t.CommerceYield(player).yield;

	private static int DistinctResources(List<Tile> tiles, ResourceCategory category) =>
		tiles.Where(t => t.Resource != null && t.Resource.Category == category).Select(t => t.Resource.Name).Distinct().Count();

	/// For each start, the land tiles (within `radius` steps over land) nearer to
	/// it than to any other start; a tile at the same distance from several starts
	/// is shared between them.
	public static Dictionary<Tile, double> LandShares(List<Tile> starts, int radius) {
		List<Dictionary<Tile, int>> distances = starts.Select(s => LandDistances(s, radius)).ToList();
		Dictionary<Tile, double> shares = starts.ToDictionary(s => s, _ => 0.0);
		foreach (Tile tile in distances.SelectMany(d => d.Keys).Distinct()) {
			int nearest = distances.Min(d => d.GetValueOrDefault(tile, int.MaxValue));
			List<int> owners = Enumerable.Range(0, starts.Count).Where(i => distances[i].GetValueOrDefault(tile, int.MaxValue) == nearest).ToList();
			foreach (int i in owners) {
				shares[starts[i]] += 1.0 / owners.Count;
			}
		}
		return shares;
	}

	// Steps from `start` to each land tile within `radius`, walking over land.
	private static Dictionary<Tile, int> LandDistances(Tile start, int radius) {
		Dictionary<Tile, int> distance = new() { [start] = 0 };
		Queue<Tile> queue = new([start]);
		while (queue.Count > 0) {
			Tile tile = queue.Dequeue();
			int steps = distance[tile];
			if (steps == radius) continue;
			foreach (Tile next in tile.neighbors.Values) {
				if (Tile.IsTileValid(next) && next.IsLand() && !distance.ContainsKey(next)) {
					distance[next] = steps + 1;
					queue.Enqueue(next);
				}
			}
		}
		return distance;
	}
}
