using System.Collections.Generic;
using System.Linq;

namespace C7GameData {
	// What the trade advisor shows: our luxuries and strategic resources (our
	// own, imported, exported), the civs we trade with or could, and our
	// cities off the capital's trade network.
	public static class TradeReport {
		// A resource reaching our capital: from our own sources, from deals in,
		// and given away in deals out.
		public record ResourceRow(Resource resource, int local, int imported, int exported);

		public enum Standing {
			Trading,
			CanTrade,
			CannotTrade,
		}

		// A civ we know: whether we trade with it, and if we can't, why not.
		public record Partner(Player civ, Standing standing, string reason);

		// Every luxury and strategic resource we know about, in the game's
		// order.
		public static List<ResourceRow> Resources(GameData gameData, Player player) {
			City capital = player.cities.Find(c => c.IsCapital());
			Dictionary<Resource, int> available = capital == null ? new() : gameData.GetTradeNetwork().GetResourcesAvailableToCity(player, capital);
			List<ResourceRow> rows = new();
			foreach (Resource resource in gameData.Resources.Where(r => r.Category is ResourceCategory.LUXURY or ResourceCategory.STRATEGIC
				&& player.KnowsAboutResource(r))) {
				int imported = Deals(gameData, player, resource, DealDetails.Inbound);
				int exported = Deals(gameData, player, resource, DealDetails.Outbound);
				int local = available.GetValueOrDefault(resource) - imported + exported;
				rows.Add(new ResourceRow(resource, System.Math.Max(0, local), imported, exported));
			}
			return rows;
		}

		// Our running deals for `resource` in one direction, with civs whose
		// capitals trade with ours (only those deliver).
		private static int Deals(GameData gameData, Player player, Resource resource, DealDetails direction) {
			int count = 0;
			foreach ((ID id, PlayerRelationship relationship) in player.playerRelationships) {
				Player other = gameData.GetPlayer(id);
				if (other == null || !gameData.GetTradeNetwork().CapitalsTrade(player, other)) {
					continue;
				}
				count += relationship.multiTurnDeals.Count(d => d.dealDetails == direction && d.resourcePerTurn == resource.Key
					&& d.dealSubType is DealSubType.ResourcePerTurn or DealSubType.LuxuryPerTurn);
			}
			return count;
		}

		// The civs we know: trading (a running deal), able to trade (at peace,
		// no embargo, and our capitals trade), or not, with the reason.
		public static List<Partner> Partners(GameData gameData, Player player) {
			List<Partner> result = new();
			foreach (Player other in gameData.GetKnownRivals(player)) {
				string reason = PlayerRelationship.AtWar(player, other) ? "We are at war."
					: Agreements.Embargoed(player, other) ? "An embargo stands between us."
					: !gameData.GetTradeNetwork().CapitalsTrade(player, other) ? "No trade route joins our capitals."
					: null;
				bool trading = player.playerRelationships[other.id].multiTurnDeals
					.Any(d => d.dealSubType is DealSubType.GoldPerTurn or DealSubType.ResourcePerTurn or DealSubType.LuxuryPerTurn);
				Standing standing = reason != null ? Standing.CannotTrade : trading ? Standing.Trading : Standing.CanTrade;
				result.Add(new Partner(other, standing, reason));
			}
			return result.OrderBy(p => p.standing).ToList();
		}

		// Our cities off the capital's trade network, which get none of its
		// resources.
		public static List<City> Unconnected(GameData gameData, Player player) {
			return player.cities.Where(c => !c.IsCapital() && !gameData.GetTradeNetwork().ConnectedToCapital(player, c)).ToList();
		}

		// What `them` could give us: resources reaching their capital that we
		// have none of.
		public static List<Resource> TheyCouldSell(GameData gameData, Player us, Player them) {
			HashSet<Resource> ours = Resources(gameData, us).Where(r => r.local + r.imported > 0).Select(r => r.resource).ToHashSet();
			return Trading.Tradeable(gameData, them).Where(r => us.KnowsAboutResource(r) && !ours.Contains(r)).ToList();
		}
	}
}
