using System.Linq;
using C7GameData;
using C7GameData.Save;
using Serilog;

namespace C7Engine.AI {
	// The AI trading goods with other AIs (Civ3's AI asks "Care to trade
	// maps?" and offers luxuries): it swaps world maps when both would learn
	// something, and a spare resource the other lacks for one it lacks. Each
	// side judges the swap as any deal (TradeOffer values), so it happens only
	// when both gain. Humans trade these on the deal screen.
	public static class TradeAI {
		private static ILogger log = Log.ForContext(typeof(TradeAI));

		public static void SwapGoods(Player us, GameData gameData) {
			foreach (Player them in gameData.players.Where(p => p != us && !p.isHuman && !p.isBarbarians && !p.defeated).ToList()) {
				if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship) || relationship.AtWar()
					|| Agreements.Embargoed(us, them)) {
					continue;
				}
				SwapMaps(us, them, gameData);
				SwapResources(us, them, gameData);
			}
		}

		private static void SwapMaps(Player us, Player them, GameData gameData) {
			if (!us.HasTechWithFlag(SaveTech.Flag.EnablesMapTrading) || !them.HasTechWithFlag(SaveTech.Flag.EnablesMapTrading)) {
				return;
			}
			TradeOffer ours = new() { worldMap = true };
			TradeOffer theirs = new() { worldMap = true };
			if (theirs.GoldEquivalentFor(gameData, us, them) > 0 && ours.GoldEquivalentFor(gameData, them, us) > 0
				&& them.WouldAcceptDealFrom(gameData, us, ours, theirs)) {
				log.Information($"{us} and {them} trade world maps");
				us.ExecuteDeal(gameData, them, theirs, ours);
			}
		}

		private static void SwapResources(Player us, Player them, GameData gameData) {
			Resource Spare(Player giver, Player receiver) => Trading.Tradeable(gameData, giver)
				.FirstOrDefault(r => Trading.ResourceCount(gameData, giver, r) > 1 && Trading.ResourceCount(gameData, receiver, r) == 0);
			Resource ourSpare = Spare(us, them);
			Resource theirSpare = Spare(them, us);
			if (ourSpare == null || theirSpare == null) {
				return;
			}
			TradeOffer ours = new() { resources = [ourSpare.Key] };
			TradeOffer theirs = new() { resources = [theirSpare.Key] };
			if (us.WouldAcceptDealFrom(gameData, them, theirs, ours) && them.WouldAcceptDealFrom(gameData, us, ours, theirs)) {
				log.Information($"{us} trades {ourSpare.Name} to {them} for {theirSpare.Name}");
				us.ExecuteDeal(gameData, them, theirs, ours);
			}
		}
	}
}
