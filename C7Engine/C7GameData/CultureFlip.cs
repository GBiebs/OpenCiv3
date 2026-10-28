using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.AI;
using Serilog;

namespace C7GameData {
	// Civ3's culture flipping ("cultural conversion", Civilopedia
	// "Conversion"): each turn a city may defect to another civ whose culture
	// draws it. The chance, as Firaxis's Soren Johnson gave it (CivFanatics
	// Civ3 FAQ, "What is the probability my city will flip"):
	//
	//   P = [(F + T) × Cc × H × (Cte / Cty) − G] / D
	//
	// F: the city's citizens of that civ's nationality, resisters counting
	//    double;
	// T: the tiles of the city's 21 that civ owns, whatever the city's own
	//    borders;
	// Cc: 2 if that civ has more culture in this city than its owner, else 1;
	// H: ½ while the city celebrates We Love the King Day, 2 in disorder;
	// Cte / Cty: that civ's total culture over the owner's;
	// G: the garrison, units that can defend (an army counts once);
	// D: 2000 times the ratio of the city's distances to that civ's capital
	//    and to its owner's, held between ¼ and 4 (Flip Calc).
	//
	// A capital never flips, nor a city on the turn it changed hands. The
	// garrison is lost (as players report), the citizens keep their
	// nationality, and the small wonders, which belong to their civ, are lost.
	public static class CultureFlip {
		private static ILogger log = Log.ForContext(typeof(CultureFlip));

		private static City CapitalOf(Player player) {
			return player.cities.Find(c => c.IsCapital());
		}

		// Whether `city` may flip at all this turn.
		public static bool CanFlip(GameData gameData, City city) {
			return gameData.rules.AllowCulturalConversions && !city.owner.isBarbarians && !city.IsCapital()
				&& city.capturedOnTurn != gameData.turn && city.flippedOnTurn != gameData.turn;
		}

		// The chance, between 0 and 1, that `city` flips to `to` this turn.
		public static double Chance(GameData gameData, City city, Player to) {
			Player owner = city.owner;
			CultureFlipRules rules = gameData.rules.cultureFlip;
			if (to == owner || to.isBarbarians || to.defeated || !CanFlip(gameData, city)) {
				return 0;
			}
			City theirCapital = CapitalOf(to);
			if (theirCapital == null) {
				return 0;
			}

			int foreigners = city.residents.Where(r => r.nationality == to.civilization).Sum(r => r.resisting ? 2 : 1);
			int tiles = city.location.GetTilesWithinRankDistance(gameData.rules.MaxRankOfWorkableTiles)
				.Count(t => t != city.location && t.OwningPlayer() == to);
			double localCulture = city.GetCultureFor(to) > city.GetCultureFor(owner) ? rules.localCultureFactor : 1;
			double mood = city.celebrating ? rules.celebratingFactor : city.isInCivilDisorder ? rules.disorderFactor : 1;
			double cultureRatio = (double)to.TotalCulture() / Math.Max(1, owner.TotalCulture());
			int garrison = city.location.unitsOnTile.Count(u => u.owner == owner && !u.IsInArmy() && u.CanDefendItself());

			City ourCapital = CapitalOf(owner);
			double distanceRatio = ourCapital == null
				? rules.minDistanceRatio
				: Math.Clamp((double)city.location.RankDistanceTo(theirCapital.location) / Math.Max(1, city.location.RankDistanceTo(ourCapital.location)),
					rules.minDistanceRatio, Math.Max(rules.minDistanceRatio, rules.maxDistanceRatio));

			double chance = ((foreigners + tiles) * localCulture * mood * cultureRatio - garrison) / (rules.divisor * distanceRatio);
			return Math.Clamp(chance, 0, 1);
		}

		// Once a turn, after the owner's moods and celebrations: each of its
		// cities may flip to one of the other civs.
		public static void CheckFlips(GameData gameData, Player owner) {
			if (owner.isBarbarians || owner.defeated) {
				return;
			}
			foreach (City city in owner.cities.ToList()) {
				foreach (Player other in gameData.players) {
					double chance = Chance(gameData, city, other);
					if (chance > 0 && GameData.rng.NextDouble() < chance) {
						Flip(gameData, city, other);
						break;
					}
				}
			}
		}

		// `city` defects to `to`.
		public static void Flip(GameData gameData, City city, Player to) {
			Player from = city.owner;
			foreach (MapUnit unit in city.location.unitsOnTile.Where(u => u.owner == from && !u.IsInArmy()).ToList()) {
				gameData.RemoveUnit(unit);
			}
			List<string> wonders = city.constructed_buildings.Where(cb => cb.building.IsGreatWonder()).Select(cb => cb.building.name).ToList();
			foreach (CityBuilding smallWonder in city.constructed_buildings.Where(cb => cb.building.isSmallWonder).ToList()) {
				city.RemoveBuilding(smallWonder);
			}

			CityInteractions.ChangeOwner(gameData, city, to);
			city.flippedOnTurn = gameData.turn;
			city.flippedFrom = from;
			Attitude.OnCityFlipped(gameData, from, to);
			// Its people chose to join: those of the new owner's nationality
			// stop resisting.
			foreach (CityResident resident in city.residents.Where(r => r.nationality == to.civilization)) {
				resident.resisting = false;
			}
			foreach (CityResident resident in city.residents.Where(r => !r.resisting)) {
				CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
			}
			city.SetItemBeingProduced(ChooseProducible.Choose(city, to));
			from.DoCorruptionCalculations(gameData);
			to.DoCorruptionCalculations(gameData);
			log.Information($"{city.name} flips from {from} to {to}{(wonders.Count > 0 ? $" with {string.Join(", ", wonders)}" : "")}");
			new MsgCityFlipped(city, from.id, to.id, wonders).send();
		}

		// A human who gained a flipped city may refuse it, as Civ3 offers
		// ("Rebuff the rebels"): it goes back to its old owner (without the
		// garrison it lost), and can't flip again this turn.
		public static bool Rebuff(GameData gameData, City city) {
			Player from = city.flippedFrom;
			if (from == null || from.defeated || city.flippedOnTurn != gameData.turn) {
				return false;
			}
			Player refuser = city.owner;
			CityInteractions.ChangeOwner(gameData, city, from);
			city.flippedFrom = null;
			Attitude.OnFlipRefused(gameData, from, refuser);
			foreach (CityResident resident in city.residents.Where(r => !r.resisting)) {
				CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
			}
			city.SetItemBeingProduced(ChooseProducible.Choose(city, from));
			from.DoCorruptionCalculations(gameData);
			refuser.DoCorruptionCalculations(gameData);
			log.Information($"{refuser} rebuffs the rebels of {city.name}, which goes back to {from}");
			new MsgCityRebuffed(city).send();
			return true;
		}
	}
}
