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
			if (gameData.gameOver || gameData.victoryConditions?.AllowDiplomaticVictory != true || Seat(gameData)?.owner != owner) {
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
				await HoldElection(gameData);
			}
		}

		// Every civ votes (a human is asked), everyone hears the result, and a
		// winner becomes Secretary-General and wins the game.
		public static async Task<Player> HoldElection(GameData gameData) {
			List<Player> candidates = Candidates(gameData);
			Dictionary<Player, Player> votes = new();
			foreach (Player voter in Voters(gameData)) {
				if (voter.isHuman) {
					C7Engine.TurnHandling.HandControlTo(voter);
					new MsgVoteRequest(voter.id, candidates).send();
					MsgVote vote = await EngineStorage.WaitForMessageToEngine<MsgVote>();
					votes[voter] = vote.candidateId == null ? null : candidates.Find(c => c.id == vote.candidateId);
				} else {
					votes[voter] = Vote(gameData, voter, candidates);
				}
			}
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
	}
}
