namespace C7GameData {

	// What the advisors say without Civ3's script.txt (in standalone mode):
	// an entry for each key AdvisorSpeech can choose, in our own words, with
	// the same placeholders and the face Civ3's entry asks for ("#advisor"
	// lines). So the advisors choose their topics and faces as Civ3's do
	// whether or not Civ3 is installed; only the wording differs.
	public static class AdvisorFallbackScript {
		private static ScriptText text;

		public static ScriptText Text => text ??= ScriptText.Parse(Entries);

		private const string Entries = """
			#DOMESTICADVICERANK
			We need more cities!

			#DOMESTICADVICECITYSIZE
			$CITY0 $DOES1, $TITLE2$PUNCT3  $OPTIONALSTRING4

			#DOMESTICADVICEHAPPINESS
			$RULER_TITLE0, the people of $CITY1 are $HAPPY2.

			#DOMESTICADVICEANARCHY
			#advisor Domestic Surprised
			The land is in anarchy, and it's anyone's guess what happens next. Order should return in about $NUM0 turns.

			#TRADEADVICEACQUIREMORE
			#advisor Trade Sad
			We ought to get hold of more resources and luxuries.

			#TRADEADVICEMORE
			#advisor Trade Sad
			Our $RESOURCE0 all comes from one place. We should find more of it, and trade it with our friends.

			#TRADEADVICEROUTE
			#advisor Trade Happy
			We should open a trade route to the $CIVNOUN0.

			#TRADEADVICEEMBARGO
			#advisor Trade Happy
			An embargo with the $CIVNOUN0 could help us in our war against $CIVNAME1.

			#TRADEADVICEWEHAVENOTHING
			#advisor Trade Sad
			We have no resources at all to trade. We must get some before others take them all.

			#MILITARYADVICERANK_WEAK
			#advisor Military Angry
			Next to theirs, our military is weak!

			#MILITARYADVICERANK_AVERAGE
			#advisor Military Sad
			Next to theirs, our military is about average.

			#MILITARYADVICERANK_STRONG
			#advisor Military Happy
			Next to theirs, our military is strong!

			#MILITARYADVICESUPPORT_TOO_COSTLY
			#advisor Military Sad
			We can't pay for a larger army unless we find more gold.

			#MILITARYADVICESUPPORT_NOTENOUGHUNITS
			#advisor Military Happy
			We could afford a larger army.

			#MILITARYADVICESUPPORT_NOTENOUGHUNITS_BESTUNIT
			#advisor Military Happy
			We could afford a larger army. Let's train more $UNIT0.

			#MILITARYADVICEBARBARIANS
			#advisor Military Angry
			$BARBARIAN0 tribes are near $CITY1!

			#MILITARYADVICEMOLE
			#advisor Military Angry
			We should plant spies, to learn more of our enemies (and of our friends)!

			#FOREIGNADVICERANK
			We need more friends!

			#FA_NO_KNOWLEDGE
			We know nothing of the $CIVADJ0 military.

			#FA_BEST_UNIT
			The best unit we know the $CIVNOUN0 have is the $UNIT1.

			#FA_CULTURAL
			The $CIVADJ0 people are $CULTURAL_OPINION1 our culture.

			#FA_NO_CAN_TRADE
			We can't trade with the $CIVADJ0 people without a trade route.

			#FA_IMPORT_GOOD
			The $CIVNOUN0 have $RESOURCES1 to trade, if you wish.

			#FA_NO_SURPLUS
			The $CIVNOUN0 have no resources to spare for us.

			#FA_WE_NO_SURPLUS
			We have no resources to spare for the $CIVNOUN0.

			#FA_EXPORT_GOOD
			The $CIVNOUN0 might take some of our $RESOURCES1.

			#FA_HAPPINESS_1
			The $CIVNOUN0 are $MOOD1 toward us.

			#FA_HAPPINESS_2
			The $CIVNOUN0 are $MOOD1 with us.

			#CULTURALADVICEOPINION
			The $CIVADJ2 people are $CULTURAL_OPINION1 our culture.

			#CULTURALADVICEARTS
			#advisor Cultural Happy
			If we study the arts, we can build great wonders of the mind, and our culture will spread.

			#CULTURALADVICEWONDER
			#advisor Cultural Surprised
			We should build $WONDER0! The world would envy us.

			#CULTURALADVICEIMPROVEMENT
			Building the $IMPROVEMENT0 in each of our cities would draw people to us.

			#SCIENCEADVICERANK_WEAK
			#advisor Science Sad
			We are falling behind in learning. We can't let this go on!

			#SCIENCEADVICERANK_WEAK_FUNDING
			#advisor Science Sad
			We can't keep up in research without more funding. Please raise science spending.

			#SCIENCEADVICERANK_AVERAGE
			#advisor Science Happy
			We are as advanced as most.

			#SCIENCEADVICERANK_AVERAGE_FUNDING
			#advisor Science Happy
			Our research only just gets by. Please raise science funding.

			#SCIENCEADVICERANK_STRONG
			#advisor Science Happy
			We lead the world in learning!

			#SCIENCEADVICERANK_STRONG_FUNDING
			#advisor Science Happy
			Generous funding keeps our research going well!

			#SCIENCEADVICETECHRECOMMEND
			#advisor Science Happy
			I suggest we research $TECH0.

			#TIMESLEEP
			#advisor Happy
			Who needs sleep?

			#MYSTERIOUS
			#advisor Happy
			Culture binds our people together.

			#GOODEVIL
			#advisor Happy
			$OURCIV0 will win in the end. $ENEMYCIV1 doesn't stand a chance.

			#MILITARYNUKE
			#advisor Surprised
			You have the nukes. You decide.

			#DISORDERNEWS
			#advisor Sad
			$CITY0 is burning. More news later.

			#CARROT
			#advisor Happy
			Next to you, most people are not very bright...

			#ADVISORANGRY
			#advisor Angry
			This isn't even my day to work!

			#ADVISORRANDDEF
			#advisor Happy
			Long live the $TITLE0!
			""";
	}
}
