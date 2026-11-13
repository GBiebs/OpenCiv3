using System.Collections.Generic;
using System.Threading.Tasks;
using C7GameData;
using Serilog;
using static C7GameData.PlayerRelationship;

namespace C7Engine.AI {
	// As in Civ3, a civ doesn't put up with another civ's military units in
	// its territory: "You can eject rival military units from your
	// territory during diplomatic negotiations" (Civilopedia,
	// GCON_Territory), and a peace treaty means not to "even enter the
	// other's territory with military units. Units that violate this
	// agreement may be asked to leave" (manual p.~158). Each turn an AI
	// tells every civ it is at peace with, and that has no right of
	// passage, to take its units out of its borders, if they are enough of
	// a threat (see IsThreatening). Military allies are welcome.
	//
	// An AI told to leave withdraws, and its units are moved out at once.
	// A human is asked as Civ3 asks (Conquests diplomacy.txt): first a
	// warning (#AIBORDERWARNING), which they may promise to heed
	// (#USERWILLRETREAT) or answer with war (#USERWAR); then, if their
	// units are still there on our next turn, "leave or declare war"
	// (#AIBORDERWAR), which they answer by having their units moved out
	// (#USERWILLRETURNHOME, "<Your troops will move automatically.>") or
	// with war. "You are _always_ asked to leave or declare war on the
	// second turn" (Erlend Philip Wood, C3X thread, post-16132380). The AI
	// itself never goes to war over it: a civ planning a war declares it
	// when it's ready.
	//
	// Owner's ruling, 2026-10-31: AIs make these demands of humans, in
	// Civ3's form as above. This reverses the ruling of 2026-10-22, under
	// which an AI never told a human to leave.
	public static class TerritoryDemands {
		private static ILogger log = Log.ForContext(typeof(TerritoryDemands));

		// How long a civ's promises to keep out are remembered.
		public const int MEMORY_TURNS = 20;

		public static async Task MakeDemands(Player us, GameData gameData) {
			Dictionary<Player, List<MapUnit>> trespassers = FindTrespassers(us, gameData);
			// "The AI will only ask them to leave if it can see them"
			// (Erlend Philip Wood, C3X thread, post-16823338): with the AI
			// fog of war, only the trespassers we can see.
			if (AIFogOfWar.EnabledFor(us)) {
				AIFogOfWar.Refresh(us);
				foreach (Player them in new List<Player>(trespassers.Keys)) {
					trespassers[them].RemoveAll(u => !AIFogOfWar.SeesUnitsOn(us, u.location));
					if (trespassers[them].Count == 0) {
						trespassers.Remove(them);
					}
				}
			}
			// A lone unit that left on its own starts the count over if it
			// comes back.
			foreach (Player them in gameData.players) {
				if (!trespassers.ContainsKey(them) && us.playerRelationships.TryGetValue(them.id, out PlayerRelationship view)) {
					view.loneTrespasserSinceTurn = -1;
				}
			}

			foreach (KeyValuePair<Player, List<MapUnit>> trespass in trespassers) {
				Player them = trespass.Key;
				// Units with nowhere to go can't be told to leave, so they
				// neither count against their civ nor give us a reason for war.
				List<MapUnit> units = trespass.Value.FindAll(u => u.CanWithdraw());
				PlayerRelationship ourView = us.playerRelationships[them.id];
				// A human we warned on our last turn, whose units are still
				// here, is told to leave or declare war, however few they are.
				bool ultimatum = them.isHuman && units.Count > 0 && ourView.lastWithdrawalDemandTurn >= 0
					&& ourView.lastWithdrawalDemandTurn == gameData.turn - 1;
				if (!ultimatum && !IsThreatening(units, ourView, gameData.turn)) {
					continue;
				}

				// Forget promises the other civ kept for a good while.
				if (ourView.lastWithdrawalDemandTurn < 0
					|| gameData.turn - ourView.lastWithdrawalDemandTurn > MEMORY_TURNS) {
					ourView.recentWithdrawals = 0;
				}
				ourView.lastWithdrawalDemandTurn = gameData.turn;

				if (!them.isHuman) {
					// AIs back down rather than start a war over it.
					int withdrawn = Withdraw(us, them, units);
					if (withdrawn > 0) {
						++ourView.recentWithdrawals;
					}
					log.Information("{Them} withdrew {Count} units from {Us}'s territory", them, withdrawn, us);
					continue;
				}

				bool agreed = await AskHuman(us, them, units, ultimatum);
				if (!agreed) {
					// "Remove your forces from our territory or declare WAR!":
					// they chose war, and declare it, openly, having been
					// warned (so it is no sneak attack).
					log.Information("{Them} refused to leave {Us}'s territory and declares war", them, us);
					them.DeclareWarOn(us, gameData.turn, afterWarning: true);
					MsgWarDeclaration.Announce(them, us);
				} else if (ultimatum) {
					// "<Your troops will move automatically.>"
					int withdrawn = Withdraw(us, them, units);
					if (withdrawn > 0) {
						++ourView.recentWithdrawals;
					}
					log.Information("{Them} withdrew {Count} units from {Us}'s territory", them, withdrawn, us);
				} else {
					// They promised to move their troops out themselves; if
					// they haven't by our next turn, the ultimatum follows.
					log.Information("{Them} promised to take its units out of {Us}'s territory", them, us);
				}
			}
		}

		// Puts the demand to a human, and waits for their answer: true if
		// they agree to leave, false if they choose war. A human who can't
		// be reached, or closes the screen without answering, is taken to
		// agree, so a stray key press can't start a war.
		private static async Task<bool> AskHuman(Player us, Player them, List<MapUnit> units, bool ultimatum) {
			// A LAN guest who has left can't answer.
			if (!EngineStorage.IsPlayerReachable(them.id)) {
				return true;
			}
			// The human answering takes the UI. In a hotseat game they may
			// not be the player at the screen.
			if (EngineStorage.uiFollowsActivePlayer) {
				EngineStorage.uiControllerID = them.id;
			}
			EngineStorage.diplomacyPlayerID = them.id;
			EngineStorage.diplomacyAIPlayerID = us.id;
			EngineStorage.territoryDemandAnswer = null;
			new MsgShowTerritoryDemand(us, them, units.Count, ultimatum, NearestCityName(us, units)).send();
			// A LAN host closes the screen for a guest who leaves or rejoins
			// while it is up, so this always ends.
			await EngineStorage.WaitForDiplomacyCompleted(them.id);
			EngineStorage.diplomacyPlayerID = null;
			EngineStorage.diplomacyAIPlayerID = null;

			bool answer = EngineStorage.territoryDemandAnswer ?? true;
			EngineStorage.territoryDemandAnswer = null;
			return answer;
		}

		// The name of our city nearest the first of the units, for Civ3's
		// "your troops near $CITY3", or null if we have none.
		private static string NearestCityName(Player us, List<MapUnit> units) {
			Tile at = units.Count > 0 ? units[0].location : null;
			City nearest = at?.owningCity is City c && c.owner == us ? c : null;
			if (nearest == null && at != null) {
				int best = int.MaxValue;
				foreach (City city in us.cities) {
					int d = city.location.DistanceTo(at);
					if (d < best) {
						best = d;
						nearest = city;
					}
				}
			}
			return nearest?.name;
		}

		// How many turns a lone combat unit may stay in our territory before
		// we tell its civ to take it out: one of these, picked at random.
		public const int MIN_LONE_TRESPASSER_TURNS = 2;
		public const int MAX_LONE_TRESPASSER_TURNS = 3;

		// Whether the units a civ has in our territory are worth a demand.
		// Only combat units count, so workers, settlers and scouts are let
		// be. Two or more land units, two or more ships, or a land unit and
		// a ship are told to leave at once. A single land unit is put up
		// with for a couple of turns, and a single ship for good.
		public static bool IsThreatening(List<MapUnit> units, PlayerRelationship ourView, int turn) {
			int land = units.FindAll(u => u.IsCombatUnit() && u.IsLandUnit()).Count;
			int sea = units.FindAll(u => u.IsCombatUnit() && u.IsWaterUnit()).Count;

			if (land != 1 || sea != 0) {
				ourView.loneTrespasserSinceTurn = -1;
				return land >= 2 || sea >= 2 || (land >= 1 && sea >= 1);
			}

			if (ourView.loneTrespasserSinceTurn < 0) {
				ourView.loneTrespasserSinceTurn = turn;
				ourView.loneTrespasserPatience = GameData.rng.Next(MIN_LONE_TRESPASSER_TURNS, MAX_LONE_TRESPASSER_TURNS + 1);
			}
			if (turn - ourView.loneTrespasserSinceTurn + 1 < ourView.loneTrespasserPatience) {
				return false;
			}
			ourView.loneTrespasserSinceTurn = -1;
			return true;
		}

		// The units of each civ we'd tell to leave that are in our territory,
		// all of which we can see. Units aboard a transport go wherever it
		// goes, so only the transport is listed.
		public static Dictionary<Player, List<MapUnit>> FindTrespassers(Player us, GameData gameData) {
			Dictionary<Player, List<MapUnit>> result = new();
			foreach (Player them in gameData.players) {
				List<MapUnit> units = TrespassersOf(us, them, gameData);
				if (units.Count > 0) {
					result[them] = units;
				}
			}
			return result;
		}

		// Like FindTrespassers, for a single civ. Only military units: "You
		// can eject rival military units from your territory" (Civilopedia,
		// GCON_Territory), and "they never boot you if you only move
		// workers" (creamcheese, CFC "Right of Passage agreements",
		// https://forums.civfanatics.com/threads/right-of-passage-agreements.427477).
		// Nor hidden nationality units, which "are no longer forcibly
		// removed from enemy territory via diplomacy" (PTW readme, 1.21f).
		private static List<MapUnit> TrespassersOf(Player us, Player them, GameData gameData) {
			List<MapUnit> result = new();
			if (!MustStayOutOf(them, us, gameData)) {
				return result;
			}
			foreach (MapUnit unit in them.units) {
				Tile tile = unit.location;
				if (unit.IsLoaded() || tile == null || tile == Tile.NONE || tile.OwningPlayer() != us) {
					continue;
				}
				if (!unit.IsCombatUnit() || unit.unitType.hasHiddenNationality) {
					continue;
				}
				result.Add(unit);
			}
			return result;
		}

		// The units us could tell them to take out of its territory, leaving
		// out any that have nowhere to go.
		public static List<MapUnit> UnitsToWithdraw(Player us, Player them, GameData gameData) {
			return TrespassersOf(us, them, gameData).FindAll(u => u.CanWithdraw());
		}

		// A human tells an AI to take its units out of the human's territory,
		// from the talk screen, as in Civ3. The AI withdraws them, unless it
		// is the stronger of the two, in which case it refuses and declares
		// war on the human. Returns whether the AI withdrew, or null if it
		// had nothing to withdraw.
		public static bool? DemandFromHuman(Player human, Player ai, GameData gameData) {
			List<MapUnit> units = UnitsToWithdraw(human, ai, gameData);
			if (ai.isHuman || units.Count == 0) {
				return null;
			}

			if (ai.CompareMilitaryStrengthTo(human) == Player.MilitaryStrength.StrongTo && PeaceAI.WouldWinWarWith(gameData, ai, human)) {
				log.Information("{Ai} refused to leave {Human}'s territory", ai, human);
				// Having been warned, it goes to war openly, even though its
				// units are inside the human's borders.
				ai.DeclareWarOn(human, gameData.turn, afterWarning: true);
				MsgWarDeclaration.Announce(ai, human);
				return false;
			}

			int withdrawn = Withdraw(human, ai, units);
			log.Information("{Ai} withdrew {Count} units from {Human}'s territory", ai, withdrawn, human);
			return true;
		}

		// Whether us would tell them to stay out: civs at war fight instead,
		// and right of passage, military alliances and permanent alliances
		// let units in.
		private static bool MustStayOutOf(Player them, Player us, GameData gameData) {
			if (them == us || them.IsIndependent || them.defeated || !them.isIncludedInGame) {
				return false;
			}
			if (!TryGetRelationship(us, them, out PlayerRelationship relationship) || relationship.AtWar()) {
				return false;
			}
			return !HaveActiveRightOfPassage(us, them) && !HaveMilitaryAlliance(relationship)
				&& !gameData.AreInLockedPeace(us, them);
		}

		private static bool HaveMilitaryAlliance(PlayerRelationship relationship) {
			return relationship.multiTurnDeals.Exists(d => d.dealSubType == DealSubType.MilitaryAlliance);
		}

		// When two civs make peace, each one's units in the other's territory
		// are moved out at once, as when told to leave (see Withdraw): to the
		// nearest tile outside every other civ's borders they could stand on
		// and get to, or failing that, for a land unit, its nearest city. A
		// peace treaty means not to "even enter the other's territory with
		// military units" (Civ3 manual, p.~158), and Civ3 moves units out
		// rather than walking them: "Units removed from enemy territory for
		// border violations are teleported to nearest non-offending spot"
		// (CFC Civ3 Strategy Compilation; compendium 6.10, "Removed
		// units are teleported out"). UNVERIFIED: no Civ3 source was found
		// that says this happens on signing peace itself, rather than on the
		// demand that follows; it is the project owner's ruling, as in
		// GBiebs/OpenCiv3 9999221c (see the compendium's open questions).
		// Hidden nationality units stay, as they aren't removed by
		// diplomacy (PTW readme, 1.21f), and units aboard go with their
		// transport. Returns how many units moved out.
		public static int WithdrawAfterPeace(Player left, Player right) {
			int withdrawn = 0;
			foreach ((Player us, Player them) in new[] { (left, right), (right, left) }) {
				List<MapUnit> units = them.units.FindAll(u => !u.IsLoaded() && u.location != null && u.location != Tile.NONE
					&& u.location.OwningPlayer() == us && !u.unitType.hasHiddenNationality);
				int moved = Withdraw(us, them, units);
				if (moved > 0) {
					log.Information("With peace signed, {Count} of {Them}'s units left {Us}'s territory", moved, them, us);
				}
				withdrawn += moved;
			}
			return withdrawn;
		}

		// Moves the units out, and with them the rest of their civ's land
		// units in our territory. Only military units give cause for a
		// demand, but once one is made the others go too: a lone explorer
		// "can meander all over the place", but with an ultimatum "he gets
		// teleported" (Crow T Robot, CFC "Stopping AIs walking through my
		// land", https://forums.civfanatics.com/threads/stopping-ais-walking-through-my-land.69754/).
		// Returns how many units moved out.
		private static int Withdraw(Player us, Player them, List<MapUnit> units) {
			List<MapUnit> leaving = new(units);
			foreach (MapUnit unit in them.units) {
				Tile tile = unit.location;
				if (leaving.Contains(unit) || unit.IsLoaded() || tile == null || tile == Tile.NONE || tile.OwningPlayer() != us
					|| !unit.IsLandUnit() || unit.unitType.hasHiddenNationality) {
					continue;
				}
				leaving.Add(unit);
			}
			int withdrawn = 0;
			foreach (MapUnit unit in leaving) {
				if (unit.WithdrawToNearestFreeTile()) {
					++withdrawn;
				}
			}
			return withdrawn;
		}
	}
}
