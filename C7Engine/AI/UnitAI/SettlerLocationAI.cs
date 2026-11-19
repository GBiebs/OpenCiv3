using System;
using C7Engine.AI;
using C7Engine.AI.Advanced;
using C7GameData;
using C7GameData.AIData;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace C7Engine {
	public class SettlerLocationAI {
		private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<SettlerLocationAI>();

		//Figures out where to plant Settlers.
		// Sites further than maxDistance from the start aren't considered,
		// nor under Rising Tide those lasting fewer than tideWaves waves (see
		// TideAI.LastsLongEnough).
		public static Tile FindSettlerLocation(Tile start, Player player, HashSet<Tile> excludedTiles = null, int maxDistance = int.MaxValue,
				int tideWaves = TideAI.MinSiteWaves) {
			Dictionary<Tile, float> scores = GetScoredSettlerCandidates(start, player, excludedTiles, tideWaves);
			if (maxDistance != int.MaxValue) {
				foreach (Tile t in scores.Keys.Where(t => start.DistanceTo(t) > maxDistance).ToList()) {
					scores.Remove(t);
				}
			}
			if (scores.Count == 0 || scores.Values.Max() <= 0) {
				return Tile.NONE;   //nowhere to settle
			}

			// Under Rising Tide the walk also eats into the turns the city
			// will stand (see TideAI).
			if (TideAI.IsOn()) {
				return scores.MaxBy(t => t.Value * TravelFactor(start.DistanceTo(t.Key)) * TideAI.TravelSurvivalFactor(t.Key, start.DistanceTo(t.Key))).Key;
			}

			// The settler's own walk counts too: a site a little better but
			// far across the empire (perhaps one a nearer city's settler
			// would take) isn't worth the turns. The scores above measure
			// distance from our nearest city, for every site, which is what
			// counting open sites wants; this only picks among them.
			// fast-expand (Advanced only): the walk counts for more, and a
			// voyage more still (see FastExpand.SiteTravelFactor).
			if (FastExpand.On(player)) {
				return scores.MaxBy(t => t.Value * FastExpand.SiteTravelFactor(start, t.Key)).Key;
			}
			Tile result = scores.MaxBy(t => t.Value * TravelFactor(start.DistanceTo(t.Key))).Key;
			return result;
		}

		// HEURISTIC: a site's worth to a settler falls by this share for
		// each tile it must walk beyond the first few. (A settler loses a
		// turn of the city's growth for about each tile.)
		private const float TravelDiscountPerTile = 0.04f;
		private const int FreeTravelTiles = 3;

		private static float TravelFactor(int distance) {
			int beyond = Math.Max(0, distance - FreeTravelTiles);
			return Math.Max(0.1f, 1f - TravelDiscountPerTile * beyond);
		}

		// Whether a settler may still found its city at the tile, by the
		// rules sites are chosen by: no city within 2 tiles (see
		// TooCloseToCity), not in another civ's land, while at war not next
		// to an enemy's land, and under Rising Tide not where the sea will
		// soon be. A settler checks this each turn, so it learns on the way
		// that a rival has settled near its site or its borders have grown
		// over it.
		public static bool IsStillValidSite(Tile t, Player player) {
			if (t == null || t == Tile.NONE || TooCloseToCity(t, null) || !t.IsAllowCities() || IsOwnedByRival(t, player)) {
				return false;
			}
			if (!TideAI.LastsLongEnough(t, TideAI.MinKeptSiteWaves)) {
				return false;
			}
			bool atWar = PlayerRelationship.IsInAnyWar(player, EngineStorage.gameData?.players ?? new());
			return !atWar || !NextToHostileTerritory(t, player) || HordeWar.MaySettleNextTo(player, t);
		}

		// Scores the known tiles on the start tile's continent, and on the
		// landmasses our transports can reach (see SeaRoutes), where the player
		// could found a city with no city within 2 tiles. Every such tile is in
		// the result with a positive score, however poor the site. The result
		// lists the tiles in the order of the player's known tiles.
		//
		// This runs for every production decision and settler plan, so the
		// parts that rarely change are cached between calls: which tiles are
		// too close to a city (until a city is founded or destroyed) and each
		// tile's yield score (until something its yields depend on changes).
		// The results are the same as computing everything from scratch.
		public static Dictionary<Tile, float> GetScoredSettlerCandidates(Tile start, Player player, HashSet<Tile> excludedTiles = null,
				int tideWaves = TideAI.MinSiteWaves) {
			List<MapUnit> playerSettlers = player.units.FindAll(u => u.unitType.isSettler);
			Dictionary<Tile, bool> invalidLocations = InvalidLocationMemo();
			HashSet<Tile> tilesNearSettlerDestinations = null;

			// Other landmasses are settled by sea, once we have (or can build)
			// a transport that can reach them.
			HashSet<int> overseas = null;
			List<Tile> candidates = new();
			bool atWar = PlayerRelationship.IsInAnyWar(player, EngineStorage.gameData?.players ?? new());
			bool tide = TideAI.IsOn();
			foreach (Tile t in player.tileKnowledge.knownTiles) {
				// Cheapest checks first.
				if (t.continent != start.continent) {
					if (!t.IsLand()) {
						continue;
					}
					overseas ??= SeaRoutes.ReachableLandmasses(player);
					if (!overseas.Contains(t.continent)) {
						continue;
					}
				}
				if (TooCloseToCity(t, invalidLocations)) {
					continue;
				}

				// Skip tiles near where our settlers are already heading, and
				// excludedTiles (the tiles a settler failed to reach, issue
				// #213).
				tilesNearSettlerDestinations ??= TilesNearSettlerDestinations(playerSettlers, tide);
				if (tilesNearSettlerDestinations.Contains(t) || !t.IsAllowCities() || (excludedTiles != null && excludedTiles.Contains(t))) {
					continue;
				}

				// Cities can't be founded in another civ's territory.
				if (IsOwnedByRival(t, player)) {
					continue;
				}

				// While at war, not on the enemy's doorstep, where the city
				// (and the settler on its way) would be easy to take. (An AI
				// heuristic.)
				// Switch horde-war: near our cities, the razed sites and gaps
				// a horde leaves (see HordeWar).
				if (atWar && NextToHostileTerritory(t, player) && !HordeWar.MaySettleNextTo(player, t)) {
					continue;
				}

				// Under Rising Tide, not where the next waves will soon take
				// the city (see TideAI).
				if (tide && !TideAI.LastsLongEnough(t, tideWaves)) {
					continue;
				}
				candidates.Add(t);
			}

			return AssignTileScores(start, player, candidates);
		}

		private static bool NextToHostileTerritory(Tile t, Player player) {
			foreach (Tile n in t.neighbors.Values) {
				if (n != Tile.NONE && AIThreat.IsHostileTerritory(player, n)) {
					return true;
				}
			}
			return false;
		}

		private static Dictionary<Tile, float> AssignTileScores(Tile startTile, Player player, List<Tile> candidates) {
			Dictionary<Tile, float> scores = new();
			if (candidates.Count == 0) {
				return scores;
			}

			PlayerScoreCache cache = GetPlayerScoreCache(player);
			var maxRank = player.rules.MaxRankOfWorkableTiles;
			Civilization.SettlerTileAdjustments adjustments = player.civilization.Adjustments;

			// Distance is measured from our nearest city, so that new cities
			// grow the empire outwards rather than wherever the settler was
			// built. Without cities yet, it's measured from the settler.
			List<Tile> ownCities = new();
			foreach (City c in player.cities) {
				ownCities.Add(c.location);
			}
			List<Tile> rivalCities = new();
			foreach (Player p in EngineStorage.gameData?.players ?? new List<Player>()) {
				if (p == player) {
					continue;
				}
				foreach (City c in p.cities) {
					// With the AI fog of war, only the cities we know of.
					if (c.location.map == startTile.map && AIFogOfWar.KnowsTile(player, c.location)) {
						rivalCities.Add(c.location);
					}
				}
			}

			cache.UpdateCities(ownCities, rivalCities, adjustments.RivalCityRadius);
			bool tide = TideAI.IsOn();
			bool siteFood = AiExperiments.Has(player, SiteFood);
			int tightTarget = TightTarget(player);
			// Switch phase-plan: while expanding, sites toward the nearest rival
			// are worth more (see PhasePlan).
			(City ours, City theirs)? rival = PhasePlan.Expanding(player) ? PhasePlan.NearestRival(player) : null;
			TerrainImprovement irrigation = tightTarget > 0 ? EngineStorage.gameData?.terrainImprovements?.Find(i => i?.key == Tile.TileOverlays.IRRIGATION) : null;

			foreach (Tile t in candidates) {
				float score = cache.TileYieldScore(t);

				// Consider all tiles within the BFC for total score.
				// Score contribution decreases linearly with distance, by 1/R with each step:
				// e.g., with four ranks of workable tiles, R=4:
				//	  city | 100% | 75% | 50% | 25% | 0% | 0% | ..
				BigFatCross bfc = cache.GetBigFatCross(t, maxRank);
				for (int i = 0; i < bfc.tiles.Length; ++i) {
					// Tiles in a rival's territory, and their resources, are
					// theirs to work, not ours.
					if (IsOwnedByRival(bfc.tiles[i], player)) {
						continue;
					}
					// With the AI fog of war, tiles we haven't explored are
					// worth nothing to us yet.
					if (!AIFogOfWar.KnowsTile(player, bfc.tiles[i])) {
						continue;
					}
					score += cache.TileYieldScore(bfc.tiles[i]) * bfc.adjustments[i];
				}

				//Prefer hills for defense, and coast for boats and such.
				if (t.baseTerrainType.Key == "hills") {
					score += player.civilization.Adjustments.HillsBonus;
				}
				if (t.NeighborsWater()) {
					score += player.civilization.Adjustments.WaterBonus;
				}

				// Let defensibility play a role
				score += (float)t.baseTerrainType.defenseBonus.amount * 20.0f;

				// Lava from a volcano next to the site would destroy the city
				// (see Volcano).
				score += SitePenalty(t, player);

				if (siteFood) {
					score += SiteFoodAdjustment(t, player, bfc);
				}

				if (tightTarget > 0 && ownCities.Count > 0 && cache.DistanceFromCities(t) <= adjustments.DistancePenaltyRadius
						&& GoodFoodTiles(t, player, bfc, ownCities, irrigation) >= tightTarget) {
					score += TightSiteBonus;
				}

				if (rival is (City, City) towards) {
					score += PhasePlan.SiteBonus(t, towards);
				}

				// Rival cities nearby will compete for the tiles, and their
				// borders will grow over them. (The penalty is added once
				// per city, as a sum of the same float, in any order.)
				int nearbyRivalCities = cache.RivalCitiesWithin(t);
				for (int i = 0; i < nearbyRivalCities; ++i) {
					score += adjustments.RivalCityPenalty;
				}

				//Lower scores if they are far away
				float preDistanceScore = score;
				int distanceFromEmpire = ownCities.Count == 0 ? DistanceFromEmpire(t, startTile, ownCities) : cache.DistanceFromCities(t);
				// Under Rising Tide the empire moves inland, away from its
				// cities on the shore, so distance from them is no fault;
				// the settler's own walk still counts (see
				// FindSettlerLocation).
				float distancePenalty = tide ? 0 : DistancePenalty(distanceFromEmpire, adjustments);
				score += distancePenalty;

				//Distance can never make a site worth something worthless; the AI will always try to settle those worthless tundras.
				//(This could actually be modified in the future, but for now is also a safety rail)
				// Such sites get a small score in (0, 1/2] instead, which still
				// falls with distance and rises with the site's worth, so that
				// among far sites the nearer and better ones are preferred
				// rather than whichever is known first.
				if (preDistanceScore > 0 && score <= 0) {
					score = preDistanceScore / (preDistanceScore - distancePenalty);
				}

				// Every candidate is a site worth settling, however poor: the
				// project owner's AI preference is that any open land tile with
				// no city within 2 tiles gets a city, so that gaps in the AI's
				// land are filled rather than skipped. (That 2-tile gap is an
				// AI choice, not a Civ3 rule; see IsInvalidCityLocation.) Sites
				// that are worthless even before distance (e.g. crowded by
				// rival cities) get a score in (0, 1/4], scaled down by the
				// distance penalty the same way as the far sites above, so
				// that they rank below any site of worth 1 or more at the same
				// distance, and the nearer and less bad ones come first.
				if (score <= 0) {
					score = 1f / ((4f - preDistanceScore) * (1f - distancePenalty));
				}

				// Under Rising Tide a site is worth more the longer the city
				// will stand (see TideAI.SiteSurvivalFactor).
				if (tide) {
					score *= TideAI.SiteSurvivalFactor(t);
				}
				scores[t] = score;
			}
			return scores;
		}

		// The experiment switch for food-first city sites.
		public const string SiteFood = "site-food";

		// The experiment switches for tighter city sites where food is
		// plentiful: a site close to our own cities (up to
		// DistancePenaltyRadius tiles, though never nearer than
		// IsInvalidCityLocation allows, 3 tiles) that still has this many good
		// food tiles of its own is scored higher. Today's score has no
		// closeness or overlap cost to drop: tiles shared with our other cities
		// count in full and the distance penalty only starts beyond the
		// radius, so the switch adds a bonus that lets such sites beat
		// farther ones.
		public const string SiteTight12 = "site-tight-12";
		public const string SiteTight7 = "site-tight-7";

		// HEURISTIC (UNVERIFIED): about the worth of 8 food on the tiles.
		internal const float TightSiteBonus = 40;

		// The good food tiles a site must have, or 0 with neither switch on.
		internal static int TightTarget(Player player) {
			if (AiExperiments.Has(player, SiteTight12)) {
				return 12;
			}
			return AiExperiments.Has(player, SiteTight7) ? 7 : 0;
		}

		// How many good food tiles the site has within its 21-tile radius: a
		// workable tile that makes 2 food or more, or will after irrigation
		// (`irrigation` is the irrigation improvement, null if the game has
		// none), counts one. Coast and sea tiles make only 1 food without a
		// Harbour (compendium 07, section 1.1), so they count half. A tile
		// within the radius of another of our cities counts half, as the two
		// cities share it. Tiles in a rival's land, or unexplored, don't
		// count.
		internal static float GoodFoodTiles(Tile site, Player player, BigFatCross bfc, List<Tile> ownCities, TerrainImprovement irrigation) {
			float total = 0;
			foreach (Tile n in bfc.tiles) {
				if (IsOwnedByRival(n, player) || !AIFogOfWar.KnowsTile(player, n)) {
					continue;
				}
				float worth;
				if (n.FoodYield(player).yield >= 2 || (irrigation != null && n.IsLand()
						&& n.baseTerrainType != null && n.FoodYield(player).yield + irrigation.GetYieldBonus(n.overlayTerrainType, Tile.YieldType.Food) >= 2
						&& n.CanBeIrrigated(irrigation, player))) {
					worth = 1;
				} else if (n.IsWater() && n.FoodYield(player).yield >= 1) {
					worth = 0.5f;
				} else {
					continue;
				}
				if (ownCities.Exists(c => c != site && n.DistanceTo(c) <= 2)) {
					worth /= 2;
				}
				total += worth;
			}
			return total;
		}

		// HEURISTIC: the worth of a site next to fresh water (a river or
		// lake), where the city needs no aqueduct to pass size 8, and the
		// cost of each of the two 2-food tiles a site lacks.
		internal const float FreshWaterBonus = 15;
		internal const float MissingFoodTilePenalty = -25;
		internal const int FoodTilesWanted = 2;

		// Under the site-food switch: a city centre makes the same food
		// whatever it stands on (Rules.CityCenterFood, 2 normally; see
		// Tile.BaseFoodYield), so bonus food under it is wasted and the
		// site's own food is scored at that. A river or lake beside the
		// site counts for it. And the site wants two tiles of 2 food or
		// more around it to grow on.
		internal static float SiteFoodAdjustment(Tile t, Player player, BigFatCross bfc) {
			float adjustment = 0;
			int centerFood = City.CenterFood(player);
			adjustment += player.civilization.Adjustments.FoodYieldBonus * (centerFood - t.FoodYield(player).yield);

			if (t.BordersRiver() || t.NeighborsFreshWater()) {
				adjustment += FreshWaterBonus;
			}

			int foodTiles = 0;
			for (int i = 0; i < bfc.tiles.Length && foodTiles < FoodTilesWanted; ++i) {
				Tile n = bfc.tiles[i];
				if (!IsOwnedByRival(n, player) && AIFogOfWar.KnowsTile(player, n) && n.FoodYield(player).yield >= 2) {
					++foodTiles;
				}
			}
			adjustment += (FoodTilesWanted - foodTiles) * MissingFoodTilePenalty;
			return adjustment;
		}

		// HEURISTIC: a small penalty for a site next to a volcano. Flood
		// plain sites aren't marked down for their disease: doing so slowed
		// expansion (the first overseas city in SettlerSailsOverseasTest came
		// 35 turns later), and fast expansion matters more.
		internal const float VolcanoPenalty = -20;

		// The (negative) score adjustment for the site's own hazards.
		internal static float SitePenalty(Tile t, Player player) {
			float penalty = 0;
			foreach (Tile n in t.neighbors.Values) {
				if (n != null && n != Tile.NONE && n.overlayTerrainType.IsVolcano) {
					penalty += VolcanoPenalty;
					break;
				}
			}
			return penalty;
		}

		// How far the tile is from our nearest city, or from `start` if we
		// have no cities.
		internal static int DistanceFromEmpire(Tile t, Tile start, List<Tile> ownCities) {
			if (ownCities.Count == 0) {
				return start.DistanceTo(t);
			}
			int result = int.MaxValue;
			foreach (Tile city in ownCities) {
				result = Math.Min(result, t.DistanceTo(city));
			}
			return result;
		}

		// The (negative) score adjustment for a site this far from the
		// empire. It grows with the square of the distance beyond the radius,
		// so that a far site needs to be much better to be worth the trip
		// and the gap in the empire it leaves.
		internal static float DistancePenalty(int distance, Civilization.SettlerTileAdjustments adjustments) {
			int beyond = distance - adjustments.DistancePenaltyRadius;
			if (beyond <= 0) {
				return 0;
			}
			return adjustments.DistancePenalty * (distance + beyond * beyond);
		}

		internal static bool IsOwnedByRival(Tile t, Player player) {
			Player owner = t.OwningPlayer();
			return owner != null && owner != player;
		}

		private static float CalculateTileYieldScore(Tile t, Player owner) {
			float score = owner.civilization.Adjustments.FoodYieldBonus * t.FoodYield(owner).yield;
			score += owner.civilization.Adjustments.ProductionYieldBonus * t.ProductionYield(owner).yield;
			score += owner.civilization.Adjustments.CommerceYieldBonus * t.CommerceYield(owner).yield;
			if (owner.KnowsAboutResource(t.Resource)) {
				if (t.Resource.Category == ResourceCategory.STRATEGIC) {
					score += owner.civilization.Adjustments.StrategicResourceBonus;
				} else if (t.Resource.Category == ResourceCategory.LUXURY) {
					score += owner.civilization.Adjustments.LuxuryResourceBonus;
				}
			}
			return score;
		}

		// The workable tiles around a city site (excluding the site itself),
		// with how much each contributes to the site's score.
		internal sealed class BigFatCross {
			public Tile[] tiles;
			public float[] adjustments;
		}

		// Everything a tile's yield score depends on, besides the player's
		// techs, government and golden age, which are checked for the whole
		// cache at once.
		internal sealed class TileYieldEntry {
			public float score;
			// The scoring pass in which the entry was last checked against
			// its tile: tiles don't change while sites are being scored, so
			// within a pass the check is only needed once.
			public int checkedPass;
			public TerrainType overlayTerrainType;
			public Resource resource;
			public bool isBonusShield;
			public bool hasCity;
			public City city;
			// The city's owner's traits (e.g. Industrious) change the city
			// tile's yield, and a captured city keeps its City object.
			public Player cityOwner;
			public int cityResidents;
			public bool cityIsCapital;
			public TerrainImprovement[] improvements = Array.Empty<TerrainImprovement>();

			public void Capture(Tile t) {
				overlayTerrainType = t.overlayTerrainType;
				resource = t.Resource;
				isBonusShield = t.isBonusShield;
				hasCity = t.HasCity();
				city = t.cityAtTile;
				cityOwner = hasCity ? city.owner : null;
				cityResidents = hasCity ? city.residents.Count : 0;
				cityIsCapital = hasCity && city.IsCapital();

				// Reuse the array when the number of improvements didn't
				// change, as is usual when an improvement is replaced.
				Dictionary<TerrainImprovement.Layer, TerrainImprovement>.ValueCollection current = t.overlays.GetImprovements();
				if (improvements.Length != current.Count) {
					improvements = new TerrainImprovement[current.Count];
				}
				current.CopyTo(improvements, 0);
			}

			public bool Matches(Tile t) {
				if (overlayTerrainType != t.overlayTerrainType || resource != t.Resource
					|| isBonusShield != t.isBonusShield || city != t.cityAtTile || hasCity != t.HasCity()) {
					return false;
				}
				if (hasCity && (cityOwner != city.owner || cityResidents != city.residents.Count || cityIsCapital != city.IsCapital())) {
					return false;
				}
				int i = 0;
				foreach (TerrainImprovement ti in t.overlays.GetImprovements()) {
					if (i >= improvements.Length || improvements[i] != ti) {
						return false;
					}
					++i;
				}
				return i == improvements.Length;
			}
		}

		private sealed class PlayerScoreCache {
			private readonly Player player;
			private Government government;
			private bool inGoldenAge;
			private int knownTechCount = -1;
			private readonly Dictionary<Tile, TileYieldEntry> yieldScores = new();

			// How far each tile is from the nearest of empireCities, and how
			// many of rivalCities are within rivalCityRadius of it. These
			// only depend on the tile and the city lists, so they are kept
			// while the lists stay the same, and brought up to date when
			// cities are only added. Call UpdateCities before asking.
			private readonly List<Tile> empireCities = new();
			private readonly Dictionary<Tile, int> empireDistances = new();
			private readonly List<Tile> rivalCities = new();
			private int rivalCityRadius = -1;
			private readonly Dictionary<Tile, int> rivalCityCounts = new();

			public PlayerScoreCache(Player player) {
				this.player = player;
			}

			public void UpdateCities(List<Tile> ownCities, List<Tile> rivals, int radius) {
				List<Tile> added = AddedTiles(empireCities, ownCities);
				if (added == null) {
					empireDistances.Clear();
				} else if (added.Count > 0) {
					foreach (Tile t in new List<Tile>(empireDistances.Keys)) {
						empireDistances[t] = Math.Min(empireDistances[t], DistanceFromEmpire(t, null, added));
					}
				}
				empireCities.Clear();
				empireCities.AddRange(ownCities);

				added = radius == rivalCityRadius ? AddedTiles(rivalCities, rivals) : null;
				if (added == null) {
					rivalCityCounts.Clear();
				} else if (added.Count > 0) {
					foreach (Tile t in new List<Tile>(rivalCityCounts.Keys)) {
						rivalCityCounts[t] += CountWithin(t, added, radius);
					}
				}
				rivalCities.Clear();
				rivalCities.AddRange(rivals);
				rivalCityRadius = radius;
			}

			// How far the tile is from the nearest of the cities given to
			// UpdateCities, which must not be empty.
			public int DistanceFromCities(Tile t) {
				if (!empireDistances.TryGetValue(t, out int distance)) {
					distance = DistanceFromEmpire(t, null, empireCities);
					empireDistances[t] = distance;
				}
				return distance;
			}

			// How many of the rival cities given to UpdateCities are within
			// its radius of the tile.
			public int RivalCitiesWithin(Tile t) {
				if (!rivalCityCounts.TryGetValue(t, out int count)) {
					count = CountWithin(t, rivalCities, rivalCityRadius);
					rivalCityCounts[t] = count;
				}
				return count;
			}

			private static int CountWithin(Tile t, List<Tile> cities, int radius) {
				int count = 0;
				foreach (Tile city in cities) {
					if (t.DistanceTo(city) <= radius) {
						++count;
					}
				}
				return count;
			}

			// The tiles in `now` that aren't in `old`, or null if some tile of
			// `old` is missing from `now`.
			private static List<Tile> AddedTiles(List<Tile> old, List<Tile> now) {
				if (old.Count == now.Count) {
					bool same = true;
					for (int i = 0; i < old.Count && same; ++i) {
						same = old[i] == now[i];
					}
					if (same) {
						return new List<Tile>();
					}
				}
				HashSet<Tile> nowSet = new(now);
				foreach (Tile t in old) {
					if (!nowSet.Contains(t)) {
						return null;
					}
				}
				HashSet<Tile> oldSet = new(old);
				List<Tile> added = new();
				foreach (Tile t in nowSet) {
					if (!oldSet.Contains(t)) {
						added.Add(t);
					}
				}
				return added;
			}

			// Counts the calls to Validate, each of which starts a scoring
			// pass.
			private int pass;

			// Starts a scoring pass, dropping all yield scores if something
			// that affects the yields of every tile changed.
			public void Validate() {
				++pass;
				if (government != player.government || inGoldenAge != player.InGoldenAge || knownTechCount != player.knownTechs.Count) {
					yieldScores.Clear();
					government = player.government;
					inGoldenAge = player.InGoldenAge;
					knownTechCount = player.knownTechs.Count;
				}
			}

			public float TileYieldScore(Tile t) {
				if (yieldScores.TryGetValue(t, out TileYieldEntry entry)) {
					if (entry.checkedPass == pass || entry.Matches(t)) {
						entry.checkedPass = pass;
						return entry.score;
					}
				} else {
					entry = new TileYieldEntry();
					yieldScores[t] = entry;
				}
				entry.checkedPass = pass;
				entry.Capture(t);
				entry.score = CalculateTileYieldScore(t, player);
				return entry.score;
			}

			// The map doesn't change shape, so these are computed once.
			public BigFatCross GetBigFatCross(Tile t, int maxRank) {
				BigFatCrosses cache = t.map != null
					? bigFatCrossesByMap.GetValue(t.map, _ => new BigFatCrosses())
					: ownBigFatCrosses;
				if (cache.rank != maxRank) {
					cache.crosses.Clear();
					cache.rank = maxRank;
				}
				if (cache.crosses.TryGetValue(t, out BigFatCross bfc)) {
					return bfc;
				}

				List<Tile> tiles = new();
				List<float> adjustments = new();
				foreach (Tile workable in t.GetTilesWithinRankDistance(maxRank)) {
					if (workable == Tile.NONE)
						continue;
					var rank = t.RankDistanceTo(workable);
					if (rank <= 0)
						continue;
					tiles.Add(workable);
					adjustments.Add(Math.Max(0, (maxRank - rank + 1f) / maxRank));
				}
				bfc = new BigFatCross() { tiles = tiles.ToArray(), adjustments = adjustments.ToArray() };
				cache.crosses[t] = bfc;
				return bfc;
			}

			// For tiles without a map, as in some tests.
			private readonly BigFatCrosses ownBigFatCrosses = new();
		}

		// The big fat crosses only depend on the shape of the map, so they are
		// shared by every player.
		private sealed class BigFatCrosses {
			public int rank = -1;
			public readonly Dictionary<Tile, BigFatCross> crosses = new();
		}

		private static readonly ConditionalWeakTable<GameMap, BigFatCrosses> bigFatCrossesByMap = new();

		private static readonly ConditionalWeakTable<Player, PlayerScoreCache> playerScoreCaches = new();

		private static PlayerScoreCache GetPlayerScoreCache(Player player) {
			PlayerScoreCache cache = playerScoreCaches.GetValue(player, p => new PlayerScoreCache(p));
			cache.Validate();
			return cache;
		}

		// Whether a tile is too close to a city to found one, remembered until
		// the set of cities changes. Kept per game in a weak table, so that a
		// finished game's tiles aren't kept alive.
		private sealed class InvalidLocations {
			public readonly Dictionary<Tile, bool> memo = new();
			public readonly List<Tile> cityTiles = new();
		}

		private static readonly ConditionalWeakTable<GameData, InvalidLocations> invalidLocationsByGame = new();

		private static Dictionary<Tile, bool> InvalidLocationMemo() {
			GameData gameData = EngineStorage.gameData;
			if (gameData == null) {
				return null;
			}
			InvalidLocations memo = invalidLocationsByGame.GetValue(gameData, _ => new InvalidLocations());
			List<Tile> cityTiles = memo.cityTiles;

			// Compare the city locations with the ones the memo was made for.
			bool changed = false;
			int i = 0;
			foreach (Player p in gameData.players) {
				foreach (City c in p.cities) {
					if (!changed && (i >= cityTiles.Count || cityTiles[i] != c.location)) {
						changed = true;
					}
					++i;
				}
			}
			changed |= i != cityTiles.Count;

			if (changed) {
				List<Tile> newCityTiles = new();
				foreach (Player p in gameData.players) {
					foreach (City c in p.cities) {
						newCityTiles.Add(c.location);
					}
				}
				ForgetChangedLocations(memo.memo, cityTiles, newCityTiles);
				cityTiles.Clear();
				cityTiles.AddRange(newCityTiles);
			}
			return memo.memo;
		}

		// Founding a city only changes the answer for the tiles within two
		// steps of it, so when cities were only added, only those tiles are
		// forgotten; they're worked out again when next asked about. Cities
		// going away (or moving between lists, as on capture) are rare
		// enough that everything is forgotten then.
		private static void ForgetChangedLocations(Dictionary<Tile, bool> memo, List<Tile> oldCityTiles, List<Tile> newCityTiles) {
			HashSet<Tile> newSet = new(newCityTiles);
			HashSet<Tile> oldSet = new(oldCityTiles);
			if (!oldSet.IsSubsetOf(newSet)) {
				memo.Clear();
				return;
			}
			foreach (Tile city in newSet) {
				if (oldSet.Contains(city)) {
					continue;
				}
				// The neighbour relation is symmetric, so these are exactly
				// the tiles that have the new city within two steps.
				memo.Remove(city);
				foreach (Tile neighbor in city.neighbors.Values) {
					memo.Remove(neighbor);
					foreach (Tile neighborOfNeighbor in neighbor.neighbors.Values) {
						memo.Remove(neighborOfNeighbor);
					}
				}
			}
		}

		// Whether the AI keeps from founding a city at the tile for a city
		// too near: within 2 tiles (see IsInvalidCityLocation), or under
		// Rising Tide only next to it (see TideAI.IsNextToCity).
		private static bool TooCloseToCity(Tile tile, Dictionary<Tile, bool> memo) {
			if (TideAI.IsOn()) {
				return TideAI.IsNextToCity(tile);
			}
			return memo == null ? IsInvalidCityLocation(tile) : IsInvalidCityLocation(tile, memo);
		}

		private static bool IsInvalidCityLocation(Tile tile, Dictionary<Tile, bool> memo) {
			if (memo == null) {
				return IsInvalidCityLocation(tile);
			}
			if (!memo.TryGetValue(tile, out bool result)) {
				result = IsInvalidCityLocation(tile);
				memo[tile] = result;
			}
			return result;
		}

		// Whether there's a city within 2 tiles (in steps, as Tile.DistanceTo
		// measures). The Civ3 rule is only that a city can't be founded next
		// to another (a minimum distance of 2, which the editor can't change:
		// https://steamcommunity.com/app/3910/discussions/0/3453716885198230413/;
		// MapUnit.canBuildCity enforces it). Keeping a free tile between the
		// AI's cities as well is the project owner's AI preference, and every
		// legal tile outside that gap is a candidate site.
		internal static bool IsInvalidCityLocation(Tile tile) {
			if (tile == Tile.NONE || tile.HasCity())
				return true;
			foreach (Tile neighbor in tile.neighbors.Values) {
				if (neighbor.HasCity()) {
					return true;
				}
				foreach (Tile neighborOfNeighbor in neighbor.neighbors.Values) {
					if (neighborOfNeighbor.HasCity()) {
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Returns the tiles near where the settlers in the list (which should be the current AI's settlers) are
		/// already heading: their destinations and the land tiles up to two steps away.
		/// Another AI's settlers don't count, as the AI shouldn't know the other AI's plans.
		/// </summary>
		/// <param name="playerSettlers">The settlers owned by the AI considering building a city.</param>
		private static HashSet<Tile> TilesNearSettlerDestinations(List<MapUnit> playerSettlers, bool tide) {
			HashSet<Tile> result = new();
			foreach (MapUnit otherSettler in playerSettlers) {
				// A settler joining a city isn't heading for a city site, and
				// may have no destination at all while it waits for room.
				if (otherSettler.currentAI is SettlerAI otherSettlerAI && otherSettlerAI.data.goal == SettlerAIData.SettlerGoal.BUILD_CITY
					&& otherSettlerAI.data.destination != null && otherSettlerAI.data.destination != Tile.NONE) {
					Tile otherDestination = otherSettlerAI.data.destination;
					result.Add(otherDestination);
					foreach (Tile innerRingTile in otherDestination.GetLandNeighbors()) {
						result.Add(innerRingTile);
						// Under Rising Tide cities may stand with only a
						// tile between them (see TideAI.IsNextToCity).
						if (tide) {
							continue;
						}
						foreach (Tile outerRingTile in innerRingTile.GetLandNeighbors()) {
							result.Add(outerRingTile);
						}
					}
				}
			}
			return result;
		}
	}
}
