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

// Play by email: each human plays their turn from the save the one before
// sent them. An AI talks to a human only at the start of the human's turn,
// on their computer, and the news they missed is written into the save.
public class PlayByEmailTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	// The AI, the human who played last (still at this computer) and the
	// human away, whose turn is next.
	private readonly Player ai;
	private readonly Player playing;
	private readonly Player away;
	private readonly ID controller = EngineStorage.uiControllerID;

	public PlayByEmailTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		gameData.playByEmail = true;
		playing = gameData.players.First(p => p.isHuman);
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(ai, away) = (ais[0], ais[1]);
		away.isHuman = true;
		EngineStorage.uiControllerID = playing.id;

		CityInteractions.BuildCity(ai.units[0].location, ai, "Home");
		CityInteractions.BuildCity(away.units[0].location, away, "Abroad");
		ai.EnsureRelationshipExists(away);
		foreach (MapUnit unit in ai.units.Concat(away.units).ToList()) {
			gameData.RemoveUnit(unit);
		}
		// The same advances, so there are none to trade (the AI's trades are
		// a roll of the dice), and none to demand.
		foreach (ID known in away.knownTechs) {
			ai.knownTechs.Add(known);
		}
		foreach (ID known in ai.knownTechs) {
			away.knownTechs.Add(known);
		}
		gameData.turn = 30;
		// No goods to swap this turn.
		ai.playerRelationships[away.id].lastGoodsOfferTurn = gameData.turn;
		SetScore(ai, away, 0);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
		EngineStorage.uiControllerID = controller;
	}

	private void SetScore(Player observer, Player subject, int score) {
		observer.playerRelationships[subject.id].attitudePenalty = 0;
		observer.playerRelationships[subject.id].attitudePenalty = score - Attitude.Score(gameData, observer, subject);
	}

	// Enough for the AI to demand half the human's gold.
	private void Outmatch() {
		away.gold = 300;
		gameData.SpawnUnit(away, gameData.unitPrototypes.Find(u => u.name == "Warrior"), away.cities[0].location);
		while (ai.CalculateMilitaryStrength() / away.CalculateMilitaryStrength() < TributeAI.OutmatchFactor) {
			gameData.SpawnUnit(ai, gameData.unitPrototypes.Find(u => u.name == "Warrior"), ai.cities[0].location);
		}
	}

	private static void Answer<T>(MessageToEngine answer) where T : MessageToEngine {
		Assert.True(System.Threading.SpinWait.SpinUntil(EngineStorage.IsWaitingForMessageToEngine<T>, TimeSpan.FromSeconds(5)));
		answer.send();
		EngineStorage.ProcessNextMessageToEngine();
	}

	private Task Arrive(Player human) {
		EngineStorage.uiControllerID = human.id;
		return TurnHandling.HearFromAIs(human);
	}

	[Fact]
	public void OnItsOwnTurnTheAIDoesntTalkToAHuman() {
		Outmatch();
		Task demanding = TributeAI.MakeDemands(ai, gameData);
		Assert.True(demanding.IsCompleted);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgTributeDemanded>());
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgHandOver>());
		Assert.Null(ai.playerRelationships[away.id].lastTributeDemandTurn);
	}

	// Not playing by email, it asks at once (handing over the UI).
	[Fact]
	public async Task OtherwiseItTalksOnItsTurn() {
		gameData.playByEmail = false;
		Outmatch();
		Task demanding = TributeAI.MakeDemands(ai, gameData);
		MsgTributeDemanded demand = Assert.Single(EngineStorage.messagesToUI.OfType<MsgTributeDemanded>());
		Answer<MsgTributeAnswer>(new MsgTributeAnswer(ai.id, away.id, demand.demand, pay: true));
		await demanding;
	}

	[Fact]
	public void TheHumanHearsItAtTheStartOfTheirTurn() {
		Outmatch();
		Task hearing = Arrive(away);
		MsgTributeDemanded demand = Assert.Single(EngineStorage.messagesToUI.OfType<MsgTributeDemanded>());
		Assert.Equal(ai, demand.demander);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgTurnReady>());

		Answer<MsgTributeAnswer>(new MsgTributeAnswer(ai.id, away.id, demand.demand, pay: true));
		Assert.True(hearing.Wait(TimeSpan.FromSeconds(5)));
		Assert.Equal(150, away.gold);
		Assert.Equal(away.id, Assert.Single(EngineStorage.messagesToUI.OfType<MsgTurnReady>()).playerId);
		Assert.Equal(gameData.turn, away.heardFromAIsOnTurn);
	}

	// A reload of the turn doesn't give the AIs a second say.
	[Fact]
	public void OnlyOnceATurn() {
		away.heardFromAIsOnTurn = gameData.turn;
		Outmatch();
		Assert.True(Arrive(away).IsCompleted);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgTributeDemanded>());
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgTurnReady>());
	}

	[Fact]
	public void TheNewsIsWrittenDownForTheHumanAway() {
		new MsgGoldenAge(away.id, true).send();
		new MsgGoldenAge(playing.id, true).send();
		new MsgGoldenAge(ai.id, true).send();
		Assert.Equal(["Our civilization has entered a Golden Age!"], away.newsWhileAway);
		Assert.Empty(playing.newsWhileAway);
	}

	[Fact]
	public void NotInAHotseatGame() {
		gameData.playByEmail = false;
		new MsgGoldenAge(away.id, true).send();
		Assert.Empty(away.newsWhileAway);
	}

	// Their news comes first, and the AIs speak once they've read it.
	[Fact]
	public void ItIsShownWhenTheyArrive() {
		new MsgGoldenAge(away.id, true).send();
		EngineStorage.messagesToUI.Clear();
		Task hearing = Arrive(away);
		MsgNewsWhileAway news = Assert.IsType<MsgNewsWhileAway>(EngineStorage.messagesToUI.Single());
		Assert.Equal(away.id, news.playerId);
		Assert.Equal(["Our civilization has entered a Golden Age!"], news.news);
		Assert.Empty(away.newsWhileAway);

		Answer<MsgNewsRead>(new MsgNewsRead());
		Assert.True(hearing.Wait(TimeSpan.FromSeconds(5)));
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgTurnReady>());
	}

	[Fact]
	public void EachSideReadsItsOwnNews() {
		City city = away.cities[0];
		MsgCityCaptured captured = new(city, ai.id, away.id, 25, [], 0);
		Assert.Equal($"{city.name} was captured by the {ai.civilization.noun}, who stole 25 gold!", NewsWhileAway.Describe(captured, away));
		Assert.Equal($"We captured {city.name}.", NewsWhileAway.Describe(captured, ai));
		Assert.Null(NewsWhileAway.Describe(captured, playing));

		MsgWarDeclarationNotification war = new(ai, away);
		Assert.Equal($"The {ai.civilization.noun} have declared war on us!", NewsWhileAway.Describe(war, away));
		// News of others' wars only for those who know both.
		Assert.Null(NewsWhileAway.Describe(war, playing));
		playing.EnsureRelationshipExists(ai);
		playing.EnsureRelationshipExists(away);
		Assert.Equal($"The {ai.civilization.noun} declared war on the {away.civilization.noun}.", NewsWhileAway.Describe(war, playing));
	}

	[Fact]
	public void TheSaveKeepsItAll() {
		away.newsWhileAway.Add("Our civilization has entered a Golden Age!");
		away.heardFromAIsOnTurn = 29;
		gameData.unitedNations.awaitedElection = new AwaitedElection {
			candidates = [ai.id],
			votes = [new CastVote { voter = ai.id, candidate = ai.id }, new CastVote { voter = playing.id }],
			awaited = [away.id],
		};

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.True(reloaded.playByEmail);
		Player them = reloaded.GetPlayer(away.id);
		Assert.Equal(["Our civilization has entered a Golden Age!"], them.newsWhileAway);
		Assert.Equal(29, them.heardFromAIsOnTurn);
		AwaitedElection election = reloaded.unitedNations.awaitedElection;
		Assert.Equal([ai.id], election.candidates);
		Assert.Equal([away.id], election.awaited);
		Assert.Equal(ai.id, election.votes[0].candidate);
		Assert.Null(election.votes[1].candidate);
	}

	// The save sent on is named for the human who plays next.
	[Fact]
	public void TheSaveIsNamedForTheNextPlayer() {
		string name = SaveNameGenerator.PlayByEmailFileName(gameData, away);
		Assert.StartsWith($"PBEM-{away.civilization.noun} to play-Turn 030-", name);
		Assert.EndsWith(".json", name);
	}

	// The United Nations' human owner calls an election once at their
	// computer (the turn order leaves it to them).
	[Fact]
	public void AHumanOwnerCallsAnElectionAtTheirTurn() {
		gameData.victoryConditions = new VictoryConditions { AllowDiplomaticVictory = true };
		City seat = away.cities[0];
		seat.AddBuilding(gameData.Buildings.Find(b => b.HasWonderFlag(SaveBuilding.WonderFlag.AllowDiplomaticVictory)), gameData);
		Assert.True(UnitedNations.ElectionDue(gameData, away));

		Task hearing = Arrive(away);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgElectionOffer>(), m => m.playerId == away.id);
		Answer<MsgCallElection>(new MsgCallElection(false));
		Assert.True(hearing.Wait(TimeSpan.FromSeconds(5)));
	}
}
