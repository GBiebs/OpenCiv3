using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using Serilog;
using static C7GameData.PlayerRelationship;

namespace C7GameData {
	// One side of a deal between two humans as the save keeps it (a
	// TradeOffer holds the techs themselves).
	public class DealTerms {
		public bool partOfPeaceTreaty;
		public int? gold;
		public List<ID> techs = new();
		public List<Agreement> agreements = new();
		public bool worldMap;
		public bool territoryMap;
		public List<ID> contacts = new();
		public List<string> resources = new();
		public int? goldPerTurn;
		public List<ID> cities = new();
		public List<ID> workers = new();

		public static DealTerms Of(TradeOffer offer) {
			return new DealTerms {
				partOfPeaceTreaty = offer.partOfPeaceTreaty,
				gold = offer.gold,
				techs = offer.techs.Select(t => t.id).ToList(),
				agreements = offer.agreements.Select(a => new Agreement { type = a.type, against = a.against }).ToList(),
				worldMap = offer.worldMap,
				territoryMap = offer.territoryMap,
				contacts = new(offer.contacts),
				resources = new(offer.resources),
				goldPerTurn = offer.goldPerTurn,
				cities = new(offer.cities),
				workers = new(offer.workers),
			};
		}

		public TradeOffer ToOffer(GameData gameData) {
			return new TradeOffer {
				partOfPeaceTreaty = partOfPeaceTreaty,
				gold = gold,
				techs = techs.Select(id => gameData.techs.Find(t => t.id == id)).Where(t => t != null).ToList(),
				agreements = agreements.Select(a => new Agreement { type = a.type, against = a.against }).ToList(),
				worldMap = worldMap,
				territoryMap = territoryMap,
				contacts = new(contacts),
				resources = new(resources),
				goldPerTurn = goldPerTurn,
				cities = new(cities),
				workers = new(workers),
			};
		}

		public bool SameAs(DealTerms other) {
			static bool Same<T>(List<T> a, List<T> b) => a.Count == b.Count && a.All(b.Contains) && b.All(a.Contains);
			return partOfPeaceTreaty == other.partOfPeaceTreaty && (gold ?? 0) == (other.gold ?? 0)
				&& Same(techs, other.techs) && worldMap == other.worldMap && territoryMap == other.territoryMap
				&& Same(contacts, other.contacts) && Same(resources, other.resources) && (goldPerTurn ?? 0) == (other.goldPerTurn ?? 0)
				&& Same(cities, other.cities) && Same(workers, other.workers)
				&& agreements.Count == other.agreements.Count && agreements.All(a => other.agreements.Any(a.SameAs));
		}
	}

	// A deal between two humans on its way from one to the other: what each
	// gives, and whether its sender has agreed to it as it stands. A new one
	// comes with an envoy, whom `to` first agrees to hear.
	public class HumanDeal {
		public ID from;
		public ID to;
		public DealTerms fromGives = new();
		public DealTerms toGives = new();
		public bool fromAccepted;
		public bool envoy;
	}

	public enum HumanDealAnswer {
		// We agree to the table as it stands: the trade is done if they
		// agreed to it too, otherwise it's sent to them with our agreement.
		Accept,
		// Sent to them to add to or change, without our agreement yet.
		Send,
		// We turn down their deal, or take ours back.
		Decline,
	}

	public enum EnvoyAnswer {
		Hear,
		Refuse,
		// Refuse, and don't come back for the rules' envoy refusal turns.
		RefuseForAWhile,
	}

	// Hotseat and play by email: diplomacy between two humans works as in
	// Civ3. Neither sees what the other has to trade: whoever begins puts
	// something on their side of the table (and agreements, which go on
	// both) and sends it. The other sees it at the start of their turn,
	// first deciding whether to hear the envoy, and can add to their side
	// and change the table before sending it back. The trade is done once
	// both have agreed to the same table, which may take a few turns. Only
	// one deal is on the table between two humans at a time.
	public static class HumanDiplomacy {
		private static ILogger log = Log.ForContext(typeof(HumanDiplomacy));

		public static HumanDeal Between(GameData gameData, Player a, Player b) {
			return gameData.humanDeals.Find(d => (d.from == a.id && d.to == b.id) || (d.from == b.id && d.to == a.id));
		}

		// The turns `them` still refuses our envoys for (0 if they don't).
		public static int EnvoysRefusedFor(GameData gameData, Player us, Player them) {
			if (!them.playerRelationships.TryGetValue(us.id, out PlayerRelationship relationship)) {
				return 0;
			}
			return System.Math.Max(0, relationship.refuseEnvoysUntilTurn - gameData.turn);
		}

		// Whether `us` can send `them` an envoy now.
		public static bool CanContact(GameData gameData, Player us, Player them) {
			return us != them && us.isHuman && them.isHuman && !us.defeated && !them.defeated
				&& us.playerRelationships.ContainsKey(them.id) && !gameData.AreInLockedWar(us, them)
				&& EnvoysRefusedFor(gameData, us, them) == 0;
		}

		// Takes off the table what `giver` can no longer give `receiver` (a
		// deal waits a turn or more): gold they no longer have, advances the
		// receiver has learned, cities and workers lost, agreements that can
		// no longer be signed, and peace once the war is over. At war, nothing
		// but peace and what comes with it; under an embargo, nothing but
		// peace. Returns whether anything was taken off.
		public static bool Prune(GameData gameData, Player giver, Player receiver, TradeOffer offer) {
			string before = offer.ToString();
			bool atWar = AtWar(giver, receiver);
			if (offer.partOfPeaceTreaty && !atWar) {
				offer.partOfPeaceTreaty = false;
			}
			if ((atWar && !offer.partOfPeaceTreaty) || (!offer.partOfPeaceTreaty && Agreements.Embargoed(giver, receiver))) {
				bool peace = offer.partOfPeaceTreaty;
				offer.Clear();
				offer.partOfPeaceTreaty = peace;
				return offer.ToString() != before;
			}
			if (offer.gold is int gold && (gold <= 0 || gold > giver.gold)) {
				offer.gold = null;
			}
			if (offer.goldPerTurn is int perTurn && perTurn <= 0) {
				offer.goldPerTurn = null;
			}
			offer.techs.RemoveAll(t => !giver.knownTechs.Contains(t.id) || receiver.knownTechs.Contains(t.id));
			if (!giver.HasTechWithFlag(Save.SaveTech.Flag.EnablesMapTrading)) {
				offer.worldMap = false;
				offer.territoryMap = false;
			}
			List<ID> introductions = giver.HasTechWithFlag(Save.SaveTech.Flag.EnablesCommunicationTrading)
				? Trading.Introductions(gameData, giver, receiver).Select(p => p.id).ToList() : new();
			offer.contacts.RemoveAll(c => !introductions.Contains(c));
			List<string> resources = Trading.Tradeable(gameData, giver).Select(r => r.Key).ToList();
			offer.resources.RemoveAll(r => !resources.Contains(r));
			List<ID> cities = Trading.TradeableCities(giver).Select(c => c.id).ToList();
			offer.cities.RemoveAll(c => !cities.Contains(c));
			List<ID> workers = Trading.TradeableWorkers(giver).Select(w => w.id).ToList();
			offer.workers.RemoveAll(w => !workers.Contains(w));
			List<Agreement> available = Agreements.Available(gameData, giver, receiver);
			offer.agreements.RemoveAll(a => !available.Any(a.SameAs));
			return offer.ToString() != before;
		}

		// The pending deal between `us` and `them` as tables from our side,
		// what's no longer possible taken off (`changed` if anything was).
		public static (TradeOffer weGive, TradeOffer theyGive) Tables(GameData gameData, HumanDeal deal, Player us, Player them, out bool changed) {
			bool ours = deal.from == us.id;
			TradeOffer weGive = (ours ? deal.fromGives : deal.toGives).ToOffer(gameData);
			TradeOffer theyGive = (ours ? deal.toGives : deal.fromGives).ToOffer(gameData);
			changed = Prune(gameData, us, them, weGive) | Prune(gameData, them, us, theyGive);
			return (weGive, theyGive);
		}

		// `us`, a human, answers the deal on the table with `them` (or makes
		// a new one), with the table as it stands on their screen.
		public static void Answer(GameData gameData, Player us, Player them, HumanDealAnswer answer, TradeOffer weGive, TradeOffer theyGive) {
			HumanDeal pending = Between(gameData, us, them);
			if (answer == HumanDealAnswer.Decline) {
				if (pending == null) {
					return;
				}
				gameData.humanDeals.Remove(pending);
				if (pending.to == us.id) {
					new MsgDiplomacyNews(them.id, $"The {us.civilization.noun} have not accepted our offer.", good: false).send();
				}
				return;
			}
			if (!us.isHuman || !them.isHuman || us.defeated || them.defeated) {
				return;
			}
			bool theirsToAnswer = pending != null && pending.to == us.id;
			if (!theirsToAnswer && !CanContact(gameData, us, them)) {
				return;
			}
			Prune(gameData, us, them, weGive);
			Prune(gameData, them, us, theyGive);
			if (weGive.IsEmpty() && theyGive.IsEmpty()) {
				return;
			}

			// They agreed to this very table: it's done.
			if (answer == HumanDealAnswer.Accept && theirsToAnswer && pending.fromAccepted
				&& pending.fromGives.SameAs(DealTerms.Of(theyGive)) && pending.toGives.SameAs(DealTerms.Of(weGive))) {
				gameData.humanDeals.Remove(pending);
				log.Information($"{us} accepts {them}'s deal");
				us.ExecuteDeal(gameData, them, theyGive, weGive);
				new MsgDiplomacyNews(us.id, $"The trade with the {them.civilization.noun} is done.", good: true).send();
				new MsgDiplomacyNews(them.id, $"The {us.civilization.noun} accepted our offer. The trade is done.", good: true).send();
				return;
			}

			// Otherwise it goes to them (a new deal with an envoy).
			bool envoy = pending == null || (pending.from == us.id && pending.envoy);
			if (pending != null) {
				gameData.humanDeals.Remove(pending);
			}
			gameData.humanDeals.Add(new HumanDeal {
				from = us.id,
				to = them.id,
				fromGives = DealTerms.Of(weGive),
				toGives = DealTerms.Of(theyGive),
				fromAccepted = answer == HumanDealAnswer.Accept,
				envoy = envoy,
			});
			log.Information($"{us} sends {them} a deal (agreed: {answer == HumanDealAnswer.Accept})");
			new MsgDiplomacyNews(us.id, $"Our envoy is on the way. The {them.civilization.noun} will see our offer on their turn.", good: true).send();
		}

		// `us` answers the envoy of `them`, who has sent a new deal.
		public static void AnswerEnvoy(GameData gameData, Player us, Player them, EnvoyAnswer answer) {
			HumanDeal pending = Between(gameData, us, them);
			if (pending == null || pending.to != us.id || !pending.envoy) {
				return;
			}
			if (answer == EnvoyAnswer.Hear) {
				pending.envoy = false;
				return;
			}
			gameData.humanDeals.Remove(pending);
			if (answer == EnvoyAnswer.RefuseForAWhile) {
				int turns = RefuseEnvoys(gameData, us, them);
				new MsgDiplomacyNews(them.id, $"The {us.civilization.noun} have refused our envoy, and asked us not to come back for {turns} turns.", good: false).send();
			} else {
				new MsgDiplomacyNews(them.id, $"The {us.civilization.noun} have refused our envoy!", good: false).send();
			}
		}

		// `us` asks `them` not to send envoys for the rules' envoy refusal
		// turns; returns those turns.
		public static int RefuseEnvoys(GameData gameData, Player us, Player them) {
			int turns = gameData.rules.EnvoyRefusalTurns;
			us.EnsureRelationshipExists(them);
			us.playerRelationships[them.id].refuseEnvoysUntilTurn = gameData.turn + turns;
			return turns;
		}

		// At the start of `human`'s turn, the deals other humans have sent
		// them, each after its envoy is heard.
		public static async Task HearFromHumans(GameData gameData, Player human) {
			foreach (HumanDeal deal in gameData.humanDeals.Where(d => d.to == human.id).ToList()) {
				Player them = gameData.GetPlayer(deal.from);
				if (!gameData.humanDeals.Contains(deal)) {
					continue;
				}
				if (them == null || !them.isHuman || them.defeated) {
					gameData.humanDeals.Remove(deal);
					continue;
				}
				if (human.defeated || gameData.gameOver) {
					break;
				}
				if (deal.envoy) {
					new MsgEnvoyArrives(human.id, them.id).send();
					await AnswerFrom<MsgEnvoyAnswer>(gameData, human);
					if (!gameData.humanDeals.Contains(deal) || deal.envoy) {
						continue;
					}
				}
				new MsgShowHumanDeal(human.id, them.id).send();
				await AnswerFrom<MsgDiplomacyCompleted>(gameData, human);
			}
		}

		// (With simultaneous moves, the others play on while they answer.)
		private static Task<T> AnswerFrom<T>(GameData gameData, Player human) where T : MessageToEngine {
			return gameData.simultaneousMoves ? EngineStorage.WaitForAnswerFrom<T>(human.id) : EngineStorage.WaitForMessageToEngine<T>();
		}

		// Online, live (Civ3's multiplayer diplomacy, LiveTalks): the table
		// both humans accepted, what's no longer possible taken off first;
		// false if nothing is left or they can't deal now.
		public static bool TradeAtTheTable(GameData gameData, Player a, Player b, TradeOffer aGives, TradeOffer bGives) {
			if (!a.isHuman || !b.isHuman || a.defeated || b.defeated || a == b || !a.playerRelationships.ContainsKey(b.id)) {
				return false;
			}
			Prune(gameData, a, b, aGives);
			Prune(gameData, b, a, bGives);
			// (A line in the two players' chats, not a popup: they saw it
			// done at the table.)
			if (aGives.IsEmpty() && bGives.IsEmpty()) {
				new MsgDiplomacyNews(a.id, $"The trade with the {b.civilization.noun} fell through: nothing on the table can still be given.", good: false) { inChat = true }.send();
				new MsgDiplomacyNews(b.id, $"The trade with the {a.civilization.noun} fell through: nothing on the table can still be given.", good: false) { inChat = true }.send();
				return false;
			}
			log.Information($"{a} and {b} make a deal at the table");
			a.ExecuteDeal(gameData, b, bGives, aGives);
			new MsgDiplomacyNews(a.id, $"The trade with the {b.civilization.noun} is done.", good: true) { inChat = true }.send();
			new MsgDiplomacyNews(b.id, $"The trade with the {a.civilization.noun} is done.", good: true) { inChat = true }.send();
			return true;
		}

		// The deals of a player who is no longer human (or no longer plays).
		public static void Forget(GameData gameData, Player player) {
			gameData.humanDeals.RemoveAll(d => d.from == player.id || d.to == player.id);
		}
	}
}
