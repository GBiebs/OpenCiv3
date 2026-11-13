using System.Collections.Generic;
using System.Linq;
using C7GameData;
using Serilog;

namespace C7GameData {
	// The saved state of the United Nations elections. Players are stored by
	// their ID strings so the state serializes as it is.
	public class UnitedNationsState {
		// The turn during which the next vote is cast: humans are asked for
		// their vote at the start of that turn, and the votes are counted
		// once every player has played it. -1 if no election has been called.
		public int votingTurn = -1;

		// The turn during which the UN's owner is next offered the choice of
		// holding an election (see C7Engine.UnitedNations), or -1 if the UN
		// hasn't been found yet. Saves from before the owner was asked don't
		// have it, and are offered one once their scheduled vote is counted.
		public int offerTurn = -1;

		// The votes human players cast for the coming election: the voter's
		// ID to the ID of the candidate they voted for, or "" to abstain.
		public Dictionary<string, string> humanVotes = new();

		// The IDs of the (two or three) candidates in the coming election,
		// stored when the voting turn begins so the humans are asked about,
		// and the votes counted for, the same civs. Null outside a voting
		// turn, and in saves from before they were stored, which store them
		// when first needed. candidateC is null when there are only two
		// candidates, and in saves from before there could be three.
		public string candidateA;
		public string candidateB;
		public string candidateC;

		// The player elected Secretary General, who has won a diplomatic
		// victory, or null.
		public string secretaryGeneral;
	}
}

namespace C7Engine {
	// Civ3's diplomatic victory. Once the United Nations (a great wonder with
	// the "allow diplomatic victory" flag) is built, and the game allows a
	// diplomatic victory, the civs vote every few turns for a Secretary
	// General.
	//
	// The CivFanatics Civ3 FAQ (https://civfanatics.com/civ3/faq/):
	//   "Each civilization gets 1 vote, and can vote for one of the
	//   candidates. At maximum, there can be three candidates: the
	//   civilization that builds the UN is always a candidate, and then if
	//   civilization(s) have 25% of the land OR population, they will also be
	//   eligible. If there are no civilizations with 25% land or population,
	//   then the civilization with the highest score becomes the second
	//   candidate. If a civilization gains a majority of the votes in the
	//   election, they become Secretary General and win a diplomatic victory!
	//   If not, the election is inconclusive. The choice for the founder of
	//   the UN to have elections comes around every 11 turns"
	//
	// For the second candidate the Conquests Civilopedia (DESC_GCON_Victory)
	// is followed over the FAQ's "highest score": "If only one civilization
	// is eligible, then the second candidate is the civilization with the
	// largest population."
	//
	// Every ElectionInterval (10) turns the founder (the UN's current owner)
	// chooses whether to hold an election: the vote comes "every 10 turns"
	// (Perez's decompile of Civ3 Conquests,
	// doc/perez-findings/Civ3_Findings.md, Round 2 "Victory conditions";
	// authoritative per the owner's 2026-11-12 ruling, superseding the FAQ's
	// 11 turns and MadScot's "it's 11 turns whether the vote is inconclusive
	// or the option declined",
	// https://forums.civfanatics.com/threads/diplomatic-victory.53460/; in the
	// same thread Darkness thought a declined vote was offered again the next
	// turn). The interval holds whether the vote was inconclusive or
	// declined. UNVERIFIED (when the
	// choice is made): a human is asked as their turn starts and the vote is
	// held during the next turn; one who doesn't answer by the end of the
	// turn declines. An AI decides as the turn starts (AIHoldsElection), and
	// the vote is held during that turn.
	//
	// Choices the FAQ doesn't settle (UNVERIFIED):
	// - The first election is offered on the turn after the UN is completed.
	// - "Land" is the share of the world's land tiles counted for domination
	//   inside a civ's borders, and "population" its share of the world's
	//   citizens. A civ needs more than 25% of either: "more than 25% of land
	//   or population" (Perez's decompile, Round 2 "Victory conditions";
	//   owner's ruling 2026-11-12). Should more than two other civs pass it,
	//   the two with the largest share (of land or population, whichever is
	//   larger) stand.
	// - Population is the civ's citizens; ties go to the higher score (the
	//   civ's accumulated score, the FAQ's measure), then to the civ listed
	//   first. The UN owner never counts as the most populous civ, as it is
	//   a candidate already.
	// - A majority means more than half of the votes of every living civ,
	//   abstentions included.
	// - A civ may only vote for a candidate it has met; one that has met none
	//   abstains. Candidates vote for themselves.
	public static class UnitedNations {
		private static readonly ILogger log = Log.ForContext(typeof(UnitedNations));

		public const int ElectionInterval = 10;

		// The most candidates in an election, and the share of the world's
		// land or population that makes a civ one.
		public const int MaxCandidates = 3;
		public const double CandidateShare = 0.25;

		public class ElectionResult {
			public List<Player> candidates = new();
			// The votes for each candidate, in the order of candidates.
			public List<int> votes = new();
			public int abstentions;
			// Every voter, and the candidate they voted for (null to abstain).
			public Dictionary<Player, Player> ballots = new();
			public Player winner;

			public int VotesFor(Player candidate) {
				int i = candidates.IndexOf(candidate);
				return i < 0 ? 0 : votes[i];
			}

			public int TotalVotes => votes.Sum() + abstentions;
		}

		public static bool DiplomaticVictoryAllowed(GameData gameData) {
			return gameData.victoryConditions?.AllowDiplomaticVictory == true;
		}

		// The city with the United Nations, or null if it hasn't been built.
		public static City City(GameData gameData) {
			foreach (City city in gameData.cities) {
				if (city.owner == null || city.owner.defeated || city.owner.IsIndependent) {
					continue;
				}
				foreach (CityBuilding cb in city.constructed_buildings) {
					if (cb.building.allowDiplomaticVictory) {
						return city;
					}
				}
			}
			return null;
		}

		public static Player Owner(GameData gameData) {
			// A LAN guest may not be sent the city, so the host says.
			if (gameData.hostFacts != null) {
				string owner = gameData.hostFacts.unitedNationsOwner;
				return owner == null ? null : gameData.players.Find(p => p.id.ToString() == owner);
			}
			return City(gameData)?.owner;
		}

		public static int Population(Player player) {
			int result = 0;
			foreach (City c in player.cities) {
				result += c.residents.Count;
			}
			return result;
		}

		// The tiles of the civ's territory counted as its land.
		private static int Land(Player player) {
			return player.tileKnowledge.CountDominationTiles();
		}

		private static int Score(GameData gameData, Player player) {
			return gameData.history != null && gameData.history.TryGetValue(player.HistoryKey, out List<HistTurnRecord> turns)
				? turns.LastOrDefault()?.Score ?? 0
				: 0;
		}

		private static IEnumerable<Player> Voters(GameData gameData) {
			return gameData.players.Where(p => !p.IsIndependent && !p.defeated && p.isIncludedInGame);
		}

		// The candidates: the UN owner first, then up to two civs with more
		// than 25% of the world's land or population, or if there are none,
		// the most populous other civ. Null if there is no UN or no rival.
		public static List<Player> Candidates(GameData gameData) {
			Player owner = Owner(gameData);
			if (owner == null) {
				return null;
			}
			List<Player> rivals = Voters(gameData).Where(p => p != owner).ToList();
			if (rivals.Count == 0) {
				return null;
			}

			int worldLand = 0;
			foreach (Tile t in gameData.map?.tiles ?? []) {
				if (t.IsCountedForDomination()) {
					++worldLand;
				}
			}
			int worldPopulation = Voters(gameData).Sum(Population);
			double Share(int part, int whole) => whole == 0 ? 0 : (double)part / whole;

			List<Player> result = [owner];
			// OrderBy is stable, so ties go to the civ listed first.
			result.AddRange(rivals
				.Select(p => (player: p, share: System.Math.Max(Share(Land(p), worldLand), Share(Population(p), worldPopulation))))
				.Where(ps => ps.share > CandidateShare)
				.OrderByDescending(ps => ps.share)
				.Take(MaxCandidates - 1)
				.Select(ps => ps.player));
			if (result.Count == 1) {
				result.Add(rivals.OrderByDescending(Population).ThenByDescending(p => Score(gameData, p)).First());
			}
			return result;
		}

		// The candidates in the election under way: the ones stored when the
		// vote was called, or else the current ones, which are stored if this
		// is the voting turn. If a stored candidate has since been destroyed,
		// the candidates are chosen again. Null if there is no UN or no rival.
		public static List<Player> BallotCandidates(GameData gameData) {
			UnitedNationsState state = gameData.unitedNations;
			if (state?.candidateA != null && state.candidateB != null) {
				List<Player> stored = new();
				foreach (string id in new[] { state.candidateA, state.candidateB, state.candidateC }) {
					if (id != null) {
						stored.Add(gameData.players.Find(p => p.id.ToString() == id));
					}
				}
				if (stored.All(p => p != null && !p.defeated)) {
					return stored;
				}
			}
			List<Player> candidates = Candidates(gameData);
			if (state != null && state.votingTurn == gameData.turn) {
				StoreCandidates(state, candidates);
			}
			return candidates;
		}

		private static void StoreCandidates(UnitedNationsState state, List<Player> candidates) {
			state.candidateA = candidates?.ElementAtOrDefault(0)?.id.ToString();
			state.candidateB = candidates?.ElementAtOrDefault(1)?.id.ToString();
			state.candidateC = candidates?.ElementAtOrDefault(2)?.id.ToString();
		}

		// Whether a human player is asked for a vote this turn: a vote is due
		// at the end of it, and the human isn't a candidate (who vote for
		// themselves), has met a candidate, and hasn't voted yet.
		public static bool HumanShouldVote(GameData gameData, Player player) {
			UnitedNationsState state = gameData.unitedNations;
			if (state == null || !player.isHuman || player.defeated || !DiplomaticVictoryAllowed(gameData)
				|| state.votingTurn != gameData.turn || state.humanVotes.ContainsKey(player.id.ToString())) {
				return false;
			}
			List<Player> candidates = BallotCandidates(gameData);
			return candidates != null && !candidates.Contains(player) && candidates.Any(c => HasMet(player, c));
		}

		// Asks a human the United Nations questions due as their turn starts
		// (or again, should they rejoin a LAN game during it): whether to
		// hold an election, if they own the UN and are offered one, and their
		// vote, if one is due, offering the candidates they have met.
		public static void AskHumanToVote(GameData gameData, Player player) {
			AskFounderToHoldElection(gameData, player);
			if (!HumanShouldVote(gameData, player)) {
				return;
			}
			new MsgShowUnitedNationsVote(player, BallotCandidates(gameData).Where(c => HasMet(player, c)).ToArray()).send();
		}

		// Whether a human who owns the United Nations is asked this turn
		// whether to hold an election: they are offered one, haven't answered
		// yet, and there is a rival to stand against them.
		public static bool FounderShouldBeAsked(GameData gameData, Player player) {
			UnitedNationsState state = gameData.unitedNations;
			return state != null && player != null && player.isHuman && !player.defeated && DiplomaticVictoryAllowed(gameData)
				&& state.secretaryGeneral == null && state.votingTurn < 0 && state.offerTurn == gameData.turn
				&& Owner(gameData) == player && Candidates(gameData) != null;
		}

		// Asks a human who owns the United Nations whether to hold an
		// election, if they are offered one; they answer with
		// MsgHoldUnitedNationsElection.
		public static void AskFounderToHoldElection(GameData gameData, Player player) {
			if (FounderShouldBeAsked(gameData, player)) {
				new MsgShowUnitedNationsElectionOffer(player, Candidates(gameData).ToArray()).send();
			}
		}

		// Records a human founder's answer. If they hold the election, the
		// civs vote during the next turn; if not, the next chance comes
		// ElectionInterval turns after this one. Returns false if they
		// weren't being asked.
		public static bool AnswerElectionOffer(GameData gameData, Player founder, bool hold) {
			if (!FounderShouldBeAsked(gameData, founder)) {
				return false;
			}
			UnitedNationsState state = gameData.unitedNations;
			if (hold) {
				state.votingTurn = gameData.turn + 1;
				log.Information("{Owner} holds a United Nations election, voting on turn {Turn}", founder, state.votingTurn);
			} else {
				state.offerTurn += ElectionInterval;
				log.Information("{Owner} holds no United Nations election until offered again on turn {Turn}", founder, state.offerTurn);
			}
			return true;
		}

		// Records a human's vote; a null candidate abstains. Votes for anyone
		// but a candidate they have met are ignored.
		public static bool CastHumanVote(GameData gameData, Player voter, Player candidate) {
			UnitedNationsState state = gameData.unitedNations;
			if (state == null || state.votingTurn != gameData.turn) {
				return false;
			}
			List<Player> candidates = BallotCandidates(gameData);
			if (candidates == null) {
				return false;
			}
			if (candidate != null && (!candidates.Contains(candidate) || !HasMet(voter, candidate))) {
				return false;
			}
			state.humanVotes[voter.id.ToString()] = candidate?.id.ToString() ?? "";
			return true;
		}

		private static bool HasMet(Player voter, Player candidate) {
			return PlayerRelationship.TryGetRelationship(voter, candidate, out _);
		}

		// Called once every player has played a turn, after the turn counter
		// has moved on. Offers the UN's owner an election once the UN exists
		// and every ElectionInterval turns after, lets an AI owner decide on
		// it, takes a human owner's silence for no, and counts the votes when
		// they are due.
		public static ElectionResult ProcessEndOfRound(GameData gameData) {
			UnitedNationsState state = gameData.unitedNations ??= new UnitedNationsState();
			if (!DiplomaticVictoryAllowed(gameData) || state.secretaryGeneral != null) {
				return null;
			}

			Player owner = Owner(gameData);
			if (owner == null) {
				state.votingTurn = -1;
				state.offerTurn = -1;
				state.humanVotes.Clear();
				StoreCandidates(state, null);
				return null;
			}

			// An election has been called.
			if (state.votingTurn >= 0) {
				// The voting turn is beginning, so call the vote.
				if (gameData.turn == state.votingTurn) {
					StoreCandidates(state, Candidates(gameData));
					return null;
				}
				if (gameData.turn < state.votingTurn) {
					return null;
				}
				return CountVotes(gameData, state);
			}

			if (state.offerTurn < 0) {
				state.offerTurn = gameData.turn;
				log.Information("The United Nations, owned by {Owner}, offers an election on turn {Turn}", owner, state.offerTurn);
			}
			// The owner let the turn they were offered an election pass
			// without holding one, so it is declined.
			while (state.offerTurn < gameData.turn) {
				state.offerTurn += ElectionInterval;
			}
			if (state.offerTurn == gameData.turn && !owner.isHuman) {
				if (AIHoldsElection(gameData, owner)) {
					// The AI decides as the turn begins, so the vote can be
					// cast during it.
					state.votingTurn = gameData.turn;
					StoreCandidates(state, Candidates(gameData));
					log.Information("{Owner} holds a United Nations election, voting on turn {Turn}", owner, state.votingTurn);
				} else {
					state.offerTurn += ElectionInterval;
					log.Information("{Owner} expects to lose and holds no United Nations election", owner);
				}
			}
			return null;
		}

		// Counts the votes once the voting turn is over, and schedules the
		// next offer of an election.
		private static ElectionResult CountVotes(GameData gameData, UnitedNationsState state) {
			ElectionResult result = HoldElection(gameData);
			state.humanVotes.Clear();
			StoreCandidates(state, null);
			// The next chance comes ElectionInterval turns after this one was
			// offered. Saves from before the owner was asked have no offer
			// turn, and voted every ElectionInterval turns.
			state.offerTurn = (state.offerTurn >= 0 ? state.offerTurn : state.votingTurn) + ElectionInterval;
			state.votingTurn = -1;
			if (result == null) {
				return null;
			}

			if (result.winner != null) {
				state.secretaryGeneral = result.winner.id.ToString();
			}
			log.Information("United Nations vote: {Candidates} got {Votes}, {Abstentions} abstaining; winner {Winner}",
				result.candidates, result.votes, result.abstentions, result.winner);
			new MsgUnitedNationsElectionResult(result.candidates, result.votes, result.abstentions, result.winner).send();
			return result;
		}

		// Counts the votes of every civ, one each, for the candidates of the
		// vote that was called. Doesn't change any state once the voting turn
		// is over.
		public static ElectionResult HoldElection(GameData gameData) {
			List<Player> candidates = BallotCandidates(gameData);
			if (candidates == null) {
				return null;
			}
			ElectionResult result = new() { candidates = candidates, votes = candidates.Select(_ => 0).ToList() };
			Player[] ballot = candidates.ToArray();
			foreach (Player voter in Voters(gameData)) {
				Player choice = Vote(gameData, voter, ballot);
				result.ballots[voter] = choice;
				int i = choice == null ? -1 : candidates.IndexOf(choice);
				if (i >= 0) {
					++result.votes[i];
				} else {
					++result.abstentions;
				}
			}
			int total = result.TotalVotes;
			for (int i = 0; i < candidates.Count; i++) {
				if (total > 0 && 2 * result.votes[i] > total) {
					result.winner = candidates[i];
				}
			}
			return result;
		}

		// Who the voter votes for, or null to abstain.
		public static Player Vote(GameData gameData, Player voter, params Player[] candidates) {
			if (candidates.Contains(voter)) {
				return voter;
			}
			if (voter.isHuman) {
				UnitedNationsState state = gameData.unitedNations;
				if (state != null && state.humanVotes.TryGetValue(voter.id.ToString(), out string vote)) {
					return candidates.FirstOrDefault(c => vote == c.id.ToString() && HasMet(voter, c));
				}
				return null;
			}
			return AIVote(gameData, voter, candidates);
		}

		// How an AI votes. From CivFanatics:
		// - https://forums.civfanatics.com/threads/when-someone-else-builds-the-u-n.85895/
		//   (Evertonian): "The AI will vote for you if their attitude towards
		//   you is 'polite' or better", unless it likes another candidate
		//   better, in which case it votes for the one it favours most; it
		//   abstains if it is cautious or worse toward every candidate, and
		//   "always votes for itself if able".
		// - https://civfanatics.com/civ3/strategy/game-mechanics/ai-attitude-study/:
		//   "The AI must be at least polite or better to vote for you in the
		//   UN, otherwise, they will abstain or vote for the other guy."
		// - https://forums.civfanatics.com/threads/diplomatic-victory.53460/
		//   (Catt): "An AI will not vote for a civ with whom it is at war".
		// The attitude itself is Attitude below. HEURISTIC: between candidates
		// it likes equally, it votes for the smaller one (by citizens), so as
		// not to hand the world to the strongest civ; further ties go to the
		// candidate listed first. No randomness is involved.
		public static Player AIVote(GameData gameData, Player voter, params Player[] candidates) {
			if (candidates.Contains(voter)) {
				return voter;
			}
			Player best = null;
			int bestAttitude = int.MaxValue;
			foreach (Player candidate in candidates) {
				int? attitude = Attitude(gameData, voter, candidate);
				if (attitude == null || PlayerRelationship.AtWar(voter, candidate)) {
					continue;
				}
				if (best == null || attitude < bestAttitude
					|| (attitude == bestAttitude && Population(candidate) < Population(best))) {
					best = candidate;
					bestAttitude = attitude.Value;
				}
			}
			return best != null && bestAttitude <= PoliteAttitude ? best : null;
		}

		// See Attitudes.
		public const int PoliteAttitude = Attitudes.PoliteAttitude;

		// HEURISTIC: an AI is wary of a candidate with at least twice its
		// citizens, and more so of one with four times as many. These are
		// attitude points.
		private const int WaryOfTwiceOurSize = 1;
		private const int WaryOfFourTimesOurSize = 2;

		// The voter's attitude toward the candidate, in the Attitude Study's
		// points (lower is better; see Attitudes), or null if they haven't
		// met. Per the project owner, the candidate's size counts too.
		internal static int? Attitude(GameData gameData, Player voter, Player candidate) {
			int? points = Attitudes.Points(gameData, voter, candidate);
			if (points == null) {
				return null;
			}

			// HEURISTIC: size (see WaryOfTwiceOurSize).
			int ours = System.Math.Max(1, Population(voter));
			int theirSize = Population(candidate);
			if (theirSize >= 4 * ours) {
				return points + WaryOfFourTimesOurSize;
			} else if (theirSize >= 2 * ours) {
				return points + WaryOfTwiceOurSize;
			}
			return points;
		}

		// Whether an AI that owns the United Nations holds the election it is
		// offered. CivFanatics
		// (https://forums.civfanatics.com/threads/when-someone-else-builds-the-u-n.85895/):
		// "An AI with a bad rep will not ask for a vote as they know that no
		// one will vote for them" (Sabo); "If they know they will most likely
		// lose they won't hold a vote." (Tomoyo). So it holds one only if it
		// expects to win. HEURISTIC: it expects every civ to vote as an AI
		// would (AIVote), humans included, as it can't know their minds.
		public static bool AIHoldsElection(GameData gameData, Player owner) {
			List<Player> candidates = Candidates(gameData);
			if (candidates == null || candidates[0] != owner) {
				return false;
			}
			Player[] ballot = candidates.ToArray();
			int voters = 0;
			int forOwner = 0;
			foreach (Player voter in Voters(gameData)) {
				++voters;
				if (AIVote(gameData, voter, ballot) == owner) {
					++forOwner;
				}
			}
			return 2 * forOwner > voters;
		}
	}
}
