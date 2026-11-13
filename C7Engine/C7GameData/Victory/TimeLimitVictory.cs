using System.Collections.Generic;
using C7GameData;

namespace C7Engine;

public class TimeLimitVictory : IVictory {
	private readonly int _turnLimit;

	public TimeLimitVictory(int turnLimit) {
		_turnLimit = turnLimit;
	}

	public int TurnLimit => _turnLimit;

	// How many turns before the last the players are warned: "a warning 20
	// turns before g_LastTurn, final result at g_LastTurn" (Perez's
	// decompile of Civ3 Conquests, doc/perez-findings/Civ3_Findings.md,
	// Round 2 "Victory conditions"; authoritative per the owner's 2026-11-12
	// ruling).
	public const int WarningTurns = 20;

	// The warning, in Civ3's words (script.txt, #RETIREWARNING, captioned
	// "End of Game"): "MANDATORY RETIREMENT YEAR! GAME WILL END AT $YEAR0".
	public static string WarningText(string endDate) => $"MANDATORY RETIREMENT YEAR!\nGAME WILL END AT {endDate}";

	// Warns every human player, as the turn WarningTurns before the turn
	// limit begins, that the game will end. Returns whether it did.
	public static bool MaybeWarn(GameData gameData) {
		if (gameData.gameOver || gameData.victories == null) {
			return false;
		}
		TimeLimitVictory limit = null;
		foreach (IVictory v in gameData.victories) {
			if (v is TimeLimitVictory t && t._turnLimit > 0) {
				limit = t;
				break;
			}
		}
		if (limit == null || gameData.turn != limit._turnLimit - WarningTurns) {
			return false;
		}
		string text = WarningText(EndDate(gameData, limit._turnLimit));
		foreach (Player player in gameData.players) {
			if (player.isHuman && !player.defeated) {
				new MsgShowDomesticAdvisorPopup(player, text).send();
			}
		}
		return true;
	}

	// The date of the last turn, or its number if the date can't be shown
	// (as without the Lua scripts that format dates).
	private static string EndDate(GameData gameData, int turn) {
		try {
			string date = gameData.timeOptions?.GetDisplayTime(turn);
			if (!string.IsNullOrEmpty(date)) {
				return date;
			}
		} catch (System.Exception) {
			// Fall through to the turn number.
		}
		return $"TURN {turn}";
	}

	public string Header() => "Time Limits";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		return new VictoryStatus {
			Player = player,
			CurrentTurn = gameData.turn
		};
	}

	// A turn limit of 0 or less means the game has none.
	public bool HasVictory(VictoryStatus status) {
		return _turnLimit > 0 && status.CurrentTurn >= _turnLimit;
	}

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		yield return TurnLimitPrint(status, rivalStatuses);
	}

	private string[] TurnLimitPrint(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		return [
			"Turn in game:",
			$"{_turnLimit}",
			"",
			"",
			"Current turn:",
			$"{status.CurrentTurn}"
		];
	}
}
