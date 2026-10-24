using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;

namespace C7GameData {
	// Civ3's resistance: when a city is captured, its citizens whose civ is at
	// war with the conqueror may refuse to work for it. Resisters don't work,
	// eat or count towards the city's mood, and the city can't hurry
	// production. Each turn a garrison of ground combat units quells some of
	// them; peace with their civ ends it. The chances come from the culture
	// level of the citizens' civ towards the conqueror and from the two
	// governments, as Firaxis described them:
	// https://civfanatics.com/civ3/strategy/game-mechanics/the-inner-workings-of-resistance-revealed/
	public static class Resistance {
		// How the people of `citizens` see the culture of `ruler`: the first
		// culture level whose ratio the ruler's culture reaches. A ruler with no
		// culture at all gets the worst level, even if the citizens have none
		// either.
		public static CultureOpinion OpinionOf(GameData gameData, Player citizens, Player ruler) {
			List<CultureOpinion> levels = gameData.cultureOpinions.OrderByDescending(o => o.cultureRatioPercent).ToList();
			if (levels.Count == 0) {
				return null;
			}
			long ours = ruler.TotalCulture();
			long theirs = citizens.TotalCulture();
			if (ours <= 0) {
				return levels.Last();
			}
			long ratioPercent = theirs <= 0 ? long.MaxValue : ours * 100 / theirs;
			return levels.FirstOrDefault(o => ratioPercent >= o.cultureRatioPercent) ?? levels.Last();
		}

		private static int GovernmentModifier(Player citizens, Player ruler) {
			string theirGovernment = citizens.government?.name;
			if (ruler.government == null || theirGovernment == null) {
				return 0;
			}
			return ruler.government.resistanceModifiers.GetValueOrDefault(theirGovernment, 0);
		}

		// The percent chance that a citizen of `citizens` resists `ruler` when
		// it captures their city.
		public static int InitialChance(GameData gameData, Player citizens, Player ruler) {
			CultureOpinion opinion = OpinionOf(gameData, citizens, ruler);
			return opinion == null ? 0 : Math.Clamp(opinion.initialResistanceChance + GovernmentModifier(citizens, ruler), 0, 100);
		}

		// The percent chance that a resister of `citizens` keeps resisting
		// `ruler` for another turn.
		public static int ContinuedChance(GameData gameData, Player citizens, Player ruler) {
			CultureOpinion opinion = OpinionOf(gameData, citizens, ruler);
			return opinion == null ? 0 : Math.Clamp(opinion.continuedResistanceChance + GovernmentModifier(citizens, ruler), 0, 100);
		}

		private static bool WouldResist(GameData gameData, CityResident resident, City city, out Player home) {
			home = gameData.PlayerOfNationality(resident.nationality);
			return home != null && home != city.owner && PlayerRelationship.AtWar(home, city.owner);
		}

		// Called when `city` has just been captured by its new owner: each
		// citizen whose civ is at war with it may start resisting. Returns how
		// many did.
		public static int StartResisting(GameData gameData, City city) {
			int count = 0;
			foreach (CityResident resident in city.residents) {
				if (!WouldResist(gameData, resident, city, out Player home)) {
					continue;
				}
				if (GameData.rng.Next(100) < InitialChance(gameData, home, city.owner)) {
					resident.tileWorked.personWorkingTile = null;
					resident.tileWorked = Tile.NONE;
					resident.citizenType = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
					resident.mood = CityResident.Mood.Content;
					resident.resisting = true;
					++count;
				}
			}
			return count;
		}

		// How many resisters `city`'s garrison can quell in a turn: one per
		// ground combat unit times the difficulty's "military law" (1 in Civ3).
		// Unit strength doesn't matter.
		public static int QuellingCapacity(GameData gameData, City city) {
			int garrison = city.location.unitsOnTile.Count(u => u.owner == city.owner && u.CanDefendOnLand());
			return garrison * gameData.gameDifficulty.MilitaryLaw;
		}

		// Once a turn for each city: resisters whose civ is no longer at war
		// with the city's owner stop resisting. Each of the others keeps
		// resisting with its continued-resistance chance; of those who would
		// stop, the garrison quells as many as it can. Returns how many stopped.
		public static int Update(GameData gameData, City city) {
			List<CityResident> resisters = city.residents.Where(r => r.resisting).ToList();
			if (resisters.Count == 0) {
				return 0;
			}
			int canQuell = QuellingCapacity(gameData, city);
			List<CityResident> stopped = new();
			foreach (CityResident resister in resisters) {
				if (!WouldResist(gameData, resister, city, out Player home)) {
					stopped.Add(resister);
				} else if (canQuell > 0 && GameData.rng.Next(100) >= ContinuedChance(gameData, home, city.owner)) {
					--canQuell;
					stopped.Add(resister);
				}
			}
			foreach (CityResident resident in stopped) {
				resident.resisting = false;
				CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
			}
			return stopped.Count;
		}
	}
}
