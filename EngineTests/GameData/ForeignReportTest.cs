using System.Collections.Generic;
using C7Engine;
using C7GameData;
using Xunit;
using static C7GameData.PlayerRelationship;

namespace EngineTests.GameData;

// What the foreign advisor shows of the civs the player knows and the ties
// between them.
public class ForeignReportTest {
	private readonly C7GameData.GameData gameData = new() {
		gameDifficulty = new Difficulty(),
		rules = new Rules() { DefaultDealDuration = 20 },
		turn = 10,
	};
	private readonly Player us;
	private readonly Player them;
	private readonly Player third;
	private readonly Player unknown;

	public ForeignReportTest() {
		EngineStorage.InitializeGameDataForTests(gameData);
		us = MakeCiv("player-1", "Romans", "Roman");
		us.isHuman = true;
		them = MakeCiv("player-2", "Greeks", "Greek");
		third = MakeCiv("player-3", "Zulus", "Zulu");
		unknown = MakeCiv("player-4", "Aztecs", "Aztec");
		us.EnsureRelationshipExists(them);
		us.EnsureRelationshipExists(third);
		them.EnsureRelationshipExists(third);
		them.EnsureRelationshipExists(unknown);
	}

	private Player MakeCiv(string id, string noun, string adjective) {
		Player player = new() {
			id = ID.FromString(id),
			civilization = new Civilization(noun) { noun = noun, adjective = adjective },
			government = new Government() { name = "Despotism" },
			rules = gameData.rules,
		};
		gameData.players.Add(player);
		return player;
	}

	[Fact]
	public void ShowsThePlayerThenTheCivsItHasMet() {
		Assert.Equal([us, them, third], ForeignReport.Civs(gameData, us));
	}

	[Fact]
	public void TiesAreBetweenKnownCivsThatHaveMet() {
		us.playerRelationships[them.id].hasEmbassy = true;
		List<ForeignTie> ties = ForeignReport.Ties(gameData, us);

		// Not the Greeks' tie to the Aztecs, whom we haven't met.
		Assert.Equal(3, ties.Count);
		Assert.All(ties, t => Assert.Equal([ForeignTieKind.Peace], t.Kinds()));
	}

	// With an embassy "you can see what civilizations they are at
	// war/peace with on the F4 Foreign Advisor screen" (CivFanatics FAQ);
	// without one, only our own ties.
	[Fact]
	public void RivalsTiesNeedAnEmbassy() {
		DeclareWar(them, third, false, 0, honourPacts: false);

		List<ForeignTie> ties = ForeignReport.Ties(gameData, us);
		Assert.Equal(2, ties.Count);
		Assert.All(ties, t => Assert.True(t.first == us || t.second == us));
		Assert.DoesNotContain("They are at war with the Zulus", ForeignReport.Treaties(gameData, us, them));

		us.playerRelationships[third.id].hasEmbassy = true;
		Assert.Equal(3, ForeignReport.Ties(gameData, us).Count);
		Assert.Contains("They are at war with the Zulus", ForeignReport.Treaties(gameData, us, them));
	}

	[Fact]
	public void TiesShowWarPactsPassageAlliancesAndEmbargoes() {
		DeclareWar(them, third, false, 0, honourPacts: false);
		RegisterMultiTurnDeal(us, them, new MultiTurnDeal(DealType.DiplomaticAgreement, DealSubType.MutualProtectionPact,
			DealDetails.Exchange, 0, null, 20, 10));
		RegisterMultiTurnDeal(us, them, new MultiTurnDeal(DealType.DiplomaticAgreement, DealSubType.RightOfPassage,
			DealDetails.Exchange, 0, null, 20, 10));
		RegisterMultiTurnDeal(us, them, new MultiTurnDeal(DealType.Alliance, DealSubType.MilitaryAlliance,
			DealDetails.Exchange, 0, null, 20, 10, third.id));
		RegisterMultiTurnDeal(us, them, new MultiTurnDeal(DealType.Embargo, DealSubType.TradeEmbargo,
			DealDetails.Exchange, 0, null, 20, 10, third.id));

		ForeignTie ours = ForeignReport.Tie(gameData, us, them);
		Assert.Equal([ForeignTieKind.Peace, ForeignTieKind.MutualProtection, ForeignTieKind.RightOfPassage,
			ForeignTieKind.Alliance, ForeignTieKind.Embargo], ours.Kinds());
		Assert.Equal([third], ours.allianceAgainst);
		Assert.Equal([third], ours.embargoAgainst);

		Assert.Equal([ForeignTieKind.War], ForeignReport.Tie(gameData, them, third).Kinds());
		Assert.Null(ForeignReport.Tie(gameData, us, unknown));
	}

	[Fact]
	public void TreatiesAreToldInCiv3sWords() {
		DeclareWar(them, third, false, 0, honourPacts: false);
		RegisterMultiTurnDeal(us, them, new MultiTurnDeal(DealType.DiplomaticAgreement, DealSubType.MutualProtectionPact,
			DealDetails.Exchange, 0, null, 20, 10));
		RegisterMultiTurnDeal(them, us, new MultiTurnDeal(DealType.Embargo, DealSubType.TradeEmbargo,
			DealDetails.Exchange, 0, null, 20, 10, third.id));
		them.government = new Government() { name = "Monarchy" };
		us.playerRelationships[them.id].hasEmbassy = true;

		Assert.Equal([
			"Government: Monarchy",
			"They are at peace with us",
			"They have a Mutual Protection Pact with us",
			"They have a Trade embargo with us against the Zulus",
			"They are at war with the Zulus",
		], ForeignReport.Treaties(gameData, us, them));

		Assert.Equal([
			"Government: Despotism",
			"We are at peace with the Greeks",
			"We have a Mutual Protection Pact with the Greeks",
			"We have a Trade embargo with the Greeks against the Zulus",
			"We are at peace with the Zulus",
		], ForeignReport.Treaties(gameData, us, us));
	}

	[Fact]
	public void AnAiCivsAttitudeIsNamedAsCiv3NamesIt() {
		AttitudeLevel? level = ForeignReport.AttitudeTowardViewer(gameData, us, them);
		Assert.NotNull(level);
		Assert.Equal(Attitudes.Level(gameData, them, us), level);
		Assert.Equal($"The Greeks are {ForeignReport.AttitudeWord(level.Value)} toward us.",
			ForeignReport.AttitudeLine(gameData, us, them));
		Assert.Equal("gracious", ForeignReport.AttitudeWord(AttitudeLevel.Gracious));
		Assert.Equal("furious", ForeignReport.AttitudeWord(AttitudeLevel.Furious));

		// Nor for ourselves nor for another human.
		Assert.Null(ForeignReport.AttitudeTowardViewer(gameData, us, us));
		them.isHuman = true;
		Assert.Null(ForeignReport.AttitudeLine(gameData, us, them));
	}

	[Fact]
	public void PotentialTradesSayWhatEachSideHasToSpare() {
		Resource wines = new() { Key = "wines", Name = "Wines" };
		Resource iron = new() { Key = "iron", Name = "Iron" };
		Resource silks = new() { Key = "silks", Name = "Silks" };

		Assert.Equal([
			"We could trade with the Greeks if you like, sire. They have some fine Wines and Silks to trade.",
			"The Greeks might go for some of our Iron.",
		], ForeignReport.PotentialTrades(them, false, false, false, [wines, silks], [iron]));

		Assert.Equal([
			"Unfortunately, the Greeks do not have any surplus resources to trade with us.",
			"We have no surplus goods to trade to the Greeks.",
		], ForeignReport.PotentialTrades(them, false, false, false, [], []));

		Assert.Equal(["We are at war with the Greeks."], ForeignReport.PotentialTrades(them, true, false, false, [], []));
		Assert.Equal(["We have an embargo against the Greeks."], ForeignReport.PotentialTrades(them, false, true, false, [], []));
		Assert.Equal(["The Greeks has a trade embargo against us."], ForeignReport.PotentialTrades(them, false, false, true, [], []));
	}

	[Fact]
	public void TheBestUnitIsKnownOnlyWithAnEmbassy() {
		UnitPrototype warrior = new() { name = "Warrior", attack = 1, defense = 1, shieldCost = 10 };
		UnitPrototype spearman = new() { name = "Spearman", attack = 1, defense = 2, shieldCost = 20 };
		them.units.Add(new MapUnit(ID.FromString("unit-1")) { unitType = warrior });
		them.units.Add(new MapUnit(ID.FromString("unit-2")) { unitType = spearman });

		Assert.Null(ForeignReport.BestKnownUnit(us, them));
		Assert.Contains("Sorry, but we have no knowledge of the Greek military.", ForeignReport.Details(gameData, us, them));

		us.playerRelationships[them.id].hasEmbassy = true;
		Assert.Equal(spearman, ForeignReport.BestKnownUnit(us, them));
		List<string> details = ForeignReport.Details(gameData, us, them);
		Assert.Contains("We have: An Embassy", details);
		Assert.Contains("The best unit the Greeks have -- that we know of -- is the Spearman.", details);

		// Settlers and workers don't count.
		them.units.Clear();
		them.units.Add(new MapUnit(ID.FromString("unit-3")) { unitType = new UnitPrototype { name = "Settler", shieldCost = 30 } });
		Assert.Null(ForeignReport.BestKnownUnit(us, them));
	}

	// Civ3 draws our leader pleased and the others by their attitude toward
	// us (FUN_00538830), whether or not we're at war.
	[Theory]
	[InlineData(AttitudeLevel.Gracious, LeaderFace.Pleased)]
	[InlineData(AttitudeLevel.Polite, LeaderFace.Pleased)]
	[InlineData(AttitudeLevel.Cautious, LeaderFace.Neutral)]
	[InlineData(AttitudeLevel.Annoyed, LeaderFace.Angry)]
	[InlineData(AttitudeLevel.Furious, LeaderFace.Angry)]
	public void ALeaderLooksAsItFeelsTowardUs(AttitudeLevel attitude, LeaderFace face) {
		Assert.Equal(face, ForeignReport.FaceToward(attitude, isViewer: false));
	}

	[Fact]
	public void OurLeaderLooksPleasedAndAHumanRivalNeutral() {
		Assert.Equal(LeaderFace.Pleased, ForeignReport.FaceToward(gameData, us, us));
		them.isHuman = true;
		Assert.Equal(LeaderFace.Neutral, ForeignReport.FaceToward(gameData, us, them));
		Assert.Equal(1, new LeaderPortrait(them, LeaderFace.Neutral).row);
	}
}
