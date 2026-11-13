using System;
using System.Collections.Generic;
using C7GameData;

namespace C7Engine;

// How a game ended, numbered as Civ3 1.22 numbers it in its game-over code
// (Civ3 FUN_004f9fb0, FUN_005dd790): the reason picks the announcement, the
// victory screen's art and the leaders' moods.
public enum GameEndReason {
	Domination = 0,
	Conquest = 1,
	Cultural = 2,
	Diplomatic = 3,
	SpaceRace = 4,
	// Time ran out, and the best score (or victory points) won.
	TimeLimit = 5,
	Retirement = 6,
	Wonder = 7,
	VictoryPoint = 8,
}

// One leader's head on the victory screen: who, in which of the screen's
// eight places, and which of the leader's victory-screen faces it is (the
// PediaIcons.txt key prefix, like "HAPPY_", put before the civ's
// RACE_ key).
public record VictoryScreenHead(Player Player, int Slot, string Mood);

// Civ3's end-of-game screen, the "victory screen" (Civ3 FUN_005dcf60 sets
// it up, FUN_005dd790 shows it, FUN_005dd960 draws it), worked out from Civ3
// 1.22: a background for the kind of ending, the leaders' heads with faces
// to suit, and every few seconds one of the other leaders says something in
// a speech bubble. Everything here is plain data, so the screen's rules can
// be tested without drawing it.
public static class VictoryScreenLayout {
	// The kinds of screen (Civ3 keeps the number at offset 0x578 of the
	// screen), which index the art and layout tables below.
	public const int ConquestScreen = 0;
	public const int CulturalScreen = 1;
	public const int DiplomaticScreen = 2;
	public const int SpaceRaceScreen = 3;
	public const int DominationScreen = 4;
	public const int TimeLimitScreen = 5;
	public const int RetirementScreen = 6;
	public const int DefeatScreen = 7;
	public const int WonderScreen = 8;
	public const int VictoryPointScreen = 9;

	// The background of each kind of screen, 1024x768 (Civ3's table at
	// 0x74c1b8). A Wonder victory shows the defeat art and layout, and a
	// Victory Point one the domination art, as Conquests shipped no art of
	// their own for them.
	private static readonly string[] Backgrounds = [
		"Art/VictoryScreen/conquer_victory.pcx",
		"Art/VictoryScreen/culture_victory.pcx",
		"Art/VictoryScreen/diplomatic_victory.pcx",
		"Art/VictoryScreen/space_victory.pcx",
		"Art/VictoryScreen/domination_victory.pcx",
		"Art/VictoryScreen/histograph_victory.pcx",
		"Art/VictoryScreen/defeat.pcx",
		"Art/VictoryScreen/defeat.pcx",
		"Art/VictoryScreen/defeat.pcx",
		"Art/VictoryScreen/domination_victory.pcx",
	];

	// The speech bubble's top, middle (repeated for each line of text) and
	// bottom (Civ3 FUN_005dcf60). Only PTW's Art folder has them.
	public const string BubbleTop = "Art/VictoryScreen/textbubble_top.pcx";
	public const string BubbleMiddle = "Art/VictoryScreen/textbubble_mid.pcx";
	public const string BubbleBottom = "Art/VictoryScreen/textbubble_bot.pcx";

	// The close button: Civ3's ExitX.pcx (loaded into the table at
	// 0xb60f48 by FUN_00555300), three 36x38 states at x = 1, 38 and 75,
	// drawn at (972, 15) on the screen and clicked within 42x42 of there
	// (FUN_005dd960, FUN_005de5c0).
	public const string ExitButtonArt = "Art/city view/city interface/ExitX.pcx";
	public const int ExitButtonX = 972, ExitButtonY = 15, ExitButtonSize = 42;

	public const int Width = 1024, Height = 768;
	public const int Slots = 8;

	// Where each of the eight heads goes, by kind of screen: the top left
	// corner of its art on the background (Civ3's tables at 0x68cc14 and
	// 0x68cd54). Place 0 is the player's own, large (200x240) head; the
	// others are small (120x150). A victory has the player's head at the
	// top and the rest in two rows below; a defeat has it in the middle
	// with the others around it, the winner's at the top.
	private static readonly (int x, int y)[] VictoryPlaces = [
		(409, 67), (281, 367), (449, 367), (614, 367), (218, 541), (371, 541), (528, 541), (681, 541),
	];
	private static readonly (int x, int y)[] DefeatPlaces = [
		(409, 302), (450, 57), (222, 141), (678, 141), (128, 355), (765, 355), (222, 562), (678, 562),
	];

	// The face prefixes in PediaIcons.txt. Its comments say what each is
	// for: HAPPY_ "conquer/domination/space winner", SHAPP_ "defeat",
	// LKISS_ "Culture/diplomatic Winners", SKISS_ "diplomatic losers",
	// ANGRY_ "domination/space losers", ILOVE_ "Cultural Victory Loser",
	// LOVED_ "Cultural Victory Winner", LHURT_ "defeat" (large) and HURTN_
	// "conquered/eliminated".
	public const string Happy = "HAPPY_";
	public const string SmallHappy = "SHAPP_";
	public const string LargeKiss = "LKISS_";
	public const string SmallKiss = "SKISS_";
	public const string Angry = "ANGRY_";
	public const string InLove = "ILOVE_";
	public const string Loved = "LOVED_";
	public const string LargeHurt = "LHURT_";
	public const string Hurt = "HURTN_";

	// The bubbles' timing, in milliseconds (Civ3 FUN_005dd790 and
	// FUN_005dd960): the first one comes 1.5 to 2 seconds after the screen
	// opens, each is up for 5.12 seconds and then fades away over 1.024
	// seconds, and the next comes 6.656 to 7.168 seconds after the last
	// one started. Enter brings on the next one at once (FUN_005de580).
	public const int FirstBubbleDelay = 0x600;
	public const int BubbleShown = 0x1400;
	public const int BubbleFadeEnd = 0x1800;
	public const int BubbleInterval = 0x1a00;
	// A random 0 or 512 more (rand() & 0x200) before the first and between
	// bubbles.
	public const int BubbleJitter = 0x200;

	// The bubble's text is wrapped to the bubble's width less 24 pixels and
	// starts 12 pixels in from its top left corner (FUN_005dd960).
	public const int BubbleTextInset = 12;

	public static string Background(int screen) => Backgrounds[screen];

	// The keys a civ's faces may have in PediaIcons.txt after the mood's
	// prefix, best first: its Civilopedia entry (like RACE_ROMANS), then,
	// for a game saved before civs kept theirs, RACE_ and its noun, name or
	// adjective, or the adjective made plural (RACE_BABYLON is Babylon's
	// name, RACE_RUSSIAN Russia's adjective, RACE_MAYANS the Maya's).
	public static List<string> RaceKeys(Civilization civ) {
		List<string> keys = [];
		if (civ == null) {
			return keys;
		}
		if (!string.IsNullOrWhiteSpace(civ.civilopediaEntry)) {
			keys.Add(civ.civilopediaEntry.Trim());
		}
		foreach (string name in new[] { civ.noun, civ.name, civ.adjective, civ.adjective == null ? null : civ.adjective + "s" }) {
			if (!string.IsNullOrWhiteSpace(name)) {
				string key = CivilopediaCatalog.KeyFromName("RACE_", name).ToUpperInvariant();
				if (!keys.Contains(key)) {
					keys.Add(key);
				}
			}
		}
		return keys;
	}

	// The style a head's frame is drawn in on our own (Modern graphics)
	// screen, for its face's mood: our portraits have one face, so the
	// frame shows the mood. Glory for the winners (happy and gloating),
	// love for the kisses and hearts, anger for the sore losers, and hurt
	// for the beaten.
	public enum FrameStyle { Glory, Love, Angry, Hurt }

	public static FrameStyle FrameFor(string mood) => mood switch {
		LargeKiss or SmallKiss or InLove or Loved => FrameStyle.Love,
		Angry => FrameStyle.Angry,
		LargeHurt or Hurt => FrameStyle.Hurt,
		_ => FrameStyle.Glory,
	};

	public static (int x, int y) Place(int screen, int slot) =>
		IsDefeatLayout(screen) ? DefeatPlaces[slot] : VictoryPlaces[slot];

	private static bool IsDefeatLayout(int screen) =>
		screen == RetirementScreen || screen == DefeatScreen || screen == WonderScreen;

	// How the game ended, for a victory condition that was met.
	// UNVERIFIED: that Civ3 counts a Regicide or Elimination win as a
	// Conquest, and a win on score at the end of time (or with every civ
	// out of time) as its time-limit ending; Civ3's own conditions are
	// numbered without them.
	public static GameEndReason ReasonFor(IVictory victory) => victory switch {
		DominationVictory => GameEndReason.Domination,
		ConquestVictory or RegicideCondition or EliminationCondition => GameEndReason.Conquest,
		CulturalVictory => GameEndReason.Cultural,
		DiplomaticVictory => GameEndReason.Diplomatic,
		SpaceRaceVictory => GameEndReason.SpaceRace,
		WonderVictory => GameEndReason.Wonder,
		VictoryPointVictory => GameEndReason.VictoryPoint,
		_ => GameEndReason.TimeLimit,
	};

	// The kind of screen the player sees (Civ3 FUN_005dd790): one for the
	// way they won, or the defeat screen if someone else won, or if they
	// retired.
	public static int ScreenFor(GameEndReason reason, bool playerWon) {
		if (!playerWon) {
			return reason == GameEndReason.Retirement ? RetirementScreen : DefeatScreen;
		}
		return reason switch {
			GameEndReason.Domination => DominationScreen,
			GameEndReason.Conquest => ConquestScreen,
			GameEndReason.Cultural => CulturalScreen,
			GameEndReason.Diplomatic => DiplomaticScreen,
			GameEndReason.SpaceRace => SpaceRaceScreen,
			GameEndReason.TimeLimit => TimeLimitScreen,
			GameEndReason.Retirement => RetirementScreen,
			GameEndReason.Wonder => WonderScreen,
			_ => VictoryPointScreen,
		};
	}

	// The heads on the screen (Civ3 FUN_005dcf60): the player's own in
	// place 0, then the other civs in order, up to eight in all. On the
	// defeat screen the winner is at the top (place 1) and the rest follow.
	// A civ that's been destroyed is shown beaten up; otherwise the face
	// suits the ending, but in a game of locked alliances the winner's
	// allies are pleased with them.
	//
	// civs are the game's civs in order, without the barbarians; winner may
	// be null (no one won).
	public static List<VictoryScreenHead> Heads(int screen, IReadOnlyList<Player> civs, Player player, Player winner,
			Func<Player, bool> alive, bool lockedAlliances, Func<Player, Player, bool> allied) {
		List<VictoryScreenHead> heads = [];
		bool winnerAtTop = screen == DefeatScreen || (screen == RetirementScreen && winner != player);
		bool winnerSeen = false;
		int next = 1;
		foreach (Player civ in civs) {
			if (civ == player) {
				heads.Add(new VictoryScreenHead(civ, 0, PlayerMood(screen, player, winner, lockedAlliances, allied)));
				continue;
			}
			int slot = next;
			if (winnerAtTop && winner != null) {
				if (civ == winner) {
					slot = 1;
					winnerSeen = true;
				} else if (!winnerSeen) {
					slot = next + 1;
				}
			}
			if (slot >= Slots) {
				continue;
			}
			next++;
			heads.Add(new VictoryScreenHead(civ, slot, RivalMood(screen, civ, winner, alive(civ), lockedAlliances, allied)));
		}
		heads.Sort((a, b) => a.Slot.CompareTo(b.Slot));
		return heads;
	}

	private static string PlayerMood(int screen, Player player, Player winner, bool lockedAlliances,
			Func<Player, Player, bool> allied) {
		if (player != winner && lockedAlliances && winner != null && allied(winner, player)) {
			return Happy;
		}
		return screen switch {
			CulturalScreen or DiplomaticScreen => LargeKiss,
			RetirementScreen or DefeatScreen => LargeHurt,
			_ => Happy,
		};
	}

	private static string RivalMood(int screen, Player civ, Player winner, bool alive, bool lockedAlliances,
			Func<Player, Player, bool> allied) {
		if (!alive) {
			return Hurt;
		}
		if (civ != winner && lockedAlliances && winner != null && allied(winner, civ)) {
			return SmallKiss;
		}
		return screen switch {
			ConquestScreen => Hurt,
			CulturalScreen => InLove,
			DiplomaticScreen => SmallKiss,
			RetirementScreen or DefeatScreen => SmallHappy,
			_ => Angry,
		};
	}

	// On the cultural victory screen, each surviving rival's head has the
	// winner's small "loved" face drawn over it, inside its heart
	// (FUN_005dd960).
	public static bool ShowsLovedFace(int screen, VictoryScreenHead head, bool alive) =>
		screen == CulturalScreen && head.Slot > 0 && alive;

	// What a civ's leader can say in the bubble, as indexes into
	// labels.txt's #LABELS list (Civ3 FUN_005dd960). A line listed more
	// than once is that much likelier to be picked.
	//
	// speakerDestroyer and playerDestroyer are who destroyed the speaker's
	// civ and the player's, or null if they're alive or it isn't known;
	// metPlayer is whether the speaker had met the player. A civ that never
	// met the player can't have been destroyed by them.
	public static List<int> Remarks(int screen, Player player, Player winner, Player speaker, bool speakerAlive,
			bool lockedAlliances, bool speakerAlliedWithWinner, Player speakerDestroyer, Player playerDestroyer,
			bool metPlayer) {
		List<int> remarks = [];
		bool destroyedByPlayer = !speakerAlive && speakerDestroyer != null && speakerDestroyer == player;
		bool destroyedByOther = !speakerAlive
			&& (speakerDestroyer != null ? speakerDestroyer != player : !metPlayer);

		if (winner == player && screen != RetirementScreen) {
			if (lockedAlliances) {
				// "WE are the champions." from the winner's allies, "Get me
				// a new team!" from the rest.
				AddRange(remarks, speakerAlliedWithWinner ? 0x26a : 0x265, 5);
				return remarks;
			}
			if (speakerAlive && (screen == CulturalScreen || screen == DiplomaticScreen)) {
				// "You're the big dog now!" and other congratulations.
				AddRange(remarks, 0x28d, 10);
				return remarks;
			}
			// "Luck, it was all luck!" and other sore losers.
			AddRange(remarks, 0x283, 10);
			if (!speakerAlive) {
				if (destroyedByPlayer) {
					// "I got trounced, and all these other clowns just
					// watched..." and the like.
					for (int i = 0; i < 2; i++) {
						AddRange(remarks, 0x297, 4);
					}
					if (screen == DiplomaticScreen) {
						// "Real warriors don't need allies to win!"
						AddCopies(remarks, 0x29d, 8);
					}
					if (screen == CulturalScreen) {
						// "I thought cultural assimilation was supposed to
						// be painless!"
						AddCopies(remarks, 0x29e, 8);
					}
				} else if (destroyedByOther && !metPlayer) {
					// "Well, I can't be blamed.  I never even met you!"
					for (int i = 0; i < 2; i++) {
						AddRange(remarks, 0x29b, 2);
					}
				}
			} else if (screen == SpaceRaceScreen) {
				// "Doh!  I thought we were [supposed] to kick your people
				// off the planet..."
				for (int i = 0; i < 3; i++) {
					AddRange(remarks, 0x29f, 2);
				}
			} else if (screen == DominationScreen) {
				// "Domination, shmomination!  I refuse to be defeated!"
				for (int i = 0; i < 4; i++) {
					AddRange(remarks, 0x2a1, 2);
				}
			}
			return remarks;
		}

		if (screen == RetirementScreen) {
			// "There's no shame in retiring... none at all... really... no,
			// really..." and the like.
			AddRange(remarks, 0x279, 10);
			if (destroyedByPlayer) {
				for (int i = 0; i < 3; i++) {
					AddRange(remarks, 0x2ab, 2);
				}
			} else if (destroyedByOther) {
				for (int i = 0; i < 3; i++) {
					AddRange(remarks, 0x2ad, 2);
				}
			}
			return remarks;
		}

		// "Go back to chieftain!" and other gloating.
		AddRange(remarks, 0x26f, 10);
		// Civ3 has lines for losing particular ways too, though the defeat
		// screen it shows for every loss never picks them.
		switch (screen) {
			case DiplomaticScreen:
				for (int i = 0; i < 4; i++) {
					remarks.Add(0x2a6);
					if (destroyedByOther) {
						remarks.Add(0x2a5);
					}
				}
				break;
			case CulturalScreen:
				AddCopies(remarks, 0x2a7, 5);
				break;
			case ConquestScreen:
				for (int i = 0; i < 5; i++) {
					remarks.Add(0x2a8);
					if (playerDestroyer != null && playerDestroyer == speaker) {
						AddCopies(remarks, 0x2a9, 2);
					}
				}
				break;
			case TimeLimitScreen:
				if (speaker == winner) {
					AddCopies(remarks, 0x2aa, 5);
				}
				break;
		}
		if (destroyedByPlayer) {
			// "You wasted your time killing me off, and look what it got
			// you."
			AddCopies(remarks, 0x2a3, 4);
		} else if (destroyedByOther) {
			// "Together we could have survived the onslaught.  We could
			// have thrived."
			AddCopies(remarks, 0x2a4, 4);
		}
		return remarks;
	}

	private static void AddRange(List<int> remarks, int first, int count) {
		for (int i = 0; i < count; i++) {
			remarks.Add(first + i);
		}
	}

	private static void AddCopies(List<int> remarks, int label, int count) {
		for (int i = 0; i < count; i++) {
			remarks.Add(label);
		}
	}

	// The announcement before the screen (Civ3 FUN_004f9fb0): script.txt's
	// END_GAME_WIN ("You have achieved $TYPE0."), END_GAME_WIN2 when time
	// ran out, or END_GAME_LOSS ("You have suffered $TYPE0."), and the
	// labels.txt line that is $TYPE0: "a Domination Victory" and so on for
	// a win, and for a loss, at random, "a humiliating ... loss" or "an
	// embarrassing ... defeat". END_GAME_WIN2 has no $TYPE0, so its label
	// is null. A null reason is a loss with no one winning, as when every
	// human player has been destroyed: "a humiliating loss" or "an
	// embarrassing defeat".
	public static (string scriptKey, int? typeLabel) Announcement(GameEndReason? reason, bool playerWon, bool humiliating) {
		if (playerWon && reason != null && reason != GameEndReason.Retirement) {
			if (reason == GameEndReason.TimeLimit) {
				return ("END_GAME_WIN2", null);
			}
			// "a Domination Victory", "a Space Race Victory", "a Diplomatic
			// Victory", "a Conquest Victory", "a Cultural Victory", "a
			// Wonder Victory", "a Victory Point Victory".
			int win = reason switch {
				GameEndReason.Domination => 0,
				GameEndReason.SpaceRace => 1,
				GameEndReason.Diplomatic => 2,
				GameEndReason.Conquest => 3,
				GameEndReason.Cultural => 4,
				GameEndReason.Wonder => 5,
				_ => 6,
			};
			return ("END_GAME_WIN", 0x23c + win);
		}
		// The plain loss or defeat, then Domination, Space Race,
		// Diplomatic, Conquest, Cultural, Wonder, Time Limit and Victory
		// Point.
		int loss = reason switch {
			GameEndReason.Domination => 1,
			GameEndReason.SpaceRace => 2,
			GameEndReason.Diplomatic => 3,
			GameEndReason.Conquest => 4,
			GameEndReason.Cultural => 5,
			GameEndReason.Wonder => 6,
			GameEndReason.TimeLimit => 7,
			GameEndReason.VictoryPoint => 8,
			_ => 0,
		};
		return ("END_GAME_LOSS", (humiliating ? 0x243 : 0x24c) + loss);
	}
}
