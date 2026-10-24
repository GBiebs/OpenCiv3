using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's military police: under some governments each military unit in a
// city makes an unhappy citizen content, up to the government's limit (BIQ:
// Despotism 2, Monarchy and Feudalism 3, Communism and Fascism 4, Republic
// and Democracy none). An army is one unit.
public class MilitaryPoliceTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;
	private readonly int unhappyAlone;

	public MilitaryPoliceTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Garrison");
		foreach (MapUnit unit in city.location.unitsOnTile.ToList()) {
			gameData.RemoveUnit(unit);
		}
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < 12) {
			CityResident resident = new() { city = city, nationality = us.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
		// Only the citizens working tiles, so the faces show every policeman.
		foreach (CityResident specialist in city.residents.Where(r => !r.citizenType.IsDefaultCitizen).ToList()) {
			city.RemoveCitizen(specialist);
		}
		unhappyAlone = Unhappy();
	}

	private int Unhappy() {
		city.RecalculateCitizenMoods(gameData);
		return city.residents.Count(r => r.mood == CityResident.Mood.Unhappy);
	}

	private MapUnit Spawn(string type) {
		return gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == type), city.location);
	}

	[Fact]
	public void EachUnitPolicesUpToTheGovernmentsLimit() {
		Assert.Equal("Despotism", us.government.name);
		Assert.True(unhappyAlone >= 4);

		Spawn("Spearman");
		Assert.Equal(unhappyAlone - 1, Unhappy());
		Spawn("Spearman");
		Assert.Equal(unhappyAlone - 2, Unhappy());
		Spawn("Spearman");
		Assert.Equal(unhappyAlone - 2, Unhappy());

		us.government = gameData.governments.Find(g => g.name == "Republic");
		Assert.Equal(unhappyAlone, Unhappy());
	}

	[Fact]
	public void UnitsThatCantDefendDontPolice() {
		Spawn("Worker");
		Spawn("Explorer");
		Assert.Equal(unhappyAlone, Unhappy());
	}

	[Fact]
	public void AnArmyIsOneUnit() {
		us.government = gameData.governments.Find(g => g.name == "Communism");
		Assert.Equal(4, us.government.militaryPoliceLimit);
		MapUnit army = Spawn("Army");
		foreach (string member in new[] { "Swordsman", "Swordsman", "Spearman" }) {
			army.TakeIntoArmy(Spawn(member));
		}
		Assert.Equal(3, army.Passengers().Count);

		Assert.Equal(unhappyAlone - 1, Unhappy());
	}
}
