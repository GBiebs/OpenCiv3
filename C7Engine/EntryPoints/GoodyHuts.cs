using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;
using C7GameData;
using Serilog;

namespace C7Engine;

// Goody huts: tribal villages scattered over the map at the start of the
// game. The first civ to walk a land unit into one gets what's inside, and the
// hut is gone.
//
// As in Civ3, a hut holds a city, a tech, gold, a settler, a map of the
// area, a conscript Warrior, barbarians, or nothing. What's inside is picked
// at random with the odds of Conquests' table for the difficulty level, with
// a table of its own for Expansionist civs, which never find barbarians (see
// BaseOdds and ExpansionistOdds).
//
// Which outcomes are possible follows the conditions Firaxis gave for Civ3
// (see https://forums.civfanatics.com/threads/goody-huts.60142/ and
// https://civfanatics.com/civ3/strategy/game-mechanics/probabilities-of-goody-huts-c3c/):
// gold only where there's no resource, techs only in the Ancient era,
// settlers only for civs with fewer cities than average and no settler of
// their own, and barbarians only for non-expansionist civs with a city and a
// military unit, away from any city, and not for units that treat all
// terrain as roads (explorers). Maps and nothing are always possible.
// An outcome that can't happen gets no odds, and the rest share its odds in
// proportion to their own. That is Civ3's redraw: "If the result is
// something that you cant get (ex: barbs for expansionists), there will be a
// redraw." (Oystein,
// https://forums.civfanatics.com/threads/probabilities-of-goody-huts-c3c.93244/),
// "mit unveränderten Wahrscheinlichkeiten ... (gedächtnislos)" ("with
// unchanged probabilities, memoryless"; CivWiki, Bonushütte (Civ3),
// https://www.civ-wiki.de/), and a memoryless redraw gives each possible
// outcome its odds divided by the possible outcomes' total.
public static class GoodyHuts {
	private static readonly ILogger log = Log.ForContext(typeof(GoodyHuts));

	public enum Outcome {
		Gold,
		Tech,
		Unit,
		Settler,
		City,
		Barbarians,
		// A map of the land around the hut.
		Map,
		Nothing,
	}

	// The odds of each outcome, in percent, on each difficulty level from
	// Chieftain to Sid, from CivFanatics' "Probabilities of Goody Huts (C3C)"
	// (https://civfanatics.com/civ3/strategy/game-mechanics/probabilities-of-goody-huts-c3c/),
	// as transcribed by the project's owner (owner's decision, 2026-10-11).
	internal const int LevelCount = 8;
	internal static readonly IReadOnlyDictionary<Outcome, double[]> BaseOdds = new Dictionary<Outcome, double[]> {
		[Outcome.City] = [5, 10, 5, 5, 5, 0, 0, 0],
		[Outcome.Tech] = [35, 25, 20, 15, 10, 5, 0, 0],
		[Outcome.Gold] = [15, 10, 10, 10, 5, 5, 5, 0],
		[Outcome.Settler] = [15, 10, 10, 10, 5, 5, 0, 0],
		[Outcome.Map] = [10, 10, 10, 10, 5, 5, 5, 5],
		[Outcome.Unit] = [10, 10, 10, 10, 5, 5, 5, 5],
		[Outcome.Nothing] = [0, 0, 0, 0, 0, 0, 0, 0],
		[Outcome.Barbarians] = [10, 25, 35, 40, 65, 75, 85, 90],
	};

	// Expansionist civs' odds. The owner's note on the source: "Expansion
	// gives the probabilities of one level lower (for Chieftain, there is no
	// difference). Exception: City and Map are increased by 5 for Warlord and
	// Chieftain." The values are the base table one level lower with the
	// barbarians taken out and the rest scaled back up to 100%.
	// Chieftain and Warlord follow CivWiki's table (Bonushütte (Civ3),
	// https://www.civ-wiki.de/, rev. 50187; compendium 07), whose Map is 15
	// there, as the owner's note says ("Map are increased by 5"), where the
	// owner's transcription had 10. CivWiki's columns then add up to 105
	// (City 10, Tech 35, Gold 15, Settler 15, Map 15, Unit 10, Nothing 5),
	// so each is scaled by 100/105 and rounded to a tenth: 9.5, 33.3, 14.3,
	// 14.3, 14.3, 9.5, 4.8 (owner's decision, 2026-10-20). The other
	// columns are as given.
	internal static readonly IReadOnlyDictionary<Outcome, double[]> ExpansionistOdds = new Dictionary<Outcome, double[]> {
		[Outcome.City] = [9.5, 9.5, 13.3, 7.7, 8.3, 14.3, 0.0, 0.0],
		[Outcome.Tech] = [33.3, 33.3, 33.3, 30.8, 25.0, 28.6, 20.0, 0.0],
		[Outcome.Gold] = [14.3, 14.3, 13.3, 15.4, 16.7, 14.3, 20.0, 33.3],
		[Outcome.Settler] = [14.3, 14.3, 13.3, 15.4, 16.7, 14.3, 20.0, 0.0],
		[Outcome.Map] = [14.3, 14.3, 13.3, 15.4, 16.7, 14.3, 20.0, 33.3],
		[Outcome.Unit] = [9.5, 9.5, 13.3, 15.4, 16.7, 14.3, 20.0, 33.3],
		[Outcome.Nothing] = [4.8, 4.8, 0, 0, 0, 0, 0, 0],
		[Outcome.Barbarians] = [0, 0, 0, 0, 0, 0, 0, 0],
	};

	// How far around the hut its map shows. UNVERIFIED: Civ3's map from a
	// hut shows the area around it, but how much isn't documented.
	internal const int MapRadius = 5;

	// The unit a hut's warriors join as, if the rules have it.
	internal const string HutUnitName = "Warrior";

	// The gold a hut holds: 25, and from the Middle Ages on 25 or 50.
	// "Befindet sich der Spieler bereits im Mittelalter, kommen auch Funde im
	// Wert von 50 Goldstücken vor." ("If the player is already in the Middle
	// Ages, finds worth 50 gold also occur."; CivWiki, Bonushütte (Civ3),
	// https://www.civ-wiki.de/, compendium 07 section 12.1). UNVERIFIED: how
	// often 50 comes up; here half the time.
	internal const int AncientGold = 25;
	internal const int LaterGold = 50;

	// The column of the odds tables for the player: the difficulty level's
	// place among the game's levels, from 0 (Chieftain) to 7 (Sid). A ruleset
	// with other than eight levels has its levels spread over the columns,
	// its easiest on Chieftain's and its hardest on Sid's.
	//
	// AIs, and games whose level isn't among the listed ones, get Monarch's
	// odds, the middle of the scale. UNVERIFIED: which odds Civ3 gives the
	// AIs isn't documented.
	internal const int MiddleLevel = 3;
	internal static int Level(GameData gameData, Player player) {
		if (!player.isHuman) {
			return MiddleLevel;
		}
		List<Difficulty> levels = gameData.difficulties;
		Difficulty current = gameData.gameDifficulty;
		int index = levels.FindIndex(d => d == current || (current?.Name != null && d.Name == current.Name));
		if (index < 0 || levels.Count < 2) {
			return MiddleLevel;
		}
		if (levels.Count == LevelCount) {
			return index;
		}
		return (int)Math.Round(index * (LevelCount - 1) / (double)(levels.Count - 1));
	}

	// How hard the level is, from 0 (Chieftain) to 1 (Sid), for how many
	// barbarians come out of a hut.
	internal static double Hardness(int level) {
		return level / (double)(LevelCount - 1);
	}

	// Opens the hut on the tile for the unit that entered it, if there is
	// one and the unit may. Returns what was found, or null if nothing
	// happened.
	public static Outcome? Enter(GameData gameData, MapUnit unit, Tile tile) {
		if (!tile.hasGoodyHut || unit.owner.IsIndependent || !unit.IsLandUnit()) {
			return null;
		}
		log.Information("{Player}'s {Unit} entered a goody hut at {Tile}", unit.owner, unit, tile);
		return Open(gameData, unit.owner, unit, tile);
	}

	// Opens the huts next to a city just founded, for the city's owner.
	// Barbarians never come out of them, as a hut next to a city never
	// holds barbarians (see CanReleaseBarbarians). Not documented for
	// Civ3: owner's decision, 2026-10-12, "Planting a city next to a
	// goody hut should pop it for a reward, with no chance of barbarians".
	public static void OpenNextToNewCity(GameData gameData, City city) {
		if (city.owner.IsIndependent) {
			return;
		}
		foreach (Tile tile in city.location.neighbors.Values) {
			if (tile != Tile.NONE && tile.hasGoodyHut) {
				log.Information("{Player}'s new city {City} is next to a goody hut at {Tile}", city.owner, city, tile);
				Open(gameData, city.owner, null, tile);
			}
		}
	}

	// Opens the huts the city's borders have grown over, for its owner.
	// Huts "can also be opened by border growth": for an Expansionist civ a
	// hut opened at the fourth culture ring can give a city (CivWiki,
	// Bonushütte (Civ3), https://www.civ-wiki.de/; d73070d0,
	// http://www.civforum.de/showthread.php?t=66671). As with a city
	// founded next to one, no barbarians come out (no unit opened it).
	public static void OpenWithinBorders(GameData gameData, City city) {
		if (city.owner == null || city.owner.IsIndependent) {
			return;
		}
		foreach (Tile tile in city.GetTilesWithinBorders()) {
			if (tile != Tile.NONE && tile.hasGoodyHut && tile.owningCity == city) {
				log.Information("{Player}'s city {City} grew its borders over a goody hut at {Tile}", city.owner, city, tile);
				Open(gameData, city.owner, null, tile, byBorders: true);
			}
		}
	}

	// The rules' money resource (Civ3's General Settings' Default Money
	// Resource: "the selected resource will be placed on the map in the
	// location of the goody hut whenever a goody hut is entered"), unless
	// the tile has a resource already. None by default.
	private static void LeaveMoneyResource(GameData gameData, Tile tile) {
		string name = gameData.rules?.DefaultMoneyResource;
		if (string.IsNullOrEmpty(name) || (tile.Resource != null && tile.Resource != Resource.NONE)) {
			return;
		}
		Resource money = gameData.Resources.FirstOrDefault(r => r.Name == name);
		if (money != null) {
			tile.Resource = money;
		}
	}

	// Opens the hut for the player, whose unit entered it, or with no unit
	// for a city founded next to it or whose borders grew over it.
	private static Outcome Open(GameData gameData, Player player, MapUnit unit, Tile tile, bool byBorders = false) {
		tile.hasGoodyHut = false;
		LeaveMoneyResource(gameData, tile);
		TileChangeJournal.Record(tile);

		int level = Level(gameData, player);
		Dictionary<Outcome, double> weights = Weigh(gameData, player, unit, tile, level, byBorders);
		Outcome outcome = Pick(weights);
		log.Information("{Player} found {Outcome} in the goody hut at {Tile}", player, outcome, tile);

		string message = Apply(gameData, player, tile, ref outcome, Hardness(level));
		if (player.IsToldNews) {
			new MsgShowMilitaryAdvisorPopup(player, message, happy: outcome is not (Outcome.Barbarians or Outcome.Nothing)).send();
		}

		// What the hut brings up after the news goes after it, as the UI drops
		// the news if another popup is already showing.
		switch (outcome) {
			case Outcome.Tech:
				player.AskWhatToResearch(gameData);
				break;
			case Outcome.City when player.isHuman && tile.cityAtTile != null:
				// As for a city the player founds, open its screen.
				new MsgCityCreated(tile.cityAtTile) { recipient = player }.send();
				break;
		}
		return outcome;
	}

	// The odds of each outcome for this player, in percent, on the level
	// (see Level). Outcomes that can't happen, like a tech when there's
	// nothing left to learn, have none, and the rest are weighed against
	// each other.
	internal static Dictionary<Outcome, double> Weigh(GameData gameData, MapUnit unit, Tile tile, int level) {
		return Weigh(gameData, unit.owner, unit, tile, level);
	}

	// As above, for the player; unit is null for a hut opened by a city
	// founded next to it, or by border growth if byBorders.
	internal static Dictionary<Outcome, double> Weigh(GameData gameData, Player player, MapUnit unit, Tile tile, int level, bool byBorders = false) {
		level = Math.Clamp(level, 0, LevelCount - 1);
		bool expansionist = player.civilization?.traits.Contains(Civilization.Trait.Expansionist) == true;
		IReadOnlyDictionary<Outcome, double[]> odds = expansionist ? ExpansionistOdds : BaseOdds;
		List<Player> civs = gameData.players.Where(p => !p.IsIndependent && !p.defeated).ToList();

		// Settlers go only to civs with fewer cities than the average. Cities
		// do too: UNVERIFIED, as the article gives no condition for a city
		// beyond somewhere to found it.
		bool behindOnCities = player.cities.Count < Average(civs, p => p.cities.Count);
		// A hut opened by founding a city next to it (no unit) holds neither:
		// "You cant get a settler or city when building a city, even if you
		// use your last settler to build the city."
		// (https://forums.civfanatics.com/threads/probabilities-of-goody-huts-c3c.93244/)
		// Border growth may give both.
		bool openedByUnit = unit != null;
		bool settlerOrCityPossible = openedByUnit || byBorders;
		// Once every Ancient tech is known, a hut opened by border growth or
		// a city founding holds no unit, settler or city; one opened by a
		// unit still may (CivWiki, Bonushütte (Civ3); d73070d0, civforum.de
		// thread 66671, post #8: "Es ist offensichtlich möglich, weiterhin
		// auch Städte, Krieger und Siedler zu finden, wenn man Hütten mit
		// Einheiten aufdeckt").
		bool peopleFromCityHuts = openedByUnit || !KnowsEveryAncientTech(gameData, player);
		Dictionary<Outcome, bool> possible = new() {
			[Outcome.Gold] = tile.Resource == null || tile.Resource == Resource.NONE,
			[Outcome.Unit] = peopleFromCityHuts && HutUnitFor(gameData) != null,
			[Outcome.Tech] = player.EraIndex() == 0 && TechsToLearn(gameData, player).Count > 0,
			[Outcome.Settler] = settlerOrCityPossible && peopleFromCityHuts && behindOnCities && SettlerFor(gameData, player) != null && !HasSettler(player),
			[Outcome.City] = settlerOrCityPossible && peopleFromCityHuts && behindOnCities && CanFoundCity(gameData, player, tile),
			[Outcome.Barbarians] = unit != null && CanReleaseBarbarians(gameData, unit, tile),
			[Outcome.Map] = true,
			[Outcome.Nothing] = true,
		};

		Dictionary<Outcome, double> weights = new();
		foreach (Outcome outcome in Enum.GetValues<Outcome>()) {
			weights[outcome] = possible[outcome] ? odds[outcome][level] : 0;
		}
		// Should nothing on the table be possible, the hut is empty.
		if (weights.Values.Sum() <= 0) {
			weights[Outcome.Nothing] = 1;
		}
		return weights;
	}

	private static bool KnowsEveryAncientTech(GameData gameData, Player player) {
		return gameData.techs.Where(t => EraUtils.GetEraIndex(t.EraCivilopediaName) == 0).All(t => player.knownTechs.Contains(t.id));
	}

	private static double Average(List<Player> civs, Func<Player, int> measure) {
		return civs.Count == 0 ? 0 : civs.Average(measure);
	}

	internal static Outcome Pick(Dictionary<Outcome, double> weights) {
		double total = weights.Values.Sum();
		double roll = GameData.rng.NextDouble() * total;
		// Enum order, so the same roll always picks the same outcome.
		foreach (Outcome outcome in Enum.GetValues<Outcome>()) {
			if (!weights.TryGetValue(outcome, out double weight) || weight <= 0) {
				continue;
			}
			if (roll < weight) {
				return outcome;
			}
			roll -= weight;
		}
		return Outcome.Nothing;
	}

	// Gives the player what they found, returning the news for a human. It
	// sends nothing to the UI, so the news can go first (see Enter). If
	// what was picked can't happen after all (barbarians with nowhere to
	// stand), the hut holds gold instead, and the outcome says so.
	//
	// The news is Civ3's, from Conquests' Text/script.txt (#GOODY_TECH,
	// #GOODY_MERCENARIES, #GOODY_SETTLERS, #GOODY_CITY, #GOODY_MAPS,
	// #GOODY_NOTHING, #GOODY_BARBARIANS, #GOODY_MONEY), with $BARBARIAN0
	// the village's tribe (see BarbarianInteractions.TribeName). #GOODY_TECH
	// puts the tech on a line of its own ("The $BARBARIAN0 tribe has taught
	// us ^^$LINK0."); here it ends the one line.
	private static string Apply(GameData gameData, Player player, Tile tile, ref Outcome outcome, double hardness) {
		string tribe = BarbarianInteractions.TribeName(gameData, tile);
		switch (outcome) {
			case Outcome.Tech: {
					Tech tech = PickTech(gameData, player, TechsToLearn(gameData, player));
					player.AcquireTech(gameData, tech);
					return $"The {tribe} tribe has taught us {tech.Name}.";
				}
			case Outcome.Unit: {
					UnitPrototype type = HutUnitFor(gameData);
					MapUnit recruit = gameData.SpawnUnit(player, type, tile);
					// Conscripts, the least experienced level.
					if (gameData.experienceLevels.Count > 0) {
						recruit.experienceLevel = gameData.experienceLevels[0];
						recruit.experienceLevelKey = recruit.experienceLevel.key;
						recruit.hitPointsRemaining = recruit.maxHitPoints;
					}
					return $"This friendly {tribe} village gave us a skilled {type.name}.";
				}
			case Outcome.Settler: {
					UnitPrototype type = SettlerFor(gameData, player);
					gameData.SpawnUnit(player, type, tile);
					return $"A friendly {tribe} {type.name} wants to join our {player.government?.name ?? "civilization"}.";
				}
			case Outcome.City: {
					CityInteractions.BuildCity(tile, player, player.GetNextCityName());
					return $"An advanced {tribe} village has joined us!";
				}
			case Outcome.Map: {
					RevealMap(player, tile);
					return $"The friendly {tribe} tribe gave us maps of their region.";
				}
			case Outcome.Nothing:
				return $"This {tribe} village is deserted.";
			case Outcome.Barbarians: {
					UnitPrototype first = ReleaseBarbarians(gameData, tile, hardness);
					if (first != null) {
						return $"We have disturbed an angry {tribe} {first.name}.";
					}
					outcome = Outcome.Gold;
					goto default;
				}
			case Outcome.Gold:
			default: {
					int gold = GoldFor(player);
					player.gold += gold;
					return $"We got {gold} gold from the {tribe} tribe's village.";
				}
		}
	}

	// Shows the player the land around the hut, remembering it as it is now.
	private static void RevealMap(Player player, Tile tile) {
		foreach (Tile t in tile.GetTilesWithinRankDistance(MapRadius)) {
			if (t != Tile.NONE) {
				player.tileKnowledge.AddTileToKnown(t);
				player.tileKnowledge.RememberImprovements(t);
			}
		}
		player.tileKnowledge.RecomputeActiveTiles();
	}

	internal static int GoldFor(Player player) {
		if (player.EraIndex() <= 0) {
			return AncientGold;
		}
		return GameData.rng.Next(2) == 0 ? AncientGold : LaterGold;
	}

	// The techs a hut can teach: Ancient-era techs the player can research,
	// but not the one being researched unless it's the last of them. "Man
	// kann nur Technologien der Antike erhalten, wenn diese noch nicht
	// erforscht wurden" ("you can only receive Ancient-era techs not yet
	// researched"; CivWiki, Bonushütte (Civ3)), and huts "won't give the
	// tech being researched unless it is the only Ancient Era tech left"
	// (Aeson, https://codehappy.net/apolyton/threads/43888-1.htm).
	internal static List<Tech> TechsToLearn(GameData gameData, Player player) {
		List<Tech> techs = player.GetAvailableTechsToResearch(gameData.techs)
			.Where(t => EraUtils.GetEraIndex(t.EraCivilopediaName) == 0)
			.ToList();
		if (techs.Count > 1) {
			techs.RemoveAll(t => t.id == player.currentlyResearchedTech);
		}
		return techs;
	}

	// Picks the tech a hut teaches by the values Civ3's AI puts on techs
	// when choosing research (DevelopmentAI.TechValue), in a roll weighted
	// as research is: a tech's chance is in proportion to its value. Owner's
	// decision, 2026-10-21: hut techs follow the order the AI weights techs,
	// in place of the earlier inverse-cost weighting, which came from
	// CivWiki's "billigere Technologien eine höhere Fundwahrscheinlichkeit
	// besitzen als teurere" ("cheaper techs are likelier than dearer ones";
	// Bonushütte (Civ3)). TechValue divides most of a tech's worth by the
	// turns it would take to research, so cheaper techs still tend to come
	// up more often.
	internal static Tech PickTech(GameData gameData, Player player, List<Tech> techs) {
		int beakers = player.BeakersPerTurn();
		double[] weights = techs.Select(t => Math.Max(0, DevelopmentAI.TechValue(gameData, player, t, beakers))).ToArray();
		double total = weights.Sum();
		if (total <= 0) {
			return techs[GameData.rng.Next(techs.Count)];
		}
		double roll = GameData.rng.NextDouble() * total;
		for (int i = 0; i < techs.Count; ++i) {
			if (roll < weights[i]) {
				return techs[i];
			}
			roll -= weights[i];
		}
		return techs[techs.Count - 1];
	}

	// The Warrior, or failing that the cheapest fighting land unit that
	// needs no tech.
	internal static UnitPrototype HutUnitFor(GameData gameData) {
		return gameData.unitPrototypes.FirstOrDefault(p => p.name == HutUnitName)
			?? gameData.unitPrototypes
				.Where(p => p.IsLandUnit() && p.requiredTech == null && p.attack > 0
					&& !p.isSettler && !p.isWorker && !p.isLeader && !p.isArmy && p.populationCost == 0)
				.OrderBy(p => p.shieldCost)
				.FirstOrDefault();
	}

	private static UnitPrototype SettlerFor(GameData gameData, Player player) {
		return gameData.unitPrototypes
			.Where(p => p.isSettler && p.IsLandUnit() && p.producibleBy.Contains(player.civilization) && player.HasRequiredTechnology(p))
			.OrderBy(p => p.shieldCost)
			.FirstOrDefault();
	}

	// Whether the player has a settler, or a city building one.
	private static bool HasSettler(Player player) {
		return player.units.Any(u => u.unitType.isSettler)
			|| player.cities.Any(c => c.itemBeingProduced is UnitPrototype p && p.isSettler);
	}

	// Whether the villagers can found a city where the hut stood: somewhere
	// a settler could, outside any other civ's borders, for a civ that has a
	// city already, and at least four tiles from any city: "Hütten müssen
	// einen Abstand von mindestens 4 zur nächstgelegenen Stadt besitzen,
	// damit sie zu einer Stadt werden können." ("a hut must be at least
	// distance 4 from the nearest city to become a city"), distance counted
	// as for corruption, and "Es ist erst dann möglich, eine Stadt zu finden,
	// wenn man bereits eine besitzt." ("you can only find a city once you own
	// one"; CivWiki, Bonushütte (Civ3); d73070d0,
	// http://www.civforum.de/showthread.php?t=66671). UNVERIFIED: whether
	// other civs' cities count; here they do.
	internal const double MinCityDistance = 4;
	private static bool CanFoundCity(GameData gameData, Player player, Tile tile) {
		if (tile.HasCity() || !tile.IsAllowCities() || player.cities.Count == 0) {
			return false;
		}
		if (gameData.cities.Any(c => c.location != null && c.location != Tile.NONE && CityDistance(c.location, tile) < MinCityDistance)) {
			return false;
		}
		if (tile.owningCity != null && tile.owningCity.owner != player) {
			return false;
		}
		// The AI's cities keep a free tile between them and any other, so
		// that their first rings of tiles never overlap, as for the cities
		// its settlers found (SettlerLocationAI; owner's decision,
		// 2026-10-20: "It is allowed for humans to").
		if (!player.isHuman) {
			return !SettlerLocationAI.IsInvalidCityLocation(tile);
		}
		return tile.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity());
	}

	// The distance as corruption counts it, a diagonal step one and a half:
	// max(x,y) + min(x,y)/2 along the map's two axes.
	internal static double CityDistance(Tile a, Tile b) {
		GameMap map = a.map;
		int dx = Math.Abs(map.CalculateXDelta(a.XCoordinate, b.XCoordinate));
		int dy = Math.Abs(map.CalculateYDelta(a.YCoordinate, b.YCoordinate));
		// Each step along one of the map's diagonal axes changes both
		// coordinates by one.
		double u = (dx + dy) / 2.0;
		double v = Math.Abs(dx - dy) / 2.0;
		return Math.Max(u, v) + 0.5 * Math.Min(u, v);
	}

	// Barbarians only come out of huts when they're in the game at all, away
	// from the player's own lands and from any city, for civs that aren't
	// expansionist and have a city and a military unit, and for units
	// without the All Terrain As Roads ability (so not explorers): "The unit
	// popping the hut must not have the "All Terrain As Roads" ability."
	// (https://forums.civfanatics.com/threads/probabilities-of-goody-huts-c3c.93244/)
	private static bool CanReleaseBarbarians(GameData gameData, MapUnit unit, Tile tile) {
		if (gameData.barbarianInfo.barbarianActivity == BarbarianActivity.None || gameData.barbarianInfo.basicBarbarian == null) {
			return false;
		}
		Player player = unit.owner;
		if (unit.unitType.allTerrainAsRoads || tile.owningCity?.owner == player) {
			return false;
		}
		if (player.civilization?.traits.Contains(Civilization.Trait.Expansionist) == true
			|| player.cities.Count == 0 || !player.units.Any(u => u.unitType.attack > 0)) {
			return false;
		}
		if (tile.neighbors.Values.Any(n => n != Tile.NONE && n.HasCity())) {
			return false;
		}
		return gameData.players.Any(p => p.isBarbarians) && BarbarianTiles(tile).Count > 0;
	}

	private static List<Tile> BarbarianTiles(Tile tile) {
		return tile.neighbors.Values
			.Where(n => n != Tile.NONE && n.IsLand() && !n.IsImpassable() && !n.HasCity() && n.unitsOnTile.Count == 0)
			.ToList();
	}

	// Spawns barbarians around the hut: more of them on harder levels, and an
	// advanced one on the hardest. Returns the first one's type, for the
	// news, or null if none came out.
	private static UnitPrototype ReleaseBarbarians(GameData gameData, Tile tile, double hardness) {
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		BarbarianInfo info = gameData.barbarianInfo;
		int count = 1 + (hardness >= 0.5 ? 1 : 0) + (hardness >= 0.85 ? 1 : 0);
		List<Tile> tiles = BarbarianTiles(tile);
		UnitPrototype first = null;
		// Advanced barbarians wait for four cities per civ (see
		// BarbarianInteractions.AdvancedBarbariansAvailable).
		bool advancedAllowed = BarbarianInteractions.AdvancedBarbariansAvailable(gameData);
		for (int i = 0; i < count && tiles.Count > 0; ++i) {
			Tile at = tiles[GameData.rng.Next(tiles.Count)];
			tiles.Remove(at);
			UnitPrototype type = hardness >= 0.85 && i == 0 && advancedAllowed && info.advancedBarbarian != null ? info.advancedBarbarian : info.basicBarbarian;
			gameData.SpawnUnit(barbarians, type, at);
			first ??= type;
		}
		return first;
	}
}
