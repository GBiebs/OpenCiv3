using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7GameData;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// What Civ3's advisors say on their screens (Civ3 FUN_0042ce80 and the
// advisor functions it calls), in the words of script.txt.
public class AdvisorSpeechTest {
	// Random numbers that never make a joke (Civ3's one in 500) and always
	// pick the first of a list, unless told otherwise.
	private class FixedRandom : Random {
		public int Value;
		public bool Joke;

		public override int Next(int maxValue) {
			if (maxValue == AdvisorSpeech.JokeOdds) {
				return Joke ? 0 : 1;
			}
			return Math.Min(Value, maxValue - 1);
		}
	}

	private static List<string> Keys(AdvisorSpeech speech, AdvisorKind advisor, AdvisorFacts facts, int count, Random rng = null) {
		rng ??= new FixedRandom();
		return Enumerable.Range(0, count).Select(_ => speech.Next(advisor, facts, rng)?.Key).ToList();
	}

	private static CityAdviceFacts City(string name, int surplus = 2, bool full = false, GrowthBlocker blocker = GrowthBlocker.None,
		int happy = 0, int unhappy = 0, bool disorder = false) => new(name, surplus, full, blocker, happy, unhappy, disorder);

	// ---- script.txt ----

	[Fact]
	public void EntriesHaveTheirTextAndTheFaceOfTheirAdvisorLine() {
		ScriptText script = ScriptText.Parse("""
			; A comment
			#TRADEADVICEMORE
			#advisor Trade Sad
			We have only one source of $RESOURCE0.


			#DOMESTICADVICERANK
			Build more cities!

			#comment DOES = Thrives
			#CULTURALADVICERANK
			#advisor Cultural Happy
			Building cultural improvements is good.
			#comment $LINK<cultural improvements=GCON_Culture>

			#CULTURALADVICELIBRARIES
			By providing our people with books,
			let's build [Libraries.]

			#DOMESTICADVICERANK
			A second entry under the same key.
			""".Replace("\t", ""));

		ScriptEntry more = script.Get("TRADEADVICEMORE");
		Assert.Equal("We have only one source of $RESOURCE0.", more.Text);
		Assert.Equal(AdvisorMood.Sad, more.Mood);

		ScriptEntry rank = script.Get("domesticadvicerank");
		Assert.Equal("Build more cities!", rank.Text);
		Assert.Null(rank.Mood);

		Assert.Equal("Building cultural improvements is good.", script.Get("CULTURALADVICERANK").Text);
		Assert.Equal(AdvisorMood.Happy, script.Get("CULTURALADVICERANK").Mood);
		Assert.Equal("By providing our people with books, let's build [Libraries.]", script.Get("CULTURALADVICELIBRARIES").Text);
		Assert.False(script.Has("comment"));
	}

	[Fact]
	public void PlaceholdersAreFilledByTheirSlot() {
		Assert.Equal("Rome is growing slowly, King.  We need an aqueduct.",
			ScriptText.Format("$CITY0 $DOES1, $TITLE2$PUNCT3  $OPTIONALSTRING4", ["Rome", "is growing slowly", "King", ".", "We need an aqueduct."]));
		// $NUMn is filled from the number slots, the rest from the text slots.
		Assert.Equal("We have 3 excess Iron, and the Romans would love to have it.",
			ScriptText.Format("We have $NUM0 excess $RESOURCE1, and the $CIVNoun0 would love to have it.", ["Romans", "Iron"], [3]));
		// Highlights and links show their words.
		Assert.Equal("We should build Pyramids! We need a viable trade route.",
			ScriptText.Format("We should build [$WONDER0!] We need a $LINK<viable trade route=GCON_Trade>.", ["Pyramids"]));
	}

	[Fact]
	public void ALineIsSaidWithItsEntrysFaceOverTheCodes() {
		ScriptText script = ScriptText.Parse("#TRADEADVICEMORE\n#advisor Trade Sad\nOnly one $RESOURCE0.\n\n#DOMESTICADVICECITYSIZE\n$CITY0 $DOES1.\n");
		Assert.Equal(("Only one Iron.", AdvisorMood.Sad), new AdvisorLine("TRADEADVICEMORE").Text(0, "Iron").Say(script));
		Assert.Equal(("Rome thrives.", AdvisorMood.Surprised),
			new AdvisorLine("DOMESTICADVICECITYSIZE").Text(0, "Rome").Text(1, "thrives").WithMood(AdvisorMood.Surprised).Say(script));
		Assert.Null(new AdvisorLine("NOSUCHKEY").Say(script));
	}

	// ---- Domestic ----

	[Fact]
	public void TheDomesticAdvisorGoesRoundCitySizeHappinessAndAnarchy() {
		AdvisorFacts facts = new() { Cities = [City("Rome")], RulerTitles = ["Chief"], AnarchyTurns = 2 };
		Assert.Equal(["DOMESTICADVICECITYSIZE", "DOMESTICADVICEHAPPINESS", "DOMESTICADVICEANARCHY", "DOMESTICADVICECITYSIZE"],
			Keys(new AdvisorSpeech(), AdvisorKind.Domestic, facts, 4));

		// Out of anarchy, that topic is skipped.
		facts.AnarchyTurns = 0;
		Assert.Equal(["DOMESTICADVICECITYSIZE", "DOMESTICADVICEHAPPINESS", "DOMESTICADVICECITYSIZE"],
			Keys(new AdvisorSpeech(), AdvisorKind.Domestic, facts, 3));
	}

	[Fact]
	public void WithNoCitiesTheDomesticAdvisorSaysToBuildSome() {
		Assert.Equal(["DOMESTICADVICERANK", "DOMESTICADVICERANK"], Keys(new AdvisorSpeech(), AdvisorKind.Domestic, new AdvisorFacts(), 2));
	}

	[Fact]
	public void AGrowingCityIsAlwaysGrowingSlowly() {
		AdvisorFacts facts = new() { RulerTitles = ["Chief"] };
		// Civ3 overwrites "thrives" with "is growing slowly" (FUN_0052b200).
		AdvisorLine growing = AdvisorSpeech.Domestic(1, new AdvisorFacts { Cities = [City("Rome", surplus: 5)], RulerTitles = ["Chief"] }, new FixedRandom());
		Assert.Equal(["Rome", "is growing slowly", "Chief", ".", ""], growing.Texts.Take(5));
		Assert.Equal(AdvisorMood.Sad, growing.Mood);

		AdvisorLine stuck = AdvisorSpeech.Domestic(1, new AdvisorFacts {
			Cities = [City("Rome", surplus: 0, full: true, blocker: GrowthBlocker.Aqueduct)],
			RulerTitles = ["Chief"]
		}, new FixedRandom());
		Assert.Equal("is not growing", stuck.Texts[1]);
		Assert.Equal("We need an aqueduct to continue growing.", stuck.Texts[4]);

		// The improvement is only asked for once the food box is full.
		AdvisorLine filling = AdvisorSpeech.Domestic(1, new AdvisorFacts {
			Cities = [City("Rome", full: false, blocker: GrowthBlocker.Hospital)],
			RulerTitles = ["Chief"]
		}, new FixedRandom());
		Assert.Equal("", filling.Texts[4]);
	}

	[Theory]
	[InlineData(3, 1, "happy", AdvisorMood.Happy)]
	[InlineData(1, 3, "unhappy", AdvisorMood.Sad)]
	[InlineData(2, 2, "content", AdvisorMood.Happy)]
	public void ACitysMoodIsWhicheverOfHappyAndUnhappyIsMore(int happy, int unhappy, string word, AdvisorMood mood) {
		AdvisorLine line = AdvisorSpeech.Domestic(2, new AdvisorFacts { Cities = [City("Rome", happy: happy, unhappy: unhappy)], RulerTitles = ["Chief"] },
			new FixedRandom());
		Assert.Equal(["Chief", "Rome", word], line.Texts.Take(3));
		Assert.Equal(mood, line.Mood);
	}

	// ---- Trade ----

	[Fact]
	public void TheTradeAdvisorGoesRoundItsTopicsEndingWithAcquireMore() {
		AdvisorFacts all = new() {
			SingleSourceResources = ["Iron"],
			CivsWithoutTradeRoute = ["Romans"],
			EmbargoPartners = [("Greeks", "Rome")],
			HasNoResources = true,
		};
		Assert.Equal(["TRADEADVICEMORE", "TRADEADVICEROUTE", "TRADEADVICEEMBARGO", "TRADEADVICEWEHAVENOTHING", "TRADEADVICEACQUIREMORE", "TRADEADVICEMORE"],
			Keys(new AdvisorSpeech(), AdvisorKind.Trade, all, 6));
		Assert.Equal(["TRADEADVICEACQUIREMORE", "TRADEADVICEACQUIREMORE"], Keys(new AdvisorSpeech(), AdvisorKind.Trade, new AdvisorFacts(), 2));
	}

	// ---- Military ----

	[Theory]
	[InlineData(79, 100, "MILITARYADVICERANK_WEAK")]
	[InlineData(80, 100, "MILITARYADVICERANK_AVERAGE")]
	[InlineData(100, 80, "MILITARYADVICERANK_AVERAGE")]
	[InlineData(100, 79, "MILITARYADVICERANK_STRONG")]
	public void ArmiesAreWeakOrStrongPastFourFifths(long ours, long theirs, string key) {
		Assert.Equal(key, AdvisorSpeech.Military(0, new AdvisorFacts { Strength = (ours, theirs) }).Key);
	}

	[Fact]
	public void WithNoRivalPickedThereIsNoComparison() {
		Assert.Null(AdvisorSpeech.Military(0, new AdvisorFacts()));
	}

	[Fact]
	public void SupportIsTooCostlyWhenPayingForUnitsWithLittleGold() {
		Assert.Equal("MILITARYADVICESUPPORT_TOO_COSTLY",
			AdvisorSpeech.Military(1, new AdvisorFacts { CityCount = 4, NetGoldPerTurn = 4, UnitSupportCost = 2 }).Key);
		Assert.Equal("MILITARYADVICESUPPORT_NOTENOUGHUNITS",
			AdvisorSpeech.Military(1, new AdvisorFacts { CityCount = 4, NetGoldPerTurn = 5, UnitSupportCost = 2 }).Key);
		AdvisorLine free = AdvisorSpeech.Military(1, new AdvisorFacts { CityCount = 4, NetGoldPerTurn = 0, UnitSupportCost = 0, BestUnit = "Spearman" });
		Assert.Equal("MILITARYADVICESUPPORT_NOTENOUGHUNITS_BESTUNIT", free.Key);
		Assert.Equal("Spearman", free.Texts[0]);
	}

	[Fact]
	public void TheMilitaryAdvisorGoesRoundSupportBarbariansAndSpies() {
		AdvisorFacts facts = new() { BarbariansNear = ("Huns", "Rome"), CanPlantSpy = true, CityCount = 1, NetGoldPerTurn = 0, UnitSupportCost = 1 };
		Assert.Equal(["MILITARYADVICESUPPORT_TOO_COSTLY", "MILITARYADVICEBARBARIANS", "MILITARYADVICEMOLE", "MILITARYADVICESUPPORT_TOO_COSTLY"],
			Keys(new AdvisorSpeech(), AdvisorKind.Military, facts, 4));
	}

	// ---- Foreign ----

	private static ForeignAdviceFacts Civ(AttitudeLevel? attitude = AttitudeLevel.Polite, string bestUnit = null, bool canTrade = true,
		string theirs = null, string ours = null) =>
		new("Romans", "Roman", attitude, "impressed with", AdvisorMood.Sad, bestUnit, canTrade, theirs, ours);

	[Fact]
	public void TheForeignAdvisorTalksOfThePickedLeaderTopicByTopic() {
		AdvisorFacts facts = new() { ForeignCivs = [Civ(bestUnit: "Legionary")], SelectedForeignCiv = 0 };
		Assert.Equal(["FA_BEST_UNIT", "FA_CULTURAL", "FA_HAPPINESS_1", "FA_BEST_UNIT"], Keys(new AdvisorSpeech(), AdvisorKind.Foreign, facts, 4));

		facts.ForeignTradeView = true;
		facts.ForeignCivs = [Civ(theirs: "Silks")];
		Assert.Equal(["FA_IMPORT_GOOD", "FA_WE_NO_SURPLUS", "FA_HAPPINESS_1"], Keys(new AdvisorSpeech(), AdvisorKind.Foreign, facts, 3));
	}

	[Fact]
	public void WithNoLeadersTheForeignAdvisorSaysToMakeFriends() {
		Assert.Equal(["FOREIGNADVICERANK", "FOREIGNADVICERANK"], Keys(new AdvisorSpeech(), AdvisorKind.Foreign, new AdvisorFacts(), 2));
	}

	[Fact]
	public void AnAnnoyedLeaderIsAnnoyedWithUs() {
		AdvisorLine annoyed = AdvisorSpeech.ForeignTopic(Civ(AttitudeLevel.Annoyed), 0, tradeView: false);
		Assert.Equal("FA_HAPPINESS_2", annoyed.Key);
		Assert.Equal(["Romans", "annoyed"], annoyed.Texts.Take(2));
		Assert.Equal(AdvisorMood.Sad, annoyed.Mood);
		Assert.Equal("FA_HAPPINESS_1", AdvisorSpeech.ForeignTopic(Civ(AttitudeLevel.Cautious), 0, tradeView: false).Key);
		Assert.Equal("FA_NO_KNOWLEDGE", AdvisorSpeech.ForeignTopic(Civ(), 1, tradeView: false).Key);
		Assert.Equal("FA_NO_CAN_TRADE", AdvisorSpeech.ForeignTopic(Civ(canTrade: false), 2, tradeView: true).Key);
	}

	// ---- Culture ----

	[Fact]
	public void TheCulturalAdvisorGoesRoundImprovementsOpinionsWondersAndTheArts() {
		AdvisorFacts facts = new() {
			CultureImprovementChoices = ["Temple"],
			WonderChoices = ["Pyramids"],
			CultureOpinions = [new("Roman", "in awe of", AdvisorMood.Happy)],
		};
		Assert.Equal(["CULTURALADVICEIMPROVEMENT", "CULTURALADVICEOPINION", "CULTURALADVICEWONDER", "CULTURALADVICEARTS", "CULTURALADVICEIMPROVEMENT"],
			Keys(new AdvisorSpeech(), AdvisorKind.Culture, facts, 5));
		AdvisorLine opinion = AdvisorSpeech.Culture(3, facts, new FixedRandom());
		Assert.Equal("in awe of", opinion.Texts[1]);
		Assert.Equal("Roman", opinion.Texts[2]);
	}

	[Theory]
	// Ours over 4/3 of theirs: happy; over 3/4 of it: sad; less: happy.
	[InlineData(150, 100, AdvisorMood.Happy)]
	[InlineData(100, 100, AdvisorMood.Sad)]
	[InlineData(80, 100, AdvisorMood.Sad)]
	[InlineData(75, 100, AdvisorMood.Happy)]
	public void TheFaceForAnOpinionOfOurCultureIsCiv3s(int ours, int theirs, AdvisorMood mood) {
		Assert.Equal(mood, AdvisorFactsGatherer.Opinion(null, ours, theirs).mood);
	}

	// ---- Science ----

	[Fact]
	public void TheScienceAdvisorComparesFundsAndRecommends() {
		Assert.Equal("SCIENCEADVICERANK_WEAK", AdvisorSpeech.Science(0, new AdvisorFacts { CivsAheadInScience = 2, CivsNotAheadInScience = 1 }).Key);
		Assert.Equal("SCIENCEADVICERANK_AVERAGE", AdvisorSpeech.Science(0, new AdvisorFacts()).Key);
		Assert.Equal("SCIENCEADVICERANK_STRONG", AdvisorSpeech.Science(0, new AdvisorFacts { CivsNotAheadInScience = 1 }).Key);

		Assert.Equal("SCIENCEADVICERANK_STRONG_FUNDING", AdvisorSpeech.Science(1, new AdvisorFacts { ScienceRate = 7 }).Key);
		Assert.Equal("SCIENCEADVICERANK_AVERAGE_FUNDING", AdvisorSpeech.Science(1, new AdvisorFacts { ScienceRate = 3 }).Key);
		Assert.Equal("SCIENCEADVICERANK_WEAK_FUNDING", AdvisorSpeech.Science(1, new AdvisorFacts { ScienceRate = 2 }).Key);

		Assert.Null(AdvisorSpeech.Science(2, new AdvisorFacts { Researching = true, RecommendedTech = "Alphabet" }));
		Assert.Equal("Alphabet", AdvisorSpeech.Science(2, new AdvisorFacts { RecommendedTech = "Alphabet" }).Texts[0]);

		Assert.Equal(["SCIENCEADVICERANK_AVERAGE_FUNDING", "SCIENCEADVICERANK_AVERAGE", "SCIENCEADVICERANK_AVERAGE_FUNDING"],
			Keys(new AdvisorSpeech(), AdvisorKind.Science, new AdvisorFacts { ScienceRate = 5, Researching = true }, 3));
	}

	// ---- Jokes ----

	[Fact]
	public void AJokeThatDoesntSuitTheAdvisorGivesWayToTheNext() {
		// Topic 7 is the trade advisor's; the others fall through to the
		// catch-all.
		FixedRandom rng = new() { Joke = true, Value = 7 };
		AdvisorFacts facts = new() { LeaderTitle = "King" };
		Assert.Equal("ADVISORANGRY", AdvisorSpeech.Joke(AdvisorKind.Trade, facts, rng).Key);
		AdvisorLine hail = AdvisorSpeech.Joke(AdvisorKind.Domestic, facts, rng);
		Assert.Equal("ADVISORRANDDEF", hail.Key);
		Assert.Equal("King", hail.Texts[0]);

		// Late at night, anyone may say so.
		facts.LocalHour = 3;
		Assert.Equal("TIMESLEEP", AdvisorSpeech.Joke(AdvisorKind.Domestic, facts, new FixedRandom { Joke = true, Value = 1 }).Key);
		Assert.Null(AdvisorSpeech.Joke(AdvisorKind.Domestic, facts, new FixedRandom()));
	}

	// Every script.txt key AdvisorSpeech can choose.
	private static readonly string[] AllKeys = [
		"DOMESTICADVICERANK", "DOMESTICADVICECITYSIZE", "DOMESTICADVICEHAPPINESS", "DOMESTICADVICEANARCHY",
		"TRADEADVICEMORE", "TRADEADVICEROUTE", "TRADEADVICEEMBARGO", "TRADEADVICEWEHAVENOTHING", "TRADEADVICEACQUIREMORE",
		"MILITARYADVICERANK_WEAK", "MILITARYADVICERANK_AVERAGE", "MILITARYADVICERANK_STRONG", "MILITARYADVICESUPPORT_TOO_COSTLY",
		"MILITARYADVICESUPPORT_NOTENOUGHUNITS", "MILITARYADVICESUPPORT_NOTENOUGHUNITS_BESTUNIT", "MILITARYADVICEBARBARIANS",
		"MILITARYADVICEMOLE", "FOREIGNADVICERANK", "FA_NO_KNOWLEDGE", "FA_BEST_UNIT", "FA_CULTURAL", "FA_NO_CAN_TRADE",
		"FA_IMPORT_GOOD", "FA_NO_SURPLUS", "FA_WE_NO_SURPLUS", "FA_EXPORT_GOOD", "FA_HAPPINESS_1", "FA_HAPPINESS_2",
		"CULTURALADVICEIMPROVEMENT", "CULTURALADVICEOPINION", "CULTURALADVICEWONDER", "CULTURALADVICEARTS",
		"SCIENCEADVICERANK_WEAK", "SCIENCEADVICERANK_AVERAGE", "SCIENCEADVICERANK_STRONG", "SCIENCEADVICERANK_WEAK_FUNDING",
		"SCIENCEADVICERANK_AVERAGE_FUNDING", "SCIENCEADVICERANK_STRONG_FUNDING", "SCIENCEADVICETECHRECOMMEND",
		"ADVISORRANDDEF", "ADVISORANGRY", "CARROT", "DISORDERNEWS", "MILITARYNUKE", "GOODEVIL", "MYSTERIOUS", "TIMESLEEP",
	];

	// ---- Civ3's own script.txt ----

	[SkippableFact]
	public void EveryAdvisorLineIsInCiv3sScript() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		ScriptText script = ScriptText.Load(Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Text", "script.txt"));

		List<string> missing = AllKeys.Where(k => !script.Has(k) || script.Get(k).Text.Length == 0).ToList();
		Assert.Empty(missing);

		Assert.Equal(("Build more cities!", AdvisorMood.Happy), new AdvisorLine("DOMESTICADVICERANK").Say(script));
		Assert.Equal(("We are in a state of anarchy. It's touch and go... We should have the government back under control in about 3 turns.",
			AdvisorMood.Surprised), new AdvisorLine("DOMESTICADVICEANARCHY").Number(0, 3).Say(script));
		Assert.Equal(("We should build Pyramids! We'd be the envy of the world.", AdvisorMood.Surprised),
			new AdvisorLine("CULTURALADVICEWONDER").Text(0, "Pyramids").Say(script));
		Assert.Equal(("Rome is growing slowly, Chief.", AdvisorMood.Sad),
			AdvisorSpeech.Domestic(1, new AdvisorFacts { Cities = [City("Rome")], RulerTitles = ["Chief"] }, new FixedRandom()).Say(script));
	}

	// ---- Without Civ3's script.txt ----

	[Fact]
	public void TheFallbackScriptHasEveryAdvisorLine() {
		ScriptText script = AdvisorFallbackScript.Text;
		List<string> missing = AllKeys.Where(k => !script.Has(k) || script.Get(k).Text.Length == 0).ToList();
		Assert.Empty(missing);

		Assert.Equal(("Rome is growing slowly, Chief.", AdvisorMood.Sad),
			AdvisorSpeech.Domestic(1, new AdvisorFacts { Cities = [City("Rome")], RulerTitles = ["Chief"] }, new FixedRandom())
				.Say(script));
		Assert.Equal(("Next to theirs, our military is weak!", AdvisorMood.Angry),
			new AdvisorLine("MILITARYADVICERANK_WEAK").Say(script));
		Assert.Equal(("The Greeks are furious with us.", AdvisorMood.Sad),
			new AdvisorLine("FA_HAPPINESS_2").Text(0, "Greeks").Text(1, "furious").WithMood(AdvisorMood.Sad).Say(script));
	}

	// The fallback's words are ours, but its faces are Civ3's.
	[SkippableFact]
	public void TheFallbackScriptHasCiv3sFaces() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		ScriptText civ3 = ScriptText.Load(Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Text", "script.txt"));
		foreach (string key in AllKeys) {
			Assert.True(civ3.Get(key)?.Mood == AdvisorFallbackScript.Text.Get(key)?.Mood, key);
		}
	}
}
