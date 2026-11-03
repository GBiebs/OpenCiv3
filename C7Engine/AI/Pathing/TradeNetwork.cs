using System.Collections.Generic;
using System.Linq;
using System;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Pathing {
	public class TradeNetworkSegment {
		public Dictionary<Resource, int> resourceCounts = new();
		public HashSet<Tile> tiles = new();

		public void AddTile(Tile t, Player p) {
			tiles.Add(t);
			// Our resources: in our territory, or under our colony.
			if (t.Resource != Resource.NONE && (t.OwningPlayer() == p || Structures.Has(t, Tile.TileOverlays.COLONY, p))) {
				if (resourceCounts.TryGetValue(t.Resource, out int currentCount)) {
					resourceCounts[t.Resource] = currentCount + 1;
				} else {
					resourceCounts[t.Resource] = 1;
				}
			}
		}

		// Takes in another segment's tiles and resources: their cities now
		// trade with ours.
		public void Absorb(TradeNetworkSegment other) {
			tiles.UnionWith(other.tiles);
			foreach ((Resource r, int count) in other.resourceCounts) {
				resourceCounts[r] = resourceCounts.GetValueOrDefault(r) + count;
			}
		}
	}

	// A class for calculating the trade networks.
	//
	// The main idea is that calculating the entire empire's trade network once
	// is faster than doing it repeatedly for each city. By doing it once we can
	// do a simple flood fill of the road network, rather than needing to do
	// actual pathfinding between different tiles.
	//
	// Cities trade (Civilopedia "Trade Route Connections") when a road or
	// railroad runs between them, when both have a Harbor and a water route we
	// have seen and can sail safely joins them (coast; sea with a tech that
	// enables trade over sea, Astronomy; ocean with one that enables it over
	// ocean, Navigation or Magnetism), or when both have an Airport. No route
	// passes through the territory of a civ we're at war with, and enemy ships
	// next to a city blockade its harbor.
	public class TradeNetwork {
		private Dictionary<Player, Dictionary<City, TradeNetworkSegment>> segments = new();

		public TradeNetwork(GameData gameData) {
			foreach (Player p in gameData.players) {
				ComputeTradeNetwork(p);
			}
			ApplyResourceDeals(gameData);
		}

		// Resources traded between civs (see Trading): while the two capitals
		// trade, a road joining them or harbors in both capitals' networks,
		// one of the giver's moves from its capital's network to the
		// receiver's (a civ that trades away its only one goes without).
		private void ApplyResourceDeals(GameData gameData) {
			foreach (Player giver in gameData.players) {
				City giverCapital = giver.cities.Find(c => c.IsCapital());
				if (giverCapital == null) {
					continue;
				}
				foreach ((ID id, PlayerRelationship relationship) in giver.playerRelationships) {
					Player receiver = gameData.GetPlayer(id);
					City receiverCapital = receiver?.cities.Find(c => c.IsCapital());
					if (receiverCapital == null || !segments.ContainsKey(receiver)) {
						continue;
					}
					foreach (MultiTurnDeal deal in relationship.multiTurnDeals.Where(d => d.dealDetails == DealDetails.Outbound
						&& d.dealSubType is DealSubType.ResourcePerTurn or DealSubType.LuxuryPerTurn)) {
						Resource resource = gameData.Resources.Find(r => r.Key == deal.resourcePerTurn);
						TradeNetworkSegment from = segments[giver][giverCapital];
						TradeNetworkSegment to = segments[receiver][receiverCapital];
						if (resource == null || from.resourceCounts.GetValueOrDefault(resource) <= 0
							|| !CapitalsTrade(giver, giverCapital, receiver, receiverCapital)) {
							continue;
						}
						from.resourceCounts[resource]--;
						if (from.resourceCounts[resource] == 0) {
							from.resourceCounts.Remove(resource);
						}
						to.resourceCounts[resource] = to.resourceCounts.GetValueOrDefault(resource) + 1;
					}
				}
			}
		}

		// Whether `a` and `b` can trade resources: both have a capital and the
		// two capitals trade (see ApplyResourceDeals).
		public bool CapitalsTrade(Player a, Player b) {
			City aCapital = a.cities.Find(c => c.IsCapital());
			City bCapital = b.cities.Find(c => c.IsCapital());
			return aCapital != null && bCapital != null && segments.ContainsKey(a) && segments.ContainsKey(b)
				&& CapitalsTrade(a, aCapital, b, bCapital);
		}

		private bool CapitalsTrade(Player a, City aCapital, Player b, City bCapital) {
			TradeNetworkSegment ours = segments[a][aCapital];
			TradeNetworkSegment theirs = segments[b][bCapital];
			if (ours.tiles.Contains(bCapital.location) || theirs.tiles.Contains(aCapital.location)) {
				return true;
			}
			bool HarborOn(Player p, TradeNetworkSegment segment) =>
				p.cities.Any(c => segments[p][c] == segment && HasWorking(c, building => building.allowsWaterTrade));
			return HarborOn(a, ours) && HarborOn(b, theirs);
		}

		private void ComputeTradeNetwork(Player player) {
			HashSet<Tile> seen = new();

			segments[player] = new();
			foreach (City c in player.cities) {
				if (segments[player].ContainsKey(c)) {
					continue;
				}

				// If we don't know about this city yet, start a new network
				// segment and do a flood fill for all roads coming from the
				// city.
				TradeNetworkSegment segment = new();
				segments[player][c] = segment;

				Queue<Tile> toCheck = new();
				toCheck.Enqueue(c.location);
				seen.Add(c.location);

				while (toCheck.Count > 0) {
					Tile x = toCheck.Dequeue();
					segment.AddTile(x, player);

					if (x.cityAtTile != null) {
						segments[player][x.cityAtTile] = segment;
					}

					foreach (Tile n in x.neighbors.Values) {
						if (n.IsRoaded() && !IsEnemyTerritory(n, player) && seen.Add(n)) {
							toCheck.Enqueue(n);
						}
					}
				}
			}

			Join(player, player.cities.Where(c => HasWorking(c, b => b.allowsAirTrade)));
			ConnectHarbors(player);
		}

		private static bool IsEnemyTerritory(Tile t, Player player) {
			Player owner = t.OwningPlayer();
			return owner != null && owner != player && !player.IsAtPeaceWith(owner);
		}

		private static bool HasWorking(City city, Func<Building, bool> predicate) {
			return city.GetBuildings().Any(cb => predicate(cb.building) && !cb.building.IsObsolete(city.owner));
		}

		// Enemy ships next to the city blockade its harbor.
		private static bool Blockaded(City city, Player player) {
			return city.location.neighbors.Values.Any(t => t.IsWater()
				&& t.unitsOnTile.Any(u => u.IsWaterUnit() && !player.IsAtPeaceWith(u.owner)));
		}

		// Labels each body of water our harbors touch that we can trade over,
		// and joins the harbor cities on each.
		private void ConnectHarbors(Player player) {
			bool sea = player.HasTechWithFlag(SaveTech.Flag.EnablesTradeOverSea);
			bool ocean = player.HasTechWithFlag(SaveTech.Flag.EnablesTradeOverOcean);
			bool Sailable(Tile t) => t.IsWater() && (t.IsCoast() || (t.IsSea() ? sea : ocean))
				&& player.tileKnowledge.isTileKnown(t) && !IsEnemyTerritory(t, player);

			Dictionary<Tile, int> bodyOf = [];
			List<List<City>> harborsOn = [];
			foreach (City city in player.cities.Where(c => HasWorking(c, b => b.allowsWaterTrade) && !Blockaded(c, player))) {
				foreach (Tile start in city.location.neighbors.Values.Where(Sailable)) {
					if (!bodyOf.TryGetValue(start, out int body)) {
						body = harborsOn.Count;
						harborsOn.Add([]);
						Queue<Tile> toCheck = new();
						toCheck.Enqueue(start);
						bodyOf[start] = body;
						while (toCheck.Count > 0) {
							foreach (Tile n in toCheck.Dequeue().neighbors.Values) {
								if (!bodyOf.ContainsKey(n) && Sailable(n)) {
									bodyOf[n] = body;
									toCheck.Enqueue(n);
								}
							}
						}
					}
					harborsOn[body].Add(city);
				}
			}
			foreach (List<City> harbors in harborsOn) {
				Join(player, harbors);
			}
		}

		// The cities trade with each other: their segments become one.
		private void Join(Player player, IEnumerable<City> cities) {
			Dictionary<City, TradeNetworkSegment> ofCity = segments[player];
			TradeNetworkSegment joined = null;
			foreach (City city in cities) {
				TradeNetworkSegment segment = ofCity[city];
				if (joined == null || segment == joined) {
					joined ??= segment;
					continue;
				}
				joined.Absorb(segment);
				foreach (City other in ofCity.Where(kv => kv.Value == segment).Select(kv => kv.Key).ToList()) {
					ofCity[other] = joined;
				}
			}
		}

		// Returns the resources and their counts that are available to the given
		// city.
		public Dictionary<Resource, int> GetResourcesAvailableToCity(Player player, City city) {
			TradeNetworkSegment segment = segments[player][city];
			Dictionary<Resource, int> result = new();
			foreach ((Resource r, int count) in segment.resourceCounts) {
				if (player.KnowsAboutResource(r)) {
					result[r] = count;
				}
			}
			return result;
		}

		public bool HasTradeAccess(Tile t, Player p, Resource r) {
			if (!p.KnowsAboutResource(r)) {
				return false;
			}

			foreach (TradeNetworkSegment segment in segments[p].Values) {
				if (segment.tiles.Contains(t) && segment.resourceCounts.TryGetValue(r, out int count) && count > 0) {
					return true;
				}
			}
			return false;
		}

		// Whether the city is on the same network as its owner's capital (the
		// city with the palace, not necessarily the first city).
		public bool ConnectedToCapital(Player p, City c) {
			City capital = p.cities.Find(city => city.IsCapital());
			return capital != null
				&& segments[p].TryGetValue(c, out TradeNetworkSegment segment)
				&& segments[p].TryGetValue(capital, out TradeNetworkSegment capitalSegment)
				&& segment == capitalSegment;
		}
	}
}
