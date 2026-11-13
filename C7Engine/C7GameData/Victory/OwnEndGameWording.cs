using System.Collections.Generic;

namespace C7Engine;

// OpenCiv3's own words for the end of the game, for when Civ3's labels.txt
// and script.txt aren't there (standalone mode). They stand in for Civ3's
// lines one for one, by the same #LABELS indexes and script.txt keys that
// VictoryScreenLayout picks, so the screen works the same with either.
// They're written fresh, not taken from Civ3.
public static class OwnEndGameWording {
	// The announcements, with Civ3's $WHO1 (the player's leader) and $TYPE0
	// (one of the labels below).
	public static readonly Dictionary<string, (string caption, string text)> Popups = new() {
		["END_GAME_WIN"] = ("Victory!", "Well done, $WHO1!  History will record $TYPE0."),
		["END_GAME_WIN2"] = ("The Age Is Over", "The last turn has passed, and your people stand above all others, $WHO1!"),
		["END_GAME_LOSS"] = ("A Dark Day", "Our people will long remember this: $TYPE0."),
	};

	// The labels, by their index in Civ3's #LABELS.
	public static readonly Dictionary<int, string> Labels = new() {
		// How the game was won (VictoryScreenLayout.Announcement).
		[0x23c] = "a victory by domination",
		[0x23d] = "a victory in the race to the stars",
		[0x23e] = "a victory by diplomacy",
		[0x23f] = "a victory by conquest",
		[0x240] = "a victory of culture",
		[0x241] = "a victory of great wonders",
		[0x242] = "a victory on points",
		// How it was lost: the plain one, then by domination, the space
		// race, diplomacy, conquest, culture, wonders, time and points.
		[0x243] = "a bitter loss",
		[0x244] = "a bitter loss to a dominant rival",
		[0x245] = "a bitter loss in the race to the stars",
		[0x246] = "a bitter loss at the council table",
		[0x247] = "a bitter loss on the battlefield",
		[0x248] = "a bitter loss to a greater culture",
		[0x249] = "a bitter loss in the building of wonders",
		[0x24a] = "a bitter loss as time ran out",
		[0x24b] = "a bitter loss on points",
		[0x24c] = "a shameful defeat",
		[0x24d] = "a shameful defeat by a dominant rival",
		[0x24e] = "a shameful defeat in the race to the stars",
		[0x24f] = "a shameful defeat at the council table",
		[0x250] = "a shameful defeat on the battlefield",
		[0x251] = "a shameful defeat by a greater culture",
		[0x252] = "a shameful defeat in the building of wonders",
		[0x253] = "a shameful defeat as time ran out",
		[0x254] = "a shameful defeat on points",

		// A locked alliance's losers, then its winners.
		[0x265] = "I want a trade to the other alliance.",
		[0x266] = "Next time, pick your friends more carefully.",
		[0x267] = "Chained to this lot for the whole game. Never again.",
		[0x268] = "I signed the wrong treaty. Clearly.",
		[0x269] = "Allies like these are why walls were invented.",
		[0x26a] = "Together we stood, and together we won!",
		[0x26b] = "Best alliance this world has ever seen.",
		[0x26c] = "We could not have asked for a finer partner.",
		[0x26d] = "Hear that? It's the other side packing up.",
		[0x26e] = "Shoulder to shoulder, nobody could touch us.",

		// The winners gloat over a defeat.
		[0x26f] = "Better luck in your next life.",
		[0x270] = "Chin up. Someone has to come last.",
		[0x271] = "Your capital will make a lovely summer palace.",
		[0x272] = "We marched, you ran, and the bards wrote it down.",
		[0x273] = "Your borders were more of a suggestion.",
		[0x274] = "Is that a crown, or just a very worried hat?",
		[0x275] = "Every road led to us. You should have taken one.",
		[0x276] = "We'll name a small village after you. A very small one.",
		[0x277] = "And that, children, is how not to run an empire.",
		[0x278] = "Was that your last city? Oops.",

		// The rivals see a player retire.
		[0x279] = "Leaving so soon? We were just getting started.",
		[0x27a] = "Retired? Is that what we're calling it now?",
		[0x27b] = "Don't forget your crown on the way out.",
		[0x27c] = "Your statue will be... somewhere. Probably.",
		[0x27d] = "A little more practice and you might have lasted the winter.",
		[0x27e] = "Was it something we said?",
		[0x27f] = "Look at those royal heels go!",
		[0x280] = "Yes, run along now.",
		[0x281] = "Even the mighty need a nap.",
		[0x282] = "One day a ruler will see a game through to the end. Not today.",

		// Losers grumble at the player's win.
		[0x283] = "Pure luck, and you know it.",
		[0x284] = "I lost to you? The historians will never believe it.",
		[0x285] = "Oh, wonderful. Congratulations. Truly.",
		[0x286] = "Fortune smiled on you. She'll frown soon enough.",
		[0x287] = "I demand a rematch. And a better map.",
		[0x288] = "I wasn't even trying, you understand.",
		[0x289] = "Someone check the dice.",
		[0x28a] = "Fine. Set the pieces up again.",
		[0x28b] = "Bet you can't do that twice.",
		[0x28c] = "Next time. Just you wait.",

		// Rivals congratulate a cultural or diplomatic win.
		[0x28d] = "The world is yours now!",
		[0x28e] = "A masterful game, start to finish.",
		[0x28f] = "A feast in your honor!",
		[0x290] = "You played well. Next time I won't be so kind.",
		[0x291] = "Our poets are already writing about you.",
		[0x292] = "My sincere congratulations, friend.",
		[0x293] = "And the winner is... you!",
		[0x294] = "It was a privilege to share a world with you.",
		[0x295] = "The stars were with you today. Until next time.",
		[0x296] = "I was brilliant! Splendid! Magnificent! And still I lost...",

		// A civ the player destroyed complains.
		[0x297] = "I fell, and everyone else just watched.",
		[0x298] = "I was already beaten. Was the last city really necessary?",
		[0x299] = "You hit hard, I'll give you that.",
		[0x29a] = "Give me a moment to find my crown, then we go again.",
		// A civ destroyed by someone else.
		[0x29b] = "Don't look at me. We never even met!",
		[0x29c] = "Everyone was so busy with me, they forgot to stop you.",
		// A destroyed civ, about the way the player won.
		[0x29d] = "A true conqueror needs no friends to win!",
		[0x29e] = "They told me being absorbed by your culture wouldn't hurt.",
		// Losers of a space race and a domination win.
		[0x29f] = "Wait, weren't we meant to launch first?",
		[0x2a0] = "Fine, take the stars. We'll keep the ground.",
		[0x2a1] = "Over? I still have cities! I still have an army!",
		[0x2a2] = "Dominated? I prefer \"temporarily outnumbered\".",

		// Destroyed civs, after a defeat.
		[0x2a3] = "You spent so long destroying me, and for what?",
		[0x2a4] = "If we had stood together, we might both still be here.",
		[0x2a5] = "Save me, and I'd have voted for you.",
		[0x2a6] = "A ruler without friends rules alone, and loses alone.",
		[0x2a7] = "No army can save a people without culture.",
		[0x2a8] = "Your words were fine. My sword was finer.",
		[0x2a9] = "Out of cities already? How careless of me.",
		[0x2aa] = "Done and dusted!",
		// Destroyed civs, about a player who retired.
		[0x2ab] = "You could beat me, but you couldn't finish the game.",
		[0x2ac] = "Brave when winning, gone at the first sign of trouble.",
		[0x2ad] = "I'd rather have run too, but I stayed and fell.",
	};
}
