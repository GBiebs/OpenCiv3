using System;
using System.Linq;
using C7GameData;
using static C7GameData.PlayerRelationship;

namespace C7Engine.AI {
	// Trial switch "prey-war": Advanced AI only. Once its land runs out, the
	// rule planner names a much weaker neighbour on its landmass as prey
	// (RulePlanner.PlanPeace: the Target stance, an aggression shift and an
	// Offense focus). Without this switch that plan almost never became a
	// war: a declaration in peacetime comes only from Civ3's plotting rolls
	// (StockWarPlotting.RollsToPlot), where the Target stance only picks the
	// victim once all the rolls pass, and the shift moves one roll of a
	// dozen. A civ ranked fifth in power, short of the units per city the
	// rolls ask for, low on gold and under a government with war weariness
	// passes them about one turn in 1600. And lean-military capped its
	// soldiers, so the Offense focus built none.
	//
	// With it on, the prey gets a war campaign at once (WarCampaign: gather
	// at the nearest city, declare when the stack is ready, march, siege,
	// city after city). At peace the stack waits for at least MinAttackers,
	// or goes after GoAfterTurns with two thirds of the attackers it wants,
	// as a campaign already at war does.
	// Its cities build the attackers it is short of, past lean-military's
	// cap. The campaign is dropped if, before the war, the plan calls the
	// prey a friend or names other prey, and given up (with the usual cooldown) once the war goes badly
	// (PeaceAI.Assess Losing or worse); peace is left to PeaceAI. The peace
	// cooldown and locked peace still bar a declaration.
	//
	// What it takes, and keeps: on replays of the first version, a quarter
	// of the cities it took (4 of 17) flipped back to the prey 2 to 4 turns
	// later, at 2% to 8% a turn by Civ3's flip formula: big cities full of
	// the prey's people, near their capital, left with one unit as the
	// stack went straight on. So the prey's cities are scored by the chance
	// we keep them (FlipChanceIfTaken, KeepChance) and by their defence
	// bonuses (walls, size, hills), and one that would flip back too readily
	// even with MaxFlipGarrison units isn't a target at all
	// (WarCampaign.PickPreyTarget). A city taken keeps enough of the stack
	// to hold its flip chance to MaxHeldFlipChance, which the campaign then
	// doesn't take back; the stack waits in it for MinAttackers again
	// before the next city, and before the war it waits a while for its
	// siege engines. On the same replays none of 16 cities taken flipped
	// back.
	public static class PreyWar {
		public const string Switch = "prey-war";

		public static bool On(Player player) => player != null && player.IsAdvancedAi && AiExperiments.Has(player, Switch);

		// HEURISTIC: turns of gathering at peace after which the stack goes
		// with two thirds of the attackers it wants (WarCampaign gives up a
		// gathering after 25).
		public const int GoAfterTurns = 15;
		// HEURISTIC: the fewest attackers the stack gathers at peace. The
		// campaign's guess for a city it can't see is 2 or 3 defenders and
		// 4 or 5 attackers; on the seed 7530 replay the first prey city,
		// once seen, wanted 11, and a stack of 6 Archers fell back from it.
		public const int MinAttackers = 8;

		// The rival the plan names as prey: one we are at peace with, may
		// declare on, and have no protection pact with. Null if none.
		public static Player Prey(Player player, GameData gameData) {
			if (!On(player)) {
				return null;
			}
			return gameData.players.FirstOrDefault(p => p != player && !p.IsIndependent && !p.defeated && p.cities.Count > 0
				&& Advanced.PlanEffects.Stance(player, p) == RivalStance.Target
				&& WarCampaign.CanDeclareWarOn(player, p)
				&& !Pacts.HasDeal(player, p, DealSubType.MutualProtectionPact));
		}

		// Whether a campaign gathering on this prey before the war goes on:
		// it does while the plan neither calls them a friend nor names
		// other prey. A re-plan that sees a city site open up again and
		// turns back to expanding (it flips back and forth as sites open
		// and fill) doesn't call the stack off.
		public static bool StillPrey(Player player, GameData gameData, Player enemy) {
			if (Advanced.PlanEffects.Stance(player, enemy) == RivalStance.Friend
					|| Pacts.HasDeal(player, enemy, DealSubType.MutualProtectionPact)) {
				return false;
			}
			Player named = Prey(player, gameData);
			return named == null || named == enemy;
		}

		// Whether a war on the prey is going badly enough to stop pressing it.
		public static bool GoingBadly(Player player, Player enemy) {
			return PeaceAI.Assess(player, enemy) <= PeaceAI.WarOutlook.Losing;
		}

		// The chance each turn that the prey's city, once ours and held by
		// this many land combat units, flips back to them, by Civ3's flip
		// formula (see CultureFlip):
		//   S = min(max(T - 2, 0) + F, 10), times 2 if they have made more
		//   culture in it than we have, times the total-culture factor,
		//   less G; the chance is S / R
		// worked out for the city as it would be just after we take it:
		// F, the prey's nationals living in it; T, the tiles of its radius
		// that their other cities' borders hold (the borders of the city
		// itself become ours); the city's culture moving from their total to
		// ours; G the units times the difficulty's citizens quelled per
		// unit; R by the distances to the two capitals. Resisters, who count
		// twice, disorder and the flip cooldown after a conquest are left
		// out (UNVERIFIED: our estimate, not Civ3 code).
		// A city already ours (just taken) is taken as it is.
		public static double FlipChanceIfTaken(GameData gameData, Player player, Player enemy, City city, int garrison) {
			if (enemy == null || enemy == player || gameData.rules?.AllowCultureFlips == false) {
				return 0;
			}
			bool ours = city.owner == player;
			int foreigners = city.residents.Count(r => r.nationality == enemy.civilization);
			int tiles = 0;
			foreach (Tile t in city.location.GetTilesWithinRankDistance(CultureFlip.FlipRadius(gameData))) {
				if (t != city.location && t.owningCity != null && t.owningCity != city && t.owningCity.owner == enemy) {
					++tiles;
				}
			}
			int s = CultureFlip.BaseScore(foreigners, tiles);
			if (s <= 0) {
				return 0;
			}
			bool memory = city.GetCultureFor(enemy) > city.GetCultureFor(player);
			int theirCulture = Math.Max(0, CultureReport.TotalCulture(enemy) - (ours ? 0 : city.GetCultureFor(enemy)));
			int ourCulture = Math.Max(0, CultureReport.TotalCulture(player) + (ours ? 0 : city.GetCultureFor(player)));
			s = CultureFlip.ApplyMultipliers(s, false, memory, ourCulture, theirCulture, false);
			s -= garrison * CultureFlip.QuellPerUnit(gameData, player);
			if (s <= 0) {
				return 0;
			}
			int r = CultureFlip.Range(city, player, enemy);
			return Math.Clamp((double)s / r, 0, 1);
		}

		// The land combat units that would hold the city to a flip chance of
		// at most MaxHeldFlipChance (see FlipChanceIfTaken), or int.MaxValue
		// if MaxFlipGarrison wouldn't.
		public static int GarrisonAgainstFlip(GameData gameData, Player player, Player enemy, City city) {
			for (int g = 1; g <= MaxFlipGarrison; ++g) {
				if (FlipChanceIfTaken(gameData, player, enemy, city, g) <= MaxHeldFlipChance) {
					return g;
				}
			}
			return int.MaxValue;
		}

		// Whether the prey's city is worth taking: held by MaxFlipGarrison
		// units, its flip chance would be at most MaxTargetFlipChance.
		public static bool WorthKeeping(GameData gameData, Player player, City city) {
			return FlipChanceIfTaken(gameData, player, city.owner, city, MaxFlipGarrison) <= MaxTargetFlipChance;
		}

		// The chance we still hold the prey's city KeepHorizonTurns after
		// taking it, held by PlannedGarrison units, if flips were all that
		// could take it back.
		public static double KeepChance(GameData gameData, Player player, City city) {
			return Math.Pow(1 - FlipChanceIfTaken(gameData, player, city.owner, city, PlannedGarrison), KeepHorizonTurns);
		}

		// The multiplier on a defender's strength in the city: its size and
		// walls bonuses and its terrain's (fortifying and rivers left out).
		public static double DefenceFactor(City city) {
			return StrengthBonus.ListToMultiplier(city.GetDefenseBonuses().Append(city.location.overlayTerrainType.defenseBonus));
		}

		// UNVERIFIED (ours): the flip chance per turn, with a full garrison,
		// above which a city isn't worth taking. On the replays three of the
		// captures that flipped back (Antium, Rome, Ravenna) would have had
		// 3.6% to 5% a turn even with 5 units; the fourth (Boston) 1.2%, and
		// it flipped at 2.2% held by one; those kept had 0% to 1.5%.
		public const double MaxTargetFlipChance = 0.02;
		// UNVERIFIED (ours): the garrison and turns that KeepChance assumes.
		public const int PlannedGarrison = 2;
		public const int KeepHorizonTurns = 30;

		// UNVERIFIED (ours): the flip chance per turn we accept in a city we
		// took. At 1%, about three in four such cities are still ours 30
		// turns on. On the replays, the captures that flipped back did so at
		// 2% to 8% a turn, within 2 to 4 turns, each held by one unit.
		public const double MaxHeldFlipChance = 0.01;
		// UNVERIFIED (ours): the most units a campaign leaves in a city it
		// took to hold it against a flip. Units in a city that flips are
		// lost with it, so a city that would need more isn't worth taking.
		public const int MaxFlipGarrison = 4;
	}
}
