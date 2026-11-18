using System.Collections.Generic;
using C7Engine;
using C7Engine.AI;
using C7GameData;

// What computer players say on the diplomacy screens, in Civ3's words from
// Conquests' Text/diplomacy.txt (see DiplomacyText), by the speaking civ and
// its mood toward the human. Without Civ3's text (as in standalone mode),
// each label's first line.
public static class DiplomacyLines {
	private const string MediaPath = "Text/diplomacy.txt";

	private static string loadedPath;
	private static DiplomacyText text;

	// Read again when the file to use changes, as for a scenario with its
	// own text.
	private static DiplomacyText Text {
		get {
			string path = Util.HasCiv3Media(MediaPath) ? Util.Civ3MediaPath(MediaPath) : null;
			if (text == null || path != loadedPath) {
				text = path == null ? new DiplomacyText() : DiplomacyText.Load(path);
				loadedPath = path;
			}
			return text;
		}
	}

	// The first line of each label used, from Conquests' diplomacy.txt.
	private static readonly Dictionary<string, string> Fallback = new() {
		["AIWILLACCEPT"] = "This offer seems fair.",
		["AIWEAKREJECT"] = "I think we are very close to a deal. Can you provide a little more?",
		["AINEUTRALREJECT"] = "I honestly cannot accept such an offer.",
		["AISTRONGREJECT"] = "I cannot agree to this at all, friend $PLAYER0. It appears to be unfair on the face of it.",
		["AITOTALREJECT"] = "Unfortunately, this does not work for us at present.",
		["AICOUNTERPEACE"] = "How about this?",
		["AINOCOUNTERPEACE"] = "Regrettably, we don't see how a deal could be worked out...",
		["USERASKFORCOUNTER"] = "What do you want for $GIVE0?",
		["USERASKFOREXCHANGE"] = "Would you care to offer something in exchange for $GIVE0?",
	};

	// The label of the AI's reply to a deal, by how it takes it.
	public static string Key(DealReaction reaction) => reaction switch {
		DealReaction.Accept => "AIWILLACCEPT",
		DealReaction.WeakReject => "AIWEAKREJECT",
		DealReaction.NeutralReject => "AINEUTRALREJECT",
		DealReaction.StrongReject => "AISTRONGREJECT",
		_ => "AITOTALREJECT",
	};

	// What the AI says, for the label: pick chooses among the label's lines
	// for the civ and mood, so the same pick gives the same line.
	public static string Say(GameData gameData, string key, Player ai, Player human, int pick, int? mood = null) {
		DiplomacyEntry entry = Text.Get(key);
		string line = entry?.Line(gameData.DiplomacyTextIndex(ai.civilization), mood ?? Mood(gameData, ai, human), pick);
		if (line == null) {
			entry = null;
			line = Fallback.GetValueOrDefault(key, "");
		}
		return DiplomacyText.Fill(entry, line, Speakers(ai, human));
	}

	// The label's line for each of the three moods, friendliest first (the
	// same one three times for a label without moods).
	public static List<string> SayInEveryMood(GameData gameData, string key, Player ai, Player human, int pick) {
		return [Say(gameData, key, ai, human, pick, 0), Say(gameData, key, ai, human, pick, 1), Say(gameData, key, ai, human, pick, 2)];
	}

	// The three moods of a "#mood 1" label, friendliest first. UNVERIFIED:
	// which of the five attitudes go with which: gracious and polite with the
	// first, cautious the second, annoyed and furious the third.
	private static int Mood(GameData gameData, Player ai, Player human) {
		return Attitudes.Level(gameData, ai, human) switch {
			AttitudeLevel.Gracious or AttitudeLevel.Polite => 0,
			AttitudeLevel.Annoyed or AttitudeLevel.Furious => 2,
			_ => 1,
		};
	}

	private static DiplomacySpeakers Speakers(Player ai, Player human) {
		Civilization ours = human?.civilization;
		Civilization theirs = ai.civilization;
		return new DiplomacySpeakers(
			ours?.leader ?? human?.name ?? "", ours?.name ?? "", ours?.adjective ?? "", ours?.noun ?? "",
			theirs?.leader ?? "", theirs?.name ?? "", theirs?.adjective ?? "", theirs?.noun ?? "");
	}
}
