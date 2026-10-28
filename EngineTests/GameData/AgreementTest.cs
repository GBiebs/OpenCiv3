using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Diplomatic agreements: right of passage, mutual protection pacts,
// military alliances and trade embargoes, signed by civs at peace with an
// embassy once both know the advance that enables each.
public class AgreementTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Player third;

	public AgreementTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(3).ToArray();
		(us, them, third) = (ais[0], ais[1], ais[2]);
		foreach (Player p in ais) {
			CityInteractions.BuildCity(p.units[0].location, p, $"{p.civilization.name} home");
		}
		us.EnsureRelationshipExists(them);
		us.EnsureRelationshipExists(third);
		them.EnsureRelationshipExists(third);
		Embassies.Open(us, them);
		foreach (Player p in new[] { us, them }) {
			foreach (string tech in new[] { "Writing", "Map Making", "Nationalism" }) {
				p.knownTechs.Add(gameData.techs.Find(t => t.Name == tech).id);
			}
		}
		gameData.turn = 30;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private static Agreement Passage => new() { type = DealSubType.RightOfPassage };
	private static Agreement Pact => new() { type = DealSubType.MutualProtectionPact };
	private Agreement AllianceAgainstThird => new() { type = DealSubType.MilitaryAlliance, against = third.id };
	private Agreement EmbargoAgainstThird => new() { type = DealSubType.TradeEmbargo, against = third.id };

	private bool Available(Agreement agreement) => Agreements.Available(gameData, us, them).Any(a => a.SameAs(agreement));

	// How `observer` feels about `subject`, for good.
	private void Feel(Player observer, Player subject, int points) {
		observer.playerRelationships[subject.id].attitudePenalty = points;
	}

	[Fact]
	public void AgreementsNeedPeaceAnEmbassyAndTheAdvances() {
		Assert.True(Available(Passage));
		Assert.True(Available(Pact));
		Assert.True(Available(EmbargoAgainstThird));
		// An alliance is against a civ one of us fights.
		Assert.False(Available(AllianceAgainstThird));
		us.DeclareWarOn(third, gameData.turn);
		Assert.True(Available(AllianceAgainstThird));

		// Both need the advance.
		them.knownTechs.Remove(gameData.techs.Find(t => t.Name == "Nationalism").id);
		Assert.False(Available(Pact));
		Assert.False(Available(EmbargoAgainstThird));
		Assert.True(Available(Passage));

		us.playerRelationships[them.id].hasEmbassy = false;
		them.playerRelationships[us.id].hasEmbassy = false;
		Assert.Empty(Agreements.Available(gameData, us, them));
		Embassies.Open(us, them);

		us.DeclareWarOn(them, gameData.turn);
		Assert.Empty(Agreements.Available(gameData, us, them));
	}

	[Fact]
	public void AnAgreementRunsTheDealDurationForBoth() {
		Agreements.Sign(gameData, us, them, Pact);
		Assert.True(Agreements.Has(us, them, DealSubType.MutualProtectionPact));
		Assert.True(Agreements.Has(them, us, DealSubType.MutualProtectionPact));
		Assert.False(Available(Pact));

		gameData.turn += gameData.rules.DefaultDealDuration - 1;
		PlayerRelationship.CheckForObsoleteDeals(us, gameData.players, gameData.turn);
		Assert.True(Agreements.Has(us, them, DealSubType.MutualProtectionPact));
		gameData.turn += 1;
		PlayerRelationship.CheckForObsoleteDeals(us, gameData.players, gameData.turn);
		Assert.False(Agreements.Has(us, them, DealSubType.MutualProtectionPact));
	}

	[Fact]
	public void APactDrawsThePartnerIntoAWarOnItsPartner() {
		Agreements.Sign(gameData, us, them, Pact);
		third.DeclareWarOn(them, gameData.turn);
		Assert.True(PlayerRelationship.AtWar(us, third));
		Assert.Contains(EngineStorage.messagesToUI, m => m is MsgWarDeclarationNotification n && n.aggressor == us && n.opponent == third);
	}

	[Fact]
	public void APactDoesntDrawThePartnerIntoAWarItsPartnerStarts() {
		Agreements.Sign(gameData, us, them, Pact);
		them.DeclareWarOn(third, gameData.turn);
		Assert.False(PlayerRelationship.AtWar(us, third));
		Assert.False(PlayerRelationship.AtWar(us, them));
	}

	[Fact]
	public void AnAllyJoinsTheWar() {
		us.DeclareWarOn(third, gameData.turn);
		Agreements.Sign(gameData, us, them, AllianceAgainstThird);
		Assert.True(PlayerRelationship.AtWar(them, third));
		Assert.True(Agreements.Has(them, us, DealSubType.MilitaryAlliance, third.id));
	}

	[Fact]
	public void ASeparatePeaceBreaksTheAlliance() {
		Player bystander = gameData.players.First(p => !p.isHuman && !p.isBarbarians && p != us && p != them && p != third);
		bystander.EnsureRelationshipExists(us);
		bystander.EnsureRelationshipExists(them);
		us.DeclareWarOn(third, gameData.turn);
		Agreements.Sign(gameData, us, them, AllianceAgainstThird);
		int bystanderBefore = bystander.playerRelationships[us.id].attitudePenalty;
		int themBefore = them.playerRelationships[us.id].attitudePenalty;

		PlayerRelationship.SignPeaceAfterWar(us, third, gameData);
		PlayerRelationship themToUs = them.playerRelationships[us.id];
		Assert.Equal(themBefore + gameData.rules.attitude.brokeAlliance, themToUs.attitudePenalty);
		Assert.Equal(1, themToUs.brokenAlliances);
		Assert.False(Agreements.Has(us, them, DealSubType.MilitaryAlliance));
		Assert.False(Agreements.Has(them, us, DealSubType.MilitaryAlliance));
		Assert.Equal(bystanderBefore + gameData.rules.attitude.brokeAllianceWithFriend, bystander.playerRelationships[us.id].attitudePenalty);
	}

	[Fact]
	public void AnEmbargoEndsTradeWithItsTarget() {
		PlayerRelationship.RegisterMultiTurnDeal(us, third, new MultiTurnDeal(DealType.Gold, DealSubType.GoldPerTurn, DealDetails.Outbound,
			5, null, 20, gameData.turn));
		Agreements.Sign(gameData, us, them, EmbargoAgainstThird);
		Assert.DoesNotContain(us.playerRelationships[third.id].multiTurnDeals, d => d.dealSubType == DealSubType.GoldPerTurn);
		Assert.DoesNotContain(third.playerRelationships[us.id].multiTurnDeals, d => d.dealSubType == DealSubType.GoldPerTurn);
		Assert.True(Agreements.Embargoed(us, third));
		Assert.True(Agreements.Embargoed(third, them));
		Assert.False(Agreements.Embargoed(us, them));

		third.gold = 500;
		Assert.False(us.WouldAcceptDealFrom(gameData, third, new TradeOffer { gold = 500 }, new TradeOffer()));
	}

	// Makes `observer`'s attitude score toward `subject` exactly `score`.
	private void SetScore(Player observer, Player subject, int score) {
		Feel(observer, subject, 0);
		Feel(observer, subject, score - Attitude.Score(gameData, observer, subject));
		Assert.Equal(score, Attitude.Score(gameData, observer, subject));
	}

	[Fact]
	public void WhatTheAISigns() {
		// A pact with civs it's at least Polite to, right of passage at
		// least Cautious about.
		SetScore(us, them, -1);
		Assert.True(AgreementAI.WouldSign(gameData, us, them, Pact));
		Assert.True(AgreementAI.WouldSign(gameData, us, them, Passage));
		SetScore(us, them, 0);
		Assert.False(AgreementAI.WouldSign(gameData, us, them, Pact));
		Assert.True(AgreementAI.WouldSign(gameData, us, them, Passage));
		SetScore(us, them, 1);
		Assert.False(AgreementAI.WouldSign(gameData, us, them, Passage));
		SetScore(us, them, -1);

		// An embargo against a civ it's Annoyed with.
		SetScore(us, third, 0);
		Assert.False(AgreementAI.WouldSign(gameData, us, them, EmbargoAgainstThird));
		SetScore(us, third, 1);
		Assert.True(AgreementAI.WouldSign(gameData, us, them, EmbargoAgainstThird));
	}

	[Fact]
	public void TheAIWontTrustTreatyBreakers() {
		Feel(us, them, -20);
		third.playerRelationships[them.id].warDeclarationWithRoPActiveCount = 1;
		Assert.False(AgreementAI.WouldSign(gameData, us, them, Passage));
		Assert.True(AgreementAI.WouldSign(gameData, us, them, Pact));
		third.playerRelationships[them.id].brokenAlliances = 1;
		Assert.False(AgreementAI.WouldSign(gameData, us, them, Pact));
	}

	[Fact]
	public void TheAIJoinsAWarAgainstACivItDislikesIfStrongEnough() {
		Feel(us, them, -20);
		them.DeclareWarOn(third, gameData.turn);
		// Not against a civ it doesn't mind.
		Assert.False(AgreementAI.WouldSign(gameData, us, them, AllianceAgainstThird));
		Feel(us, third, 5);
		foreach (MapUnit unit in third.units.ToList()) {
			gameData.RemoveUnit(unit);
		}
		Assert.True(AgreementAI.WouldSign(gameData, us, them, AllianceAgainstThird));

		// Not while its own peace treaty with them runs.
		us.DeclareWarOn(third, gameData.turn);
		PlayerRelationship.SignPeaceAfterWar(us, third, gameData);
		Feel(us, third, 5);
		Assert.False(AgreementAI.WouldSign(gameData, us, them, AllianceAgainstThird));
		gameData.turn += gameData.rules.DefaultDealDuration;
		Assert.True(AgreementAI.WouldSign(gameData, us, them, AllianceAgainstThird));

		// Not much weaker than them.
		UnitPrototype strong = gameData.unitPrototypes.MaxBy(u => u.attack + u.defense);
		for (int i = 0; i < 5; i++) {
			gameData.SpawnUnit(third, strong, third.cities[0].location);
		}
		Assert.False(AgreementAI.WouldSign(gameData, us, them, AllianceAgainstThird));
	}

	[Fact]
	public void AnAIAsksAFriendToJoinItsWar() {
		us.DeclareWarOn(third, gameData.turn);
		Feel(them, us, -20);
		SetScore(them, third, 5);
		Feel(us, them, -20);
		foreach (MapUnit unit in third.units.ToList()) {
			gameData.RemoveUnit(unit);
		}
		AgreementAI.ProposeAgreements(us, gameData).Wait();
		Assert.True(Agreements.Has(us, them, DealSubType.MilitaryAlliance, third.id));
		Assert.True(PlayerRelationship.AtWar(them, third));
		Assert.Equal(gameData.turn, us.playerRelationships[them.id].lastAgreementOfferTurn);
	}

	[Fact]
	public void AnAIAsksOnlyEverySoOften() {
		Feel(us, them, -30);
		Feel(them, us, -30);
		us.playerRelationships[them.id].lastAgreementOfferTurn = gameData.turn - AgreementAI.ProposeEveryTurns + 1;
		AgreementAI.ProposeAgreements(us, gameData).Wait();
		Assert.False(Agreements.Has(us, them, DealSubType.MutualProtectionPact));
		gameData.turn += 1;
		// Gracious: a pact.
		AgreementAI.ProposeAgreements(us, gameData).Wait();
		Assert.True(Agreements.Has(us, them, DealSubType.MutualProtectionPact));
	}

	[Fact]
	public async Task AHumanIsAskedOnTheDealScreen() {
		them.isHuman = true;
		us.DeclareWarOn(third, gameData.turn);
		Feel(us, them, -20);
		Task asking = AgreementAI.ProposeAgreements(us, gameData);
		MsgShowTradeOffer shown = Assert.IsType<MsgShowTradeOffer>(EngineStorage.messagesToUI.Single(m => m is MsgShowTradeOffer));
		Assert.True(shown.aiGive.agreements.Single().SameAs(AllianceAgainstThird));
		Assert.True(shown.aiWant.agreements.Single().SameAs(AllianceAgainstThird));
		new MsgDiplomacyCompleted().send();
		EngineStorage.ProcessNextMessageToEngine();
		await asking;
	}

	[Fact]
	public void ADealSignsItsAgreementsOnce() {
		Feel(us, them, -20);
		TradeOffer theirs = new() { agreements = [Passage] };
		TradeOffer ours = new() { agreements = [Passage] };
		Assert.True(us.WouldAcceptDealFrom(gameData, them, theirs, ours));
		us.ExecuteDeal(gameData, them, theirs, ours);
		Assert.Single(us.playerRelationships[them.id].multiTurnDeals, d => d.dealSubType == DealSubType.RightOfPassage);

		Feel(us, them, 20);
		Assert.False(us.WouldAcceptDealFrom(gameData, them, new TradeOffer { agreements = [Pact] }, new TradeOffer { agreements = [Pact] }));
	}

	[Fact]
	public void ReputationAndOffersAreSaved() {
		PlayerRelationship relationship = us.playerRelationships[them.id];
		relationship.brokenAlliances = 2;
		relationship.lastAgreementOfferTurn = 25;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		PlayerRelationship again = reloaded.GetPlayer(us.id).playerRelationships[them.id];
		Assert.Equal(2, again.brokenAlliances);
		Assert.Equal(25, again.lastAgreementOfferTurn);
	}
}
