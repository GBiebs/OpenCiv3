using System;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using C7GameData.Save;
using Serilog;

namespace C7Engine.AI {
	// The AI trading goods (Civ3's AI asks "Care to trade maps?" and offers
	// luxuries): it swaps world maps when both would learn something, and a
	// spare resource the other lacks for one it lacks. Another AI judges the
	// swap as any deal (TradeOffer values), so it happens when both gain.
	//
	// With a human the AI trades too (the user's decision, as in Civ3), but
	// on worse terms: it prices the maps and resources it gives at what
	// they're worth to the human times the difficulty's "AI to AI trade
	// rate" (Civ3's DIFF, 110% at Chieftain to 200% at Sid; the rate at which
	// the AIs favor each other), and asks for gold to make up the difference.
	// The same price holds when the human proposes the trade (see
	// Player.WouldAcceptDealFrom). It offers a human a swap at most every
	// HumanOfferEveryTurns turns, and only one the human can pay for.
	public static class TradeAI {
		private static ILogger log = Log.ForContext(typeof(TradeAI));

		internal const int HumanOfferEveryTurns = 10;

		public static async Task SwapGoods(Player us, GameData gameData) {
			foreach (Player them in gameData.players.Where(p => p != us && !p.isBarbarians && !p.defeated).ToList()) {
				if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship) || relationship.AtWar()
					|| Agreements.Embargoed(us, them)) {
					continue;
				}
				if (them.isHuman) {
					if (relationship.lastGoodsOfferTurn is int last && gameData.turn - last < HumanOfferEveryTurns) {
						continue;
					}
					TradeOffer ours = MapsOffer(us, them, gameData) ?? ResourceOffer(us, them, gameData);
					TradeOffer theirs = ours == null ? null : ours.worldMap ? new TradeOffer { worldMap = true } : WantedResource(us, them, gameData);
					if (ours == null || theirs == null || !PriceForHuman(gameData, us, them, ours, theirs)) {
						continue;
					}
					relationship.lastGoodsOfferTurn = gameData.turn;
					log.Information($"{us} offers {them} {ours.ToString()} for {theirs.ToString()}");
					TurnHandling.HandControlTo(them);
					new MsgShowTradeOffer(us, them, theirs, ours).send();
					await EngineStorage.WaitForMessageToEngine<MsgDiplomacyCompleted>();
					continue;
				}
				SwapMaps(us, them, gameData);
				SwapResources(us, them, gameData);
			}
		}

		// The extra gold `ai` wants from a human for the maps and resources in
		// `aiGives`: their worth to the human at the AI-to-AI trade rate, less
		// what giving them costs the AI.
		public static int HumanPremium(GameData gameData, Player ai, Player human, TradeOffer aiGives) {
			TradeOffer goods = new() { worldMap = aiGives.worldMap, territoryMap = aiGives.territoryMap, resources = aiGives.resources };
			int rate = gameData.gameDifficulty?.AIToAITradeRate is int r && r > 0 ? r : 100;
			int theirWorth = goods.GoldEquivalentFor(gameData, human, ai) * rate / 100;
			return Math.Max(0, theirWorth - goods.GoldEquivalentFor(gameData, ai, ai));
		}

		// Adds the gold `us` wants on top of `theirs` (what the human gives)
		// for `ours`; false if the human can't pay it.
		private static bool PriceForHuman(GameData gameData, Player us, Player them, TradeOffer ours, TradeOffer theirs) {
			int price = ours.GoldEquivalentFor(gameData, us, us) + HumanPremium(gameData, us, them, ours);
			int shortfall = price - theirs.GoldEquivalentFor(gameData, us, them);
			if (shortfall > them.gold) {
				return false;
			}
			if (shortfall > 0) {
				theirs.gold = shortfall;
			}
			return ours.GoldEquivalentFor(gameData, them, us) > 0;
		}

		// Our world map, if we can trade maps and they'd learn something.
		private static TradeOffer MapsOffer(Player us, Player them, GameData gameData) {
			if (!us.HasTechWithFlag(SaveTech.Flag.EnablesMapTrading) || !them.HasTechWithFlag(SaveTech.Flag.EnablesMapTrading)) {
				return null;
			}
			TradeOffer ours = new() { worldMap = true };
			return ours.GoldEquivalentFor(gameData, them, us) > 0 ? ours : null;
		}

		private static Resource Spare(GameData gameData, Player giver, Player receiver) {
			return Trading.Tradeable(gameData, giver)
				.FirstOrDefault(r => Trading.ResourceCount(gameData, giver, r) > 1 && Trading.ResourceCount(gameData, receiver, r) == 0);
		}

		// A spare resource of ours they lack.
		private static TradeOffer ResourceOffer(Player us, Player them, GameData gameData) {
			Resource resource = Spare(gameData, us, them);
			return resource == null ? null : new TradeOffer { resources = [resource.Key] };
		}

		// A resource of the human's we lack (spare ones first).
		private static TradeOffer WantedResource(Player us, Player them, GameData gameData) {
			Resource wanted = Trading.Tradeable(gameData, them)
				.Where(r => Trading.ResourceCount(gameData, us, r) == 0)
				.OrderByDescending(r => Trading.ResourceCount(gameData, them, r))
				.FirstOrDefault();
			return wanted == null ? null : new TradeOffer { resources = [wanted.Key] };
		}

		private static void SwapMaps(Player us, Player them, GameData gameData) {
			TradeOffer ours = MapsOffer(us, them, gameData);
			TradeOffer theirs = new() { worldMap = true };
			if (ours != null && theirs.GoldEquivalentFor(gameData, us, them) > 0
				&& them.WouldAcceptDealFrom(gameData, us, ours, theirs)) {
				log.Information($"{us} and {them} trade world maps");
				us.ExecuteDeal(gameData, them, theirs, ours);
			}
		}

		private static void SwapResources(Player us, Player them, GameData gameData) {
			Resource ourSpare = Spare(gameData, us, them);
			Resource theirSpare = Spare(gameData, them, us);
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
