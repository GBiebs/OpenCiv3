using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;
using C7GameData;
using Serilog;

namespace C7Engine {
	// Civ3's culture flipping: a city may defect to a neighbouring civ whose
	// culture outweighs its owner's ("cultural reversion").
	//
	// The math is the Civ3 binary's, City_CheckCultureFlip, as decompiled by
	// Perez (Civ3 binary, doc/perez-findings/Civ3_Findings.md, "Appendix:
	// culture flipping, exact math" and "Round 2c: Culture flip: the
	// setters"). The project owner ruled on 2026-11-12 that it is
	// authoritative, superseding the owner's 2026-10-20 ruling that the
	// garrison is divided by the culture ratio (Soren Johnson's worked
	// example). For each candidate civ X, in player (slot) order:
	//
	//  1. S = max(T - 2, 0) + resisters + nationals, where T is the tiles of
	//     the city's radius (centre excluded) inside X's borders, nationals
	//     are the citizens of X's nationality, and resisters those of them
	//     who resist, so resisters count twice. Skip X if S <= 0; cap S at
	//     10.
	//  2. In integer math, in this order: S *= 2 in civil disorder; S *= 2
	//     if X has made more culture in this city than the owner
	//     (City.perPlayerCulture); with A the owner's total culture and B
	//     X's, S <<= 2 if (A+1)*4 < B+1, else S = (B+1)*S/(A+1); S /= 2
	//     while celebrating.
	//  3. G = military land units of any owner on the city tile times the
	//     difficulty's citizens quelled per unit (DIFF+0x78, our
	//     Difficulty.MilitaryLaw: its BIQ offset is 0x44 + 13*4 = 0x78, and
	//     DIFF+0x64 is AttackBonusAgainstBarbarians as in Perez's combat
	//     notes). Skip X if S - G <= 0, else S -= G.
	//  4. R = clamp(2000 * d(city, X's capital) / d(city, owner's capital),
	//     500, 8000), or 2000 if either capital is missing, with Civ3's
	//     tile distance (TileDistance).
	//  5. The city flips to X if Rand(R) < S, a chance of S / R. The first
	//     civ whose roll succeeds takes it.
	//
	// A capital never flips. A city whose flip cooldown is at least 1 is
	// skipped, the cooldown going down by one each check. Founding a city,
	// a flip and a peaceful transfer (trade or gift) set it to 10, a plain
	// military conquest to 1, and setting it raises every other owner's
	// cities nearby to at least the same (SetFlipCooldown).
	//
	// NOT IMPLEMENTED: when the owner lost its capital last turn
	// (Player+0x15c8, capitalLostTimer) the binary replaces S by a
	// floating-point formula Perez could not recover.
	//
	// The checks run once per round (the "interturn"), after every player
	// has played; in Civ3 they run at the start of each city's processing.
	//
	// When a city flips, "All the city defenders die. The 'capturing' civ
	// gets one free defender in the city", and units outside the city are
	// kept (https://forums.civfanatics.com/threads/cultural-conversion-question.87425/;
	// "All units that are in the city are lost",
	// https://forums.civfanatics.com/threads/armies-captured-in-culture-flips.90904/).
	//
	// UNVERIFIED, our choices:
	//  - Which land units are "military" (Map_CountUnitsOnTile mode 4):
	//    those with at least 1 attack and 1 defence, as Firaxis' Dan Magaha
	//    described ("any unit with at least 1 point of offensive and
	//    defensive capability",
	//    https://forums.civfanatics.com/threads/culture-flipping-exposed.14337/),
	//    and every unit of an army that can fight, with the army itself
	//    (alexman, https://codehappy.net/apolyton/threads/51715-4.htm).
	//  - Each city's memory (City.perPlayerCulture) is the culture the city
	//    made while each civ owned it: a turn's culture is added to its owner
	//    at the time, the totals are kept when the city changes hands, and
	//    they are saved (SaveCity.perPlayerCulture).
	//  - The cities protected by a cooldown: Civ3 takes the first 76 of its
	//    city-radius offsets, about radius 5; we take every tile within rank
	//    4 (61) and the first 15 of rank 5 in our own spiral order, whose
	//    order within a ring isn't Civ3's.
	//  - The free defender is the best defensive land unit the new owner
	//    can build in the city.
	//  - The city otherwise changes hands as when traded
	//    (CityInteractions.TransferCity): it keeps its citizens and
	//    buildings, except the palace and small wonders, and doesn't resist.
	//  - Flips happen at war or at peace alike, with no diplomatic penalty;
	//    no source says otherwise.
	// The BIQ's CULT section (propaganda and resistance chances) plays no
	// part in flips.
	public static class CultureFlip {
		private static readonly ILogger log = Log.ForContext(typeof(CultureFlip));

		// The base points before the multipliers are at most this.
		public const int MaxBaseScore = 10;
		// R is 2000 by default, and 500 to 8000 by the distances to the two
		// capitals.
		public const int DefaultRange = 2000;
		public const int MinRange = 500;
		public const int MaxRange = 8000;

		// The cooldown a founded, flipped or peacefully transferred city
		// gets, and a conquered one.
		public const int Cooldown = 10;
		public const int ConquestCooldown = 1;
		// How many of the city-radius offsets around a city whose cooldown is
		// set have other owners' cities raised to it.
		public const int CooldownOffsets = 76;

		// The chance a city flips to a civ this interturn, and why.
		public class Chance {
			public Player to;
			// The citizens of the civ's nationality, resisters twice.
			public int foreigners;
			// The tiles of the city's radius inside the civ's borders.
			public int tiles;
			// max(tiles - 2, 0) + foreigners, capped at MaxBaseScore.
			public int baseScore;
			// 2 if the civ has made more culture in the city than its owner.
			public int memoryFactor;
			// 2 in disorder, 1/2 celebrating (each applied in turn).
			public double moodFactor;
			// The factor the total culture applied: 4, or (B+1)/(A+1).
			public double cultureRatio;
			// B and A, the civs' total culture.
			public int otherCulture;
			public int ownerCulture;
			// G: the garrison's units times the citizens each quells.
			public int garrison;
			// S, after the multipliers and the garrison.
			public int score;
			// R, the range of the roll.
			public int range;
			// R / 2000.
			public double distanceRatio;
			// S / R.
			public double probability;
		}

		// Rolls for every city that may flip; called once per round, after
		// every player has played (the "interturn").
		public static void ProcessEndOfRound(GameData gameData) {
			bool allowed = gameData.rules?.AllowCultureFlips != false;
			RoundFacts facts = new();
			foreach (City city in gameData.cities.ToList()) {
				// The cooldown only counts down; the city isn't checked.
				if (city.flipCooldown >= 1) {
					--city.flipCooldown;
					continue;
				}
				if (!allowed) {
					continue;
				}
				// Each civ that might take the city rolls in turn, in player
				// order, and the first to succeed takes it.
				foreach (Chance chance in Chances(gameData, city, facts)) {
					if (GameData.rng.Next(chance.range) < chance.score) {
						Player from = city.owner;
						if (Defect(gameData, city, chance.to)) {
							AiExperiments.onCultureFlip?.Invoke(city, from, chance);
							// The flip changes both civs' culture and may
							// move their capitals.
							facts = new();
						}
						break;
					}
				}
			}
		}

		// Sets the city's flip cooldown to n turns, and raises that of every
		// other owner's city nearby to at least n (Civ3's
		// City_SetFlipCooldown): founding or taking a city briefly shields
		// the foreign cities around it from flips too.
		public static void SetFlipCooldown(GameData gameData, City city, int n) {
			city.flipCooldown = n;
			if (gameData == null) {
				return;
			}
			foreach (Tile t in CooldownTiles(city.location)) {
				City other = t.cityAtTile;
				if (other != null && other != city && other.owner != city.owner && other.flipCooldown < n) {
					other.flipCooldown = n;
				}
			}
		}

		// The first CooldownOffsets tiles around the tile, by rank (see
		// above).
		internal static IEnumerable<Tile> CooldownTiles(Tile center) {
			return center.GetTilesWithinRankDistance(5)
				.Select((t, i) => (t, i))
				.OrderBy(x => center.RankDistanceTo(x.t)).ThenBy(x => x.i)
				.Take(CooldownOffsets)
				.Select(x => x.t);
		}

		// What every city's chances need to know about the civs, worked out
		// once per round rather than once per city and civ. Thrown away when
		// a city flips.
		private sealed class RoundFacts {
			private readonly Dictionary<Player, int> totalCulture = new();

			public int TotalCulture(Player player) {
				if (!totalCulture.TryGetValue(player, out int culture)) {
					culture = CultureReport.TotalCulture(player);
					totalCulture[player] = culture;
				}
				return culture;
			}

			// Reused for each city: how many of the tiles around it each
			// civ owns.
			public readonly Dictionary<Player, int> tilesOwned = new();
			public readonly List<Tile> tilesScratch = new();
		}

		// The civ the city is likeliest to flip to this interturn, with the
		// chance, or null if it can't flip.
		public static Chance BestChance(GameData gameData, City city) {
			return Chances(gameData, city, new RoundFacts()).MaxBy(c => c.probability);
		}

		// The chance of each civ that might take the city this interturn,
		// those above 0 only, in player order.
		private static List<Chance> Chances(GameData gameData, City city, RoundFacts facts) {
			List<Chance> result = new();
			Player owner = city.owner;
			if (owner == null || owner.IsIndependent || owner.defeated || city.flipCooldown >= 1 || city.IsCapital()) {
				return result;
			}

			// The tiles each civ owns around the city, counted once for
			// every civ.
			Dictionary<Player, int> tilesOwned = facts.tilesOwned;
			tilesOwned.Clear();
			List<Tile> around = facts.tilesScratch;
			around.Clear();
			city.location.GetTilesWithinRankDistance(FlipRadius(gameData), around);
			foreach (Tile t in around) {
				Player tileOwner = t.OwningPlayer();
				if (t != city.location && tileOwner != null) {
					tilesOwned.TryGetValue(tileOwner, out int n);
					tilesOwned[tileOwner] = n + 1;
				}
			}

			foreach (Player other in gameData.players) {
				if (other == owner || other.isBarbarians || other.IsIndependent || other.defeated) {
					continue;
				}
				tilesOwned.TryGetValue(other, out int tiles);
				Chance chance = ChanceOfFlippingTo(gameData, city, other, Foreigners(city, other), tiles, facts);
				if (chance.probability > 0) {
					result.Add(chance);
				}
			}
			return result;
		}

		// The chance, from 0 to 1, that the city flips to the other civ this
		// interturn, by the formula above.
		public static Chance ChanceOfFlippingTo(GameData gameData, City city, Player other) {
			int tiles = 0;
			foreach (Tile t in city.location.GetTilesWithinRankDistance(FlipRadius(gameData))) {
				if (t != city.location && t.OwningPlayer() == other) {
					++tiles;
				}
			}
			return ChanceOfFlippingTo(gameData, city, other, Foreigners(city, other), tiles, new RoundFacts());
		}

		// The radius of the city's tiles that count towards T: Civ3's
		// work-radius positions 1 to 20.
		internal static int FlipRadius(GameData gameData) {
			return Math.Max(1, gameData.rules?.MaxRankOfWorkableTiles ?? 2);
		}

		// The citizens of the other civ's nationality, resisters twice.
		private static int Foreigners(City city, Player other) {
			int foreigners = 0;
			int resistersFrom = city.residents.Count - Math.Clamp(city.resisters, 0, city.residents.Count);
			for (int i = 0; i < city.residents.Count; ++i) {
				Civilization nationality = city.residents[i].nationality;
				if (nationality != null && nationality == other.civilization && nationality != city.owner.civilization) {
					// Resisters, the last ones in residents, count twice.
					foreigners += i >= resistersFrom ? 2 : 1;
				}
			}
			return foreigners;
		}

		// Step 1: the base points, 0 if the civ has no claim.
		public static int BaseScore(int foreigners, int tiles) {
			int s = Math.Max(tiles - 2, 0) + foreigners;
			return s <= 0 ? 0 : Math.Min(s, MaxBaseScore);
		}

		// Step 2: the multipliers, in integer math. ownerCulture and
		// otherCulture are the civs' total culture (A and B).
		public static int ApplyMultipliers(int s, bool disorder, bool moreLocalCulture, int ownerCulture, int otherCulture, bool celebrating) {
			if (disorder) {
				s *= 2;
			}
			if (moreLocalCulture) {
				s *= 2;
			}
			long a = Math.Max(0, ownerCulture) + 1L;
			long b = Math.Max(0, otherCulture) + 1L;
			if (a * 4 < b) {
				s <<= 2;
			} else {
				s = (int)(b * s / a);
			}
			if (celebrating) {
				s /= 2;
			}
			return s;
		}

		// The citizens each unit of the garrison quells: the difficulty's
		// "citizens quelled by military" (DIFF+0x78) of the city's owner.
		public static int QuellPerUnit(GameData gameData, Player owner) {
			return gameData?.DifficultyFor(owner)?.MilitaryLaw ?? 1;
		}

		private static Chance ChanceOfFlippingTo(GameData gameData, City city, Player other, int foreigners, int tiles, RoundFacts facts) {
			Player owner = city.owner;
			Chance chance = new() { to = other, foreigners = foreigners, tiles = tiles };

			chance.baseScore = BaseScore(foreigners, tiles);
			chance.memoryFactor = city.GetCultureFor(other) > city.GetCultureFor(owner) ? 2 : 1;
			chance.moodFactor = (city.isInCivilDisorder ? 2 : 1) * (city.celebrating ? 0.5 : 1);
			chance.otherCulture = facts.TotalCulture(other);
			chance.ownerCulture = facts.TotalCulture(owner);
			chance.cultureRatio = (chance.ownerCulture + 1.0) * 4 < chance.otherCulture + 1.0
				? 4 : (chance.otherCulture + 1.0) / (chance.ownerCulture + 1.0);
			chance.garrison = Garrison(city) * QuellPerUnit(gameData, owner);
			chance.range = Range(city, owner, other);
			chance.distanceRatio = (double)chance.range / DefaultRange;
			if (chance.baseScore <= 0) {
				return chance;
			}

			int s = ApplyMultipliers(chance.baseScore, city.isInCivilDisorder, chance.memoryFactor == 2,
				chance.ownerCulture, chance.otherCulture, city.celebrating);
			s -= chance.garrison;
			if (s <= 0) {
				return chance;
			}
			chance.score = s;
			chance.probability = Math.Min(1, (double)s / chance.range);
			return chance;
		}

		// The military land units in the city, whoever owns them: those with
		// at least 1 attack and 1 defence (Magaha, above). Every unit in an
		// army counts, and the army too, though it has no strength of its
		// own, so long as it holds a unit that can fight: "all individual
		// units within an army count, including the actual army unit"
		// (alexman, above).
		public static int Garrison(City city) {
			return city.location.unitsOnTile.Count(u => u.IsLandUnit() && CountsAsGarrison(u));
		}

		private static bool CountsAsGarrison(MapUnit unit) {
			if (unit.IsArmy()) {
				return unit.IsCombatUnit();
			}
			return unit.unitType.attack >= 1 && unit.unitType.defense >= 1;
		}

		// R: 2000 * d(city, other's capital) / d(city, owner's capital),
		// clamped to 500..8000, or 2000 if either has no capital.
		public static int Range(City city, Player owner, Player other) {
			int ours = DistanceToCapital(city, owner);
			int theirs = DistanceToCapital(city, other);
			if (ours < 0 || theirs < 0) {
				return DefaultRange;
			}
			// Only the owner's capital itself is 0 from it, and capitals
			// never flip.
			if (ours == 0) {
				return MaxRange;
			}
			long r = (long)DefaultRange * theirs / ours;
			return (int)Math.Clamp(r, MinRange, MaxRange);
		}

		// Civ3's tile distance from the city to the civ's capital, or -1 if
		// it has none.
		public static int DistanceToCapital(City city, Player player) {
			City capital = player.cities.Find(c => c.IsCapital());
			if (capital == null) {
				return -1;
			}
			return TileDistance(city.location, capital.location);
		}

		// Civ3's tile distance (Civ3 binary, doc/perez-findings/Civ3_Findings.md,
		// culture-flip appendix): with dx and dy the absolute, wrap-aware
		// coordinate differences, max(dx,dy) - (((dx+dy)/2 - min(dx,dy)) + 1)/2
		// in integer math. A step to a side-adjacent tile counts 1, to a
		// corner-adjacent one 1, and two steps east 3, so it is the
		// community's "max(x,y) + 0.5*min(x,y)" on the map's diagonal axes.
		public static int TileDistance(Tile a, Tile b) {
			GameMap map = a.map;
			int dx = Math.Abs(map.CalculateXDelta(b.XCoordinate, a.XCoordinate));
			int dy = Math.Abs(map.CalculateYDelta(b.YCoordinate, a.YCoordinate));
			return Math.Max(dx, dy) - (((dx + dy) / 2 - Math.Min(dx, dy)) + 1) / 2;
		}

		// The city's people have risen up and pledged allegiance to the
		// other civ, which may take the city or refuse it: Civ3 asks "the
		// loyal citizens of $CITY1 have overthrown their oppressors and
		// have pledged allegiance to us! They yearn to be with us.", with
		// the answers "Great! Install a new governor." and "We don't want
		// $CITY1. Rebuff the rebels." (script.txt, WE_CONVERT_CITY), and an
		// AI may refuse too, telling the owner "the $CIVADJ2 government
		// refuses to acknowledge their claim! Thus, we have regained
		// control of $CITY1!" (THEY_REFUSED_TO_CONVERT_CITY).
		//
		// A human is asked when their turn starts (AskHuman), and the city
		// stays with its owner until they answer (AnswerOffer). The offer
		// lapses at the end of the turn. Humans play before the AIs (see
		// TurnHandling.PlayersInTurnOrder), so an AI owner can't change the
		// city before the human answers. An AI decides at once
		// (AiAcceptsFlip). Returns whether the city flipped now.
		//
		// In a game with more than one human player (hotseat or LAN) no one
		// is asked: the city flips to a human at once (owner's decision,
		// 2026-10-21). Single-player keeps the question.
		public static bool Defect(GameData gameData, City city, Player to) {
			if (to.isHuman && Volcano.IsMultiplayer(gameData)) {
				Flip(gameData, city, to);
				return true;
			}
			if (to.isHuman) {
				city.cultureFlipOfferedTo = to;
				city.cultureFlipOfferTurn = gameData.turn;
				log.Information("{City} would flip from {From} to {To}, who is asked", city, city.owner, to);
				return false;
			}
			if (!AiAcceptsFlip(city, to)) {
				Refuse(city, to);
				return false;
			}
			Flip(gameData, city, to);
			return true;
		}

		// HEURISTIC (no source says how a Civ3 AI decides): an AI takes
		// every city that flips to it, except from a civ it is in a
		// military alliance with, whose war it would rather not hurt.
		public static bool AiAcceptsFlip(City city, Player to) {
			return !Pacts.HasDeal(to, city.owner, DealSubType.MilitaryAlliance);
		}

		// Whether the human is being asked to take the city this turn.
		public static bool OfferStands(GameData gameData, City city, Player player) {
			return city != null && player != null && city.cultureFlipOfferedTo == player
				&& city.cultureFlipOfferTurn == gameData.turn && !player.defeated
				&& city.owner != null && city.owner != player && !city.owner.defeated
				&& city.ownerChangedTurn != gameData.turn && gameData.cities.Contains(city);
		}

		// Asks the human, as their turn starts (or again, should they rejoin
		// a LAN game during it), about each city that has risen up for them;
		// they answer with MsgAnswerCultureFlip.
		public static void AskHuman(GameData gameData, Player player) {
			foreach (City city in gameData.cities) {
				if (OfferStands(gameData, city, player)) {
					List<string> wonders = city.constructed_buildings
						.Where(cb => cb.building.IsGreatWonder()).Select(cb => cb.building.name).ToList();
					new MsgShowCultureFlipOffer(player, city, wonders).send();
				}
			}
		}

		// The human's answer to the offer: the city flips, or stays with its
		// owner, who is told. Returns false if no offer stood.
		public static bool AnswerOffer(GameData gameData, Player player, City city, bool accept) {
			if (!OfferStands(gameData, city, player)) {
				return false;
			}
			city.cultureFlipOfferedTo = null;
			city.cultureFlipOfferTurn = -1;
			if (accept) {
				Flip(gameData, city, player);
			} else {
				Refuse(city, player);
			}
			return true;
		}

		// The civ the city rose up for won't have it, so the owner keeps it.
		private static void Refuse(City city, Player to) {
			log.Information("{To} refused {City}, which stays with {Owner}", to, city, city.owner);
			if (city.owner.IsToldNews) {
				new MsgShowMilitaryAdvisorPopup(city.owner,
					$"The people of {city.name} have risen up in revolt and pledged allegiance to the {to.civilization.noun}!\n"
					+ $"However, the {to.civilization.adjective} government refuses to acknowledge their claim!\n"
					+ $"Thus, we have regained control of {city.name}!", happy: true).send();
			}
		}

		// Hands the city to the civ whose culture won it over. Propaganda
		// (Espionage) makes a city defect the same way, and tells the two
		// sides itself, so notify is false for it.
		public static void Flip(GameData gameData, City city, Player to, bool notify = true) {
			Player from = city.owner;
			Tile tile = city.location;
			log.Information("{City} has flipped from {From} to {To}", city, from, to);
			city.cultureFlipOfferedTo = null;
			city.cultureFlipOfferTurn = -1;

			// "All units that are in the city are lost."
			foreach (MapUnit unit in tile.unitsOnTile.ToList()) {
				if (unit.owner != to && unit.hitPointsRemaining > 0 && tile.unitsOnTile.Contains(unit)) {
					gameData.RemoveUnit(unit);
				}
			}

			// UNVERIFIED: as when a capital is captured, its spaceship is lost.
			if (city.capital) {
				SpaceRace.DestroySpaceship(from, to);
			}

			CityInteractions.TransferCity(city, to);

			// "The 'capturing' civ gets one free defender in the city."
			UnitPrototype defender = city.ListProductionOptions(gameData).OfType<UnitPrototype>()
				.Where(u => u.IsLandUnit() && u.defense > 0)
				.MaxBy(u => u.defense);
			if (defender != null) {
				city.AddUnit(defender, gameData);
			}

			if (!notify) {
				return;
			}

			// Both sides hear of it; a spectator watching every civ hears it
			// once.
			NewsEvent news = new();
			if (from.IsToldNews) {
				new MsgShowDomesticAdvisorPopup(from, $"The people of {city.name}, drawn by the culture of the {to.civilization.noun}, have defected to them!") { newsEvent = news }.send();
			}
			if (to.IsToldNews) {
				new MsgShowDomesticAdvisorPopup(to, $"Drawn by our culture, the people of {city.name} have left the {from.civilization.noun} and joined us!") { newsEvent = news }.send();
			}
		}
	}
}
