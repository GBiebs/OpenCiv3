using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.AIData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's AI attitude (CivFanatics' "AI Attitude Study"): a score where good
// things are negative; -11 and lower Gracious, -1 to -10 Polite, 0 Cautious,
// 1 to 10 Annoyed, 11 and up Furious. Each test checks what one thing adds,
// everything else held still.
public class AttitudeTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	// How `observer` feels about `subject`; `friend` is a third civ.
	private readonly Player observer;
	private readonly Player subject;
	private readonly Player friend;
	private readonly City observerCity;
	private readonly City subjectCity;

	public AttitudeTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(3).ToArray();
		(observer, subject, friend) = (ais[0], ais[1], ais[2]);
		observerCity = CityInteractions.BuildCity(observer.units[0].location, observer, "Home");
		subjectCity = CityInteractions.BuildCity(subject.units[0].location, subject, "Abroad");
		CityInteractions.BuildCity(friend.units[0].location, friend, "Elsewhere");
		observer.EnsureRelationshipExists(subject);
		friend.EnsureRelationshipExists(observer);
		friend.EnsureRelationshipExists(subject);

		// Hold still: no favorite or shunned government, different culture
		// groups, and the subject without the power lead.
		foreach (Player p in new[] { observer, subject, friend }) {
			p.civilization.favoriteGovernment = null;
			p.civilization.shunnedGovernment = null;
			p.civilization.aggressionLevel = 0;
		}
		CultureGroup[] groups = gameData.cultureGroups.Take(3).ToArray();
		observer.civilization.SetCultureGroup(groups[0].index, groups[0].name);
		subject.civilization.SetCultureGroup(groups[1].index, groups[1].name);
		friend.civilization.SetCultureGroup(groups[2].index, groups[2].name);
		foreach (MapUnit unit in subject.units.ToList()) {
			gameData.RemoveUnit(unit);
		}
		gameData.gameDifficulty = gameData.difficulties[0];
		// Mid-game (a peace treaty signed on turn 0 never runs out).
		gameData.turn = 10;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private int Score() => Attitude.Score(gameData, observer, subject);
	private AttitudeRules Rules => gameData.rules.attitude;

	private void Sign(DealSubType type, Player a, Player b) {
		PlayerRelationship.RegisterMultiTurnDeal(a, b, new MultiTurnDeal(DealType.DiplomaticAgreement, type, DealDetails.Exchange,
			0, null, 20, gameData.turn));
	}

	[Fact]
	public void TheLevelsFollowTheStudy() {
		Assert.Equal(Attitude.Level.Gracious, Attitude.LevelOf(-11, Rules));
		Assert.Equal(Attitude.Level.Polite, Attitude.LevelOf(-10, Rules));
		Assert.Equal(Attitude.Level.Polite, Attitude.LevelOf(-1, Rules));
		Assert.Equal(Attitude.Level.Cautious, Attitude.LevelOf(0, Rules));
		Assert.Equal(Attitude.Level.Annoyed, Attitude.LevelOf(1, Rules));
		Assert.Equal(Attitude.Level.Annoyed, Attitude.LevelOf(10, Rules));
		Assert.Equal(Attitude.Level.Furious, Attitude.LevelOf(11, Rules));
	}

	[Fact]
	public void CivsWeHaventMetAreCautious() {
		Player stranger = gameData.players.First(p => !p.isBarbarians && !observer.playerRelationships.ContainsKey(p.id) && p != observer);
		Assert.Equal(0, Attitude.Score(gameData, observer, stranger));
	}

	[Fact]
	public void AggressionCountsMoreOnHarderDifficulties() {
		// Both in Despotism: -1 for the same government.
		int baseline = Score();
		int At(int difficulty, int aggression) {
			gameData.gameDifficulty = gameData.difficulties[difficulty];
			observer.civilization.aggressionLevel = aggression;
			return Score() - baseline;
		}
		// Chieftain: aggression as it is, but never below -1.
		Assert.Equal(2, At(0, 2));
		Assert.Equal(-1, At(0, -2));
		Assert.Equal(-1, At(1, -1));
		// Regent and Monarch: one more.
		Assert.Equal(3, At(2, 2));
		Assert.Equal(-1, At(3, -2));
		// Emperor and up: two more, but never above 3.
		Assert.Equal(3, At(4, 2));
		Assert.Equal(3, At(6, 1));
		Assert.Equal(0, At(7, -2));
	}

	[Fact]
	public void GovernmentsAndCultureGroups() {
		Assert.Equal(observer.government, subject.government);
		int same = Score();
		subject.government = gameData.governments.First(g => g != observer.government && g.name != "Anarchy");
		Assert.Equal(-Rules.sameGovernment, Score() - same);
		int different = Score();

		// Them in the government we shun, us not.
		observer.civilization.shunnedGovernment = subject.government.name;
		Assert.Equal(Rules.shunnedGovernment, Score() - different);
		// Both in it: just the same government.
		Government shunned = subject.government;
		subject.government = observer.government;
		observer.civilization.shunnedGovernment = observer.government.name;
		Assert.Equal(0, Score() - same);
		subject.government = shunned;
		observer.civilization.shunnedGovernment = null;

		// Both in our favorite.
		subject.government = observer.government;
		observer.civilization.favoriteGovernment = observer.government.name;
		Assert.Equal(Rules.favoriteGovernment, Score() - same);
		observer.civilization.favoriteGovernment = null;

		subject.civilization.SetCultureGroup(observer.civilization.cultureGroup.index, observer.civilization.cultureGroup.name);
		Assert.Equal(Rules.sameCultureGroup, Score() - same);
	}

	[Fact]
	public void EmbassiesAndTreatiesWhileTheyLast() {
		int baseline = Score();
		Embassies.Open(observer, subject);
		Assert.Equal(Rules.embassy, Score() - baseline);
		int withEmbassy = Score();

		Sign(DealSubType.RightOfPassage, observer, subject);
		Assert.Equal(Rules.rightOfPassage, Score() - withEmbassy);
		int withPassage = Score();

		Sign(DealSubType.MutualProtectionPact, observer, subject);
		Assert.Equal(Rules.mutualProtection, Score() - withPassage);
	}

	[Fact]
	public void AResourceTradeIsAlsoADealRunning() {
		int baseline = Score();
		PlayerRelationship.RegisterMultiTurnDeal(subject, observer, new MultiTurnDeal(DealType.Luxury, DealSubType.LuxuryPerTurn,
			DealDetails.Outbound, 0, "Wines", 20, gameData.turn));
		Assert.Equal(Rules.resourceTrade + Rules.recentTrade, Score() - baseline);
	}

	[Fact]
	public void ACommonEnemyAndAnAllianceAgainstIt() {
		int baseline = Score();
		observer.DeclareWarOn(friend, gameData.turn);
		subject.DeclareWarOn(friend, gameData.turn);
		Assert.Equal(Rules.commonEnemy, Score() - baseline);
		PlayerRelationship.RegisterMultiTurnDeal(observer, subject, new MultiTurnDeal(DealType.Alliance, DealSubType.MilitaryAlliance,
			DealDetails.Exchange, 0, null, 20, gameData.turn, friend.id));
		Assert.Equal(Rules.commonEnemy + Rules.alliance, Score() - baseline);
	}

	[Fact]
	public void AnEmbargoAgainstUsAngersUs() {
		int baseline = Score();
		PlayerRelationship.RegisterMultiTurnDeal(subject, friend, new MultiTurnDeal(DealType.Embargo, DealSubType.TradeEmbargo,
			DealDetails.Exchange, 0, null, 20, gameData.turn, observer.id));
		Assert.Equal(Rules.tradeEmbargoAgainstUs, Score() - baseline);
	}

	[Fact]
	public void GiftsCountAPointPerTenGoldUpToTenAndAreForgotten() {
		int baseline = Score();
		subject.gold = 1000;
		observer.ExecuteDeal(gameData, subject, new TradeOffer { gold = 55 }, new TradeOffer());
		// 55 gold: 5 points, and a recent trade.
		Assert.Equal(-5 + Rules.recentTrade, Score() - baseline);

		// Whichever side carries the deal out.
		subject.ExecuteDeal(gameData, observer, new TradeOffer(), new TradeOffer { gold = 300 });
		Assert.Equal(-Rules.maximumGiftPoints + Rules.recentTrade, Score() - baseline);
		Assert.Equal(355, observer.playerRelationships[subject.id].giftGold);
		Assert.Equal(0, subject.playerRelationships[observer.id].giftGold);

		// 355 gold, forgotten at 10 a turn: still the maximum after 25 turns
		// (255 left), 3 points after 32 (35 left), none after 36.
		for (int turn = 0; turn < 25; turn++) {
			Attitude.UpdateForNewTurn(gameData);
		}
		gameData.turn += 25;
		Assert.Equal(-Rules.maximumGiftPoints, Score() - baseline);
		for (int turn = 0; turn < 7; turn++) {
			Attitude.UpdateForNewTurn(gameData);
		}
		Assert.Equal(-3, Score() - baseline);
		for (int turn = 0; turn < 4; turn++) {
			Attitude.UpdateForNewTurn(gameData);
		}
		Assert.Equal(0, Score() - baseline);
	}

	[Fact]
	public void ATradeCountsForTwentyTurnsAndIsNoGift() {
		int baseline = Score();
		int theirBaseline = Attitude.Score(gameData, subject, observer);
		Tech tech = gameData.techs.First(t => !subject.knownTechs.Contains(t.id) && !observer.knownTechs.Contains(t.id));
		observer.knownTechs.Add(tech.id);
		subject.gold = 1000;
		observer.ExecuteDeal(gameData, subject, new TradeOffer { gold = 300 }, new TradeOffer { techs = [tech] });
		Assert.Equal(Rules.recentTrade, Score() - baseline);
		Assert.Equal(Rules.recentTrade, Attitude.Score(gameData, subject, observer) - theirBaseline);
		Assert.Equal(0, observer.playerRelationships[subject.id].giftGold);
		gameData.turn += Rules.recentTradeTurns - 1;
		Assert.Equal(Rules.recentTrade, Score() - baseline);
		gameData.turn += 1;
		Assert.Equal(0, Score() - baseline);
	}

	[Fact]
	public void TheCivWithMoreCultureIsLiked() {
		int baseline = Score();
		subjectCity.perPlayerCulture[subject] = 50;
		Assert.Equal(-Rules.cultureLead, Score() - baseline);
		observerCity.perPlayerCulture[observer] = 80;
		Assert.Equal(Rules.cultureLead, Score() - baseline);
	}

	[Fact]
	public void APowerLeadHalvesTheGoodPointsButNotRightOfPassage() {
		Embassies.Open(observer, subject);
		Sign(DealSubType.RightOfPassage, observer, subject);
		Sign(DealSubType.MutualProtectionPact, observer, subject);
		// Same government -1, embassy -2, pact -10: -13; passage -5.
		Assert.Equal(-13 - 5, Score());

		UnitPrototype strong = gameData.unitPrototypes.MaxBy(u => u.attack + u.defense);
		for (int i = 0; i < 3; i++) {
			gameData.SpawnUnit(subject, strong, subjectCity.location);
		}
		Assert.True(subject.CalculateMilitaryStrength() > observer.CalculateMilitaryStrength());
		// Half of -13, away from zero, is -7.
		Assert.Equal(-7 - 5, Score());
	}

	[Fact]
	public void ADeclarationOfWarIsRememberedAndBreakingATreatyMore() {
		int baseline = Score();
		int friendBaseline = Attitude.Score(gameData, friend, subject);
		subject.DeclareWarOn(observer, gameData.turn);
		Assert.Equal(Rules.declaredWarOnUs, observer.playerRelationships[subject.id].attitudePenalty);
		Assert.Equal(Rules.declaredWarOnFriend, Attitude.Score(gameData, friend, subject) - friendBaseline);

		// A peace treaty runs 20 turns; breaking it counts too.
		PlayerRelationship.SignPeaceAfterWar(observer, subject, gameData);
		gameData.turn += 5;
		subject.DeclareWarOn(observer, gameData.turn);
		Assert.Equal(2 * Rules.declaredWarOnUs + Rules.brokePeaceTreaty, observer.playerRelationships[subject.id].attitudePenalty);
		Assert.Equal(2 * Rules.declaredWarOnFriend + Rules.brokePeaceTreatyWithFriend, Attitude.Score(gameData, friend, subject) - friendBaseline);

		// After its term, no treaty is broken.
		PlayerRelationship.SignPeaceAfterWar(observer, subject, gameData);
		gameData.turn += gameData.rules.DefaultDealDuration;
		subject.DeclareWarOn(observer, gameData.turn);
		Assert.Equal(3 * Rules.declaredWarOnUs + Rules.brokePeaceTreaty, observer.playerRelationships[subject.id].attitudePenalty);
	}

	[Fact]
	public void BreakingARightOfPassageIsRememberedAndTarnishesTheReputation() {
		Sign(DealSubType.RightOfPassage, subject, observer);
		subject.DeclareWarOn(observer, gameData.turn);
		Assert.Equal(Rules.declaredWarOnUs + Rules.brokeRightOfPassage, observer.playerRelationships[subject.id].attitudePenalty);
		Assert.Equal(1, observer.playerRelationships[subject.id].brokenDeals);
		Assert.Equal(Rules.declaredWarOnFriend + Rules.brokeRightOfPassageWithFriend, friend.playerRelationships[subject.id].attitudePenalty);
	}

	[Fact]
	public void BreakingARightOfPassageWithUnitsInsideIsWorse() {
		Sign(DealSubType.RightOfPassage, subject, observer);
		PlayerRelationship.DeclareWar(subject, observer, sneakAttack: true, gameData.turn, gameData.turn + 5);
		Assert.Equal(Rules.declaredWarOnUs + Rules.brokeRightOfPassageWithUnitsInside, observer.playerRelationships[subject.id].attitudePenalty);
		Assert.Equal(Rules.declaredWarOnFriend + Rules.brokeRightOfPassageWithUnitsInsideWithFriend, friend.playerRelationships[subject.id].attitudePenalty);
	}

	[Fact]
	public void RazingACityIsRemembered() {
		City victimCity = CityInteractions.BuildCity(
			gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
				&& gameData.cities.All(c => c.location.DistanceTo(t) > 3)), observer, "Victim");
		CityInteractions.ChangeOwner(gameData, victimCity, subject);
		victimCity.capturedOnTurn = gameData.turn;
		victimCity.capturedFrom = observer;
		Assert.True(CityInteractions.RazeCity(victimCity, subject));
		Assert.Equal(Rules.razedOurCity, observer.playerRelationships[subject.id].attitudePenalty);
		Assert.Equal(Rules.razedFriendsCity, friend.playerRelationships[subject.id].attitudePenalty);
	}

	[Fact]
	public void TakingAFlippedCityIsRememberedAndRefusingItLiked() {
		City border = CityInteractions.BuildCity(
			gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
				&& gameData.cities.All(c => c.location.DistanceTo(t) > 3)), observer, "Border");
		CultureFlip.Flip(gameData, border, subject);
		Assert.Equal(Rules.tookOurFlippedCity, observer.playerRelationships[subject.id].attitudePenalty);
		Assert.True(CultureFlip.Rebuff(gameData, border));
		Assert.Equal(Rules.refusedOurFlippedCity, observer.playerRelationships[subject.id].attitudePenalty);
	}

	[Fact]
	public void AttitudeMemoriesAreSaved() {
		PlayerRelationship relationship = observer.playerRelationships[subject.id];
		relationship.attitudePenalty = 7;
		relationship.giftGold = 120;
		relationship.lastTradeTurn = 3;
		relationship.brokenDeals = 2;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		PlayerRelationship again = reloaded.GetPlayer(observer.id).playerRelationships[subject.id];
		Assert.Equal(7, again.attitudePenalty);
		Assert.Equal(120, again.giftGold);
		Assert.Equal(3, again.lastTradeTurn);
		Assert.Equal(2, again.brokenDeals);
	}

	[Fact]
	public void CivsHaveCiv3sAggression() {
		Assert.Equal(2, gameData.civilizations.Find(c => c.noun == "Germans").aggressionLevel);
		Assert.Equal(-2, gameData.civilizations.Find(c => c.noun == "French").aggressionLevel);
	}

	[Fact]
	public void TheAIGoesToWarWithCivsItDislikesAndHasMet() {
		Player a = subject;
		Player b = friend;
		observer.EnsureRelationshipExists(b);
		// Both with a city on our continent (the AI doesn't fight overseas).
		foreach (Player p in new[] { a, b }) {
			Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
				&& t.continent == observerCity.location.continent && gameData.cities.All(c => c.location.DistanceTo(t) > 3));
			CityInteractions.BuildCity(site, p, $"{p.civilization.name} outpost");
		}
		observer.playerRelationships[a.id].attitudePenalty = 1_000_000;
		Assert.Equal(a, WarPriority.PickPlayerToFight(observer));
		observer.playerRelationships[a.id].attitudePenalty = 0;
		observer.playerRelationships[b.id].attitudePenalty = 1_000_000;
		Assert.Equal(b, WarPriority.PickPlayerToFight(observer));

		// Not while a peace treaty runs.
		observer.DeclareWarOn(b, gameData.turn);
		PlayerRelationship.SignPeaceAfterWar(observer, b, gameData);
		observer.playerRelationships[b.id].attitudePenalty = 1_000_000;
		Assert.NotEqual(b, WarPriority.PickPlayerToFight(observer));
		gameData.turn += gameData.rules.DefaultDealDuration;
		Assert.Equal(b, WarPriority.PickPlayerToFight(observer));

		// Never a civ we haven't met.
		observer.playerRelationships.Remove(b.id);
		b.playerRelationships.Remove(observer.id);
		Assert.NotEqual(b, WarPriority.PickPlayerToFight(observer));
	}
}
