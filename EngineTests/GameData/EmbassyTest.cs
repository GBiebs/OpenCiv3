using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Embassies: with Writing, a civ pays distance × level + base + size (base 20
// in Conquests) to open an embassy with a civ it has met and is at peace
// with; the embassy goes both ways.
public class EmbassyTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City ourCapital;
	private readonly City theirCapital;

	public EmbassyTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		ourCapital = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		theirCapital = CityInteractions.BuildCity(them.units[0].location, them, "Abroad");
		us.EnsureRelationshipExists(them);
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == "Writing").id);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private void Grow(City city, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			CityResident resident = new() { city = city, nationality = city.owner.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
	}

	[Fact]
	public void MissionsComeFromTheRuleset() {
		Assert.Equal(EspionageMission.Keys, gameData.rules.EspionageMissions.Select(m => m.key));
		Assert.Equal(20, gameData.rules.Mission(EspionageMission.Embassy).baseCost);
		Assert.Equal(60, gameData.rules.Mission(EspionageMission.PlantSpy).baseCost);
	}

	[Fact]
	public void TheCostIsDistanceTimesLevelPlusBasePlusSize() {
		int distance = ourCapital.location.DistanceTo(theirCapital.location);
		Assert.Equal(distance * 1 + 20 + 1, Embassies.Cost(gameData, us, them));

		// A city (size 7-12) counts the distance twice.
		Grow(theirCapital, 8);
		Assert.Equal(2, theirCapital.SizeLevel());
		Assert.Equal(distance * 2 + 20 + 8, Embassies.Cost(gameData, us, them));

		// From our city nearest their capital.
		Tile near = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(theirCapital.location) >= 3 && t.DistanceTo(theirCapital.location) < distance
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 2));
		CityInteractions.BuildCity(near, us, "Outpost");
		Assert.Equal(near.DistanceTo(theirCapital.location) * 2 + 20 + 8, Embassies.Cost(gameData, us, them));

		// The base cost is the mission's.
		gameData.rules.Mission(EspionageMission.Embassy).baseCost = 50;
		Assert.Equal(near.DistanceTo(theirCapital.location) * 2 + 50 + 8, Embassies.Cost(gameData, us, them));
	}

	[Fact]
	public void AnEmbassyNeedsWritingContactPeaceAndTheirCapital() {
		Assert.True(Embassies.CanEstablish(us, them));

		us.knownTechs.Remove(gameData.techs.Find(t => t.Name == "Writing").id);
		Assert.False(Embassies.CanEstablish(us, them));
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == "Writing").id);

		Player stranger = gameData.players.First(p => !p.isBarbarians && p != us && p != them && !us.playerRelationships.ContainsKey(p.id));
		Assert.False(Embassies.CanEstablish(us, stranger));

		us.DeclareWarOn(them, gameData.turn);
		Assert.False(Embassies.CanEstablish(us, them));
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.True(Embassies.CanEstablish(us, them));

		theirCapital.capital = false;
		Assert.False(Embassies.CanEstablish(us, them));
	}

	[Fact]
	public void BuildingOnePaysAndOpensItBothWays() {
		int cost = Embassies.Cost(gameData, us, them);
		us.gold = cost - 1;
		Assert.False(Embassies.Establish(gameData, us, them));
		Assert.Equal(cost - 1, us.gold);
		Assert.False(Embassies.Has(us, them));

		us.gold = cost + 5;
		Assert.True(Embassies.Establish(gameData, us, them));
		Assert.Equal(5, us.gold);
		Assert.True(Embassies.Has(us, them));
		Assert.True(Embassies.Has(them, us));

		// Only one.
		Assert.False(Embassies.CanEstablish(us, them));
		Assert.False(Embassies.Establish(gameData, us, them));
		Assert.Equal(5, us.gold);
	}

	[Fact]
	public void ThePlayersMessageBuildsItAndTellsTheUI() {
		us.gold = 500;
		int cost = Embassies.Cost(gameData, us, them);
		new MsgEstablishEmbassy(us.id, them.id).process();
		Assert.True(Embassies.Has(us, them));
		Assert.Equal(500 - cost, us.gold);
		Assert.True(EngineStorage.messagesToUI.TryDequeue(out MessageToUI message));
		MsgEmbassyEstablished established = Assert.IsType<MsgEmbassyEstablished>(message);
		Assert.Equal(us.id, established.playerId);
		Assert.Equal(cost, established.cost);
		Assert.Equal(theirCapital, established.capital);
	}

	[Fact]
	public void TheAIBuildsEmbassiesWithGoldToSpare() {
		int cost = Embassies.Cost(gameData, us, them);
		us.gold = cost + PlayerAI.UPGRADE_GOLD_RESERVE - 1;
		PlayerAI.BuildEmbassies(us, gameData);
		Assert.False(Embassies.Has(us, them));

		us.gold = cost + PlayerAI.UPGRADE_GOLD_RESERVE;
		PlayerAI.BuildEmbassies(us, gameData);
		Assert.True(Embassies.Has(us, them));
		Assert.Equal(PlayerAI.UPGRADE_GOLD_RESERVE, us.gold);
	}

	[Fact]
	public void EmbassiesAreSaved() {
		Embassies.Open(us, them);
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Player usAgain = reloaded.GetPlayer(us.id);
		Player themAgain = reloaded.GetPlayer(them.id);
		Assert.True(Embassies.Has(usAgain, themAgain));
		Assert.True(Embassies.Has(themAgain, usAgain));
	}
}

// Embassies in Civ3's saves: right of passage needs one, so the civs with a
// right of passage in this save have embassies.
public class EmbassyImportTest : RemoteSaveLoader {
	[SkippableFact]
	public async Task CivsWithARightOfPassageHaveEmbassies() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string uri = "https://www.dropbox.com/scl/fi/pb3k02ufgi0q7okwwykvs/MultiTurnDeal_Save_A.SAV?rlkey=y44s3c8czlp01evm3h35sgm1u&st=e6o8e2h0&dl=1";
		(SaveGame game, Exception ex, _) = await LoadGameAndData("MultiTurnDeal_Save_A.SAV", "saves/multi-turn-deals", uri);
		Assert.Null(ex);

		PlayerRelationship oneToThree = game.Players[1].playerRelationships["player-3"];
		Assert.Contains(oneToThree.multiTurnDeals, d => d.dealSubType == DealSubType.RightOfPassage);
		Assert.True(oneToThree.hasEmbassy);
		SavePlayer three = game.Players.First(p => p.id.ToString() == "player-3");
		Assert.True(three.playerRelationships[game.Players[1].id.ToString()].hasEmbassy);

		// Civ3's mission costs come from its rules.
		Assert.Equal(20, game.Rules.Mission(EspionageMission.Embassy).baseCost);
		Assert.Equal(EspionageMission.Keys, game.Rules.EspionageMissions.Select(m => m.key));
	}
}
