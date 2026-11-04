using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Hotseat and play by email: two humans trade as in Civ3. The deal goes to
// the other at the start of their turn (an envoy first), and is done once
// both have agreed to the same table.
public class HumanDiplomacyTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	// The human who makes the offer, and the one it goes to.
	private readonly Player us;
	private readonly Player them;
	private readonly Player ai;
	// An advance only we know, and one only they know.
	private readonly Tech ours;
	private readonly Tech theirs;
	private readonly ID controller = EngineStorage.uiControllerID;

	public HumanDiplomacyTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		us = gameData.players.First(p => p.isHuman);
		Player[] others = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(them, ai) = (others[0], others[1]);
		them.isHuman = true;
		us.EnsureRelationshipExists(them);
		us.EnsureRelationshipExists(ai);
		them.EnsureRelationshipExists(ai);
		us.gold = 300;
		them.gold = 100;
		ours = gameData.techs.First(t => !us.knownTechs.Contains(t.id) && !them.knownTechs.Contains(t.id));
		theirs = gameData.techs.First(t => t != ours && !us.knownTechs.Contains(t.id) && !them.knownTechs.Contains(t.id));
		us.knownTechs.Add(ours.id);
		them.knownTechs.Add(theirs.id);
		gameData.turn = 30;
		EngineStorage.uiControllerID = us.id;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
		EngineStorage.uiControllerID = controller;
	}

	private void Offer(Player from, Player to, HumanDealAnswer answer, TradeOffer fromGives, TradeOffer toGives) {
		HumanDiplomacy.Answer(gameData, from, to, answer, fromGives, toGives);
	}

	private HumanDeal Pending() => HumanDiplomacy.Between(gameData, us, them);

	private string NewsFor(Player player) => EngineStorage.messagesToUI.OfType<MsgDiplomacyNews>().LastOrDefault(m => m.playerId == player.id)?.text;

	private static void Answer<T>(params MessageToEngine[] answers) where T : MessageToEngine {
		Assert.True(System.Threading.SpinWait.SpinUntil(EngineStorage.IsWaitingForMessageToEngine<T>, TimeSpan.FromSeconds(5)));
		foreach (MessageToEngine answer in answers) {
			answer.send();
			EngineStorage.ProcessNextMessageToEngine();
		}
	}

	// Sending a deal changes nothing yet: it waits for their turn.
	[Fact]
	public void ADealWaitsForTheirTurn() {
		Offer(us, them, HumanDealAnswer.Send, new TradeOffer { gold = 50 }, new TradeOffer());
		HumanDeal deal = Assert.Single(gameData.humanDeals);
		Assert.Equal(us.id, deal.from);
		Assert.Equal(them.id, deal.to);
		Assert.Equal(50, deal.fromGives.gold);
		Assert.False(deal.fromAccepted);
		Assert.True(deal.envoy);
		Assert.Equal(300, us.gold);
		Assert.Equal($"Our envoy is on the way. The {them.civilization.noun} will see our offer on their turn.", NewsFor(us));
	}

	// The trade is done once both agree to the same table.
	[Fact]
	public void BothAgreeToTheSameTable() {
		Offer(us, them, HumanDealAnswer.Accept, new TradeOffer { gold = 50 }, new TradeOffer { techs = [theirs] });
		Assert.Equal(300, us.gold);

		Offer(them, us, HumanDealAnswer.Accept, new TradeOffer { techs = [theirs] }, new TradeOffer { gold = 50 });
		Assert.Empty(gameData.humanDeals);
		Assert.Equal(250, us.gold);
		Assert.Equal(150, them.gold);
		Assert.Contains(theirs.id, us.knownTechs);
		Assert.Equal($"The trade with the {us.civilization.noun} is done.", NewsFor(them));
		Assert.Equal($"The {them.civilization.noun} accepted our offer. The trade is done.", NewsFor(us));
	}

	// Sent without our agreement, they can't take it as it is: their
	// agreement sends it back for ours.
	[Fact]
	public void WithoutOurAgreementItComesBack() {
		Offer(us, them, HumanDealAnswer.Send, new TradeOffer { gold = 50 }, new TradeOffer());
		Offer(them, us, HumanDealAnswer.Accept, new TradeOffer(), new TradeOffer { gold = 50 });
		Assert.Equal(300, us.gold);
		HumanDeal deal = Assert.Single(gameData.humanDeals);
		Assert.Equal(them.id, deal.from);
		Assert.True(deal.fromAccepted);
		// Answering us, no envoy.
		Assert.False(deal.envoy);

		Offer(us, them, HumanDealAnswer.Accept, new TradeOffer { gold = 50 }, new TradeOffer());
		Assert.Equal(250, us.gold);
		Assert.Empty(gameData.humanDeals);
	}

	// They add to their side: our agreement no longer stands, and it comes
	// back to us with theirs.
	[Fact]
	public void AChangedTableGoesBack() {
		Offer(us, them, HumanDealAnswer.Accept, new TradeOffer { techs = [ours] }, new TradeOffer());
		Offer(them, us, HumanDealAnswer.Accept, new TradeOffer { gold = 80 }, new TradeOffer { techs = [ours] });
		Assert.DoesNotContain(ours.id, them.knownTechs);
		HumanDeal deal = Assert.Single(gameData.humanDeals);
		Assert.Equal(us.id, deal.to);
		Assert.Equal(80, deal.fromGives.gold);

		Offer(us, them, HumanDealAnswer.Accept, new TradeOffer { techs = [ours] }, new TradeOffer { gold = 80 });
		Assert.Contains(ours.id, them.knownTechs);
		Assert.Equal(380, us.gold);
	}

	// One deal at a time between two humans: a new one replaces ours, and
	// still comes with the envoy they haven't heard yet.
	[Fact]
	public void OneDealOnTheTable() {
		Offer(us, them, HumanDealAnswer.Send, new TradeOffer { gold = 50 }, new TradeOffer());
		Offer(us, them, HumanDealAnswer.Send, new TradeOffer { gold = 70 }, new TradeOffer());
		HumanDeal deal = Assert.Single(gameData.humanDeals);
		Assert.Equal(70, deal.fromGives.gold);
		Assert.True(deal.envoy);
	}

	[Fact]
	public void TurningItDownTellsThem() {
		Offer(us, them, HumanDealAnswer.Send, new TradeOffer { gold = 50 }, new TradeOffer());
		Offer(them, us, HumanDealAnswer.Decline, new TradeOffer(), new TradeOffer());
		Assert.Empty(gameData.humanDeals);
		Assert.Equal($"The {them.civilization.noun} have not accepted our offer.", NewsFor(us));
	}

	// A deal waits a turn or more: what can no longer be given comes off
	// the table, and the table as it was can't be accepted.
	[Fact]
	public void WhatIsNoLongerThereComesOff() {
		Offer(us, them, HumanDealAnswer.Accept, new TradeOffer { gold = 200, techs = [ours] }, new TradeOffer());
		us.gold = 100;
		them.knownTechs.Add(ours.id);

		(TradeOffer weGive, TradeOffer theyGive) = HumanDiplomacy.Tables(gameData, Pending(), them, us, out bool changed);
		Assert.True(changed);
		Assert.True(weGive.IsEmpty());
		Assert.Null(theyGive.gold);
		Assert.Empty(theyGive.techs);

		// Accepting what's left (nothing) does nothing.
		Offer(them, us, HumanDealAnswer.Accept, new TradeOffer(), new TradeOffer { gold = 200, techs = [ours] });
		Assert.Equal(100, us.gold);
	}

	// At war, peace first; signing it ends the war.
	[Fact]
	public void PeaceBetweenHumans() {
		us.DeclareWarOn(them, gameData.turn);
		Assert.True(PlayerRelationship.AtWar(us, them));
		Assert.False(HumanDiplomacy.Prune(gameData, us, them, new TradeOffer { partOfPeaceTreaty = true, gold = 50 }));
		TradeOffer goldOnly = new() { gold = 50 };
		Assert.True(HumanDiplomacy.Prune(gameData, us, them, goldOnly));
		Assert.True(goldOnly.IsEmpty());

		Offer(us, them, HumanDealAnswer.Accept, new TradeOffer { partOfPeaceTreaty = true, gold = 50 }, new TradeOffer { partOfPeaceTreaty = true });
		Offer(them, us, HumanDealAnswer.Accept, new TradeOffer { partOfPeaceTreaty = true }, new TradeOffer { partOfPeaceTreaty = true, gold = 50 });
		Assert.False(PlayerRelationship.AtWar(us, them));
		Assert.Equal(150, them.gold);
	}

	// Only between humans who have met.
	[Fact]
	public void OnlyWithAHuman() {
		Offer(us, ai, HumanDealAnswer.Send, new TradeOffer { gold = 50 }, new TradeOffer());
		Assert.Empty(gameData.humanDeals);
		Assert.False(HumanDiplomacy.CanContact(gameData, us, ai));
		Assert.True(HumanDiplomacy.CanContact(gameData, us, them));
	}

	// At the start of their turn they're asked whether to hear the envoy,
	// then shown the deal; in hotseat as by email.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TheyHearTheEnvoyAndSeeTheDeal(bool byEmail) {
		gameData.playByEmail = byEmail;
		Offer(us, them, HumanDealAnswer.Accept, new TradeOffer { gold = 50 }, new TradeOffer());
		EngineStorage.messagesToUI.Clear();
		EngineStorage.uiControllerID = them.id;

		Task hearing = TurnHandling.HearFromOthers(them);
		MsgEnvoyArrives envoy = Assert.IsType<MsgEnvoyArrives>(EngineStorage.messagesToUI.Single());
		Assert.Equal(them.id, envoy.playerId);
		Assert.Equal(us.id, envoy.fromId);

		Answer<MsgEnvoyAnswer>(new MsgEnvoyAnswer(us.id, EnvoyAnswer.Hear));
		Assert.True(System.Threading.SpinWait.SpinUntil(() => EngineStorage.messagesToUI.OfType<MsgShowHumanDeal>().Any(), TimeSpan.FromSeconds(5)));
		MsgShowHumanDeal shown = EngineStorage.messagesToUI.OfType<MsgShowHumanDeal>().Single();
		Assert.Equal(us.id, shown.fromId);

		Answer<MsgDiplomacyCompleted>(new MsgHumanDeal(us.id, HumanDealAnswer.Accept, new TradeOffer(), new TradeOffer { gold = 50 }),
			new MsgDiplomacyCompleted());
		Assert.True(hearing.Wait(TimeSpan.FromSeconds(5)));
		Assert.Equal(150, them.gold);
		Assert.Equal(them.id, EngineStorage.messagesToUI.OfType<MsgTurnReady>().Single().playerId);
	}

	// Refused, the deal is gone and we hear of it on our turn (by email,
	// written into the save).
	[Fact]
	public void TheEnvoyIsRefused() {
		gameData.playByEmail = true;
		Offer(us, them, HumanDealAnswer.Accept, new TradeOffer { gold = 50 }, new TradeOffer());
		EngineStorage.uiControllerID = them.id;
		Task hearing = TurnHandling.HearFromOthers(them);
		Answer<MsgEnvoyAnswer>(new MsgEnvoyAnswer(us.id, EnvoyAnswer.Refuse));
		Assert.True(hearing.Wait(TimeSpan.FromSeconds(5)));

		Assert.Empty(gameData.humanDeals);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowHumanDeal>());
		Assert.Equal([$"The {them.civilization.noun} have refused our envoy!"], us.newsWhileAway);
		Assert.True(HumanDiplomacy.CanContact(gameData, us, them));
	}

	// "Do not contact me for 20 turns."
	[Fact]
	public void RefusedForAWhile() {
		Offer(us, them, HumanDealAnswer.Send, new TradeOffer { gold = 50 }, new TradeOffer());
		HumanDiplomacy.AnswerEnvoy(gameData, them, us, EnvoyAnswer.RefuseForAWhile);
		Assert.Empty(gameData.humanDeals);
		Assert.Equal(20, HumanDiplomacy.EnvoysRefusedFor(gameData, us, them));
		Assert.False(HumanDiplomacy.CanContact(gameData, us, them));
		Assert.True(HumanDiplomacy.CanContact(gameData, them, us));
		Assert.Equal($"The {them.civilization.noun} have refused our envoy, and asked us not to come back for 20 turns.", NewsFor(us));

		Offer(us, them, HumanDealAnswer.Send, new TradeOffer { gold = 50 }, new TradeOffer());
		Assert.Empty(gameData.humanDeals);
		gameData.turn += 20;
		Assert.True(HumanDiplomacy.CanContact(gameData, us, them));
	}

	// Their deal comes back to us without an envoy, and a deal from a human
	// who has left (handed to the AI) is dropped.
	[Fact]
	public void NoEnvoyForAnAnswer() {
		Offer(us, them, HumanDealAnswer.Send, new TradeOffer { gold = 50 }, new TradeOffer());
		Offer(them, us, HumanDealAnswer.Send, new TradeOffer { techs = [theirs] }, new TradeOffer { gold = 50 });
		Task hearing = TurnHandling.HearFromOthers(us);
		Assert.True(System.Threading.SpinWait.SpinUntil(() => EngineStorage.messagesToUI.OfType<MsgShowHumanDeal>().Any(), TimeSpan.FromSeconds(5)));
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgEnvoyArrives>());
		Answer<MsgDiplomacyCompleted>(new MsgDiplomacyCompleted());
		Assert.True(hearing.Wait(TimeSpan.FromSeconds(5)));
		// Not answered: it stays for later.
		Assert.Single(gameData.humanDeals);

		them.isHuman = false;
		EngineStorage.messagesToUI.Clear();
		Assert.True(TurnHandling.HearFromOthers(us).IsCompleted);
		Assert.Empty(gameData.humanDeals);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowHumanDeal>());
	}

	[Fact]
	public void TheSaveKeepsTheDeal() {
		Agreement passage = new() { type = DealSubType.RightOfPassage };
		gameData.humanDeals.Add(new HumanDeal {
			from = us.id,
			to = them.id,
			fromGives = DealTerms.Of(new TradeOffer { gold = 50, techs = [ours], agreements = [passage] }),
			toGives = DealTerms.Of(new TradeOffer { agreements = [passage] }),
			envoy = true,
		});
		them.playerRelationships[us.id].refuseEnvoysUntilTurn = 44;

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		HumanDeal deal = Assert.Single(reloaded.humanDeals);
		Assert.Equal(us.id, deal.from);
		Assert.True(deal.envoy);
		Assert.Equal([ours.id], deal.fromGives.techs);
		Assert.Equal(DealSubType.RightOfPassage, Assert.Single(deal.toGives.agreements).type);
		Assert.True(deal.fromGives.SameAs(Pending().fromGives));
		Assert.Equal(44, reloaded.GetPlayer(them.id).playerRelationships[us.id].refuseEnvoysUntilTurn);
	}

	[Fact]
	public void TheTermsKeepWhatIsOnTheTable() {
		TradeOffer offer = new() { gold = 5, techs = [ours], worldMap = true, contacts = [ai.id], resources = ["Iron"], goldPerTurn = 3 };
		DealTerms terms = DealTerms.Of(offer);
		Assert.True(terms.SameAs(DealTerms.Of(terms.ToOffer(gameData))));
		Assert.False(terms.SameAs(DealTerms.Of(new TradeOffer { gold = 5 })));
	}
}
