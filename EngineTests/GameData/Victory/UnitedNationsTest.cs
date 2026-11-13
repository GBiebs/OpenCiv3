using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using Xunit;

namespace EngineTests.GameData.Victory;

public class UnitedNationsTest : System.IDisposable {
	private readonly C7GameData.GameData gameData = new() {
		gameDifficulty = new Difficulty(),
		rules = new Rules(),
		history = new Dictionary<string, List<HistTurnRecord>>(),
		timeOptions = new TimeOptions(),
		victoryConditions = new VictoryConditions { AllowDiplomaticVictory = true },
		turn = 50,
	};
	private int nextId = 1;

	public UnitedNationsTest() {
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private Player MakePlayer(string civName, int population, bool human = false) {
		Player player = new() {
			id = ID.FromString($"player-{nextId++}"),
			isHuman = human,
			civilization = new Civilization(civName) { noun = civName },
			government = new Government(),
			rules = gameData.rules,
		};
		gameData.players.Add(player);
		gameData.history[player.id.ToString()] = new List<HistTurnRecord> { new HistTurnRecord() };
		if (population > 0) {
			Tile tile = new(ID.None("tile")) { continent = 1 };
			City city = new(tile, player, $"{civName}ville", ID.FromString($"city-{nextId++}"));
			tile.cityAtTile = city;
			player.cities.Add(city);
			gameData.cities.Add(city);
			for (int i = 0; i < population; i++) {
				city.residents.Add(new CityResident { city = city });
			}
		}
		return player;
	}

	private void BuildUnitedNations(Player owner) {
		SaveBuilding sb = new() { name = "The United Nations", greatWonderProperties = new() };
		sb.flags.Add(SaveBuilding.Flag.AllowDiplomaticVictory);
		owner.cities[0].AddBuilding(new Building(sb, gameData));
	}

	private static void Meet(params Player[] players) {
		foreach (Player a in players) {
			foreach (Player b in players) {
				if (a != b) {
					a.EnsureRelationshipExists(b);
				}
			}
		}
	}

	[Fact]
	public void NoUnitedNationsMeansNoCandidates() {
		MakePlayer("Romans", 3);
		MakePlayer("Greeks", 2);
		Assert.Null(UnitedNations.Candidates(gameData));
		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(-1, gameData.unitedNations.votingTurn);
	}

	// https://civfanatics.com/civ3/faq/: "the civilization that builds the
	// UN is always a candidate, and then if civilization(s) have 25% of the
	// land OR population, they will also be eligible."
	[Fact]
	public void CandidatesAreTheOwnerAndCivsWithAQuarterOfThePopulation() {
		Player rome = MakePlayer("Romans", 3);
		MakePlayer("Greeks", 2);
		Player egypt = MakePlayer("Egyptians", 7);
		BuildUnitedNations(rome);

		// Egypt has 7 of 12 citizens; Greece 2.
		Assert.Equal([rome, egypt], UnitedNations.Candidates(gameData));
	}

	// A candidate needs "more than 25% of land or population" (Perez's
	// decompile, Round 2; owner's ruling 2026-11-12): exactly a quarter isn't
	// enough.
	[Fact]
	public void ExactlyAQuarterIsNotEnoughToStand() {
		Player rome = MakePlayer("Romans", 2);
		MakePlayer("Egyptians", 2);
		Player greece = MakePlayer("Greeks", 4);
		BuildUnitedNations(rome);

		// Egypt has exactly 2 of 8 citizens.
		Assert.Equal([rome, greece], UnitedNations.Candidates(gameData));
	}

	// "At maximum, there can be three candidates".
	[Fact]
	public void ThereAreAtMostThreeCandidates() {
		Player rome = MakePlayer("Romans", 1);
		Player egypt = MakePlayer("Egyptians", 3);
		Player greece = MakePlayer("Greeks", 3);
		MakePlayer("Persians", 3);
		BuildUnitedNations(rome);

		// Each rival has 30% of the citizens; ties go to the civ listed first.
		Assert.Equal([rome, egypt, greece], UnitedNations.Candidates(gameData));

		// The larger share stands first.
		greece.cities[0].residents.Add(new CityResident { city = greece.cities[0] });
		Assert.Equal([rome, greece, egypt], UnitedNations.Candidates(gameData));
	}

	// Conquests Civilopedia (DESC_GCON_Victory): "If only one civilization
	// is eligible, then the second candidate is the civilization with the
	// largest population." Ties go to the higher score.
	[Fact]
	public void WithoutAQuarterOfTheWorldTheMostPopulousCivStands() {
		Player rome = MakePlayer("Romans", 5);
		MakePlayer("Egyptians", 2);
		Player greece = MakePlayer("Greeks", 2);
		Player persia = MakePlayer("Persians", 2);
		MakePlayer("Aztecs", 2);
		BuildUnitedNations(rome);
		gameData.history[greece.id.ToString()][0] = new HistTurnRecord { Score = 100 };
		// The owner is a candidate already, however many its citizens.
		gameData.history[rome.id.ToString()][0] = new HistTurnRecord { Score = 500 };

		// Equal populations: the higher score stands.
		Assert.Equal([rome, greece], UnitedNations.Candidates(gameData));

		// Persia, with 3 of 14 citizens (under a quarter), outnumbers Greece,
		// whatever Greece's score.
		persia.cities[0].residents.Add(new CityResident { city = persia.cities[0] });
		Assert.Equal([rome, persia], UnitedNations.Candidates(gameData));
	}

	private static void Deal(Player a, Player b, DealSubType type) {
		PlayerRelationship.RegisterMultiTurnDeal(a, b,
			new MultiTurnDeal(DealType.DiplomaticAgreement, type, DealDetails.Exchange, 0, null, 0, 0, null));
	}

	// A Right of Passage makes an AI polite (-5 attitude points).
	private static void Befriend(Player a, Player b) => Deal(a, b, DealSubType.RightOfPassage);

	private int Attitude(Player voter, Player candidate) => UnitedNations.Attitude(gameData, voter, candidate).Value;

	// https://civfanatics.com/civ3/strategy/game-mechanics/ai-attitude-study/:
	// "The AI must be at least polite or better to vote for you in the UN".
	[Fact]
	public void AIAbstainsWhenMerelyAtPeace() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 3);
		Player greece = MakePlayer("Greeks", 2);
		Meet(rome, egypt, greece);

		Assert.Equal(0, Attitude(greece, rome));
		Assert.Null(UnitedNations.AIVote(gameData, greece, rome, egypt));

		Befriend(greece, rome);
		Assert.Equal(-5, Attitude(greece, rome));
		Assert.Equal(rome, UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	// https://forums.civfanatics.com/threads/diplomatic-victory.53460/ (Catt):
	// "An AI will not vote for a civ with whom it is at war".
	[Fact]
	public void AINeverVotesForAnEnemy() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 3);
		Player greece = MakePlayer("Greeks", 2);
		Meet(rome, egypt, greece);
		Befriend(greece, egypt);
		Assert.Equal(egypt, UnitedNations.AIVote(gameData, greece, rome, egypt));

		PlayerRelationship.DeclareWar(greece, egypt, false, 0);
		// However well it might think of them otherwise.
		egypt.playerRelationships[greece.id].hasEmbassy = true;
		Assert.True(Attitude(greece, egypt) <= UnitedNations.PoliteAttitude);
		Assert.Null(UnitedNations.AIVote(gameData, greece, rome, egypt));

		Befriend(greece, rome);
		Assert.Equal(rome, UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	[Fact]
	public void AIAbstainsWhenItHasMetNeitherCandidate() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Player greece = MakePlayer("Greeks", 2);

		Assert.Null(UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	[Fact]
	public void AIAbstainsWhenAtWarWithEveryCandidate() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Player greece = MakePlayer("Greeks", 2);
		Meet(rome, egypt, greece);
		PlayerRelationship.DeclareWar(greece, egypt, false, 0);
		PlayerRelationship.DeclareWar(greece, rome, false, 0);

		Assert.Null(UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	// https://forums.civfanatics.com/threads/when-someone-else-builds-the-u-n.85895/
	// (Evertonian): "An AI always votes for itself if able".
	[Fact]
	public void ACandidateVotesForItself() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Meet(rome, egypt);
		Deal(rome, egypt, DealSubType.MutualProtectionPact);

		Assert.Equal(egypt, UnitedNations.AIVote(gameData, egypt, rome, egypt));
	}

	// Evertonian: an AI polite to more than one candidate votes for the one
	// it favours most.
	[Fact]
	public void AIPrefersTheFriendlierCandidate() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 3);
		Player greece = MakePlayer("Greeks", 2);
		Meet(rome, egypt, greece);
		Befriend(greece, rome);
		Deal(greece, egypt, DealSubType.MutualProtectionPact);

		Assert.Equal(-5, Attitude(greece, rome));
		Assert.Equal(-10, Attitude(greece, egypt));
		Assert.Equal(egypt, UnitedNations.AIVote(gameData, greece, rome, egypt));

		// Caught spies (+1 each) and past wars (+4 each) count against a
		// candidate.
		greece.playerRelationships[egypt.id].espionageIncidents = 2;
		greece.playerRelationships[egypt.id].warDeclarationCount = 1;
		Assert.Equal(-4, Attitude(greece, egypt));
		Assert.Equal(rome, UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	// The study's "War with common enemy: -3".
	[Fact]
	public void AFellowEnemyOfAThirdCivIsLiked() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 3);
		Player greece = MakePlayer("Greeks", 2);
		Player persia = MakePlayer("Persians", 2);
		Meet(rome, egypt, greece, persia);
		PlayerRelationship.DeclareWar(greece, persia, false, 0);
		PlayerRelationship.DeclareWar(egypt, persia, false, 0);

		// Less the +1 to its reputation for declaring war on Persia.
		Assert.Equal(-2, Attitude(greece, egypt));
		Assert.Equal(egypt, UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	// Reputation: the study's "Break peace treaty: +4 (other civs: +1)".
	[Fact]
	public void ACandidateThatDeclaredWarOnOthersIsLikedLess() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 2);
		Player greece = MakePlayer("Greeks", 2);
		Player persia = MakePlayer("Persians", 2);
		Meet(rome, egypt, greece, persia);
		Befriend(greece, rome);
		Befriend(greece, egypt);

		// Equally liked, so the smaller civ gets the vote.
		Assert.Equal(egypt, UnitedNations.AIVote(gameData, greece, rome, egypt));

		persia.playerRelationships[egypt.id].warDeclarationCount = 2;
		Assert.Equal(-3, Attitude(greece, egypt));
		Assert.Equal(rome, UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	// HEURISTIC (the project owner: size counts): an AI is wary of a much
	// larger candidate.
	[Fact]
	public void AIIsWaryOfAMuchLargerCandidate() {
		Player rome = MakePlayer("Romans", 2);
		Player egypt = MakePlayer("Egyptians", 8);
		Player greece = MakePlayer("Greeks", 2);
		Meet(rome, egypt, greece);
		Befriend(greece, rome);
		Befriend(greece, egypt);
		Deal(greece, egypt, DealSubType.GoldPerTurn);

		// Egypt has four times Greece's citizens: -5 - 1 + 2.
		Assert.Equal(-4, Attitude(greece, egypt));
		Assert.Equal(rome, UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	// The study: "If you have a power lead, most of the good effects
	// (negative numbers) are halved."
	[Fact]
	public void AStrongerCandidateCountsForHalf() {
		Player rome = MakePlayer("Romans", 2);
		Player egypt = MakePlayer("Egyptians", 2);
		Player greece = MakePlayer("Greeks", 2);
		Meet(rome, egypt, greece);
		Befriend(greece, rome);
		Deal(greece, rome, DealSubType.GoldPerTurn);
		Deal(greece, egypt, DealSubType.MutualProtectionPact);
		Assert.Equal(egypt, UnitedNations.AIVote(gameData, greece, rome, egypt));

		egypt.units.Add(new MapUnit(ID.FromString("unit-1")) {
			owner = egypt,
			unitType = new UnitPrototype { attack = 10, defense = 10 },
			experienceLevel = new ExperienceLevel("regular", "Regular", 3, 0, 0),
		});
		Assert.Equal(-5, Attitude(greece, egypt));
		Assert.Equal(rome, UnitedNations.AIVote(gameData, greece, rome, egypt));
	}

	[Fact]
	public void AIChoosesAmongThreeCandidates() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 3);
		Player persia = MakePlayer("Persians", 3);
		Player greece = MakePlayer("Greeks", 2);
		Meet(rome, egypt, persia, greece);
		Befriend(greece, egypt);
		Befriend(greece, persia);
		Deal(greece, rome, DealSubType.MutualProtectionPact);
		PlayerRelationship.DeclareWar(greece, rome, false, 0);

		// At war with Rome; Egypt and Persia equally liked, Egypt listed first.
		Assert.Equal(egypt, UnitedNations.AIVote(gameData, greece, rome, egypt, persia));

		Deal(greece, persia, DealSubType.GoldPerTurn);
		Assert.Equal(persia, UnitedNations.AIVote(gameData, greece, rome, egypt, persia));
	}

	// "Each civilization gets 1 vote" and "If a civilization gains a
	// majority of the votes in the election, they become Secretary General".
	[Fact]
	public void EachCivHasOneVoteAndAMajorityWins() {
		Player rome = MakePlayer("Romans", 1);
		Player egypt = MakePlayer("Egyptians", 20);
		Player greece = MakePlayer("Greeks", 1);
		Player persia = MakePlayer("Persians", 1);
		BuildUnitedNations(rome);
		Meet(rome, egypt, greece, persia);
		PlayerRelationship.DeclareWar(greece, egypt, false, 0);
		PlayerRelationship.DeclareWar(persia, egypt, false, 0);
		Befriend(greece, rome);
		Befriend(persia, rome);

		// Egypt's 20 citizens count for no more than Rome's one.
		UnitedNations.ElectionResult result = UnitedNations.HoldElection(gameData);
		Assert.Equal([rome, egypt], result.candidates);
		Assert.Equal([3, 1], result.votes);
		Assert.Equal(0, result.abstentions);
		Assert.Equal(rome, result.winner);
	}

	[Fact]
	public void AThirdCandidateCanDenyAMajority() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 4);
		Player greece = MakePlayer("Greeks", 4);
		Player persia = MakePlayer("Persians", 1);
		BuildUnitedNations(rome);
		Meet(rome, egypt, greece, persia);
		PlayerRelationship.DeclareWar(persia, egypt, false, 0);
		PlayerRelationship.DeclareWar(persia, greece, false, 0);
		Befriend(persia, rome);

		// Rome 2 (with Persia), Egypt 1, Greece 1: not more than half of 4.
		UnitedNations.ElectionResult result = UnitedNations.HoldElection(gameData);
		Assert.Equal([rome, egypt, greece], result.candidates);
		Assert.Equal(2, result.VotesFor(rome));
		Assert.Equal(1, result.VotesFor(greece));
		Assert.Null(result.winner);
	}

	[Fact]
	public void AbstentionsCanDenyAMajority() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 4);
		Player greece = MakePlayer("Greeks", 2);
		BuildUnitedNations(rome);
		Meet(rome, egypt);

		// 1 for Rome, 1 for Egypt, 1 abstaining: no majority of 3.
		UnitedNations.ElectionResult result = UnitedNations.HoldElection(gameData);
		Assert.Equal(1, result.abstentions);
		Assert.Null(result.ballots[greece]);
		Assert.Null(result.winner);
	}

	[Fact]
	public void HumansVoteAsTheyChoseAndOtherwiseAbstain() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Player human = MakePlayer("Greeks", 2, human: true);
		BuildUnitedNations(rome);
		Meet(rome, egypt, human);
		gameData.unitedNations.votingTurn = gameData.turn;

		Assert.True(UnitedNations.HumanShouldVote(gameData, human));
		Assert.Null(UnitedNations.Vote(gameData, human, rome, egypt));

		Assert.True(UnitedNations.CastHumanVote(gameData, human, rome));
		Assert.False(UnitedNations.HumanShouldVote(gameData, human));
		Assert.Equal(rome, UnitedNations.Vote(gameData, human, rome, egypt));

		// Only candidates can be voted for.
		Assert.False(UnitedNations.CastHumanVote(gameData, human, human));
	}

	[Fact]
	public void HumanIsAskedToVoteOnlyOnTheVotingTurn() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Player human = MakePlayer("Greeks", 2, human: true);
		BuildUnitedNations(rome);
		Meet(rome, egypt, human);
		gameData.unitedNations.votingTurn = gameData.turn + 1;

		UnitedNations.AskHumanToVote(gameData, human);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsVote>());

		gameData.unitedNations.votingTurn = gameData.turn;
		UnitedNations.AskHumanToVote(gameData, human);
		MsgShowUnitedNationsVote msg = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsVote>());
		Assert.Equal(human, msg.recipient);
		Assert.Equal([rome, egypt], msg.candidates);
	}

	[Fact]
	public void HumanIsOfferedOnlyTheCandidatesTheyHaveMet() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 5);
		Player persia = MakePlayer("Persians", 5);
		Player human = MakePlayer("Greeks", 2, human: true);
		BuildUnitedNations(rome);
		Meet(rome, egypt, persia);
		Meet(human, rome);
		Meet(human, persia);
		gameData.unitedNations.votingTurn = gameData.turn;

		UnitedNations.AskHumanToVote(gameData, human);
		MsgShowUnitedNationsVote msg = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsVote>());
		Assert.Equal([rome, persia], msg.candidates);
		Assert.Equal(persia.id.ToString(), gameData.unitedNations.candidateC);
		Assert.False(UnitedNations.CastHumanVote(gameData, human, egypt));
		Assert.True(UnitedNations.CastHumanVote(gameData, human, persia));
	}

	[Fact]
	public void ElectionIsScheduledThenHeldAndTheWinnerGetsADiplomaticVictory() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Player greece = MakePlayer("Greeks", 2);
		BuildUnitedNations(rome);
		Meet(rome, egypt, greece);
		PlayerRelationship.DeclareWar(greece, egypt, false, 0);
		Befriend(greece, rome);
		gameData.victories.Add(new DiplomaticVictory());

		// The round that finds the UN offers its AI owner an election, which
		// it holds during the coming turn, expecting to win it.
		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(50, gameData.unitedNations.offerTurn);
		Assert.Equal(50, gameData.unitedNations.votingTurn);
		TurnHandling.CheckVictory(gameData);
		Assert.False(gameData.gameOver);

		// Once that turn has been played, the votes are counted.
		gameData.turn = 51;
		UnitedNations.ElectionResult result = UnitedNations.ProcessEndOfRound(gameData);
		Assert.Equal(rome, result.winner);
		Assert.Equal(rome.id.ToString(), gameData.unitedNations.secretaryGeneral);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgUnitedNationsElectionResult>());

		TurnHandling.CheckVictory(gameData);
		Assert.True(gameData.gameOver);
		Assert.Equal(rome, gameData.winner);
		Assert.IsType<DiplomaticVictory>(EngineStorage.messagesToUI.OfType<MsgVictory>().Single().victory);
	}

	[Fact]
	public void VotesAreCountedForTheCandidatesTheHumanWasAskedAbout() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Player greece = MakePlayer("Greeks", 2);
		Player human = MakePlayer("Persians", 2, human: true);
		BuildUnitedNations(rome);
		Meet(rome, egypt, greece, human);
		gameData.unitedNations.votingTurn = gameData.turn;

		// The vote is called as the voting turn begins.
		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(egypt.id.ToString(), gameData.unitedNations.candidateB);
		UnitedNations.AskHumanToVote(gameData, human);
		Assert.Null(gameData.unitedNations.candidateC);
		MsgShowUnitedNationsVote msg = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsVote>());
		Assert.Equal([rome, egypt], msg.candidates);
		Assert.True(UnitedNations.CastHumanVote(gameData, human, egypt));

		// Greece grows to a third of the world during the voting turn, but
		// the vote is still between the candidates the human was asked about.
		for (int i = 0; i < 5; i++) {
			greece.cities[0].residents.Add(new CityResident { city = greece.cities[0] });
		}
		Assert.Contains(greece, UnitedNations.Candidates(gameData));
		gameData.turn = 51;
		UnitedNations.ElectionResult result = UnitedNations.ProcessEndOfRound(gameData);
		Assert.Equal([rome, egypt], result.candidates);
		Assert.Equal(egypt, result.ballots[human]);
		Assert.Null(gameData.unitedNations.candidateA);
		Assert.Null(gameData.unitedNations.candidateB);
		Assert.Null(gameData.unitedNations.candidateC);
	}

	[Fact]
	public void CandidatesAreStoredWhenFirstNeededInOlderSaves() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Player human = MakePlayer("Greeks", 2, human: true);
		BuildUnitedNations(rome);
		Meet(rome, egypt, human);
		gameData.unitedNations.votingTurn = gameData.turn;

		Assert.True(UnitedNations.CastHumanVote(gameData, human, egypt));
		Assert.Equal(rome.id.ToString(), gameData.unitedNations.candidateA);
		Assert.Equal(egypt.id.ToString(), gameData.unitedNations.candidateB);
	}

	// A save from before the owner was asked has a vote scheduled but no
	// offer turn.
	[Fact]
	public void FailedVoteInAnOlderSaveOffersTheNextOne() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 4);
		MakePlayer("Greeks", 2);
		BuildUnitedNations(rome);
		Meet(rome, egypt);
		gameData.unitedNations.votingTurn = 49;

		UnitedNations.ElectionResult result = UnitedNations.ProcessEndOfRound(gameData);
		Assert.Null(result.winner);
		Assert.Null(gameData.unitedNations.secretaryGeneral);
		// The vote comes "every 10 turns" (Perez's decompile, Round 2;
		// owner's ruling 2026-11-12, superseding the FAQ's 11).
		Assert.Equal(10, UnitedNations.ElectionInterval);
		Assert.Equal(-1, gameData.unitedNations.votingTurn);
		Assert.Equal(49 + UnitedNations.ElectionInterval, gameData.unitedNations.offerTurn);
	}

	[Fact]
	public void FailedVoteOffersTheNextElectionTenTurnsAfterTheLast() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 4);
		MakePlayer("Greeks", 2);
		BuildUnitedNations(rome);
		Meet(rome, egypt);
		gameData.unitedNations.offerTurn = 48;
		gameData.unitedNations.votingTurn = 49;

		Assert.Null(UnitedNations.ProcessEndOfRound(gameData).winner);
		Assert.Equal(48 + UnitedNations.ElectionInterval, gameData.unitedNations.offerTurn);
	}

	// https://forums.civfanatics.com/threads/when-someone-else-builds-the-u-n.85895/:
	// "If they know they will most likely lose they won't hold a vote."
	[Fact]
	public void AnAIFounderHoldsAnElectionOnlyWhenItExpectsToWin() {
		Player rome = MakePlayer("Romans", 3);
		Player egypt = MakePlayer("Egyptians", 7);
		Player greece = MakePlayer("Greeks", 2);
		BuildUnitedNations(rome);
		Meet(rome, egypt, greece);

		// Greece would abstain: no majority, so no election, and the next
		// chance is ElectionInterval turns away.
		Assert.False(UnitedNations.AIHoldsElection(gameData, rome));
		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(-1, gameData.unitedNations.votingTurn);
		Assert.Equal(50 + UnitedNations.ElectionInterval, gameData.unitedNations.offerTurn);
		Assert.Null(gameData.unitedNations.candidateA);

		// Nothing happens until then.
		Befriend(greece, rome);
		for (gameData.turn = 51; gameData.turn < 60; ++gameData.turn) {
			Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
			Assert.Equal(-1, gameData.unitedNations.votingTurn);
		}

		// Now Greece would vote for Rome.
		Assert.True(UnitedNations.AIHoldsElection(gameData, rome));
		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(60, gameData.unitedNations.votingTurn);
		Assert.Equal(rome.id.ToString(), gameData.unitedNations.candidateA);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsElectionOffer>());
	}

	// The founder is offered an election "every 10 turns" (Perez's
	// decompile, Round 2; owner's ruling 2026-11-12).
	[Fact]
	public void AHumanFounderChoosesToHoldAnElection() {
		Player human = MakePlayer("Romans", 3, human: true);
		Player egypt = MakePlayer("Egyptians", 7);
		Player greece = MakePlayer("Greeks", 2, human: true);
		BuildUnitedNations(human);
		Meet(human, egypt, greece);

		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(50, gameData.unitedNations.offerTurn);
		Assert.Equal(-1, gameData.unitedNations.votingTurn);

		// Only the founder is asked.
		UnitedNations.AskHumanToVote(gameData, greece);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsElectionOffer>());
		Assert.False(UnitedNations.AnswerElectionOffer(gameData, greece, true));
		UnitedNations.AskHumanToVote(gameData, human);
		MsgShowUnitedNationsElectionOffer offer = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsElectionOffer>());
		Assert.Equal(human, offer.recipient);
		Assert.Equal([human, egypt], offer.candidates);

		// The vote is held during the next turn.
		Assert.True(UnitedNations.AnswerElectionOffer(gameData, human, true));
		Assert.Equal(51, gameData.unitedNations.votingTurn);
		Assert.False(UnitedNations.FounderShouldBeAsked(gameData, human));
		Assert.False(UnitedNations.AnswerElectionOffer(gameData, human, false));

		gameData.turn = 51;
		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(human.id.ToString(), gameData.unitedNations.candidateA);
		EngineStorage.messagesToUI.Clear();
		UnitedNations.AskHumanToVote(gameData, greece);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsVote>());
		Assert.True(UnitedNations.CastHumanVote(gameData, greece, human));

		gameData.turn = 52;
		UnitedNations.ElectionResult result = UnitedNations.ProcessEndOfRound(gameData);
		Assert.Equal(human, result.winner);
		Assert.Equal(-1, gameData.unitedNations.votingTurn);
		Assert.Equal(50 + UnitedNations.ElectionInterval, gameData.unitedNations.offerTurn);
	}

	// The interval is the same whether the vote is inconclusive or the
	// option declined (MadScot,
	// https://forums.civfanatics.com/threads/diplomatic-victory.53460/,
	// whose 11 turns Perez's decompile corrects to 10).
	[Fact]
	public void AHumanFounderWhoDeclinesIsAskedAgainTenTurnsLater() {
		Player human = MakePlayer("Romans", 3, human: true);
		Player egypt = MakePlayer("Egyptians", 7);
		BuildUnitedNations(human);
		Meet(human, egypt);
		UnitedNations.ProcessEndOfRound(gameData);

		Assert.True(UnitedNations.AnswerElectionOffer(gameData, human, false));
		Assert.Equal(60, gameData.unitedNations.offerTurn);
		Assert.Equal(-1, gameData.unitedNations.votingTurn);
		Assert.False(UnitedNations.FounderShouldBeAsked(gameData, human));
		UnitedNations.AskHumanToVote(gameData, human);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowUnitedNationsElectionOffer>());

		for (gameData.turn = 51; gameData.turn < 60; ++gameData.turn) {
			Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
			Assert.False(UnitedNations.FounderShouldBeAsked(gameData, human));
		}
		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.True(UnitedNations.FounderShouldBeAsked(gameData, human));
	}

	// An unanswered offer can't hold the game up: once the turn is over, it
	// was declined.
	[Fact]
	public void AnUnansweredOfferIsDeclinedAtTheEndOfTheTurn() {
		Player human = MakePlayer("Romans", 3, human: true);
		Player egypt = MakePlayer("Egyptians", 7);
		BuildUnitedNations(human);
		Meet(human, egypt);
		UnitedNations.ProcessEndOfRound(gameData);
		Assert.True(UnitedNations.FounderShouldBeAsked(gameData, human));

		gameData.turn = 51;
		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(-1, gameData.unitedNations.votingTurn);
		Assert.Equal(60, gameData.unitedNations.offerTurn);
		Assert.False(UnitedNations.FounderShouldBeAsked(gameData, human));
		Assert.False(UnitedNations.AnswerElectionOffer(gameData, human, true));
	}

	[Fact]
	public void NoElectionsWithoutDiplomaticVictory() {
		gameData.victoryConditions.AllowDiplomaticVictory = false;
		Player rome = MakePlayer("Romans", 3);
		MakePlayer("Egyptians", 4);
		BuildUnitedNations(rome);

		Assert.Null(UnitedNations.ProcessEndOfRound(gameData));
		Assert.Equal(-1, gameData.unitedNations.votingTurn);
	}

	[Fact]
	public void DiplomaticVictoryIsRegisteredWhenAllowed() {
		SaveGame save = new() { VictoryConditions = new VictoryConditions { AllowDiplomaticVictory = true } };
		Assert.True(VictoryConditions.NewGameDefaults().AllowDiplomaticVictory);
		Assert.True(new DiplomaticVictory().HasVictory(new VictoryStatus { ElectedSecretaryGeneral = true }));
		Assert.False(new DiplomaticVictory().HasVictory(new VictoryStatus()));
		Assert.NotNull(save.UnitedNations);
	}
}
