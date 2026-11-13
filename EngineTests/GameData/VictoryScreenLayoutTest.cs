using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Xunit;

namespace EngineTests.GameData;

// Civ3's end-of-game screen: which screen, whose heads where and with what
// faces, and what the other leaders say (Civ3 FUN_005dcf60, FUN_005dd790,
// FUN_005dd960, FUN_004f9fb0).
public class VictoryScreenLayoutTest {
	private static Player Civ(string name) => new() { civilization = new Civilization(name) };

	private static List<VictoryScreenHead> Heads(int screen, List<Player> civs, Player player, Player winner,
			HashSet<Player> dead = null, bool lockedAlliances = false, HashSet<(Player, Player)> allies = null) =>
		VictoryScreenLayout.Heads(screen, civs, player, winner, p => dead == null || !dead.Contains(p), lockedAlliances,
			(a, b) => allies != null && (allies.Contains((a, b)) || allies.Contains((b, a))));

	[Fact]
	public void TheScreenSuitsHowThePlayerWonOrLost() {
		Assert.Equal(VictoryScreenLayout.DominationScreen, VictoryScreenLayout.ScreenFor(GameEndReason.Domination, true));
		Assert.Equal(VictoryScreenLayout.ConquestScreen, VictoryScreenLayout.ScreenFor(GameEndReason.Conquest, true));
		Assert.Equal(VictoryScreenLayout.CulturalScreen, VictoryScreenLayout.ScreenFor(GameEndReason.Cultural, true));
		Assert.Equal(VictoryScreenLayout.TimeLimitScreen, VictoryScreenLayout.ScreenFor(GameEndReason.TimeLimit, true));
		Assert.Equal(VictoryScreenLayout.WonderScreen, VictoryScreenLayout.ScreenFor(GameEndReason.Wonder, true));
		Assert.Equal(VictoryScreenLayout.DefeatScreen, VictoryScreenLayout.ScreenFor(GameEndReason.SpaceRace, false));
		Assert.Equal(VictoryScreenLayout.RetirementScreen, VictoryScreenLayout.ScreenFor(GameEndReason.Retirement, false));

		Assert.Equal("Art/VictoryScreen/domination_victory.pcx", VictoryScreenLayout.Background(VictoryScreenLayout.DominationScreen));
		Assert.Equal("Art/VictoryScreen/defeat.pcx", VictoryScreenLayout.Background(VictoryScreenLayout.DefeatScreen));
		Assert.Equal((409, 67), VictoryScreenLayout.Place(VictoryScreenLayout.DominationScreen, 0));
		Assert.Equal((409, 302), VictoryScreenLayout.Place(VictoryScreenLayout.DefeatScreen, 0));
		Assert.Equal((450, 57), VictoryScreenLayout.Place(VictoryScreenLayout.DefeatScreen, 1));
	}

	[Fact]
	public void TheVictoryConditionsMapToCiv3sEndings() {
		Assert.Equal(GameEndReason.Domination, VictoryScreenLayout.ReasonFor(new DominationVictory(60, 66)));
		Assert.Equal(GameEndReason.Conquest, VictoryScreenLayout.ReasonFor(new ConquestVictory()));
		Assert.Equal(GameEndReason.Diplomatic, VictoryScreenLayout.ReasonFor(new DiplomaticVictory()));
		Assert.Equal(GameEndReason.TimeLimit, VictoryScreenLayout.ReasonFor(new ScoreVictory()));
	}

	[Fact]
	public void AWinnerIsOnTopWithTheRestBelow() {
		Player us = Civ("Romans"), greeks = Civ("Greeks"), egyptians = Civ("Egyptians"), zulus = Civ("Zulus");
		List<VictoryScreenHead> heads = Heads(VictoryScreenLayout.DominationScreen, [greeks, us, egyptians, zulus], us, us,
			dead: [egyptians]);

		Assert.Equal([us, greeks, egyptians, zulus], heads.Select(h => h.Player));
		Assert.Equal([0, 1, 2, 3], heads.Select(h => h.Slot));
		// Domination: "Happy ... domination winner", "Angry ... domination
		// losers", and the destroyed beaten up.
		Assert.Equal(["HAPPY_", "ANGRY_", "HURTN_", "ANGRY_"], heads.Select(h => h.Mood));
	}

	[Fact]
	public void TheLosersFacesSuitTheWayTheyLost() {
		Player us = Civ("Romans"), greeks = Civ("Greeks");
		Assert.Equal(["LKISS_", "ILOVE_"], Heads(VictoryScreenLayout.CulturalScreen, [us, greeks], us, us).Select(h => h.Mood));
		Assert.Equal(["LKISS_", "SKISS_"], Heads(VictoryScreenLayout.DiplomaticScreen, [us, greeks], us, us).Select(h => h.Mood));
		Assert.Equal(["HAPPY_", "HURTN_"], Heads(VictoryScreenLayout.ConquestScreen, [us, greeks], us, us).Select(h => h.Mood));
	}

	[Fact]
	public void OnTheDefeatScreenTheWinnerIsAtTheTop() {
		Player us = Civ("Romans"), greeks = Civ("Greeks"), egyptians = Civ("Egyptians"), zulus = Civ("Zulus"), aztecs = Civ("Aztecs");
		List<VictoryScreenHead> heads = Heads(VictoryScreenLayout.DefeatScreen, [greeks, us, egyptians, zulus, aztecs], us, zulus);

		Assert.Equal([us, zulus, greeks, egyptians, aztecs], heads.Select(h => h.Player));
		Assert.Equal([0, 1, 2, 3, 4], heads.Select(h => h.Slot));
		Assert.Equal(["LHURT_", "SHAPP_", "SHAPP_", "SHAPP_", "SHAPP_"], heads.Select(h => h.Mood));
	}

	[Fact]
	public void AtMostEightHeadsAreShownButAlwaysTheWinners() {
		Player us = Civ("Romans");
		List<Player> others = Enumerable.Range(0, 10).Select(i => Civ($"Civ{i}")).ToList();
		Player winner = others[^1];
		List<VictoryScreenHead> heads = Heads(VictoryScreenLayout.DefeatScreen, [us, .. others], us, winner);

		Assert.Equal(8, heads.Count);
		Assert.Equal(winner, heads.Single(h => h.Slot == 1).Player);
		Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], heads.Select(h => h.Slot));
	}

	[Fact]
	public void InALockedAllianceTheWinnersAlliesArePleased() {
		Player us = Civ("Romans"), greeks = Civ("Greeks"), zulus = Civ("Zulus");
		List<VictoryScreenHead> heads = Heads(VictoryScreenLayout.DefeatScreen, [us, greeks, zulus], us, zulus,
			lockedAlliances: true, allies: [(zulus, us)]);

		Assert.Equal("HAPPY_", heads.Single(h => h.Player == us).Mood);
		Assert.Equal("SHAPP_", heads.Single(h => h.Player == greeks).Mood);
	}

	[Fact]
	public void LosersGrumbleAboutADominationWin() {
		Player us = Civ("Romans"), greeks = Civ("Greeks");
		List<int> remarks = VictoryScreenLayout.Remarks(VictoryScreenLayout.DominationScreen, us, us, greeks, true,
			false, false, null, null, metPlayer: true);

		// "Luck, it was all luck!" and the rest of the ten, and "What do you
		// mean the game's over???" and "Domination, shmomination!" four
		// times each.
		Assert.Equal(18, remarks.Count);
		Assert.Equal(Enumerable.Range(0x283, 10), remarks.Take(10));
		Assert.Equal(4, remarks.Count(r => r == 0x2a1));
		Assert.Equal(4, remarks.Count(r => r == 0x2a2));
	}

	[Fact]
	public void LeadersCongratulateACulturalWin() {
		Player us = Civ("Romans"), greeks = Civ("Greeks");
		List<int> remarks = VictoryScreenLayout.Remarks(VictoryScreenLayout.CulturalScreen, us, us, greeks, true,
			false, false, null, null, metPlayer: true);

		Assert.Equal(Enumerable.Range(0x28d, 10), remarks);
	}

	[Fact]
	public void ADestroyedCivThatNeverMetTheWinnerBlamesSomeoneElse() {
		Player us = Civ("Romans"), greeks = Civ("Greeks");
		List<int> remarks = VictoryScreenLayout.Remarks(VictoryScreenLayout.DominationScreen, us, us, greeks, false,
			false, false, null, null, metPlayer: false);

		// "Well, I can't be blamed.  I never even met you!"
		Assert.Equal(2, remarks.Count(r => r == 0x29b));
		Assert.Equal(2, remarks.Count(r => r == 0x29c));
	}

	[Fact]
	public void ACivThePlayerDestroyedComplains() {
		Player us = Civ("Romans"), greeks = Civ("Greeks");
		List<int> remarks = VictoryScreenLayout.Remarks(VictoryScreenLayout.CulturalScreen, us, us, greeks, false,
			false, false, speakerDestroyer: us, null, metPlayer: true);

		Assert.Equal(10 + 8 + 8, remarks.Count);
		Assert.Equal(8, remarks.Count(r => r == 0x29e));
	}

	[Fact]
	public void AlliesInALockedAllianceCheerOrMoan() {
		Player us = Civ("Romans"), greeks = Civ("Greeks");
		Assert.Equal(Enumerable.Range(0x26a, 5), VictoryScreenLayout.Remarks(VictoryScreenLayout.DominationScreen, us, us,
			greeks, true, true, true, null, null, true));
		Assert.Equal(Enumerable.Range(0x265, 5), VictoryScreenLayout.Remarks(VictoryScreenLayout.DominationScreen, us, us,
			greeks, true, true, false, null, null, true));
	}

	[Fact]
	public void TheWinnersGloatOverADefeat() {
		Player us = Civ("Romans"), greeks = Civ("Greeks"), zulus = Civ("Zulus");
		List<int> remarks = VictoryScreenLayout.Remarks(VictoryScreenLayout.DefeatScreen, us, zulus, greeks, false,
			false, false, null, null, metPlayer: false);

		// "Go back to chieftain!" and the rest, and "Together we could have
		// survived the onslaught." from a civ someone else destroyed.
		Assert.Equal(Enumerable.Range(0x26f, 10), remarks.Take(10));
		Assert.Equal([0x2a4, 0x2a4, 0x2a4, 0x2a4], remarks.Skip(10));
	}

	[Fact]
	public void RetiringIsMocked() {
		Player us = Civ("Romans"), greeks = Civ("Greeks");
		Assert.Equal(Enumerable.Range(0x279, 10), VictoryScreenLayout.Remarks(VictoryScreenLayout.RetirementScreen, us, null,
			greeks, true, false, false, null, null, true));
	}

	[Fact]
	public void TheAnnouncementNamesTheVictoryOrTheLoss() {
		Assert.Equal(("END_GAME_WIN", 0x23c), VictoryScreenLayout.Announcement(GameEndReason.Domination, true, false));
		Assert.Equal(("END_GAME_WIN", 0x23f), VictoryScreenLayout.Announcement(GameEndReason.Conquest, true, false));
		Assert.Equal(("END_GAME_WIN", 0x242), VictoryScreenLayout.Announcement(GameEndReason.VictoryPoint, true, false));
		Assert.Equal(("END_GAME_WIN2", (int?)null), VictoryScreenLayout.Announcement(GameEndReason.TimeLimit, true, false));
		// "a humiliating Conquest loss", "an embarrassing Conquest defeat".
		Assert.Equal(("END_GAME_LOSS", 0x247), VictoryScreenLayout.Announcement(GameEndReason.Conquest, false, true));
		Assert.Equal(("END_GAME_LOSS", 0x250), VictoryScreenLayout.Announcement(GameEndReason.Conquest, false, false));
		// Retiring, or losing with no winner: "a humiliating loss".
		Assert.Equal(("END_GAME_LOSS", 0x243), VictoryScreenLayout.Announcement(GameEndReason.Retirement, true, true));
		Assert.Equal(("END_GAME_LOSS", 0x24c), VictoryScreenLayout.Announcement(null, false, false));
	}

	[Fact]
	public void TheWordsAreReadAsCiv3ReadsThem() {
		string[] labels = ["; comment", "#LABELS", "Please Wait...", "a Domination Victory", "#NEXT_SECTION", "Not a label"];
		string[] script = [
			"#END_GAME_WIN_TT",
			"#caption You Win!!",
			"#text",
			"^Congratulations $WHO1!  You have achieved $TYPE0.",
			"^",
			"^Time Spent: $NUM0",
			"",
			"#GAMEOVERMAN",
			"#xs 440",
			"#caption Game Over, Man!",
			"So, this game is, like, over.",
			"",
			"Good! I'm done!",
			"Wait, lemme just play 'couple more turns...",
			"",
		];
		Civ3EndGameText text = Civ3EndGameText.FromLines(labels, script);

		Assert.Equal("Please Wait...", text.Label(0));
		Assert.Equal("a Domination Victory", text.Label(1));
		Assert.Null(text.Label(2));

		Civ3EndGameText.ScriptPopup win = text.Popup("END_GAME_WIN_TT");
		Assert.Equal("You Win!!", win.Caption);
		Assert.Equal("Congratulations $WHO1!  You have achieved $TYPE0.\n \nTime Spent: $NUM0", win.Text);
		Assert.Empty(win.Choices);

		Civ3EndGameText.ScriptPopup gameOver = text.Popup("GAMEOVERMAN");
		Assert.Equal("Game Over, Man!", gameOver.Caption);
		Assert.Equal("So, this game is, like, over.", gameOver.Text);
		Assert.Equal(["Good! I'm done!", "Wait, lemme just play 'couple more turns..."], gameOver.Choices);
	}

	[Fact]
	public void RetiringIsShownAsADefeat() {
		// Retiring shows the defeat layout, with the player beaten up in the
		// middle and the rest gloating round them, and the loss's
		// announcement ("You have suffered a humiliating loss.").
		Player us = Civ("Romans"), greeks = Civ("Greeks"), egyptians = Civ("Egyptians");
		int screen = VictoryScreenLayout.ScreenFor(GameEndReason.Retirement, false);
		Assert.Equal(VictoryScreenLayout.RetirementScreen, screen);
		Assert.Equal("Art/VictoryScreen/defeat.pcx", VictoryScreenLayout.Background(screen));
		Assert.Equal(VictoryScreenLayout.Place(VictoryScreenLayout.DefeatScreen, 0), VictoryScreenLayout.Place(screen, 0));
		Assert.Equal(VictoryScreenLayout.Place(VictoryScreenLayout.DefeatScreen, 3), VictoryScreenLayout.Place(screen, 3));

		List<VictoryScreenHead> heads = Heads(screen, [greeks, us, egyptians], us, null);
		Assert.Equal([(us, 0, VictoryScreenLayout.LargeHurt), (greeks, 1, VictoryScreenLayout.SmallHappy), (egyptians, 2, VictoryScreenLayout.SmallHappy)],
			heads.Select(h => (h.Player, h.Slot, h.Mood)));
		Assert.Equal(("END_GAME_LOSS", 0x243), VictoryScreenLayout.Announcement(GameEndReason.Retirement, false, true));
		Assert.Equal(("END_GAME_LOSS", 0x24c), VictoryScreenLayout.Announcement(GameEndReason.Retirement, false, false));
	}

	[Fact]
	public void OurFramesShowTheMoods() {
		Assert.Equal(VictoryScreenLayout.FrameStyle.Glory, VictoryScreenLayout.FrameFor(VictoryScreenLayout.Happy));
		Assert.Equal(VictoryScreenLayout.FrameStyle.Glory, VictoryScreenLayout.FrameFor(VictoryScreenLayout.SmallHappy));
		Assert.Equal(VictoryScreenLayout.FrameStyle.Love, VictoryScreenLayout.FrameFor(VictoryScreenLayout.LargeKiss));
		Assert.Equal(VictoryScreenLayout.FrameStyle.Love, VictoryScreenLayout.FrameFor(VictoryScreenLayout.SmallKiss));
		Assert.Equal(VictoryScreenLayout.FrameStyle.Love, VictoryScreenLayout.FrameFor(VictoryScreenLayout.InLove));
		Assert.Equal(VictoryScreenLayout.FrameStyle.Angry, VictoryScreenLayout.FrameFor(VictoryScreenLayout.Angry));
		Assert.Equal(VictoryScreenLayout.FrameStyle.Hurt, VictoryScreenLayout.FrameFor(VictoryScreenLayout.LargeHurt));
		Assert.Equal(VictoryScreenLayout.FrameStyle.Hurt, VictoryScreenLayout.FrameFor(VictoryScreenLayout.Hurt));
	}

	[Fact]
	public void ACivsFacesAreFoundByItsEntryOrItsNames() {
		Civilization rome = new("Rome") { noun = "Romans", adjective = "Roman", civilopediaEntry = "RACE_ROMANS" };
		Assert.Equal("RACE_ROMANS", VictoryScreenLayout.RaceKeys(rome)[0]);
		// A game saved before civs kept their Civilopedia entry.
		Civilization babylon = new("Babylon") { noun = "Babylonians", adjective = "Babylonian" };
		Assert.Contains("RACE_BABYLON", VictoryScreenLayout.RaceKeys(babylon));
		Civilization maya = new("Maya") { noun = "Maya", adjective = "Mayan" };
		Assert.Contains("RACE_MAYANS", VictoryScreenLayout.RaceKeys(maya));
		Assert.Empty(VictoryScreenLayout.RaceKeys(null));
	}

	[Fact]
	public void WithoutCiv3sTextOurOwnWordsStandIn() {
		Civ3EndGameText own = Civ3EndGameText.OwnWording();
		// Every announcement and every remark the screen can pick.
		foreach (string key in new[] { "END_GAME_WIN", "END_GAME_WIN2", "END_GAME_LOSS" }) {
			Civ3EndGameText.ScriptPopup popup = own.Popup(key);
			Assert.False(string.IsNullOrWhiteSpace(popup.Caption));
			Assert.False(string.IsNullOrWhiteSpace(popup.Text));
		}
		Assert.Contains("$TYPE0", own.Popup("END_GAME_LOSS").Text);
		for (int label = 0x23c; label <= 0x254; label++) {
			Assert.False(string.IsNullOrWhiteSpace(own.Label(label)), $"label {label:x}");
		}
		for (int label = 0x265; label <= 0x2ad; label++) {
			Assert.False(string.IsNullOrWhiteSpace(own.Label(label)), $"label {label:x}");
		}
		// Retiring: the loss's announcement and the rivals' mockery.
		Player us = Civ("Romans"), greeks = Civ("Greeks");
		(string scriptKey, int? typeLabel) = VictoryScreenLayout.Announcement(GameEndReason.Retirement, false, false);
		Assert.Equal("a shameful defeat", own.Label(typeLabel.Value));
		Assert.NotNull(own.Popup(scriptKey));
		Assert.All(VictoryScreenLayout.Remarks(VictoryScreenLayout.RetirementScreen, us, null, greeks, true, false, false, null, null, true),
			label => Assert.False(string.IsNullOrWhiteSpace(own.Label(label))));
		// The game over question keeps its own fallback (EndOfGame.ShowGameOver).
		Assert.Null(own.Popup("GAMEOVERMAN"));
	}
}
