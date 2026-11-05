using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using Serilog;

namespace C7Engine {
	// Online: a player who has left the game (MsgPlayerLeftGame). Every
	// game does the same, at the same point: what the engine asks them is
	// answered as closing its popup would, then their civilization is taken
	// out of the game and the others play on.
	public static class LeavingPlayers {
		private static readonly ILogger log = Log.ForContext(typeof(LeavingPlayers));

		// Nothing paid, given or called; the vote abstained, the envoy
		// refused, the attack called off.
		private static readonly List<(Type type, Func<MessageToEngine> answer)> cautious = [
			(typeof(MsgDiplomacyCompleted), () => new MsgDiplomacyCompleted()),
			(typeof(MsgTributeAnswer), () => new MsgTributeAnswer(null, null, null, pay: false)),
			(typeof(MsgEnvoyAnswer), () => new MsgEnvoyAnswer(null, EnvoyAnswer.Refuse)),
			(typeof(MsgStealthTarget), () => new MsgStealthTarget(null)),
			(typeof(MsgCallElection), () => new MsgCallElection(false)),
			(typeof(MsgVote), () => new MsgVote(null)),
			(typeof(MsgNewsRead), () => new MsgNewsRead()),
		];

		// The answer to what the engine waits for, if it's a question.
		internal static MessageToEngine AnswerFor(IEnumerable<Type> waitedFor) {
			List<Type> waiting = waitedFor.ToList();
			foreach ((Type type, Func<MessageToEngine> answer) in cautious) {
				if (waiting.Contains(type)) {
					return answer();
				}
			}
			return null;
		}

		// Their units and cities go; if it was their turn, the next player's
		// begins.
		internal static async Task TakeOut(GameData gameData, ID playerId) {
			Player player = gameData.GetPlayer(playerId);
			if (player == null || player.defeated) {
				return;
			}
			bool theirTurn = TurnHandling.IsTurnOf(gameData, player.id) && !player.hasPlayedThisTurn;
			log.Information($"{player} has left the game: their civilization is taken out");
			HumanDiplomacy.Forget(gameData, player);
			gameData.RemoveCivilization(player);
			if (theirTurn) {
				await TurnHandling.AdvanceTurn();
			}
		}
	}
}
