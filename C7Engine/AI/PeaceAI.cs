using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using Serilog;
using static C7GameData.PlayerRelationship;

namespace C7Engine.AI {
	// How an AI weighs up making peace, and when it asks for it.
	//
	// What Civ3 players report of its AI, which this follows loosely:
	// - It won't talk at first: not until the other side has hurt it ("You
	//   defeat a few of their troops. You take a couple of cities. Atleast
	//   3-5 turns", Chieftess), and "the AI seems to respond to threat more
	//   than to losses" (rcoutme)
	//   (https://forums.civfanatics.com/threads/when-will-ai-sue-for-peace.85975/).
	//   The refusal to talk is PlayerRelationship.refuseContactUntilTurn.
	// - It gives up cities only in a peace treaty, never in ordinary trade:
	//   "You can now only 'buy' cities by demanding them in tribute as part
	//   of a peace treaty. You cannot sell your cities to the AI, for any
	//   price" (Padma), and "the only time I ever managed to get a city is
	//   when I am dominating them in a battle" (Virtual Alex)
	//   (https://forums.civfanatics.com/threads/trading-cities.177929/).
	//
	// Civ3's actual formulas aren't known, so everything below (the
	// strength ratios, the budgets, the chances) is OUR AI HEURISTIC, not a
	// Civ3 rule. Per the project owner, the AI gives cities no value in any
	// deal, never trades its cities away outside a peace treaty, and in a
	// peace treaty may give up cities when losing or demand them when
	// winning.
	public static class PeaceAI {
		private static ILogger log = Log.ForContext(typeof(PeaceAI));

		public enum WarOutlook {
			LosingBadly,
			Losing,
			Even,
			Winning,
			Dominating,
		}

		// HEURISTIC: how much stronger (by Player.CalculateMilitaryStrength,
		// the military advisor's measure) one side must be to be winning,
		// or dominating, the war.
		public const float WinningStrengthRatio = 1.5f;
		public const float DominatingStrengthRatio = 3f;

		// HEURISTIC: at these war weariness levels against the enemy (see
		// Player.WarWearinessLevel), the war looks one step worse again.
		public const int WearyLevel = 2;
		public const int VeryWearyLevel = 4;

		// HEURISTIC: what a winning AI wants for peace, in gold equivalent,
		// for each of the other side's cities. No more than a losing AI
		// offers for each of its own (ConcessionPerCityWhenLosing), or the
		// two could never agree: at 30 against 25, a war between a winning
		// and a losing AI went on for ever.
		public const int PeaceTributePerCity = 20;

		// HEURISTIC: what a losing AI will give for peace, in gold
		// equivalent, for each of its own cities.
		public const int ConcessionPerCityWhenLosing = 25;
		public const int ConcessionPerCityWhenLosingBadly = 75;

		// HEURISTIC: an AI asks for peace on some turns, and not again for a
		// while after asking.
		public const int ProposalChancePercent = 50;
		public const int DemandChancePercent = 20;
		public const int ProposalCooldownTurns = 10;

		// HEURISTIC: after this many turns of a war that neither side is
		// losing, both make peace on equal terms (white peace): neither is
		// getting anywhere.
		public const int StalemateTurns = 30;

		// Whether either side has a city on a landmass where the other has
		// one, so their land units can fight. A war across the sea can't be
		// fought by our AI, which has no seaborne invasions, so it ends in
		// white peace.
		public static bool CanFightEachOther(Player us, Player them) {
			// Without cities on both sides there's nothing to judge by:
			// count it as a war that can be fought.
			if (us.cities.Count == 0 || them.cities.Count == 0) {
				return true;
			}
			HashSet<int> ours = us.cities.Select(c => c.location.continent).ToHashSet();
			return them.cities.Any(c => ours.Contains(c.location.continent));
		}

		// How many turns the war with them has gone on. A war from an older
		// save, whose start wasn't recorded, counts from now.
		public static int WarTurns(GameData gameData, Player us, Player them) {
			if (!TryGetRelationship(us, them, out PlayerRelationship view)) {
				return 0;
			}
			if (view.warStartTurn < 0) {
				view.warStartTurn = gameData.turn;
			}
			return gameData.turn - view.warStartTurn;
		}

		// Their military strength together with that of the civs bound to
		// join a war on them (their mutual protection partners, see
		// Pacts.HonourPacts).
		public static float StrengthWithPartners(GameData gameData, Player them) {
			float total = them.CalculateMilitaryStrength();
			foreach (Player p in gameData.players) {
				if (p != them && !p.defeated && TryGetRelationship(them, p, out PlayerRelationship view)
						&& view.multiTurnDeals.Any(d => d.dealSubType == DealSubType.MutualProtectionPact)) {
					total += p.CalculateMilitaryStrength();
				}
			}
			return total;
		}

		// Whether a war on them is one we'd expect to win, and so worth
		// threatening or starting (over tribute, trespassing, or to join an
		// ally): we can reach them, aren't already at war with anyone else
		// (but them), and are WinningStrengthRatio times as strong as they
		// and their partners together. Threats with less behind them led to
		// wars the AI couldn't fight or end.
		public static bool WouldWinWarWith(GameData gameData, Player us, Player them, float extraStrength = 0) {
			if (!CanFightEachOther(us, them)) {
				return false;
			}
			if (gameData.players.Any(p => p != us && p != them && !p.IsIndependent && !p.defeated && AtWar(us, p))) {
				return false;
			}
			return us.CalculateMilitaryStrength() + extraStrength >= WinningStrengthRatio * StrengthWithPartners(gameData, them);
		}

		// Whether we're in a military alliance against them, which making
		// peace with them would break (and ruin our name with the ally, see
		// Pacts.BreakAlliancesAgainst).
		public static bool AlliedAgainst(Player us, Player them) {
			foreach (PlayerRelationship view in us.playerRelationships.Values) {
				if (view.multiTurnDeals.Any(d => d.dealSubType == DealSubType.MilitaryAlliance && d.againstPlayer == them.id)) {
					return true;
				}
			}
			return false;
		}

		// Whether we'd make peace with nothing changing hands: a war we can't
		// fight, or one that has dragged on with neither side losing.
		public static bool WantsWhitePeace(GameData gameData, Player us, Player them, WarOutlook outlook) {
			return outlook == WarOutlook.Even && (!CanFightEachOther(us, them) || WarTurns(gameData, us, them) >= StalemateTurns);
		}

		public static WarOutlook Assess(Player us, Player them) {
			// Neither side can reach the other: nobody is winning.
			if (!CanFightEachOther(us, them)) {
				return WarOutlook.Even;
			}
			float ours = us.CalculateMilitaryStrength();
			float theirs = Math.Max(1f, them.CalculateMilitaryStrength());
			float ratio = ours / theirs;
			WarOutlook outlook = ratio >= DominatingStrengthRatio ? WarOutlook.Dominating
				: ratio >= WinningStrengthRatio ? WarOutlook.Winning
				: ratio * WinningStrengthRatio > 1f ? WarOutlook.Even
				: ratio * DominatingStrengthRatio > 1f ? WarOutlook.Losing
				: WarOutlook.LosingBadly;

			// A people tired of the war push for peace.
			int weariness = Player.WarWearinessLevel(us.WarWearinessPointsAgainst(them));
			if (weariness >= WearyLevel && outlook > WarOutlook.LosingBadly) {
				--outlook;
			}
			if (weariness >= VeryWearyLevel && outlook > WarOutlook.LosingBadly) {
				--outlook;
			}
			return outlook;
		}

		// How a war looks to us when weighing up peace. Civ3: "The AI's
		// willingness to discuss peace terms depends on a) how long the war
		// has lasted and b) how much it is winning or losing the war. In
		// other words, the longer the war has lasted and the more
		// battles/cities/whatever the AI has lost relative to you, the more
		// willing it will be to go to the negotiating table" (Soren Johnson,
		// Firaxis, https://codehappy.net/apolyton/threads/41866-1.htm #27).
		// So, unlike Assess:
		// - winning or losing is judged by what each side has lost in this
		//   war (see WarLosses), the armies' sizes only breaking a tie or
		//   standing in before there has been much fighting;
		// - the war happiness we were given when they declared war doesn't
		//   count as part of our people's weariness (owner, 2026-10-31);
		// - each LongWarTurns of war takes a winning side's outlook a step
		//   down, so a long war ends in peace on plain terms.
		// How the two factors combine isn't given; the sizes are ours.
		public static WarOutlook PeaceOutlook(GameData gameData, Player us, Player them) {
			if (!CanFightEachOther(us, them)) {
				return WarOutlook.Even;
			}
			WarOutlook army = ByRatio(us.CalculateMilitaryStrength() / Math.Max(1f, them.CalculateMilitaryStrength()));
			int ours = WarLosses(us, them), theirs = WarLosses(them, us);
			WarOutlook outlook;
			if (ours + theirs >= MinWarLosses) {
				outlook = ByRatio((theirs + LossSmoothing) / (float)(ours + LossSmoothing));
				// An even fight is tipped only by a far bigger army.
				if (outlook == WarOutlook.Even && army == WarOutlook.Dominating) {
					outlook = WarOutlook.Winning;
				} else if (outlook == WarOutlook.Even && army == WarOutlook.LosingBadly) {
					outlook = WarOutlook.Losing;
				}
			} else {
				outlook = army;
			}

			// A people tired of the war push for peace.
			TryGetRelationship(us, them, out PlayerRelationship view);
			int points = us.WarWearinessPointsAgainst(them) - (view?.warHappinessAtStart ?? 0);
			int weariness = Player.WarWearinessLevel(points);
			if (weariness >= WearyLevel && outlook > WarOutlook.LosingBadly) {
				--outlook;
			}
			if (weariness >= VeryWearyLevel && outlook > WarOutlook.LosingBadly) {
				--outlook;
			}

			// A long war wears down a winner's demands.
			int steps = Math.Min(MaxLongWarSteps, WarTurns(gameData, us, them) / LongWarTurns);
			for (int i = 0; i < steps && outlook > WarOutlook.Even; ++i) {
				--outlook;
			}
			return outlook;
		}

		// UNVERIFIED (our sizes, no Civ3 source gives any): a lost city
		// counts as CityLossWeight lost units; until the two sides have lost
		// MinWarLosses between them, the armies' sizes decide; and
		// LossSmoothing is added to each side's losses before comparing, so
		// a handful of early battles doesn't make a side "dominating".
		public const int CityLossWeight = 4;
		public const int MinWarLosses = 6;
		public const int LossSmoothing = 5;

		// UNVERIFIED (ours): each LongWarTurns of war, up to MaxLongWarSteps
		// times, takes a winning side's outlook a step toward even.
		public const int LongWarTurns = StalemateTurns;
		public const int MaxLongWarSteps = 2;

		// What we have lost to them in the current war (see
		// PlayerRelationship.warUnitsLost).
		public static int WarLosses(Player us, Player them) {
			return TryGetRelationship(us, them, out PlayerRelationship view)
				? view.warUnitsLost + CityLossWeight * view.warCitiesLost : 0;
		}

		private static WarOutlook ByRatio(float ratio) {
			return ratio >= DominatingStrengthRatio ? WarOutlook.Dominating
				: ratio >= WinningStrengthRatio ? WarOutlook.Winning
				: ratio * WinningStrengthRatio > 1f ? WarOutlook.Even
				: ratio * DominatingStrengthRatio > 1f ? WarOutlook.Losing
				: WarOutlook.LosingBadly;
		}

		// How much, in gold equivalent, we'd give for peace.
		public static int ConcessionBudget(Player us, WarOutlook outlook) {
			int perCity = outlook switch {
				WarOutlook.LosingBadly => ConcessionPerCityWhenLosingBadly,
				WarOutlook.Losing => ConcessionPerCityWhenLosing,
				_ => 0,
			};
			return perCity * Math.Max(1, us.cities.Count);
		}

		// How many of our cities we'd give up for peace.
		public static int MaxCitiesToCede(WarOutlook outlook) {
			return outlook == WarOutlook.LosingBadly ? 1 : 0;
		}

		// How much more than we give, in gold equivalent, we want for peace.
		public static int TributeDemanded(Player them, WarOutlook outlook) {
			return outlook >= WarOutlook.Winning ? PeaceTributePerCity * Math.Max(1, them.cities.Count) : 0;
		}

		// The cities a player could give away in a deal (see
		// TradeOffer.ProblemWithDeal): any but its capital, if it has more
		// than one.
		public static List<City> GivableCities(Player player) {
			return TradeOffer.TradableCities(player);
		}

		// The city a player would part with most easily: its smallest, and
		// of those, the furthest from its capital.
		private static City Backwater(Player player) {
			City capital = player.cities.Find(c => c.IsCapital());
			return GivableCities(player)
				.OrderBy(c => c.residents.Count)
				.ThenByDescending(c => capital == null ? 0 : c.location.DistanceTo(capital.location))
				.FirstOrDefault();
		}

		// Whether we, an AI at war with them, would make peace on these
		// terms, where they give theirOffer and we give ourOffer, worth the
		// given gold equivalents to us (cities count for nothing).
		public static bool WouldAcceptPeace(GameData gameData, Player us, Player them, TradeOffer theirOffer, TradeOffer ourOffer,
				int theirValue, int ourValue) {
			// Total war: no peace until the victim is dead.
			if (TotalWar.RefusesPeace(us, them)) {
				return false;
			}
			WarOutlook outlook = PeaceOutlook(gameData, us, them);
			if (ourOffer.cities.Count > MaxCitiesToCede(outlook)) {
				return false;
			}
			// Not while allied against them, unless losing badly.
			if (outlook > WarOutlook.LosingBadly && AlliedAgainst(us, them)) {
				return false;
			}
			// On trial: an AI whose army is on the march against them, and
			// that isn't losing, fights on unless offered a city.
			if (AiExperiments.Has(us, "war-no-peace") && outlook >= WarOutlook.Even && theirOffer.cities.Count == 0
				&& WarCampaign.HasCampaignAgainst(us, them)) {
				return false;
			}
			// Switch horde-war: a horde that isn't losing makes peace only
			// for a city (see HordeWar).
			if (HordeWar.RefusesPeace(us, them, outlook, theirOffer)) {
				return false;
			}
			// Dominating, a city of theirs is our price, whatever else is in
			// the deal; failing that, tribute.
			if (outlook == WarOutlook.Dominating && theirOffer.cities.Count > 0) {
				return theirValue >= ourValue;
			}
			return theirValue - ourValue >= TributeDemanded(them, outlook) - ConcessionBudget(us, outlook);
		}

		// The peace we'd propose to them, as what we give and what we want,
		// or null if we wouldn't propose one: losing, we offer techs and
		// (losing badly) a city; dominating, we demand a city.
		public static (TradeOffer weGive, TradeOffer weWant)? BuildPeaceOffer(GameData gameData, Player us, Player them) {
			if (TotalWar.RefusesPeace(us, them)) {
				return null;
			}
			WarOutlook outlook = PeaceOutlook(gameData, us, them);
			TradeOffer weGive = new() { partOfPeaceTreaty = true };
			TradeOffer weWant = new() { partOfPeaceTreaty = true };
			// Kings & Nomads: a war for a goal that has been met ends in a
			// white peace, unless we're losing (see KingsAndNomadsAI.WarGoalMet).
			if (outlook >= WarOutlook.Even && KingsAndNomadsAI.WarGoalMet(gameData, us, them)) {
				return (weGive, weWant);
			}
			switch (outlook) {
				case WarOutlook.Losing:
				case WarOutlook.LosingBadly: {
						int budget = ConcessionBudget(us, outlook);
						List<Tech> techs = us.GetTechsTradableTo(them, gameData.techs);
						foreach (Tech t in techs.OrderByDescending(t => gameData.TechCostFor(t, us))) {
							int cost = gameData.TechCostFor(t, us);
							if (cost <= budget) {
								weGive.techs.Add(t);
								budget -= cost;
							}
						}
						if (MaxCitiesToCede(outlook) > 0 && Backwater(us) is City city) {
							weGive.cities.Add(city);
						}
						break;
					}
				case WarOutlook.Dominating: {
						if (Backwater(them) is not City city) {
							return null;
						}
						weWant.cities.Add(city);
						break;
					}
				case WarOutlook.Even:
					// White peace, for a war that can't be fought or is
					// going nowhere.
					if (!WantsWhitePeace(gameData, us, them, outlook)) {
						return null;
					}
					break;
				default:
					return null;
			}
			return (weGive, weWant);
		}

		// Our answer, as an AI, when a human at war with us asks what we'd
		// want for peace, as Civ3's deal screen lets them ("What do you want
		// for $GIVE0?", Conquests diplomacy.txt #USERASKFORCOUNTER): terms we
		// would accept, as what the human gives and what we give, or null if
		// there are none ("Regrettably, we don't see how a deal could be
		// worked out...", #AINOCOUNTERPEACE). They start from the peace we'd
		// offer (BuildPeaceOffer), or plain peace; if that isn't enough we
		// ask for the least gold that is, and failing that all their gold
		// and their techs, cheapest first, until it is.
		public static (TradeOffer humanGives, TradeOffer humanWants)? PeaceTermsFor(GameData gameData, Player ai, Player human) {
			if (ai.isHuman || !AtWar(ai, human) || gameData.AreInLockedWar(ai, human)
				|| !ai.WillAcceptCommunicationFrom(human, gameData.turn)) {
				return null;
			}
			(TradeOffer weGive, TradeOffer weWant) = BuildPeaceOffer(gameData, ai, human)
				?? (new TradeOffer { partOfPeaceTreaty = true }, new TradeOffer { partOfPeaceTreaty = true });
			bool Accepts() => ai.WouldAcceptDealFrom(gameData, human, weWant, weGive);
			if (Accepts()) {
				return (weWant, weGive);
			}
			int baseGold = weWant.gold ?? 0;
			int spare = human.gold - baseGold;
			if (spare > 0) {
				weWant.gold = baseGold + spare;
				if (Accepts()) {
					// The least that will do.
					int low = 0, high = spare;
					while (high - low > 1) {
						int mid = (low + high) / 2;
						weWant.gold = baseGold + mid;
						if (Accepts()) {
							high = mid;
						} else {
							low = mid;
						}
					}
					weWant.gold = baseGold + high;
					return (weWant, weGive);
				}
			}
			foreach (Tech t in human.GetTechsTradableTo(ai, gameData.techs).OrderBy(t => gameData.TechCostFor(t, human))) {
				weWant.techs.Add(t);
				if (Accepts()) {
					return (weWant, weGive);
				}
			}
			return null;
		}

		// Asks the civs we're at war with for peace, if the war is going
		// badly for us (or demands a city for it, if it's going very well).
		// A human is shown the offer and may accept it on the deal screen.
		// Playing by email, a human hears the offers at the start of their
		// turn, onlyWith them (see PlayByEmail.HearFromAIs).
		public static async Task ProposePeace(Player us, GameData gameData, Player onlyWith = null) {
			if (us.isHuman || us.IsIndependent) {
				return;
			}
			foreach (Player them in gameData.players.ToList()) {
				if (onlyWith != null && them != onlyWith) {
					continue;
				}
				// A human who can't answer now (a LAN guest who has left, or a
				// human away playing by email) isn't asked, and the offer
				// isn't spent on them.
				if (them.isHuman && !EngineStorage.IsPlayerReachable(them.id)) {
					continue;
				}
				if (them == us || them.IsIndependent || them.defeated
					|| !TryGetRelationship(us, them, out PlayerRelationship ourView) || !ourView.AtWar()
					|| gameData.AreInLockedWar(us, them)) {
					continue;
				}
				if (ourView.lastPeaceProposalTurn >= 0 && gameData.turn - ourView.lastPeaceProposalTurn < ProposalCooldownTurns) {
					continue;
				}
				// Not while allied against them, unless losing badly.
				if (AlliedAgainst(us, them) && PeaceOutlook(gameData, us, them) > WarOutlook.LosingBadly) {
					continue;
				}
				// Both sides must be willing to talk.
				if (!us.WillAcceptCommunicationFrom(them, gameData.turn)
					|| (!them.isHuman && !them.WillAcceptCommunicationFrom(us, gameData.turn))) {
					continue;
				}
				(TradeOffer weGive, TradeOffer weWant)? offer = BuildPeaceOffer(gameData, us, them);
				if (offer == null) {
					continue;
				}
				(TradeOffer weGive, TradeOffer weWant) = offer.Value;
				int chance = weWant.cities.Count > 0 ? DemandChancePercent : ProposalChancePercent;
				if (GameData.rng.Next(100) >= chance) {
					continue;
				}
				if (TradeOffer.ProblemWithDeal(gameData, us, them, weGive, weWant) != null) {
					continue;
				}
				ourView.lastPeaceProposalTurn = gameData.turn;
				log.Information("{Us} proposes peace to {Them}, giving {Give} for {Want}", us, them, weGive, weWant);

				if (them.isHuman) {
					// A LAN guest who has left can't answer, so isn't asked.
					if (!EngineStorage.IsPlayerReachable(them.id)) {
						continue;
					}
					// The human receiving the offer takes the UI to respond.
					if (EngineStorage.uiFollowsActivePlayer) {
						EngineStorage.uiControllerID = them.id;
					}
					EngineStorage.diplomacyPlayerID = them.id;
					EngineStorage.diplomacyAIPlayerID = us.id;
					new MsgShowTradeOffer(us, them, weWant, weGive).send();
					await EngineStorage.WaitForDiplomacyCompleted(them.id);
					EngineStorage.diplomacyPlayerID = null;
					EngineStorage.diplomacyAIPlayerID = null;
				} else if (them.WouldAcceptDealFrom(gameData, us, weGive, weWant)) {
					us.ExecuteDeal(gameData, them, weWant, weGive);
				}
			}
		}
	}
}
