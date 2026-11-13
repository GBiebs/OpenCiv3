using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using C7Engine.AI;
using C7Engine.Network;
using C7GameData;
using Serilog;

namespace C7Engine;

// Civ3's "Play By Email" game type. The humans take their turns as in a
// hotseat game, but each on their own computer: at the end of a turn the
// game is saved and the file sent on to the next player ("Your turn is
// over. Please Save & Exit Game, and e-mail the save to the next player").
//
// So nothing may wait on a human who isn't at the computer. A human can
// answer a question only during their own turn (CanAnswer); the computer
// players, on their turns, don't ask the humans anything, and instead have
// their say with each human at the start of the human's turn (HearFromAIs).
// The news a human is told while away travels in the save (KeepNews,
// TakeNews).
//
// Adapted from GBiebs/OpenCiv3's play by email.
public static class PlayByEmail {
	private static readonly ILogger log = Log.ForContext(typeof(PlayByEmail));

	public static bool IsOn(GameData gameData) => gameData?.playByEmail == true;

	// Whether the player can answer a question now. A computer player always
	// can; a human only on their own turn, when they're at the computer.
	public static bool CanAnswer(GameData gameData, ID playerID) {
		Player player = gameData?.GetPlayer(playerID);
		if (player == null || !player.isHuman) {
			return true;
		}
		return playerID == EngineStorage.uiControllerID && TurnHandling.IsPlayersTurn(gameData, playerID);
	}

	// At the start of a human's turn, once they've shown they're at the
	// computer: the computer players' demands and offers of peace to them,
	// as they would make them on their own turns. Once a turn, so loading
	// the save again doesn't give the computer players a second say. Then
	// the human's turn begins (MsgTurnReady).
	public static async Task HearFromAIs(Player human) {
		GameData gameData = EngineStorage.gameData;
		if (IsOn(gameData) && human.isHuman && human.heardFromAIsOnTurn != gameData.turn) {
			human.heardFromAIsOnTurn = gameData.turn;
			foreach (Player ai in gameData.players.Where(p => !p.isHuman && !p.IsIndependent && !p.defeated && p.isIncludedInGame).ToList()) {
				if (human.defeated || gameData.gameOver) {
					break;
				}
				await TributeDemands.MakeDemands(ai, gameData, onlyWith: human);
				await PeaceAI.ProposePeace(ai, gameData, onlyWith: human);
			}
		}
		new MsgTurnReady(human).send();
	}

	// Keeps the news for a human who is away (the messages the screen held
	// for them), in their part of the save, for their next turn.
	public static void KeepNews(GameData gameData, ID playerID, IEnumerable<MessageToUI> news) {
		Player player = gameData.GetPlayer(playerID);
		if (player == null) {
			return;
		}
		foreach (MessageToUI msg in news) {
			try {
				player.newsWhileAway.Add(Encoding.UTF8.GetString(NetSerialization.Serialize(msg)));
			} catch (Exception e) {
				log.Warning(e, "Couldn't keep {Message} for {Player}'s turn", msg.GetType().Name, player);
			}
		}
	}

	// The news kept for the player while they were away, in the order they
	// were told it; it's no longer kept. News about what's no longer there
	// to tell of is dropped.
	public static List<MessageToUI> TakeNews(GameData gameData, Player player) {
		List<MessageToUI> news = [];
		foreach (string json in player.newsWhileAway) {
			try {
				MessageToUI msg = NetSerialization.DeserializeMessageToUI(Encoding.UTF8.GetBytes(json));
				if (msg != null) {
					news.Add(msg);
				}
			} catch (Exception e) {
				log.Warning(e, "Couldn't read news kept for {Player}", player);
			}
		}
		player.newsWhileAway.Clear();
		return news;
	}

	// The name of the person playing, or their civilization's leader.
	public static string PlayerName(Player player) => player.name ?? player.civilization?.leader ?? player.ToString();

	// What a file name may not have on any computer the save travels to.
	public const string CharactersNotInAName = "\\/:*?\"<>|";

	// The name the save sent on is given unless the player changes it: who
	// plays next, and when.
	public static string SaveFileName(GameData gameData, Player next) {
		int turn = gameData.turn;
		string date = gameData.timeOptions?.GetDisplayTime(turn);
		string name = $"PBEM {PlayerName(next)} to play - Turn {turn}" + (string.IsNullOrEmpty(date) ? "" : $" ({date})");
		string safe = new(name.Select(c => c < ' ' || CharactersNotInAName.Contains(c) ? '_' : c).ToArray());
		return safe.Trim() + ".json";
	}

	// A name the player typed for the save, as its file name (".json" added
	// unless it's there); null if it can't be one.
	public static string FileNameTyped(string typed) {
		string name = typed?.Trim() ?? "";
		if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
			name = name[..^".json".Length].TrimEnd();
		}
		if (name == "" || name.Any(c => c < ' ' || CharactersNotInAName.Contains(c)) || name.EndsWith('.')) {
			return null;
		}
		return name + ".json";
	}
}
