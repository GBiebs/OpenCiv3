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

// Demands and tribute: the AI demands of civs with half its military or
// less that it isn't fond of; an AI gives in to three times its military;
// a refusal means war if the demander is Annoyed and no peace treaty runs,
// else its bluff is called.
public class TributeTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tech theirTech;

	public TributeTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		CityInteractions.BuildCity(us.units[0].location, us, "Home");
		CityInteractions.BuildCity(them.units[0].location, them, "Abroad");
		us.EnsureRelationshipExists(them);
		foreach (MapUnit unit in us.units.Concat(them.units).ToList()) {
			gameData.RemoveUnit(unit);
		}
		// The only advance they have that we lack.
		foreach (ID known in them.knownTechs) {
			us.knownTechs.Add(known);
		}
		theirTech = gameData.techs.First(t => !us.knownTechs.Contains(t.id) && !them.knownTechs.Contains(t.id));
		them.knownTechs.Add(theirTech.id);
		Warriors(them, 1);
		gameData.turn = 30;
		// Cautious of each other.
		SetScore(us, them, 0);
		SetScore(them, us, 0);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private void Warriors(Player player, int count) {
		for (int i = 0; i < count; i++) {
			gameData.SpawnUnit(player, gameData.unitPrototypes.Find(u => u.name == "Warrior"), player.cities[0].location);
		}
	}

	private double Ratio() => us.CalculateMilitaryStrength() / them.CalculateMilitaryStrength();

	// Our military at least `at` times theirs.
	private void Outmatch(double at) {
		while (Ratio() < at) {
			Warriors(us, 1);
		}
	}

	private void SetScore(Player observer, Player subject, int score) {
		observer.playerRelationships[subject.id].attitudePenalty = 0;
		observer.playerRelationships[subject.id].attitudePenalty = score - Attitude.Score(gameData, observer, subject);
	}

	[Fact]
	public void TheAIDemandsAnAdvanceOfACivWithHalfItsMilitary() {
		Assert.Null(TributeAI.Demand(gameData, us, them));
		Outmatch(TributeAI.OutmatchFactor);
		TradeOffer demand = TributeAI.Demand(gameData, us, them);
		Assert.Equal([theirTech], demand.techs);
	}

	[Fact]
	public void WithoutAnAdvanceToTakeItDemandsHalfTheTreasury() {
		Outmatch(TributeAI.OutmatchFactor);
		them.knownTechs.Remove(theirTech.id);
		them.gold = 99;
		Assert.Null(TributeAI.Demand(gameData, us, them));
		them.gold = 300;
		Assert.Equal(150, TributeAI.Demand(gameData, us, them).gold);
	}

	[Fact]
	public void NotOfFriendsOrAtWar() {
		Outmatch(TributeAI.OutmatchFactor);
		SetScore(us, them, -1);
		Assert.Null(TributeAI.Demand(gameData, us, them));
		SetScore(us, them, 0);
		Assert.NotNull(TributeAI.Demand(gameData, us, them));
		us.DeclareWarOn(them, gameData.turn);
		Assert.Null(TributeAI.Demand(gameData, us, them));
	}

	[Fact]
	public void AnOutmatchedAIPaysAndBothRemember() {
		Outmatch(TributeAI.GiveInFactor);
		int theirsBefore = them.playerRelationships[us.id].attitudePenalty;
		int oursBefore = us.playerRelationships[them.id].attitudePenalty;
		TributeAI.MakeDemands(us, gameData).Wait();
		Assert.Contains(theirTech.id, us.knownTechs);
		AttitudeRules rules = gameData.rules.attitude;
		Assert.Equal(theirsBefore + rules.demandedTribute, them.playerRelationships[us.id].attitudePenalty);
		Assert.Equal(oursBefore + rules.paidUsTribute, us.playerRelationships[them.id].attitudePenalty);
		// Tribute is no gift.
		Assert.Equal(0, us.playerRelationships[them.id].giftGold);
	}

	[Fact]
	public void AFriendlyCivMindsLess() {
		Outmatch(TributeAI.GiveInFactor);
		SetScore(them, us, -5);
		int before = them.playerRelationships[us.id].attitudePenalty;
		TributeAI.MakeDemands(us, gameData).Wait();
		Assert.Equal(before + gameData.rules.attitude.demandedTributeWhileFriendly, them.playerRelationships[us.id].attitudePenalty);
	}

	[Fact]
	public void ARefusalMeansWarIfTheDemanderIsAnnoyed() {
		Outmatch(TributeAI.OutmatchFactor);
		Assert.True(Ratio() < TributeAI.GiveInFactor);
		SetScore(us, them, 1);
		TributeAI.MakeDemands(us, gameData).Wait();
		Assert.DoesNotContain(theirTech.id, us.knownTechs);
		Assert.True(PlayerRelationship.AtWar(us, them));
		Assert.Contains(EngineStorage.messagesToUI, m => m is MsgWarDeclarationNotification);
	}

	[Fact]
	public void OtherwiseItsBluffIsCalled() {
		Outmatch(TributeAI.OutmatchFactor);
		int before = us.playerRelationships[them.id].attitudePenalty;
		TributeAI.MakeDemands(us, gameData).Wait();
		Assert.False(PlayerRelationship.AtWar(us, them));
		Assert.Equal(before + gameData.rules.attitude.calledOurBluff, us.playerRelationships[them.id].attitudePenalty);
	}

	[Fact]
	public void NoWarWhileAPeaceTreatyRuns() {
		us.DeclareWarOn(them, gameData.turn);
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Outmatch(TributeAI.OutmatchFactor);
		SetScore(us, them, 1);
		TributeAI.MakeDemands(us, gameData).Wait();
		Assert.False(PlayerRelationship.AtWar(us, them));
	}

	[Fact]
	public void DemandsComeEverySoOften() {
		Outmatch(TributeAI.OutmatchFactor);
		TributeAI.MakeDemands(us, gameData).Wait();
		Assert.Equal(gameData.turn, us.playerRelationships[them.id].lastTributeDemandTurn);
		int penalty = them.playerRelationships[us.id].attitudePenalty;
		gameData.turn += TributeAI.DemandEveryTurns - 1;
		TributeAI.MakeDemands(us, gameData).Wait();
		Assert.Equal(penalty, them.playerRelationships[us.id].attitudePenalty);
		gameData.turn += 1;
		TributeAI.MakeDemands(us, gameData).Wait();
		Assert.NotEqual(penalty, them.playerRelationships[us.id].attitudePenalty);
	}

	[Fact]
	public async Task AHumanIsAskedAndAnswers() {
		them.isHuman = true;
		Outmatch(TributeAI.GiveInFactor);
		Task demanding = TributeAI.MakeDemands(us, gameData);
		MsgTributeDemanded shown = Assert.IsType<MsgTributeDemanded>(EngineStorage.messagesToUI.Single(m => m is MsgTributeDemanded));
		Assert.Equal(us, shown.demander);
		Assert.Equal([theirTech], shown.demand.techs);

		new MsgTributeAnswer(us.id, them.id, shown.demand, pay: true).send();
		EngineStorage.ProcessNextMessageToEngine();
		await demanding;
		Assert.Contains(theirTech.id, us.knownTechs);
	}

	[Fact]
	public void AHumansRefusalIsTheSameAsAnAIs() {
		them.isHuman = true;
		SetScore(us, them, 1);
		new MsgTributeAnswer(us.id, them.id, new TradeOffer { techs = [theirTech] }, pay: false).process();
		Assert.True(PlayerRelationship.AtWar(us, them));
	}

	[Fact]
	public void TheAIGivesInToAHumanOnlyWhenOutmatched() {
		Player human = them;
		human.isHuman = true;
		us.gold = 100;
		Warriors(us, 1);
		Assert.False(TributeAI.WouldPay(us, human));
		Warriors(human, 10);
		Assert.True(TributeAI.WouldPay(us, human));
		TributeAI.Pay(gameData, human, us, new TradeOffer { gold = 60 });
		Assert.Equal(40, us.gold);
	}

	[Fact]
	public void TheLastDemandIsSaved() {
		us.playerRelationships[them.id].lastTributeDemandTurn = 21;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(21, reloaded.GetPlayer(us.id).playerRelationships[them.id].lastTributeDemandTurn);
	}
}
