using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData.Victory;

// Civ3's diplomatic victory: the United Nations' owner may call an election
// every 11 turns; the candidates are the owner and any civ with a quarter
// of the world's land or people (else the most populous civ); a majority of
// all the votes wins the game.
public class DiplomaticVictoryTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player human;
	private readonly Player founder;
	private readonly Player big;
	private readonly Player small;
	private readonly Player fourth;
	private readonly Dictionary<Player, City> capitals = new();
	private readonly ID controller = EngineStorage.uiControllerID;

	public DiplomaticVictoryTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		SaveGame save = fixture.saveGame.Clone();
		save.VictoryConditions = new VictoryConditions { AllowDiplomaticVictory = true };
		gameData = save.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		human = gameData.players.First(p => p.isHuman);
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).ToArray();
		(founder, big, small, fourth) = (ais[0], ais[1], ais[2], ais[3]);
		// Just the five of us; the big civ has the most people after us.
		foreach (Player other in ais.Skip(4)) {
			other.defeated = true;
		}
		foreach (Player p in new[] { human, founder, big, small, fourth }) {
			capitals[p] = CityInteractions.BuildCity(p.units[0].location, p, p.GetNextCityName());
			Grow(capitals[p], p == big ? 5 : 4);
		}
		foreach (Player a in new[] { human, founder, big, small, fourth }) {
			foreach (Player b in new[] { human, founder, big, small, fourth }.Where(b => b != a)) {
				a.EnsureRelationshipExists(b);
			}
		}
		capitals[founder].AddBuilding(gameData.Buildings.Find(b => b.HasWonderFlag(SaveBuilding.WonderFlag.AllowDiplomaticVictory)), gameData);
		gameData.turn = 20;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
		EngineStorage.uiControllerID = controller;
	}

	private void Grow(City city, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			CityResident resident = new() { city = city, nationality = city.owner.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
	}

	private void Like(Player voter, Player candidate, int score) {
		voter.playerRelationships[candidate.id].attitudePenalty = score - Attitude.Score(gameData, voter, candidate) + voter.playerRelationships[candidate.id].attitudePenalty;
	}

	[Fact]
	public void TheUnitedNationsIsWhereTheWonderIs() {
		Assert.Equal(capitals[founder], UnitedNations.Seat(gameData));
		Assert.Equal("The United Nations", UnitedNations.Seat(gameData).constructed_buildings.Select(b => b.building)
			.Single(b => b.HasWonderFlag(SaveBuilding.WonderFlag.AllowDiplomaticVictory)).name);
	}

	[Fact]
	public void TheCandidatesAreTheOwnerAndCivsWithAQuarterOfTheWorld() {
		// Nobody else has a quarter (5 of 21 people): the most populous civ runs.
		Assert.Equal([founder, big], UnitedNations.Candidates(gameData));

		// A quarter of the world's people: 9 of 34.
		Grow(capitals[small], 9);
		Grow(capitals[big], 13);
		Assert.Equal([founder, big, small], UnitedNations.Candidates(gameData));

		// No more than three: three civs have a quarter (12, 13 and 12 of 45).
		Grow(capitals[human], 12);
		Grow(capitals[small], 12);
		Assert.Equal(3, UnitedNations.Candidates(gameData).Count);
		Assert.Equal([founder, big], UnitedNations.Candidates(gameData).Take(2));

		// The rules: up to four; a third of the world (nobody: the biggest runs).
		gameData.rules.victory.maxCandidates = 4;
		Assert.Equal(4, UnitedNations.Candidates(gameData).Count);
		gameData.rules.victory.candidatePercent = 33;
		Assert.Equal([founder, big], UnitedNations.Candidates(gameData));
	}

	[Fact]
	public void TheAIVotesForItselfAnAllyOrTheCandidateItLikesBest() {
		List<Player> candidates = [founder, big];
		Assert.Equal(founder, UnitedNations.Vote(gameData, founder, candidates));
		Assert.Equal(big, UnitedNations.Vote(gameData, big, candidates));

		Like(small, founder, -5);
		Like(small, big, -1);
		Assert.Equal(founder, UnitedNations.Vote(gameData, small, candidates));
		Like(small, big, -8);
		Assert.Equal(big, UnitedNations.Vote(gameData, small, candidates));

		// Never one it's at war with.
		small.DeclareWarOn(big, gameData.turn);
		Assert.Equal(founder, UnitedNations.Vote(gameData, small, candidates));
	}

	[Fact]
	public void TheAIAbstainsIfItLikesNobody() {
		List<Player> candidates = [founder, big];
		Like(small, founder, 3);
		Like(small, big, 5);
		Assert.Equal(Attitude.Level.Annoyed, Attitude.Of(gameData, small, founder));
		Assert.Null(UnitedNations.Vote(gameData, small, candidates));

		// It hasn't met them.
		Like(small, founder, -5);
		small.playerRelationships.Remove(founder.id);
		small.playerRelationships.Remove(big.id);
		Assert.Null(UnitedNations.Vote(gameData, small, candidates));
	}

	[Fact]
	public void AWinnerNeedsAMajorityOfAllTheVotes() {
		Assert.Null(UnitedNations.Winner(new() { { human, null }, { founder, founder }, { big, big }, { small, founder } }));
		Assert.Equal(founder, UnitedNations.Winner(new() { { human, founder }, { founder, founder }, { big, big }, { small, founder } }));
		Assert.Null(UnitedNations.Winner(new() { { human, null }, { founder, founder }, { big, null }, { small, null } }));
	}

	[Fact]
	public void TheOwnerMayCallOneEvery11Turns() {
		Assert.True(UnitedNations.ElectionDue(gameData, founder));
		Assert.False(UnitedNations.ElectionDue(gameData, big));

		// Asked, it says no (it wouldn't win).
		Like(small, founder, 8);
		Like(fourth, big, -5);
		Assert.True(UnitedNations.MaybeCallElection(gameData, founder).IsCompleted);
		Assert.Null(gameData.unitedNations.secretaryGeneral);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgElectionResults>());
		gameData.turn += 10;
		Assert.False(UnitedNations.ElectionDue(gameData, founder));
		gameData.turn += 1;
		Assert.True(UnitedNations.ElectionDue(gameData, founder));

		// A new owner may call one at once.
		UnitedNations.MaybeCallElection(gameData, founder);
		Assert.False(UnitedNations.ElectionDue(gameData, founder));
		CityInteractions.CaptureCity(capitals[founder], gameData.SpawnUnit(big, gameData.unitPrototypes.Find(u => u.name == "Warrior"), capitals[small].location));
		Assert.True(UnitedNations.ElectionDue(gameData, big));

		// Not once the game is over.
		gameData.gameOver = true;
		Assert.False(UnitedNations.ElectionDue(gameData, big));
		gameData.gameOver = false;

		// Only in a game that allows a diplomatic victory.
		gameData.victoryConditions.AllowDiplomaticVictory = false;
		Assert.False(UnitedNations.ElectionDue(gameData, big));
	}

	[Fact]
	public void TheAICallsOneItWouldWinAndWinsTheGame() {
		Like(small, founder, -5);
		Like(fourth, founder, -5);
		Assert.True(UnitedNations.WouldCall(gameData, founder));

		Task call = UnitedNations.MaybeCallElection(gameData, founder);
		// The human votes.
		Assert.False(call.IsCompleted);
		MsgVoteRequest request = Assert.Single(EngineStorage.messagesToUI.OfType<MsgVoteRequest>());
		Assert.Equal(human.id, request.voterId);
		Assert.Equal([founder, big], request.candidates);
		new MsgVote(null).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(call.Wait(TimeSpan.FromSeconds(5)));

		MsgElectionResults results = Assert.Single(EngineStorage.messagesToUI.OfType<MsgElectionResults>());
		Assert.Equal(founder, results.winner);
		Assert.Contains((human, (Player)null), results.votes);
		Assert.Contains((small, founder), results.votes);
		Assert.Equal(founder.id, gameData.unitedNations.secretaryGeneral);
		Assert.True(gameData.gameOver);
		Assert.Equal(founder, gameData.winner);
		Assert.IsType<DiplomaticVictory>(Assert.Single(EngineStorage.messagesToUI.OfType<MsgVictory>()).victory);
	}

	[Fact]
	public void AHumanOwnerIsAskedAndVotes() {
		CityInteractions.CaptureCity(capitals[founder], gameData.SpawnUnit(human, gameData.unitPrototypes.Find(u => u.name == "Warrior"), capitals[human].location));
		Like(small, human, -5);
		Like(fourth, human, -5);

		Task call = UnitedNations.MaybeCallElection(gameData, human);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgElectionOffer>(), m => m.playerId == human.id);
		new MsgCallElection(true).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(SpinUntil(() => EngineStorage.messagesToUI.OfType<MsgVoteRequest>().Any()));
		new MsgVote(human.id).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(call.Wait(TimeSpan.FromSeconds(5)));

		Assert.Equal(human, Assert.Single(EngineStorage.messagesToUI.OfType<MsgElectionResults>()).winner);
		Assert.Equal(human, gameData.winner);
	}

	[Fact]
	public void AHumanOwnerMayDecline() {
		CityInteractions.CaptureCity(capitals[founder], gameData.SpawnUnit(human, gameData.unitPrototypes.Find(u => u.name == "Warrior"), capitals[human].location));
		Task call = UnitedNations.MaybeCallElection(gameData, human);
		new MsgCallElection(false).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(call.Wait(TimeSpan.FromSeconds(5)));
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgVoteRequest>());
		Assert.False(gameData.gameOver);
	}

	// Play by email: a human away votes at the start of their turn, and the
	// result waits for them; no other election is called meanwhile.
	[Fact]
	public void PlayingByEmailTheResultWaitsForAHumanAway() {
		gameData.playByEmail = true;
		Task<Player> election = UnitedNations.HoldElection(gameData);
		Assert.True(election.IsCompleted);
		Assert.Null(election.Result);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgVoteRequest>());
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgElectionResults>());
		Assert.Equal([human.id], gameData.unitedNations.awaitedElection.awaited);
		Assert.False(UnitedNations.ElectionDue(gameData, founder));

		Task voting = UnitedNations.CastAwaitedVote(gameData, human);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgVoteRequest>(), m => m.voterId == human.id);
		new MsgVote(founder.id).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(voting.Wait(TimeSpan.FromSeconds(5)));
		MsgElectionResults results = Assert.Single(EngineStorage.messagesToUI.OfType<MsgElectionResults>());
		Assert.Equal(5, results.votes.Count);
		Assert.Contains((human, founder), results.votes);
		Assert.Null(gameData.unitedNations.awaitedElection);
	}

	// The one present when it's called votes at once; a human defeated
	// before voting doesn't hold it up.
	[Fact]
	public void PlayingByEmailOnlyTheHumansAwayAreAwaited() {
		gameData.playByEmail = true;
		small.isHuman = true;
		Task<Player> election = UnitedNations.HoldElection(gameData, present: small);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgVoteRequest>(), m => m.voterId == small.id);
		new MsgVote(null).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(election.Wait(TimeSpan.FromSeconds(5)));
		Assert.Equal([human.id], gameData.unitedNations.awaitedElection.awaited);

		human.defeated = true;
		EngineStorage.messagesToUI.Clear();
		Assert.True(UnitedNations.CastAwaitedVote(gameData, small).IsCompleted);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgVoteRequest>());
		Assert.Equal(4, Assert.Single(EngineStorage.messagesToUI.OfType<MsgElectionResults>()).votes.Count);
	}

	[Fact]
	public void AnInconclusiveElectionChangesNothing() {
		Like(small, founder, 5);
		Like(small, big, 5);
		Like(fourth, big, -5);
		Task<Player> election = UnitedNations.HoldElection(gameData);
		new MsgVote(null).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(election.Wait(TimeSpan.FromSeconds(5)));
		Assert.Null(election.Result);
		Assert.Null(Assert.Single(EngineStorage.messagesToUI.OfType<MsgElectionResults>()).winner);
		Assert.Null(gameData.unitedNations.secretaryGeneral);
		Assert.False(gameData.gameOver);
	}

	[Fact]
	public async Task TheOwnerCallsOneBeforeItsTurn() {
		Like(small, founder, -5);
		Like(fourth, founder, -5);
		EngineStorage.uiControllerID = human.id;
		human.hasPlayedThisTurn = true;
		await PlayUntilOurTurn(TurnHandling.AdvanceTurn());

		Assert.True(gameData.gameOver);
		Assert.Equal(founder, gameData.winner);
		Assert.Equal(founder, EngineStorage.messagesToUI.OfType<MsgElectionResults>().Single().winner);
	}

	// Plays the turn, answering the AIs' questions to the human (no to
	// everything; abstaining in elections). Keeps the messages to the UI.
	private static async Task PlayUntilOurTurn(Task turn) {
		List<MessageToUI> seen = new();
		DateTime giveUp = DateTime.Now.AddSeconds(60);
		while (!turn.IsCompleted && DateTime.Now < giveUp) {
			EngineStorage.ProcessNextMessageToEngine();
			while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI message)) {
				seen.Add(message);
				switch (message) {
					case MsgVoteRequest:
						new MsgVote(null).send();
						break;
					case MsgElectionOffer:
						new MsgCallElection(false).send();
						break;
					case MsgShowTradeOffer:
						new MsgDiplomacyCompleted().send();
						break;
					case MsgTributeDemanded demand:
						new MsgTributeAnswer(demand.demander.id, demand.victim.id, demand.demand, pay: false).send();
						break;
				}
			}
			await Task.Delay(1);
		}
		Assert.True(turn.IsCompleted, "The turn never came back to us: " + string.Join(", ", seen.Select(m => m.GetType().Name)));
		await turn;
		foreach (MessageToUI message in seen) {
			EngineStorage.messagesToUI.Enqueue(message);
		}
	}

	[Fact]
	public void TheElectionsAreSaved() {
		gameData.unitedNations.lastOfferedTo = founder.id;
		gameData.unitedNations.lastOfferTurn = 17;
		gameData.unitedNations.secretaryGeneral = big.id;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(founder.id, reloaded.unitedNations.lastOfferedTo);
		Assert.Equal(17, reloaded.unitedNations.lastOfferTurn);
		Assert.Equal(big.id, reloaded.unitedNations.secretaryGeneral);
		Assert.Single(reloaded.victories.OfType<DiplomaticVictory>());
	}

	[Fact]
	public void TheAIGoesForTheUnitedNations() {
		Building un = gameData.Buildings.Find(b => b.HasWonderFlag(SaveBuilding.WonderFlag.AllowDiplomaticVictory));
		Assert.Equal(ChooseProducible.VictoryBuildingBoost, ChooseProducible.ScoreBuilding(capitals[big], big, un)
			- WithoutDiplomaticVictory(() => ChooseProducible.ScoreBuilding(capitals[big], big, un)));
	}

	private float WithoutDiplomaticVictory(Func<float> score) {
		gameData.victoryConditions.AllowDiplomaticVictory = false;
		try {
			return score();
		} finally {
			gameData.victoryConditions.AllowDiplomaticVictory = true;
		}
	}

	private static bool SpinUntil(Func<bool> condition) {
		return System.Threading.SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5));
	}
}
