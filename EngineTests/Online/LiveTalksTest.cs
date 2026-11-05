using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Online;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Online;

// Civ3's live diplomacy between humans online: the envoy and its three
// answers, both at the table, each change shown to the other, accepting
// (and taking it back), and the trade once both have accepted the same
// table.
public class LiveTalksTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly ID controller = EngineStorage.uiControllerID;
	private static readonly ID a = new("Player", 1), b = new("Player", 2), c = new("Player", 3);

	private readonly List<(ID to, OnlineMessage message)> sent = new();
	private readonly List<(ID a, string aTerms, ID b, string bTerms)> traded = new();
	private readonly LiveTalks talks;
	private string whyNot;

	public LiveTalksTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		talks = new LiveTalks((to, message) => sent.Add((to, message)), (_, _) => whyNot, (x, xTerms, y, yTerms) => traded.Add((x, xTerms, y, yTerms)));
	}

	public void Dispose() {
		EngineStorage.AbandonWork();
		EngineStorage.uiControllerID = controller;
	}

	private T Last<T>(ID to) where T : OnlineMessage => sent.Where(s => s.to == to).Select(s => s.message).OfType<T>().LastOrDefault();

	// A at the table with B.
	private void Open() {
		talks.Handle(a, new TalkEnvoy { with = b.ToString() });
		talks.Handle(b, new TalkAnswer { with = a.ToString(), answer = TalkAnswer.Hear });
		sent.Clear();
	}

	[Fact]
	public void AnEnvoyIsHeardRefusedOrPutOff() {
		talks.Handle(a, new TalkEnvoy { with = b.ToString() });
		Assert.Equal(a.ToString(), Last<TalkEnvoy>(b).with);
		talks.Handle(b, new TalkAnswer { with = a.ToString(), answer = TalkAnswer.Hear });
		TalkState opened = Last<TalkState>(a);
		Assert.Equal(b.ToString(), opened.with);
		Assert.True(opened.envoyFromUs);
		Assert.False(Last<TalkState>(b).envoyFromUs);
		talks.Handle(a, new TalkEnd { with = b.ToString() });
		Assert.Equal(TalkEnd.Cancelled, Last<TalkEnd>(b).reason);

		talks.Handle(a, new TalkEnvoy { with = b.ToString() });
		talks.Handle(b, new TalkAnswer { with = a.ToString(), answer = TalkAnswer.Refuse });
		Assert.Equal(TalkEnd.Refused, Last<TalkEnd>(a).reason);
		talks.Handle(a, new TalkEnvoy { with = b.ToString() });
		talks.Handle(b, new TalkAnswer { with = a.ToString(), answer = TalkAnswer.Later });
		Assert.Equal(TalkEnd.Later, Last<TalkEnd>(a).reason);
		// (Closing the envoy's popup refuses it.)
		talks.Handle(a, new TalkEnvoy { with = b.ToString() });
		talks.Handle(b, new TalkEnd { with = a.ToString() });
		Assert.Equal(TalkEnd.Refused, Last<TalkEnd>(a).reason);
	}

	// One talk at a time; and not with someone the game says we can't.
	[Fact]
	public void ATalkNeedsBothFreeAndAllowed() {
		Open();
		talks.Handle(c, new TalkEnvoy { with = b.ToString() });
		Assert.Equal(TalkEnd.Busy, Last<TalkEnd>(c).reason);
		Assert.Null(Last<TalkEnvoy>(b));
		whyNot = "We can't contact the Maya now.";
		talks.Handle(c, new TalkEnvoy { with = a.ToString() });
		TalkEnd refused = Last<TalkEnd>(c);
		Assert.Equal(TalkEnd.Unable, refused.reason);
		Assert.Equal("We can't contact the Maya now.", refused.text);
	}

	// Each change shows on the other's side; a change takes both
	// acceptances back; accepting can be taken back; both accept: trade.
	[Fact]
	public void BothAcceptTheSameTable() {
		Open();
		talks.Handle(a, new TalkTable { with = b.ToString(), terms = "gold", mood = LiveTalks.Pleased });
		TalkState seen = Last<TalkState>(b);
		Assert.Equal("gold", seen.theirTerms);
		Assert.Equal(LiveTalks.Pleased, seen.theirMood);
		talks.Handle(a, new TalkTable { with = b.ToString(), terms = "gold", accepted = true });
		Assert.True(Last<TalkState>(b).theyAccepted);
		Assert.True(Last<TalkState>(a).weAccepted);

		// B puts up something: the table changed, A's acceptance is gone.
		talks.Handle(b, new TalkTable { with = a.ToString(), terms = "tech" });
		Assert.False(Last<TalkState>(b).theyAccepted);
		Assert.Equal("tech", Last<TalkState>(a).theirTerms);

		// (Civ3's "Do Not Accept".)
		talks.Handle(b, new TalkTable { with = a.ToString(), terms = "tech", accepted = true });
		talks.Handle(b, new TalkTable { with = a.ToString(), terms = "tech", accepted = false });
		Assert.False(Last<TalkState>(a).theyAccepted);
		Assert.Empty(traded);

		talks.Handle(b, new TalkTable { with = a.ToString(), terms = "tech", accepted = true });
		talks.Handle(a, new TalkTable { with = b.ToString(), terms = "gold", accepted = true });
		Assert.Equal([(a, "gold", b, "tech")], traded);
		Assert.Equal(TalkEnd.Done, Last<TalkEnd>(a).reason);
		Assert.Equal(TalkEnd.Done, Last<TalkEnd>(b).reason);
	}

	// What's said at the table only the other sees; one who leaves ends it.
	[Fact]
	public void TheTableIsPrivate() {
		Open();
		talks.Handle(a, new TalkSay { with = b.ToString(), text = "Deal?" });
		Assert.Equal("Deal?", Last<TalkSay>(b).text);
		Assert.Single(sent);
		talks.Handle(c, new TalkSay { with = b.ToString(), text = "Me too" });
		Assert.Single(sent);
		talks.Left(b);
		Assert.Equal(TalkEnd.Left, Last<TalkEnd>(a).reason);
	}

	// A half of the table comes back as it went; nonsense is nothing.
	[Fact]
	public void TermsGoOverTheNetwork() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		TradeOffer offer = new() { gold = 25, techs = [gameData.techs[2]] };
		TradeOffer back = LiveTalks.Read(LiveTalks.Write(offer), gameData);
		Assert.Equal(25, back.gold);
		Assert.Equal([gameData.techs[2]], back.techs);
		Assert.True(LiveTalks.Read("{not terms", gameData).IsEmpty());
		Assert.Equal("", LiveTalks.Write(new TradeOffer()));
	}

	// Every game makes the trade the two accepted, what they can't give
	// taken off.
	[Fact]
	public void TheTradeTheyAccepted() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		Player us = gameData.players.First(p => p.isHuman);
		Player them = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		them.isHuman = true;
		us.EnsureRelationshipExists(them);
		us.gold = 40;
		them.gold = 0;
		Assert.True(HumanDiplomacy.TradeAtTheTable(gameData, us, them, new TradeOffer { gold = 30 }, new TradeOffer()));
		Assert.Equal(10, us.gold);
		Assert.Equal(30, them.gold);
		// (More than they have: off the table, so nothing is left.)
		Assert.False(HumanDiplomacy.TradeAtTheTable(gameData, us, them, new TradeOffer { gold = 500 }, new TradeOffer()));
		Assert.Equal(10, us.gold);
	}
}
