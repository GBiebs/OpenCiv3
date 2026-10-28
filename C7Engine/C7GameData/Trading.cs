using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// A civ's reputation: Civ3's AI won't take promises of gold or goods to
	// come from a civ that has broken a deal (diplomacy.txt: "$CIVNAME1 has
	// broken deals with us in the past, remember?"), with it or with a civ it
	// has heard of.
	public static class Reputation {
		public static bool Tarnished(GameData gameData, Player civ) {
			return gameData.players.Any(p => p.playerRelationships.TryGetValue(civ.id, out PlayerRelationship r) && r.brokenDeals > 0);
		}
	}

	// Carrying out the goods of a deal (see TradeOffer): maps reveal tiles,
	// introductions make contact, resources and gold per turn become deals
	// that run the rules' deal duration, and cities and workers change hands.
	// Resources reach the receiver when the two capitals trade (the Conquests
	// Civilopedia: "Excess resources may be traded to other civilizations if
	// the capital cities of both civilizations are connected"; see
	// TradeNetwork).
	public static class Trading {
		private static ILogger log = Log.ForContext(typeof(Trading));

		// How many of `resource` reach `player`'s capital (deals included).
		public static int ResourceCount(GameData gameData, Player player, Resource resource) {
			City capital = player.cities.Find(c => c.IsCapital());
			if (capital == null) {
				return 0;
			}
			return gameData.GetTradeNetwork().GetResourcesAvailableToCity(player, capital).GetValueOrDefault(resource);
		}

		// The resources `giver` could trade to `receiver`: those reaching its
		// capital.
		public static List<Resource> Tradeable(GameData gameData, Player giver) {
			City capital = giver.cities.Find(c => c.IsCapital());
			if (capital == null) {
				return [];
			}
			return gameData.GetTradeNetwork().GetResourcesAvailableToCity(giver, capital).Keys
				.Where(r => r.Category is ResourceCategory.LUXURY or ResourceCategory.STRATEGIC)
				.OrderBy(r => r.Name)
				.ToList();
		}

		// The cities `giver` could hand over (all but the capital), and its
		// workers.
		public static List<City> TradeableCities(Player giver) {
			return giver.cities.Where(c => !c.IsCapital()).ToList();
		}

		public static List<MapUnit> TradeableWorkers(Player giver) {
			return giver.units.Where(u => u.unitType.isWorker && !u.IsLoaded()).ToList();
		}

		// The civs `giver` could introduce `receiver` to.
		public static List<Player> Introductions(GameData gameData, Player giver, Player receiver) {
			return gameData.players.Where(p => p != giver && p != receiver && !p.isBarbarians && !p.defeated
				&& giver.playerRelationships.ContainsKey(p.id) && !receiver.playerRelationships.ContainsKey(p.id)).ToList();
		}

		// `giver` hands `offer`'s goods to `receiver`.
		public static void Give(GameData gameData, Player giver, Player receiver, TradeOffer offer) {
			if (offer.worldMap) {
				receiver.tileKnowledge.Learn(giver.tileKnowledge.knownTiles.ToList());
			}
			if (offer.territoryMap) {
				receiver.tileKnowledge.Learn(giver.tileKnowledge.OwnedTiles());
			}
			foreach (ID id in offer.contacts) {
				Player third = gameData.GetPlayer(id);
				if (third != null) {
					receiver.EnsureRelationshipExists(third);
				}
			}
			foreach (string key in offer.resources) {
				Resource resource = gameData.Resources.Find(r => r.Key == key);
				if (resource == null) {
					continue;
				}
				bool luxury = resource.Category == ResourceCategory.LUXURY;
				PlayerRelationship.RegisterMultiTurnDeal(giver, receiver, new MultiTurnDeal(
					luxury ? DealType.Luxury : DealType.Resource, luxury ? DealSubType.LuxuryPerTurn : DealSubType.ResourcePerTurn,
					DealDetails.Outbound, 0, key, gameData.rules.DefaultDealDuration, gameData.turn));
			}
			if (offer.goldPerTurn is int perTurn && perTurn > 0) {
				PlayerRelationship.RegisterMultiTurnDeal(giver, receiver, new MultiTurnDeal(DealType.Gold, DealSubType.GoldPerTurn,
					DealDetails.Outbound, perTurn, null, gameData.rules.DefaultDealDuration, gameData.turn));
			}
			foreach (ID id in offer.cities) {
				City city = gameData.cities.Find(c => c.id == id && c.owner == giver && !c.IsCapital());
				if (city != null) {
					HandOver(gameData, city, receiver);
				}
			}
			foreach (ID id in offer.workers) {
				MapUnit worker = gameData.mapUnits.Find(u => u.id == id && u.owner == giver);
				City capital = receiver.cities.Find(c => c.IsCapital()) ?? receiver.cities.FirstOrDefault();
				if (worker != null && capital != null) {
					gameData.TransferUnit(worker, receiver);
					gameData.TeleportUnit(worker, capital.location);
					// Civ3's AI takes it badly when you sell it your workers.
					Attitude.Remember(receiver, giver, gameData.rules.attitude.soldUsWorkers);
				}
			}
			if (offer.resources.Count > 0 || offer.cities.Count > 0) {
				gameData.InvalidateCachedTradeNetwork();
			}
		}

		// The giver's units in the city go to its capital.
		private static void HandOver(GameData gameData, City city, Player receiver) {
			Player giver = city.owner;
			City home = giver.cities.Find(c => c.IsCapital() && c != city);
			foreach (MapUnit unit in city.location.unitsOnTile.Where(u => u.owner == giver && !u.IsLoaded()).ToList()) {
				if (home != null) {
					gameData.TeleportUnit(unit, home.location);
				} else {
					gameData.RemoveUnit(unit);
				}
			}
			CityInteractions.ChangeOwner(gameData, city, receiver);
			city.SetItemBeingProduced(ChooseProducible.Choose(city, receiver));
			giver.DoCorruptionCalculations(gameData);
			receiver.DoCorruptionCalculations(gameData);
			log.Information($"{giver} hands {city.name} over to {receiver}");
		}
	}
}
