using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData;

// A class holding all the state of the relationship between two civs.
// If a relationship between 2 active civs doesn't exist it means that they haven't met yet.
// If a relationship exists but doesn't have any multi-turn deals on either side,
// it means that these 2 civs are at war, because Peace itself is a multi-turn deal.
// A civ can't (and shouldn't) have a relationship with themselves, barbarians or defeated players.
//
// Another important detail is that, a PlayerRelationship is a two part relationship,
// or a two-way relationship if it's easier to think about it like that.
// Therefore, a multiturn deal exists in both player's relationship info.
// If a deal is removed from player's 1 multiTurnDeals list, it still very much exists in player's 2 list,
// unless explicitly removed.
public class PlayerRelationship {
	private static ILogger log = Log.ForContext<PlayerRelationship>();

	// p1.playerRelationships[p2].warDeclarationCount is the number of times
	// p2 declared war on p1. (Civ3's trading reputation is brokeTradeDeal,
	// below; the right of passage one is rightOfPassageBetrayals.)
	public int warDeclarationCount = 0;

	// p1.playerRelationships[p2].warDeclarationWithRoPActiveCount is the number of times
	// p2 declared war on p1 while having an active RoP.
	public int warDeclarationWithRoPActiveCount = 0;

	// p1.playerRelationships[p2].brokeTradeDeal is true if p2 broke a deal
	// with p1 that counts against p2's trading reputation, after which p1
	// and the civs that know p1 won't make such deals with p2 again (see
	// TradingReputationBroken). False in saves made before it was recorded.
	public bool brokeTradeDeal = false;

	// p1.playerRelationships[p2].rightOfPassageBetrayals is the number of
	// those times p2 had units inside p1's borders: a sneak attack, after
	// which "other civs won't sign ROP with you anymore"
	// (https://civfanatics.com/civ3/strategy/game-mechanics/ai-attitude-study/;
	// see RightOfPassage.HasBetrayed).
	public int rightOfPassageBetrayals = 0;

	// p1.playerRelationships[p2].nuclearAtrocityCount is the number of
	// nuclear attacks p2 has made (on anyone) since p1 knew them. Civ3
	// treats using nuclear weapons as an atrocity every civ resents.
	// nuclearAttacksSuffered is how many of them hit p1 (see
	// RecordNuclearAttack and Attitudes.Points).
	public int nuclearAtrocityCount = 0;
	public int nuclearAttacksSuffered = 0;

	// true if a war declaration happened with units inside the player's
	// borders.
	public bool wasSneakAttacked = false;

	// If at war, refuse contact with the relevant player until this turn
	// has been reached.
	public int refuseContactUntilTurn = -1;

	public List<MultiTurnDeal> multiTurnDeals = new List<MultiTurnDeal>();

	public bool declaredWarWithActiveRightOfPassage = false;

	// p1.playerRelationships[p2].lastWithdrawalDemandTurn is the turn p1
	// last told p2 to take its units out of p1's territory, or -1 if never.
	public int lastWithdrawalDemandTurn = -1;

	// p1.playerRelationships[p2].recentWithdrawals is how many times in a
	// row p2 agreed to leave p1's territory and then came back before p1
	// forgot about it (see TerritoryDemands).
	public int recentWithdrawals = 0;

	// The turn p1 and p2 first met, or -1 if it isn't known (relationships
	// from saves made before it was recorded count as long ago).
	public int firstContactTurn = -1;

	// p1.playerRelationships[p2].lastTributeDemandTurn is the turn p1 last
	// demanded tribute from p2, or -1 if never (see TributeDemands).
	public int lastTributeDemandTurn = -1;

	// What p2 did that changes p1's attitude toward it (see Attitudes): the
	// last turn p2 paid p1 tribute, how many times p2 refused p1's demand
	// for tribute without p1 going to war, and how many of p1's workers p2
	// captured, or -1 and 0 if never.
	public int lastTributePaidTurn = -1;
	public int tributeBluffsCalled = 0;
	public int workersCaptured = 0;

	// The last turn p1 and p2 traded techs or maps, or -1 if never (the
	// name is older than maps counting).
	public int lastTechTradeTurn = -1;

	// The gold worth of p2's gifts to p1 (gold or techs given for nothing),
	// as it stood on giftTurn, the turn of the last; p1 forgets it bit by
	// bit (see Attitudes.RememberedGiftGold).
	public int giftGold = 0;
	public int giftTurn = -1;

	// p1.playerRelationships[p2].loneTrespasserSinceTurn is the turn p2
	// started keeping a single combat unit in p1's territory, which p1
	// puts up with for loneTrespasserPatience turns, or -1 if it isn't.
	public int loneTrespasserSinceTurn = -1;
	public int loneTrespasserPatience = 0;

	// p1.playerRelationships[p2].otherStartedCurrentWar is true if p2
	// declared the war p1 and p2 are currently fighting, false if p1 did,
	// and null if unknown (no war since this was recorded, as in saves made
	// before it was, or wars that came from a scenario).
	public bool? otherStartedCurrentWar = null;

	// p1.playerRelationships[p2].warWearinessPoints is how weary p1's
	// people are of fighting p2 (see Player.UpdateWarWeariness). Below zero
	// it is war happiness. It lingers after peace, fading each turn.
	public int warWearinessPoints = 0;

	// p1.playerRelationships[p2].lastPeaceProposalTurn is the turn p1, an
	// AI, last proposed peace to p2, or -1 if never (see PeaceAI).
	public int lastPeaceProposalTurn = -1;

	// p1.playerRelationships[p2].warStartTurn is the turn the current war
	// between them began, or -1 (at peace, or a war from a save made before
	// this was recorded).
	public int warStartTurn = -1;

	// p1.playerRelationships[p2].warUnitsLost and warCitiesLost are how
	// many units p1 has lost in battle to p2, and cities p2 has taken from
	// it, in the current war (0 at peace, and in saves made before this was
	// recorded). The AI judges a war by them (see PeaceAI.PeaceOutlook).
	public int warUnitsLost = 0;
	public int warCitiesLost = 0;

	// p1.playerRelationships[p2].warHappinessAtStart is the war weariness
	// points p1 was given when p2 declared the current war on it
	// (Player.WarWearinessWhenWarIsDeclaredOnUs, negative), or 0, so the
	// peace judgement can leave them out (see PeaceAI.PeaceOutlook).
	public int warHappinessAtStart = 0;

	// p1.playerRelationships[p2].peaceSignedTurn is the turn p1 and p2 last
	// made peace after a war, or -1 if they never have (or the save predates
	// this). The AI plots no war on p2 for a while after (see
	// StockWarPlotting.Excluded).
	public int peaceSignedTurn = -1;

	// p1.playerRelationships[p2].lastDealOfferTurn is the turn p1, an AI,
	// last offered p2 a deal of its own accord, or -1.
	public int lastDealOfferTurn = -1;

	// p1.playerRelationships[p2].hasEmbassy is true if p1 has an embassy in
	// p2's capital, which diplomatic missions against p2 need. War closes it
	// (see DeclareWar).
	public bool hasEmbassy = false;

	// p1.playerRelationships[p2].embassyClosedByWar is true while p1's
	// embassy in p2's capital is closed by a war between them; peace opens
	// it again (see SignPeaceAfterWar). False in saves made before it was
	// recorded, whose closed embassies stay closed.
	public bool embassyClosedByWar = false;

	// What p1's embassy reported of p2's capital when it was established,
	// or null if p1 has no embassy or it came from a scenario.
	public Espionage.CityReport embassyReport = null;

	// The embassyReport of an embassy closed by war, kept until peace opens
	// it again with the report it had (see ReopenEmbassy), or null.
	public Espionage.CityReport closedEmbassyReport = null;

	// p1.playerRelationships[p2].hasSpy is true if p1 has planted a spy in
	// p2's capital (needs the Intelligence Agency), which espionage missions
	// against p2 need.
	public bool hasSpy = false;

	// p1.playerRelationships[p2].spyPlantFailed is true after p1 failed to
	// plant a spy in p2's capital, until it clears (a 1 in 3 chance each
	// turn); p1 can't try again until then (see Espionage).
	public bool spyPlantFailed = false;

	// p1.playerRelationships[p2].espionageIncidents is the number of p2's
	// diplomatic or espionage missions against p1 that were caught.
	public int espionageIncidents = 0;

	// A copy sharing its deals and report with this one, for a LAN host to
	// change a little for one guest (see C7Engine.Network.SnapshotFilter).
	internal PlayerRelationship ShallowCopy() {
		return (PlayerRelationship)MemberwiseClone();
	}

	public bool AtWar() {
		return multiTurnDeals.Count == 0;
	}

	/// <summary>
	/// Returns <b>true</b>, and the left's player relationship to the right player, if it exists.<br/>
	/// Otherwise returns <b>false</b>.<br/>
	/// If you want the opposite relationship, flip the arguments when calling the method.
	/// </summary>
	/// <param name="left"></param>
	/// <param name="right"></param>
	/// <param name="relationship"></param>
	/// <returns></returns>
	public static bool TryGetRelationship(Player left, Player right, out PlayerRelationship relationship) {
		relationship = null;

		if (left == null || right == null) return false;
		if (left.id == right.id) return false;
		if (left.IsIndependent || right.IsIndependent) return false;
		if (left.defeated || right.defeated) return false;
		if (!left.playerRelationships.TryGetValue(right.id, out var pr)) return false;

		relationship = pr;
		return true;
	}

	public static bool AtWar(Player left, Player right) {
		// The barbarians and the city-states are at war with every civ, and
		// at peace with each other.
		if (left.IsIndependent != right.IsIndependent) return true;

		if (TryGetRelationship(left, right, out var relationship) && relationship.AtWar()) {
			return true;
		}

		return false;
	}

	public static bool AtPeace(Player left, Player right) {
		return !AtWar(left, right);
	}

	/// <summary>
	/// Returns true if the player is at war with any of the other players/AI except the barbarians.
	/// </summary>
	/// <param name="player"></param>
	/// <param name="players"></param>
	/// <returns></returns>
	public static bool IsInAnyWar(Player player, List<Player> players) {
		if (players.Any(other => !other.IsIndependent && AtWar(player, other))) {
			return true;
		}

		return false;
	}

	public static bool HaveActiveRightOfPassage(Player left, Player right) {
		return TryGetRelationship(left, right, out var pr) &&
			   pr.multiTurnDeals.Any(d => d.dealSubType == DealSubType.RightOfPassage);
	}

	// Breaks peace and all other multiturn deals when war is declared.
	// refuseContactUntilTurn is the (absolute) turn until which the defender
	// refuses to talk; currentTurn defaults to the game's. Unless
	// honourPacts is false, the defender's mutual protection partners join
	// the war (see Pacts.HonourPacts). keepingPact is true for a partner
	// joining that way, which keeps its word rather than breaking it.
	public static void DeclareWar(Player aggressor, Player defender, bool sneakAttack, int refuseContactUntilTurn, int? currentTurn = null,
			bool honourPacts = true, bool keepingPact = false) {
		// Civs in the same locked alliance, such as teammates, "can _never_
		// attack one another" (see MsgDeclareWar). Everything that declares
		// war checks first; this makes sure.
		if (EngineStorage.gameData?.AreInLockedPeace(aggressor, defender) == true) {
			Serilog.Log.Warning("{Aggressor} can't declare war on {Defender}, a locked ally", aggressor, defender);
			return;
		}
		var defenderRelationshipToAggressor = defender.playerRelationships[aggressor.id];
		var aggressorRelationshipToDefender = aggressor.playerRelationships[defender.id];
		// increment the times the aggressor has declared war on the defender
		defenderRelationshipToAggressor.warDeclarationCount++;

		// increment the times the aggressor has declared war on the defender while there is an active RoP
		if (HaveActiveRightOfPassage(aggressor, defender)) {
			defenderRelationshipToAggressor.warDeclarationWithRoPActiveCount++;
			// The aggressor's units, let in by the agreement, are still
			// inside: Civ3 leaves them there to attack from (see
			// RightOfPassage), but nobody trusts it with one again.
			if (sneakAttack) {
				defenderRelationshipToAggressor.rightOfPassageBetrayals++;
			}
		} else if (sneakAttack) {
			// A sneak attack breaks a right of passage even without one:
			// "Reputation hits come not because of war, but because of
			// declaring war with troops in enemy territory" (Catt), and
			// "having a Scout/worker/any other unit in enemy territory when
			// declaring war is enough to be a ROP-breaker" (Lt. 'Killer' M.,
			// https://forums.civfanatics.com/threads/reputation-for-sneak-attacks.28206).
			// So it counts as a right of passage broken with units inside,
			// the study's +6 (see Attitudes.Points).
			defenderRelationshipToAggressor.warDeclarationWithRoPActiveCount++;
			defenderRelationshipToAggressor.rightOfPassageBetrayals++;
		}

		// Breaking a deal the aggressor keeps paying for, or a pact, ruins
		// its trading reputation with the defender (see BreaksReputation).
		// So does breaking the peace treaty itself (BreaksPeaceTreaty).
		// Either way it is the one mark (brokeTradeDeal), so a war that
		// breaks both counts once.
		if (aggressorRelationshipToDefender.multiTurnDeals.Any(BreaksReputation)
			|| (!keepingPact && BreaksPeaceTreaty(aggressorRelationshipToDefender))) {
			defenderRelationshipToAggressor.brokeTradeDeal = true;
		}

		// update whether the defender was sneak attacked
		defenderRelationshipToAggressor.wasSneakAttacked = sneakAttack;

		// record who started this war, and when
		defenderRelationshipToAggressor.otherStartedCurrentWar = true;
		aggressorRelationshipToDefender.otherStartedCurrentWar = false;
		int warTurn = currentTurn ?? EngineStorage.gameData?.turn ?? 0;
		defenderRelationshipToAggressor.warStartTurn = warTurn;
		aggressorRelationshipToDefender.warStartTurn = warTurn;
		ResetWarRecord(defenderRelationshipToAggressor);
		ResetWarRecord(aggressorRelationshipToDefender);

		// Set for how many turns the defender will refuse contact from the aggressor
		defenderRelationshipToAggressor.refuseContactUntilTurn = refuseContactUntilTurn;

		// TODO: Figure out a better formula to calculate the aggressor's refusal in turns.
		// Right now the aggressor refuses contact for half as many turns as
		// the defender.
		//
		// The thinking is that if AI attacks a human, the human as a defender
		// might try to talk to the AI aggressor earlier than an AI in it's place is programmed to do.
		int turn = currentTurn ?? EngineStorage.gameData?.turn ?? 0;
		aggressorRelationshipToDefender.refuseContactUntilTurn = turn + Math.Max(0, refuseContactUntilTurn - turn) / 2;

		// Finally clear all multi-turn deals, including Peace, which is how we actually declare war
		aggressorRelationshipToDefender.multiTurnDeals = new List<MultiTurnDeal>();
		defenderRelationshipToAggressor.multiTurnDeals = new List<MultiTurnDeal>();

		// War closes both civs' embassies: "your embassy in their capitol is
		// automatically closed", while "Even if you go to war and your
		// embassy is closed, your spy will continue to function as long as
		// it is not caught" (Civ3.com FAQ, quoted at
		// https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/41397-espionage-you-can-t-steal-techs-from-civs-you-are-at-war-with).
		// So the planted spies stay. Peace opens the embassy again (see
		// SignPeaceAfterWar).
		CloseEmbassy(aggressorRelationshipToDefender);
		CloseEmbassy(defenderRelationshipToAggressor);

		log.Information("{Aggressor} declared war on {Defender}{SneakAttack}!" +
						" Defender is refusing contact for at least up to turn {RefuseContactUntilTurn}" +
						" ({Turns} turns)!",
						aggressor, defender, sneakAttack ? " in a sneak attack" : "",
						refuseContactUntilTurn, refuseContactUntilTurn - EngineStorage.gameData.turn);

		if (honourPacts) {
			Pacts.HonourPacts(EngineStorage.gameData, aggressor, defender, refuseContactUntilTurn, turn);
		}
	}

	// Records a nuclear attack by the attacker that hit the victims, in
	// the view of every civ that knows the attacker: "Nukes: Victim of the
	// nuke adds +32!" and "Other civs will add on +16" (AI Attitude Study,
	// https://civfanatics.com/civ3/strategy/game-mechanics/ai-attitude-study/).
	public static void RecordNuclearAttack(GameData gameData, Player attacker, IEnumerable<Player> victims) {
		if (attacker == null || attacker.IsIndependent) {
			return;
		}
		HashSet<Player> hit = new(victims ?? []);
		foreach (Player other in gameData.players) {
			if (other == attacker || other.IsIndependent || other.defeated) {
				continue;
			}
			if (other.playerRelationships.TryGetValue(attacker.id, out PlayerRelationship relationship)) {
				++relationship.nuclearAtrocityCount;
				if (hit.Contains(other)) {
					++relationship.nuclearAttacksSuffered;
				}
			}
		}
	}

	private static void CloseEmbassy(PlayerRelationship relationship) {
		if (relationship.hasEmbassy) {
			relationship.embassyClosedByWar = true;
			relationship.closedEmbassyReport = relationship.embassyReport;
		}
		relationship.hasEmbassy = false;
		relationship.embassyReport = null;
	}

	// An embassy reopened at peace has the capital report it had when it
	// was founded, kept while the war closed it, rather than a new one
	// (owner's decision, 2026-10-21): it was never established again, so
	// it makes no new investigation of the capital (see Espionage,
	// EstablishEmbassy).
	private static void ReopenEmbassy(PlayerRelationship relationship) {
		if (relationship.embassyClosedByWar) {
			relationship.hasEmbassy = true;
			relationship.embassyClosedByWar = false;
			relationship.embassyReport = relationship.closedEmbassyReport;
		}
		relationship.closedEmbassyReport = null;
	}

	// Civ3's trading reputation. A civ that breaks a deal with an AI is
	// never again trusted with deals it pays for over time: "if you are the
	// aggressor and break a 20-turn deal, ... the AIs will never again for
	// the game accept per-turn payments or deals from you", which doesn't
	// count when the AI declares war on you
	// (https://civfanatics.com/civ3/faq/). Per
	// https://civfanatics.com/civ3/strategy/game-mechanics/trading-reputation/:
	// - The deals at risk are those in which "You pay per-turn (resources,
	//   gpt, RoP) goods", "You export resources to the AI", or "MA/MPP is
	//   involved" (see BreaksReputation). A right of passage "has its own
	//   separate reputation system" (rightOfPassageBetrayals).
	// - They are broken by declaring war on the AI, by no longer being able
	//   to export a resource ("the resources are lost, depleted or
	//   disconnected", "the trade route between you and the AI is cut"; see
	//   CancelBrokenResourceDeals), or by breaking a military alliance or
	//   mutual protection pact (see SignPeaceAfterWar).
	// - "Each AI has a record of your reputation separately": an AI holds
	//   it against you if "you had a gpt deal with this AI before which got
	//   terminated prematurely" or "the AI knows or knew another AI with
	//   whom you had a broken gpt deal, and those two AI are NOT currently
	//   at war". (Civs that have met are taken to know each other; a
	//   relationship is never forgotten here.)
	// The AI then refuses such deals (Player.WouldAcceptDealFrom).
	public static bool BreaksReputation(MultiTurnDeal deal) {
		if (deal == null) {
			return false;
		}
		return deal.dealSubType switch {
			DealSubType.GoldPerTurn or DealSubType.ResourcePerTurn or DealSubType.LuxuryPerTurn
				=> deal.dealDetails == DealDetails.Outbound,
			DealSubType.MutualProtectionPact or DealSubType.MilitaryAlliance => true,
			_ => false,
		};
	}

	// Whether declaring war breaks a peace treaty, ruining the aggressor's
	// trading reputation with the defender, and so with the civs that hear
	// of it (TradingReputationBroken) (owner's decision, 2026-10-21).
	// Every pair at peace has a treaty, from first contact: "Unless we
	// declare war against the Germans, we'll come out of this encounter
	// with an automatic peace treaty with them" (Civ3 manual, p.~51-52).
	// Whether Civ3 counts this against reputation, or only attitude, is
	// disputed (compendium 06, Disputed, "Does breaking a peace treaty hurt
	// reputation?"): microbe says "there is no such a thing" as
	// peace-treaty reputation, while Smart says "You cant breake peace
	// treaty without a rep hit", and nullspace's C3C 1.22 test found a war
	// that "breaks a 20-turn peace treaty, and breaks no other deals" still
	// ruined the aggressor's reputation
	// (https://forums.civfanatics.com/threads/trading-reputation.113376/page-2);
	// "The rep hit goes to whoever actually does the declaration of war"
	// (Bamspeedy,
	// https://forums.civfanatics.com/threads/how-does-the-ai-calculate-reputation.26122).
	// The treaty's age doesn't matter: unlike Taé Shala's "if you break the
	// treaty within the remaining turns" (compendium 06.5), no term is kept
	// after which breaking it is safe. The attitude penalty for breaking a
	// peace treaty is the separate warDeclarationCount (see Attitudes).
	public static bool BreaksPeaceTreaty(PlayerRelationship aggressorRelationshipToDefender) {
		return aggressorRelationshipToDefender.multiTurnDeals.Any(d => d.dealSubType == DealSubType.Peace);
	}

	// Whether judge holds a broken deal against other (see BreaksReputation).
	public static bool TradingReputationBroken(Player judge, Player other, IEnumerable<Player> players) {
		if (judge == null || other == null || judge == other) {
			return false;
		}
		if (judge.playerRelationships.TryGetValue(other.id, out PlayerRelationship own) && own.brokeTradeDeal) {
			return true;
		}
		foreach (Player third in players) {
			if (third == judge || third == other || third.IsIndependent
				|| !judge.playerRelationships.ContainsKey(third.id)
				|| !third.playerRelationships.TryGetValue(other.id, out PlayerRelationship view) || !view.brokeTradeDeal) {
				continue;
			}
			if (third.defeated || !AtWar(judge, third)) {
				return true;
			}
		}
		return false;
	}

	private static void ResetWarRecord(PlayerRelationship view) {
		view.warUnitsLost = 0;
		view.warCitiesLost = 0;
		view.warHappinessAtStart = 0;
	}

	// Records, for the current war between them, that the loser lost units
	// in battle to the winner, or cities to it. Nothing if they aren't at
	// war (a city taken by a civ at peace, a scenario's units).
	public static void RecordWarLoss(Player loser, Player winner, int units = 0, int cities = 0) {
		if (loser == null || winner == null || loser == winner || loser.IsIndependent || winner.IsIndependent
			|| winner.id == null || !loser.playerRelationships.TryGetValue(winner.id, out PlayerRelationship view)
			|| !view.AtWar()) {
			return;
		}
		view.warUnitsLost += units;
		view.warCitiesLost += cities;
	}

	public static void SignPeaceAfterWar(Player left, Player right, GameData gameData) {
		if (left.IsIndependent || left.defeated || right.IsIndependent || right.defeated || left.id == right.id)
			throw new Exception($"Can't sign peace between {left} and {right}");

		if (!AtWar(left, right))
			throw new Exception($"This is not the proper method to use if the two players are not at war");

		MultiTurnDeal mtd = new MultiTurnDeal(DealType.DiplomaticAgreement, DealSubType.Peace, DealDetails.Exchange,
			0, null, gameData.rules.DefaultDealDuration, gameData.turn, null);

		RegisterMultiTurnDeal(left, right, mtd);

		// Peace breaks an alliance either had against the other.
		Pacts.BreakAlliancesAgainst(gameData, left, right);

		left.playerRelationships[right.id].refuseContactUntilTurn = -1;
		right.playerRelationships[left.id].refuseContactUntilTurn = -1;

		// An embassy the war closed opens again: "It is only necessary to
		// establish an embassy once with any particular civilization. Even
		// if you manage to get it closed down (through war with that
		// civilization), it reopens when peace is declared" (Civ3 manual,
		// p.~157, https://cdn.akamai.steamstatic.com/steam/apps/3910/manuals/manual.pdf).
		// UNVERIFIED: what it reported of the capital when established is
		// not reported again.
		ReopenEmbassy(left.playerRelationships[right.id]);
		ReopenEmbassy(right.playerRelationships[left.id]);

		// the war is over, so nobody started a current one
		left.playerRelationships[right.id].otherStartedCurrentWar = null;
		right.playerRelationships[left.id].otherStartedCurrentWar = null;
		left.playerRelationships[right.id].warStartTurn = -1;
		right.playerRelationships[left.id].warStartTurn = -1;
		left.playerRelationships[right.id].peaceSignedTurn = gameData.turn;
		right.playerRelationships[left.id].peaceSignedTurn = gameData.turn;
		ResetWarRecord(left.playerRelationships[right.id]);
		ResetWarRecord(right.playerRelationships[left.id]);

		// Each side's units in the other's land go home.
		C7Engine.AI.TerritoryDemands.WithdrawAfterPeace(left, right);

		log.Information("{Left} signed a peace treaty with {Right}", left, right);
	}

	public static void RegisterMultiTurnDeal(Player left, Player right, MultiTurnDeal mtd) {
		if (mtd == null || mtd.dealDetails == DealDetails.None)
			throw new Exception("Not a valid deal");

		if (mtd.dealDetails == DealDetails.Exchange) {
			RegisterTwoWayDeal(left, right, mtd);
			return;
		}
		RegisterOneWayDeal(left, right, mtd);
	}

	private static void RegisterOneWayDeal(Player left, Player right, MultiTurnDeal mtd) {
		if (mtd.dealDetails == DealDetails.None || mtd.dealDetails == DealDetails.Exchange)
			throw new Exception("This is not a valid one way deal. Perhaps you intended to use RegisterTwoWayDeal() instead.");

		// add the deal for this player
		left.playerRelationships[right.id].multiTurnDeals.Add(mtd);

		// add the deal for the other player
		right.playerRelationships[left.id].multiTurnDeals.Add(new MultiTurnDeal(mtd.dealType, mtd.dealSubType,
			mtd.dealDetails == DealDetails.Inbound ? DealDetails.Outbound : DealDetails.Inbound,
			mtd.goldPerTurn, mtd.resourcePerTurn, mtd.dealDuration, mtd.turnStartDeal, mtd.againstPlayer));
	}

	private static void RegisterTwoWayDeal(Player left, Player right, MultiTurnDeal mtd) {
		if (mtd.dealDetails != DealDetails.Exchange)
			throw new Exception("This is not a valid two way deal. Perhaps you intended to use RegisterOneWayDeal() instead.");

		// add the deal for this player
		left.playerRelationships[right.id].multiTurnDeals.Add(mtd);

		// add the deal for the other player
		right.playerRelationships[left.id].multiTurnDeals.Add(mtd);
	}

	/// <summary>
	/// This ends all multi-turn deals except peace, when they go over the initial agreed upon duration.<br/>
	/// The multi-turn deal is cancelled for both parties, so neither keeps it until the other's turn.<br/>
	/// It also cancels the resource deals the player is part of that can no longer be kept (see CancelBrokenResourceDeals).
	/// </summary>
	/// <param name="player"></param>
	/// <param name="players"></param>
	/// <param name="currentTurn"></param>
	public static void CheckForObsoleteDeals(Player player, List<Player> players, int currentTurn) {
		log.Information("Checking to terminate any deals past their due duration for player {Player}", player);

		CancelBrokenResourceDeals(EngineStorage.gameData, player, players);

		// check player's relationship with the other players
		foreach (Player other in players) {
			// if the player doesn't have a relationship with the other civ, or they are at war exit
			if (TryGetRelationship(player, other, out var relationship) && !relationship.AtWar()) {
				// we don't want to cancel peace
				List<MultiTurnDeal> deadDeals = relationship.multiTurnDeals
					.Where(mtd => mtd != null
							  && mtd.dealSubType != DealSubType.Peace
							  && mtd.TurnsRemaining(currentTurn) <= 0)
					// Mutual protection pacts between locked allies never
					// expire (they are made without an end turn).
					.Where(mtd => !(mtd.dealSubType == DealSubType.MutualProtectionPact
									&& EngineStorage.gameData.AreInLockedPeace(player, other)))
					.ToList();

				foreach (MultiTurnDeal deadDeal in deadDeals) {
					log.Information("Cancelling multi turn deal: {Player} -- {Other}", player, other);
					UnRegisterMultiTurnDeal(player, other, deadDeal);
					// Tell the humans, and let an AI offer it again (see
					// DealRenewal).
					DealRenewal.OnExpired(EngineStorage.gameData, player, other, deadDeal);
				}
			}
		}
	}

	// Removes both players' copies of a deal. The other player's copy is
	// looked up before this player's is removed.
	private static void UnRegisterMultiTurnDeal(Player player, Player other, MultiTurnDeal mtd) {
		MultiTurnDeal counterpart = MultiTurnDeal.GetCounterpartDeal(player, other, mtd);
		player.playerRelationships[other.id].multiTurnDeals.Remove(mtd);
		if (counterpart != null) {
			other.playerRelationships[player.id].multiTurnDeals.Remove(counterpart);
		}
	}

	// As in Civ3, a deal to send a resource is cancelled when the exporter
	// no longer has enough of it on its capital's network, or when the two
	// capitals are no longer connected. When the exporter is short, its
	// newest deals for the resource go first. This cancels the broken deals
	// the player sends or receives, and tells the humans involved. The
	// exporter is blamed unless blame is false, as when the Rising Tide's
	// sea broke the deal (see RisingTide.Strike).
	public static void CancelBrokenResourceDeals(GameData gameData, Player player, List<Player> players, bool blame = true) {
		List<(Player exporter, Player importer, MultiTurnDeal deal, Resource resource)> broken = BrokenResourceDeals(gameData, player, players);
		foreach (Player other in players) {
			if (other != player) {
				broken.AddRange(BrokenResourceDeals(gameData, other, players).Where(b => b.importer == player));
			}
		}

		foreach ((Player exporter, Player importer, MultiTurnDeal deal, Resource resource) in broken) {
			log.Information("Cancelling the {Resource} deal from {Exporter} to {Importer}", resource.Name, exporter, importer);
			UnRegisterMultiTurnDeal(exporter, importer, deal);
			// The exporter broke the deal (see BreaksReputation).
			if (blame) {
				importer.playerRelationships[exporter.id].brokeTradeDeal = true;
			}
			NotifyOfCancelledDeal(exporter, $"Our deal to send {resource.Name} to the {importer.civilization?.noun} has been cancelled.");
			NotifyOfCancelledDeal(importer, $"Our deal to receive {resource.Name} from the {exporter.civilization?.noun} has been cancelled.");
		}
	}

	// The exporter's resource deals that can no longer be kept, as the
	// exporter's copies.
	private static List<(Player exporter, Player importer, MultiTurnDeal deal, Resource resource)> BrokenResourceDeals(
			GameData gameData, Player exporter, List<Player> players) {
		List<(Player, Player, MultiTurnDeal, Resource)> result = new();
		Dictionary<Resource, List<(Player importer, MultiTurnDeal deal)>> byResource = new();
		C7Engine.Pathing.TradeNetwork network = gameData.GetTradeNetwork();
		foreach (Player importer in players) {
			if (!TryGetRelationship(exporter, importer, out PlayerRelationship relationship) || relationship.AtWar()) {
				continue;
			}
			// Only worked out for players with resource deals, as it may
			// need both players' networks to be computed.
			bool? connected = null;
			foreach (MultiTurnDeal deal in relationship.multiTurnDeals) {
				if (deal == null || deal.dealDetails != DealDetails.Outbound || deal.resourcePerTurn == null
					|| deal.dealSubType is not (DealSubType.ResourcePerTurn or DealSubType.LuxuryPerTurn)) {
					continue;
				}
				Resource resource = gameData.Resources.Find(r => r.Key == deal.resourcePerTurn);
				if (resource == null) {
					continue;
				}
				connected ??= network.CapitalsConnected(exporter, importer);
				if (connected == false) {
					result.Add((exporter, importer, deal, resource));
					continue;
				}
				if (!byResource.TryGetValue(resource, out var deals)) {
					deals = new();
					byResource[resource] = deals;
				}
				deals.Add((importer, deal));
			}
		}

		foreach ((Resource resource, var deals) in byResource) {
			int excess = deals.Count - network.CapitalResourceCount(exporter, resource);
			if (excess > 0) {
				result.AddRange(deals
					.OrderByDescending(d => d.deal.turnStartDeal)
					.Take(excess)
					.Select(d => (exporter, d.importer, d.deal, resource)));
			}
		}
		return result;
	}

	// Tells a human that one of their deals was cancelled, over their
	// capital. Hotseat holds the message until it is their turn.
	private static void NotifyOfCancelledDeal(Player player, string message) {
		if (!player.isHuman) {
			return;
		}
		City city = player.cities.Find(c => c.IsCapital()) ?? player.cities.FirstOrDefault();
		if (city != null) {
			new MsgShowTemporaryPopup(message, city.location, player).send();
		}
	}
}

public class MultiTurnDeal {
	// Civ3's standard deal length, for a deal made without a game (and so
	// without its rules' DefaultDealDuration) to hand.
	private const int FallbackDealDuration = 20;

	// Passed for a deal's duration to mean the rules' default.
	public const int DefaultDuration = -1;
	public DealType dealType { get; private set; }
	public DealSubType dealSubType { get; private set; }
	public DealDetails dealDetails { get; private set; }
	public int goldPerTurn { get; private set; }
	public string resourcePerTurn { get; private set; }
	public int dealDuration { get; private set; }
	public int turnStartDeal { get; private set; }

	public int turnEndDeal { get; private set; }

	// only applicable for Military Alliances and Trade Embargoes
	public ID againstPlayer { get; private set; }

	public MultiTurnDeal(DealType dealType, DealSubType dealSubType, DealDetails dealDetails, int goldPerTurn = 0,
		string resourcePerTurn = null, int dealDuration = DefaultDuration, int turnStartDeal = 0, ID againstPlayer = null) {
		// Without a duration, deals last as long as the rules say. (The
		// parameter is an int, not int?, because loading a save builds deals
		// through this constructor, whose parameters must match the
		// properties' types.)
		int duration = dealDuration != DefaultDuration ? dealDuration
			: EngineStorage.gameData?.rules?.DefaultDealDuration ?? FallbackDealDuration;
		this.dealSubType = dealSubType;
		this.dealType = dealType;
		this.dealDetails = dealDetails;
		this.goldPerTurn = goldPerTurn;
		this.resourcePerTurn = resourcePerTurn;
		this.dealDuration = duration;
		this.turnStartDeal = turnStartDeal;
		// basically peace from the start, don't need to know when it ends
		if (dealSubType == DealSubType.Peace && turnStartDeal == 0)
			this.turnEndDeal = 0;
		else
			this.turnEndDeal = turnStartDeal + duration;
		this.againstPlayer = againstPlayer;
	}

	public int TurnsRemaining(int currentTurn) {
		return turnEndDeal - currentTurn <= 0 ? 0 : turnEndDeal - currentTurn;
	}

	// A multi turn peace deal that is from the start of the game without end.
	// Used for when a civ meets another civ to establish the initial peace.
	public static MultiTurnDeal DEFAULT_PEACE => new MultiTurnDeal(DealType.DiplomaticAgreement, DealSubType.Peace,
			DealDetails.Exchange, 0, null, 0, 0, null);

	public static MultiTurnDeal DEFAULT_MUTUAL_PROTECTION_PACT => new MultiTurnDeal(DealType.DiplomaticAgreement, DealSubType.MutualProtectionPact,
			DealDetails.Exchange, 0, null, 0, 0, null);

	/// <summary>
	/// Returns the counterpart to a deal.<br/><br/>
	/// The simplest example would be, if PlayerA gives wines to PlayerB,
	/// PlayerA has an Outbound deal in their multiTurnDeals info,
	/// whereas PlayerB has an Inbound deal, while the rest is the same.<br/><br/>
	/// This method, given PlayerA, PlayerB and the PlayerA's side of the deal (Outbound),
	/// returns the PlayerB's side of the deal (Inbound).<br/><br/>
	/// We could have this also the other way around, and provide
	/// PlayerB, PlayerA and PlayerB's side of the deal (Inbound),
	/// and retrieve PlayerA's side of the deal (Outbound).
	/// </summary>
	/// <param name="playerA"></param>
	/// <param name="playerB"></param>
	/// <param name="original"></param>
	/// <returns></returns>
	public static MultiTurnDeal GetCounterpartDeal(Player playerA, Player playerB, MultiTurnDeal original) {
		MultiTurnDeal mtd = null;
		if (PlayerRelationship.TryGetRelationship(playerB, playerA, out var relationship)) {
			DealDetails oppositeDetails = DealDetails.None;
			if (original.dealDetails == DealDetails.Exchange) {
				oppositeDetails = DealDetails.Exchange;
			} else {
				if (original.dealDetails == DealDetails.Inbound) {
					oppositeDetails = DealDetails.Outbound;
				} else {
					oppositeDetails = DealDetails.Inbound;
				}
			}

			mtd = relationship.multiTurnDeals.FirstOrDefault(d => {
				return
					d.dealType == original.dealType
					&& d.dealSubType == original.dealSubType
					&& d.dealDetails == oppositeDetails
					&& d.goldPerTurn == original.goldPerTurn
					&& d.resourcePerTurn == original.resourcePerTurn
					&& d.dealDuration == original.dealDuration
					&& d.turnStartDeal == original.turnStartDeal
					&& d.againstPlayer == original.againstPlayer;
			});

		}
		return mtd;
	}
}

// https://github.com/maxpetul/C3X/blob/064c8307c5085205c0dc8f2ee5b61ad2c2606523/Civ3Conquests.h#L1399
public enum DealType {
	DiplomaticAgreement,
	Alliance,
	Embargo,
	Map,
	Communication,
	Resource,
	Luxury,
	Gold,
	Technology,
	City,
	Unit,
	None,
}

public enum DealSubType {
	Peace,
	MutualProtectionPact,
	RightOfPassage,
	MilitaryAlliance,
	TradeEmbargo,
	GoldPerTurn,
	ResourcePerTurn,
	LuxuryPerTurn,
	None,
}

public enum DealDetails {
	Inbound,
	Outbound,
	Exchange,
	None,
}
