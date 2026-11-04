using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// The United Nations' elections, Civ3's diplomatic victory (Civilopedia
	// "Victory Conditions", CivFanatics' Civ3 FAQ): once a civ has built a
	// wonder that allows a diplomatic victory (the United Nations), its
	// owner may call an election for Secretary-General every 11 turns. The
	// candidates are the owner and any civ with a quarter of the world's
	// land or people (at most three; if no other civ qualifies, the most
	// populous one). Every civ has a vote, and a candidate with a majority of
	// all the votes wins the game.
	public static class UnitedNations {
		private static ILogger log = Log.ForContext(typeof(UnitedNations));

		// The city with the wonder, or null.
		public static City Seat(GameData gameData) {
			return gameData.cities.FirstOrDefault(c => c.constructed_buildings.Any(
				cb => cb.building.HasWonderFlag(SaveBuilding.WonderFlag.AllowDiplomaticVictory)));
		}

		public static List<Player> Voters(GameData gameData) {
			return gameData.players.Where(p => !p.isBarbarians && !p.defeated).ToList();
		}

		private static int Population(Player p) => p.cities.Sum(c => c.residents.Count);

		public static List<Player> Candidates(GameData gameData) {
			City seat = Seat(gameData);
			if (seat == null) {
				return [];
			}
			Player founder = seat.owner;
			VictoryRules rules = gameData.rules.victory;
			DominationVictory share = new(0, 0);
			List<Player> others = gameData.GetRivals(founder)
				.Where(p => {
					VictoryStatus status = share.Evaluate(p, gameData);
					return status.DominationArea >= rules.candidatePercent || status.DominationPopulation >= rules.candidatePercent;
				})
				.OrderByDescending(Population)
				.Take(rules.maxCandidates - 1)
				.ToList();
			if (others.Count == 0 && gameData.GetRivals(founder).OrderByDescending(Population).FirstOrDefault() is Player biggest) {
				others.Add(biggest);
			}
			return [founder, .. others];
		}

		// Whom an AI votes for, or null to abstain: itself if it's a
		// candidate, else an ally, else the candidate it likes best, unless
		// it's at war with them or likes none of them (Annoyed or worse).
		// Civ3's AI does abstain (CivFanatics forum reports).
		public static Player Vote(GameData gameData, Player voter, List<Player> candidates) {
			if (candidates.Contains(voter)) {
				return voter;
			}
			Player best = candidates
				.Where(c => PlayerRelationship.TryGetRelationship(voter, c, out PlayerRelationship r) && !r.AtWar())
				.OrderByDescending(c => Agreements.Has(voter, c, DealSubType.MilitaryAlliance))
				.ThenBy(c => Attitude.Score(gameData, voter, c))
				.FirstOrDefault();
			if (best == null || Attitude.Of(gameData, voter, best) >= Attitude.Level.Annoyed) {
				return null;
			}
			return best;
		}

		// The winner of these votes, if any: a majority of everyone's votes,
		// abstentions included.
		public static Player Winner(Dictionary<Player, Player> votes) {
			return votes.Values.Where(c => c != null)
				.GroupBy(c => c)
				.Where(g => g.Count() * 2 > votes.Count)
				.Select(g => g.Key)
				.FirstOrDefault();
		}

		// Whether `owner` may call an election this turn: the game, which
		// allows a diplomatic victory, isn't over, it owns the wonder, and it
		// hasn't been offered one in the last turns (a new owner may call one
		// at once).
		public static bool ElectionDue(GameData gameData, Player owner) {
			if (gameData.gameOver || gameData.victoryConditions?.AllowDiplomaticVictory != true || Seat(gameData)?.owner != owner
				|| gameData.unitedNations.awaitedElection != null) {
				return false;
			}
			UnitedNationsState state = gameData.unitedNations;
			return state.lastOfferedTo != owner.id || gameData.turn - state.lastOfferTurn >= gameData.rules.victory.electionEveryTurns;
		}

		// An AI calls an election only if it expects to win it, counting on
		// no human's vote.
		public static bool WouldCall(GameData gameData, Player owner) {
			List<Player> candidates = Candidates(gameData);
			Dictionary<Player, Player> votes = Voters(gameData)
				.ToDictionary(v => v, v => v.isHuman ? null : Vote(gameData, v, candidates));
			return Winner(votes) == owner;
		}

		// Before `player`'s turn: if it owns the wonder and an election is
		// due, it may call one (a human is asked).
		public static async Task MaybeCallElection(GameData gameData, Player player) {
			if (!ElectionDue(gameData, player)) {
				return;
			}
			gameData.unitedNations.lastOfferedTo = player.id;
			gameData.unitedNations.lastOfferTurn = gameData.turn;
			bool call;
			if (player.isHuman) {
				C7Engine.TurnHandling.HandControlTo(player);
				new MsgElectionOffer(player.id).send();
				call = (await EngineStorage.WaitForMessageToEngine<MsgCallElection>()).call;
			} else {
				call = WouldCall(gameData, player);
			}
			if (call) {
				await HoldElection(gameData, present: player.isHuman ? player : null);
			}
		}

		// Every civ votes (a human is asked), everyone hears the result, and a
		// winner becomes Secretary-General and wins the game. In a game played
		// by email, the humans other than the one `present` vote at the start
		// of their turns (CastAwaitedVote), and the result waits for them.
		public static async Task<Player> HoldElection(GameData gameData, Player present = null) {
			List<Player> candidates = Candidates(gameData);
			Dictionary<Player, Player> votes = new();
			List<ID> awaited = new();
			foreach (Player voter in Voters(gameData)) {
				if (voter.isHuman && gameData.playByEmail && voter != present) {
					awaited.Add(voter.id);
				} else if (voter.isHuman) {
					votes[voter] = await AskVote(voter, candidates);
				} else {
					votes[voter] = Vote(gameData, voter, candidates);
				}
			}
			if (awaited.Count > 0) {
				gameData.unitedNations.awaitedElection = new AwaitedElection {
					candidates = candidates.Select(c => c.id).ToList(),
					votes = votes.Select(v => new CastVote { voter = v.Key.id, candidate = v.Value?.id }).ToList(),
					awaited = awaited,
				};
				log.Information($"UN election called; waiting for {awaited.Count} human votes");
				return null;
			}
			return Announce(gameData, candidates, votes);
		}

		private static async Task<Player> AskVote(Player voter, List<Player> candidates) {
			C7Engine.TurnHandling.HandControlTo(voter);
			new MsgVoteRequest(voter.id, candidates).send();
			MsgVote vote = await EngineStorage.WaitForMessageToEngine<MsgVote>();
			return vote.candidateId == null ? null : candidates.Find(c => c.id == vote.candidateId);
		}

		// Play by email: at the start of `human`'s turn, their vote in an
		// election called while they were away; once every human still in
		// the game has voted, the result.
		public static async Task CastAwaitedVote(GameData gameData, Player human) {
			AwaitedElection election = gameData.unitedNations.awaitedElection;
			if (election == null) {
				return;
			}
			List<Player> candidates = election.candidates.Select(gameData.GetPlayer).Where(c => c != null).ToList();
			if (election.awaited.Contains(human.id)) {
				Player choice = await AskVote(human, candidates);
				election.votes.Add(new CastVote { voter = human.id, candidate = choice?.id });
				election.awaited.Remove(human.id);
			}
			election.awaited.RemoveAll(id => gameData.GetPlayer(id) is not Player p || p.defeated);
			if (election.awaited.Count > 0) {
				return;
			}
			gameData.unitedNations.awaitedElection = null;
			Dictionary<Player, Player> votes = new();
			foreach (CastVote vote in election.votes) {
				if (gameData.GetPlayer(vote.voter) is Player voter && !voter.defeated) {
					votes[voter] = vote.candidate == null ? null : candidates.Find(c => c.id == vote.candidate);
				}
			}
			Announce(gameData, candidates, votes);
		}

		private static Player Announce(GameData gameData, List<Player> candidates, Dictionary<Player, Player> votes) {
			Player winner = Winner(votes);
			log.Information($"UN election: {string.Join(", ", votes.Select(v => $"{v.Key} for {v.Value?.ToString() ?? "nobody"}"))}; winner {winner?.ToString() ?? "none"}");
			new MsgElectionResults(candidates, votes.Select(v => (v.Key, v.Value)).ToList(), winner).send();
			if (winner != null) {
				gameData.unitedNations.secretaryGeneral = winner.id;
				TurnHandling.CheckVictory(gameData);
			}
			return winner;
		}
	}

	// The elections so far (see UnitedNations), saved with the game.
	public class UnitedNationsState {
		// The last civ offered an election, and when.
		public ID lastOfferedTo;
		public int lastOfferTurn;
		// The civ elected, which wins the game.
		public ID secretaryGeneral;
		// Play by email: an election waiting for humans' votes.
		public AwaitedElection awaitedElection;
	}

	public class AwaitedElection {
		public List<ID> candidates = new();
		public List<CastVote> votes = new();
		public List<ID> awaited = new();
	}

	// A vote: a null candidate abstained.
	public class CastVote {
		public ID voter;
		public ID candidate;
	}
}
