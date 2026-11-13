using System.Linq;
using C7Engine;
using C7GameData;
using Xunit;

namespace EngineTests.GameData.Victory;

public class TimeLimitVictoryTest {

	[Theory]
	[InlineData(200, 199, false)]
	[InlineData(200, 200, true)]  // reaching the limit exactly counts
	[InlineData(200, 201, true)]
	[InlineData(0, 0, false)]  // a limit of 0 or less means there is no limit
	public void HasVictory_TrueOnceCurrentTurnReachesLimit(int limit, int currentTurn, bool expected) {
		var victory = new TimeLimitVictory(limit);
		var status = new VictoryStatus { CurrentTurn = currentTurn };

		Assert.Equal(expected, victory.HasVictory(status));
	}

	[Fact]
	public void Evaluate_ReportsCurrentTurnFromGameData() {
		var victory = new TimeLimitVictory(turnLimit: 300);
		C7GameData.GameData gameData = new() { turn = 42 };
		Player player = new() { civilization = new Civilization("Rome") };

		VictoryStatus status = victory.Evaluate(player, gameData);

		Assert.Equal(42, status.CurrentTurn);
		Assert.False(victory.HasVictory(status));
	}

	// "a warning 20 turns before g_LastTurn" (Perez's decompile, Round 2;
	// owner's ruling 2026-11-12), in Civ3's words (script.txt,
	// #RETIREWARNING), to each human, once.
	[Fact]
	public void HumansAreWarnedTwentyTurnsBeforeTheEnd() {
		C7GameData.GameData gameData = new() { turn = 179, timeOptions = new TimeOptions() };
		gameData.victories.Add(new TimeLimitVictory(turnLimit: 200));
		Player human = new() { isHuman = true, civilization = new Civilization("Rome") };
		Player ai = new() { civilization = new Civilization("Egypt") };
		gameData.players.Add(human);
		gameData.players.Add(ai);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		try {
			Assert.False(TimeLimitVictory.MaybeWarn(gameData));
			gameData.turn = 180;
			Assert.True(TimeLimitVictory.MaybeWarn(gameData));
			MsgShowDomesticAdvisorPopup warning = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowDomesticAdvisorPopup>());
			Assert.Equal(human, warning.recipient);
			Assert.StartsWith("MANDATORY RETIREMENT YEAR!", warning.message);
			Assert.Contains("GAME WILL END AT", warning.message);
			gameData.turn = 181;
			Assert.False(TimeLimitVictory.MaybeWarn(gameData));
		} finally {
			EngineStorage.messagesToUI.Clear();
		}
	}
}
