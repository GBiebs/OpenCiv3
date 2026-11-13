using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Play by email: each human plays their turn from the save the human before
// them sent on. The turn passes through a save file, the news a human
// misses travels in it, and nothing waits on a human who isn't there.
public class PlayByEmailTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;

	public PlayByEmailTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		// Engine message queues are static; parallelization is disabled repo-wide (XunitSettings.cs)
		EngineStorage.messagesToUI.Clear();
		EngineStorage.pendingMessages.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
		EngineStorage.pendingMessages.Clear();
	}

	private async Task<C7GameData.GameData> CreateGameByEmail() {
		new MsgSetAnimationsEnabled(false).send();
		EngineStorage.ProcessNextMessageToEngine();

		SaveGame save = SaveGameFixture.TwoHumanSave();
		save.PlayByEmail = true;
		await CreateGame.createGame(save, (_) => fixture.behaviors);
		C7GameData.GameData gameData = EngineStorage.gameData;

		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		return gameData;
	}

	// No computer player asks a human anything while the human is away.
	private static void NoQuestionForAHuman(MessageToUI msg) {
		Assert.False(msg is MsgShowTradeOffer or MsgShowTributeDemand or MsgShowTerritoryDemand or MsgShowDealProposal,
			$"{msg.GetType().Name} asked a human who isn't at the computer");
	}

	// Saves the game to a file and opens it, as the next player does.
	private async Task<Player> SendOn(C7GameData.GameData gameData, TempDirectory folder, string fileName) {
		string path = folder.File(fileName);
		SaveManager.Save(path);
		return await CreateGame.createGame(SaveGame.Load(path, s => s), (_) => fixture.behaviors);
	}

	[Fact]
	public async Task EachTurnIsSentOnInASave() {
		using TempDirectory folder = new("pbem");
		C7GameData.GameData gameData = await CreateGameByEmail();
		Player[] humans = gameData.players.Where(p => p.isHuman).ToArray();
		EngineWaits.WaitForStartTurnMessage(NoQuestionForAHuman);
		Assert.Equal(humans[0].id, EngineStorage.uiControllerID);

		// The first human ends their turn, and the second's begins: the game
		// is saved for them and opened on their computer.
		new MsgEndTurn().send();
		MsgStartTurn next = EngineWaits.WaitForStartTurnMessage(NoQuestionForAHuman);
		Assert.Equal(humans[1].id, next.player.id);
		string fileName = PlayByEmail.SaveFileName(gameData, next.player);
		Assert.Equal(fileName, PlayByEmail.FileNameTyped(fileName));
		Player resumed = await SendOn(gameData, folder, fileName);

		C7GameData.GameData opened = EngineStorage.gameData;
		Assert.NotSame(gameData, opened);
		Assert.True(opened.playByEmail);
		Assert.Equal(humans[1].id, resumed.id);
		Assert.Equal(humans[1].id, EngineStorage.uiControllerID);
		Assert.True(opened.GetPlayer(humans[0].id).hasPlayedThisTurn);
		Assert.Equal(0, opened.turn);

		// The computer players have their say, and the turn begins.
		await PlayByEmail.HearFromAIs(resumed);
		Assert.Equal(resumed, Assert.Single(EngineStorage.messagesToUI.OfType<MsgTurnReady>()).recipient);
		Assert.Equal(0, resumed.heardFromAIsOnTurn);
		EngineStorage.messagesToUI.Clear();

		// The second human ends their turn; the computer players play theirs
		// without asking either human anything, and it's the first human's
		// turn again, in the save sent back to them.
		new MsgEndTurn().send();
		next = EngineWaits.WaitForStartTurnMessage(NoQuestionForAHuman);
		Assert.Equal(humans[0].id, next.player.id);
		Assert.Equal(1, opened.turn);
		resumed = await SendOn(opened, folder, "back.json");
		Assert.Equal(humans[0].id, resumed.id);
		Assert.Equal(1, EngineStorage.gameData.turn);
		Assert.False(resumed.hasPlayedThisTurn);
	}

	// Only the human whose turn it is can answer, so a deal another human
	// proposes them is turned down at once rather than waiting.
	[Fact]
	public async Task OnlyTheHumanWhoseTurnItIsCanAnswer() {
		C7GameData.GameData gameData = await CreateGameByEmail();
		EngineWaits.WaitForStartTurnMessage(NoQuestionForAHuman);
		Player[] humans = gameData.players.Where(p => p.isHuman).ToArray();
		Player ai = gameData.players.First(p => !p.isHuman && !p.IsIndependent);

		Assert.True(EngineStorage.IsPlayerReachable(humans[0].id));
		Assert.False(EngineStorage.IsPlayerReachable(humans[1].id));
		Assert.True(EngineStorage.IsPlayerReachable(ai.id));

		humans[0].EnsureRelationshipExists(humans[1]);
		humans[1].EnsureRelationshipExists(humans[0]);
		humans[0].gold = 50;
		EngineStorage.messagesToUI.Clear();
		// Not even "agreed" at this screen, where the other isn't.
		new MsgProposeDeal(humans[1], new TradeOffer { gold = 10 }, new TradeOffer()) { opponentAgreed = true }.send();
		EngineStorage.ProcessNextMessageToEngine();
		MsgDealResult result = Assert.Single(EngineStorage.messagesToUI.OfType<MsgDealResult>());
		Assert.False(result.accepted);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowDealProposal>());
		Assert.Null(EngineStorage.pendingDeal);
		Assert.Equal(50, humans[0].gold);

		// In a hotseat game the other human is here to answer.
		gameData.playByEmail = false;
		Assert.True(EngineStorage.IsPlayerReachable(humans[1].id));
	}

	[Fact]
	public async Task TheNewsForAHumanAwayTravelsInTheSave() {
		using TempDirectory folder = new("pbem");
		C7GameData.GameData gameData = await CreateGameByEmail();
		EngineWaits.WaitForStartTurnMessage(NoQuestionForAHuman);
		Player[] humans = gameData.players.Where(p => p.isHuman).ToArray();
		City city = gameData.cities.FirstOrDefault();

		PlayByEmail.KeepNews(gameData, humans[1].id, [
			new MsgShowDomesticAdvisorPopup(humans[1], "Our people are celebrating!"),
			new MsgDealResult(humans[1], humans[0], false),
		]);
		Player resumed = await SendOn(gameData, folder, "news.json");
		Player away = EngineStorage.gameData.GetPlayer(humans[1].id);
		Assert.Equal(2, away.newsWhileAway.Count);

		List<MessageToUI> news = PlayByEmail.TakeNews(EngineStorage.gameData, away);
		MsgShowDomesticAdvisorPopup told = Assert.IsType<MsgShowDomesticAdvisorPopup>(news[0]);
		Assert.Equal("Our people are celebrating!", told.message);
		// The players it names are the opened game's.
		Assert.Same(away, told.recipient);
		MsgDealResult deal = Assert.IsType<MsgDealResult>(news[1]);
		Assert.Same(EngineStorage.gameData.GetPlayer(humans[0].id), deal.opponent);
		// Told once.
		Assert.Empty(away.newsWhileAway);
		Assert.Empty(PlayByEmail.TakeNews(EngineStorage.gameData, away));
	}

	// The password travels in the save, slow to guess at and salted at
	// random; the save never shows it.
	[Fact]
	public async Task APasswordTravelsHashed() {
		using TempDirectory folder = new("pbem");
		C7GameData.GameData gameData = await CreateGameByEmail();
		EngineWaits.WaitForStartTurnMessage(NoQuestionForAHuman);
		Player first = gameData.players.First(p => p.isHuman);
		Player second = gameData.players.Where(p => p.isHuman).Last();
		Assert.False(first.passwordChosen);

		new MsgSetPassword("hammurabi").send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(first.passwordChosen);
		Assert.StartsWith("pbkdf2:100000:", first.passwordHash);
		Assert.True(first.CheckPassword("hammurabi"));
		Assert.False(first.CheckPassword("nebuchadnezzar"));
		Assert.False(first.CheckPassword(""));

		// The other player can't set it for them, nor theirs out of turn.
		new MsgSetPassword("sargon") { playerID = second.id }.send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.False(second.passwordChosen);

		// The same password doesn't make the same hash.
		second.SetPassword("hammurabi");
		Assert.NotEqual(first.passwordHash, second.passwordHash);

		string path = folder.File("password.json");
		SaveManager.Save(path);
		Assert.DoesNotContain("hammurabi", System.IO.File.ReadAllText(path));
		await CreateGame.createGame(SaveGame.Load(path, s => s), (_) => fixture.behaviors);
		Player reloaded = EngineStorage.gameData.GetPlayer(first.id);
		Assert.True(reloaded.passwordChosen);
		Assert.True(reloaded.CheckPassword("hammurabi"));
		Assert.False(reloaded.CheckPassword("sargon"));

		// None chosen: no password asked for.
		reloaded.SetPassword("");
		Assert.True(reloaded.passwordChosen);
		Assert.False(reloaded.HasPassword());
		Assert.True(reloaded.CheckPassword("anything"));
	}

	// The player names the save: a name any of their computers can keep.
	[Theory]
	[InlineData("Game with Bob - Turn 12", "Game with Bob - Turn 12.json")]
	[InlineData("  spaces around  ", "spaces around.json")]
	[InlineData("named.json", "named.json")]
	[InlineData("named.JSON", "named.json")]
	[InlineData("1.5 AD", "1.5 AD.json")]
	[InlineData("", null)]
	[InlineData("   ", null)]
	[InlineData(".json", null)]
	[InlineData(null, null)]
	[InlineData("Rome/Maya", null)]
	[InlineData("Rome\\Maya", null)]
	[InlineData("Turn 5: Maya", null)]
	[InlineData("what?", null)]
	[InlineData("a \"quote\"", null)]
	[InlineData("tab\there", null)]
	[InlineData("ends with a dot.", null)]
	public void ThePlayerNamesTheSave(string typed, string fileName) {
		Assert.Equal(fileName, PlayByEmail.FileNameTyped(typed));
	}
}

// Questions always get an answer: a computer player asks a human playing by
// email only at the start of the human's turn, when they're there.
public class PlayByEmailQuestionsTest : MapBase, IDisposable {
	private readonly C7GameData.GameData gameData = new() { playByEmail = true };
	private readonly Player ai = MakeCiv("player-1");
	private readonly Player human = MakeCiv("player-2");
	private readonly Player other = MakeCiv("player-3");

	public PlayByEmailQuestionsTest() {
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.pendingMessages.Clear();
		EngineStorage.messagesToUI.Clear();
		EngineStorage.ResetNetworking();
		gameData.players.Add(ai);
		gameData.players.Add(human);
		gameData.players.Add(other);
		ai.EnsureRelationshipExists(human);
		human.EnsureRelationshipExists(ai);
		human.isHuman = true;
		other.isHuman = true;
		gameData.turn = 50;

		InitilizeStartTile(MakePlainsTile(), new TileLocation(50, 50));
		ai.cities.Add(new City(startTile, ai, "Capital", ID.None("city")));
		AddWarriors(ai, 8);
		AddWarriors(human, 1);
		human.gold = 200;
		// The other human played last, and is still at the computer.
		other.hasPlayedThisTurn = true;
		EngineStorage.activePlayerID = other.id;
		EngineStorage.uiControllerID = other.id;
	}

	public void Dispose() {
		EngineStorage.ResetNetworking();
		EngineStorage.activePlayerID = null;
		EngineStorage.uiControllerID = null;
		EngineStorage.messagesToUI.Clear();
		EngineStorage.pendingMessages.Clear();
	}

	private static Player MakeCiv(string id) {
		return new Player() { id = ID.FromString(id), civilization = new Civilization(), government = new Government() };
	}

	private void AddWarriors(Player owner, int count) {
		for (int i = 0; i < count; ++i) {
			MapUnit unit = new(ID.None("unit")) {
				owner = owner,
				unitType = new UnitPrototype() { attack = 1, defense = 1 },
				experienceLevel = new ExperienceLevel("regular", "Regular", 3, 0, 0),
				location = startTile,
			};
			unit.unitType.categories.Add("Land");
			startTile.unitsOnTile.Add(unit);
			owner.units.Add(unit);
		}
	}

	// The human's turn begins, at their computer.
	private void Arrive() {
		EngineStorage.activePlayerID = human.id;
		EngineStorage.uiControllerID = human.id;
	}

	[Fact]
	public void OnItsOwnTurnTheAIDoesntAskAHumanAway() {
		Assert.True(TributeDemands.WouldDemandFrom(ai, human, gameData));
		Assert.False(PlayByEmail.CanAnswer(gameData, human.id));
		// However the dice fall, there's no demand, and nothing waits.
		for (int i = 0; i < 200; ++i) {
			Task demanding = TributeDemands.MakeDemands(ai, gameData);
			Assert.True(demanding.IsCompleted);
			Task offering = PeaceAI.ProposePeace(ai, gameData);
			Assert.True(offering.IsCompleted);
		}
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowTributeDemand>());
		Assert.Equal(-1, ai.playerRelationships[human.id].lastTributeDemandTurn);
		Assert.Equal(200, human.gold);
	}

	// At the start of their turn the AI makes its demand; once it's
	// answered, the turn begins.
	[Fact]
	public async Task TheHumanHearsItAtTheStartOfTheirTurn() {
		Arrive();
		Assert.True(PlayByEmail.CanAnswer(gameData, human.id));
		Assert.False(PlayByEmail.CanAnswer(gameData, other.id));

		// The demand is a roll of the dice: arrive until it's made.
		MsgShowTributeDemand demand = null;
		Task hearing = null;
		for (int i = 0; i < 500 && demand == null; ++i) {
			EngineStorage.messagesToUI.Clear();
			human.heardFromAIsOnTurn = -1;
			hearing = PlayByEmail.HearFromAIs(human);
			demand = EngineStorage.messagesToUI.OfType<MsgShowTributeDemand>().SingleOrDefault();
			if (demand == null) {
				Assert.True(hearing.IsCompleted);
				Assert.Single(EngineStorage.messagesToUI.OfType<MsgTurnReady>());
			}
		}
		Assert.NotNull(demand);
		Assert.Same(human, demand.humanPlayer);
		// The turn waits for the answer.
		Assert.False(hearing.IsCompleted);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgTurnReady>());

		foreach (MessageToEngine answer in new MessageToEngine[] { new MsgRespondToTributeDemand(true), new MsgDiplomacyCompleted() }) {
			answer.send();
			EngineStorage.ProcessNextMessageToEngine();
		}
		await hearing.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Same(human, Assert.Single(EngineStorage.messagesToUI.OfType<MsgTurnReady>()).recipient);
		Assert.Equal(gameData.turn, human.heardFromAIsOnTurn);
		Assert.True(human.gold < 200);
	}

	// Opening the save again doesn't give the computer players another say.
	[Fact]
	public void OnlyOnceATurn() {
		Arrive();
		human.heardFromAIsOnTurn = gameData.turn;
		for (int i = 0; i < 100; ++i) {
			Assert.True(PlayByEmail.HearFromAIs(human).IsCompleted);
		}
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowTributeDemand>());
		Assert.Equal(100, EngineStorage.messagesToUI.OfType<MsgTurnReady>().Count());
	}

	// Not playing by email, the AI asks at once.
	[Fact]
	public void OtherwiseTheHumanCanBeAsked() {
		gameData.playByEmail = false;
		Assert.True(EngineStorage.IsPlayerReachable(human.id));
		Assert.True(PlayByEmail.HearFromAIs(human).IsCompleted);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgTurnReady>());
		Assert.Equal(-1, human.heardFromAIsOnTurn);
	}
}
