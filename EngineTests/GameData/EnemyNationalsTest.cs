using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3 (Civilopedia "Nationality"): foreign nationals are no problem "unless
// you are at war with the nation that originally produced them. When at war,
// foreign nationals become unhappy more quickly than others". The user's Civ3
// saves show how much: each one is an unhappy face, specialists included.
public class EnemyNationalsTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Player third;
	private readonly City city;

	public EnemyNationalsTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(3).ToArray();
		(us, them, third) = (ais[0], ais[1], ais[2]);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Melting Pot");
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < 6) {
			CityResident resident = new() { city = city, nationality = us.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
		// Two of their people and one of a third civ's.
		city.residents[0].nationality = them.civilization;
		city.residents[1].nationality = them.civilization;
		city.residents[2].nationality = third.civilization;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private int Count(CityResident.Mood mood) {
		return city.residents.Count(r => r.citizenType.IsDefaultCitizen && r.mood == mood);
	}

	[Fact]
	public void ForeignNationalsAreNoProblemInPeace() {
		Assert.Equal(0, city.CitizensOfEnemyNationality(gameData));
	}

	[Fact]
	public void EachCitizenOfACivWeAreAtWarWithCounts() {
		us.DeclareWarOn(them, gameData.turn);
		Assert.Equal(2, city.CitizensOfEnemyNationality(gameData));

		them.DeclareWarOn(third, gameData.turn);
		Assert.Equal(2, city.CitizensOfEnemyNationality(gameData));
		third.DeclareWarOn(us, gameData.turn);
		Assert.Equal(3, city.CitizensOfEnemyNationality(gameData));
	}

	[Fact]
	public void SpecialistsCountButResistersDont() {
		us.DeclareWarOn(them, gameData.turn);
		city.residents[0].citizenType = us.GetKnownSpecialists(gameData).First();
		Assert.Equal(2, city.CitizensOfEnemyNationality(gameData));

		city.residents[1].resisting = true;
		Assert.Equal(1, city.CitizensOfEnemyNationality(gameData));
	}

	[Fact]
	public void TheyAreUnhappyFaces() {
		city.RecalculateCitizenMoods(gameData);
		int unhappyBefore = Count(CityResident.Mood.Unhappy);
		Assert.True(Count(CityResident.Mood.Content) >= 2);

		us.DeclareWarOn(them, gameData.turn);
		// War weariness doesn't come into it: being declared war on rallies
		// people, and we declared it.
		Assert.Equal((0, 0), WarWeariness.CitizensAffected(gameData, city));
		city.RecalculateCitizenMoods(gameData);
		Assert.Equal(unhappyBefore + 2, Count(CityResident.Mood.Unhappy));
	}

	[Fact]
	public void CitizensOfACivNoLongerInTheGameDontCount() {
		us.DeclareWarOn(them, gameData.turn);
		them.defeated = true;
		Assert.Equal(0, city.CitizensOfEnemyNationality(gameData));
	}

	[Fact]
	public async Task TheAIKeepsItsCitizensNationalitiesAndResisters() {
		city.residents[3].nationality = them.civilization;
		city.residents[3].resisting = true;
		city.residents[3].tileWorked.personWorkingTile = null;
		city.residents[3].tileWorked = Tile.NONE;

		await PlayerAI.PlayTurn(us, gameData);

		Assert.Equal(6, city.residents.Count);
		Assert.Equal(3, city.residents.Count(r => r.nationality == them.civilization));
		Assert.Equal(1, city.residents.Count(r => r.nationality == third.civilization));
		CityResident resister = Assert.Single(city.residents, r => r.resisting);
		Assert.Equal(them.civilization, resister.nationality);
		Assert.Equal(Tile.NONE, resister.tileWorked);
	}
}
