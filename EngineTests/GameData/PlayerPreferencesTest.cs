using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's preferences the engine follows for a human (PlayerPreferences),
// and the news some of the others choose to show.
public class PlayerPreferencesTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public PlayerPreferencesTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.isHuman = true;
		city = CityInteractions.BuildCity(us.units[0].location, us, "Seat");
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Warrior => gameData.unitPrototypes.Find(u => u.name == "Warrior");

	private MsgProductionComplete FinishWarrior() {
		city.SetItemBeingProduced(Warrior);
		city.SetStoredShields(us.ShieldCost(Warrior));
		city.HandleCityProduction(gameData);
		return EngineStorage.messagesToUI.OfType<MsgProductionComplete>().Last();
	}

	[Fact]
	public void PreferencesReachTheEngineAndAreSaved() {
		Assert.True(us.Prefers(PlayerPreferences.AlwaysBuildPreviousUnit));
		Assert.False(us.Prefers(PlayerPreferences.AskBuildOrdersAfterUnit));
		new MsgSetPreferences(us.id, "AlwaysBuildPreviousUnit=0;AskBuildOrdersAfterUnit=1").process();
		Assert.False(us.Prefers(PlayerPreferences.AlwaysBuildPreviousUnit));

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.True(reloaded.GetPlayer(us.id).Prefers(PlayerPreferences.AskBuildOrdersAfterUnit));
	}

	[Fact]
	public void AUnitIsBuiltAgainOrNotAndAskedAboutIfWanted() {
		Assert.False(FinishWarrior().askWhatNext);
		Assert.Same(Warrior, city.itemBeingProduced);

		us.preferences[PlayerPreferences.AskBuildOrdersAfterUnit] = true;
		Assert.True(FinishWarrior().askWhatNext);
		Assert.Same(Warrior, city.itemBeingProduced);

		us.preferences[PlayerPreferences.AlwaysBuildPreviousUnit] = false;
		Assert.True(FinishWarrior().askWhatNext);
	}

	[Fact]
	public void TheCapitalsGovernorRulesNewCities() {
		city.governor.manageMoods = true;
		Tile far = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && gameData.cities.All(c => c.location.DistanceTo(t) > 8));
		Assert.False(CityInteractions.BuildCity(far, us, "Plain").governor.manageMoods);

		us.preferences[PlayerPreferences.CapitalGovernorDefault] = true;
		Tile farther = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && gameData.cities.All(c => c.location.DistanceTo(t) > 8));
		Assert.True(CityInteractions.BuildCity(farther, us, "Ruled").governor.manageMoods);
	}

	[Fact]
	public void ACityAtItsLimitIsReportedOnce() {
		while (city.residents.Count < gameData.rules.MaximumLevel1CitySize) {
			city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = gameData.citizenTypes.Find(c => c.IsDefaultCitizen) });
		}
		if (city.SizeLimit(gameData) > city.residents.Count) {
			return; // (Fresh water here: a city can grow past town size.)
		}
		for (int turn = 0; turn < 3; turn++) {
			city.foodStored = city.FoodNeededToGrow();
			city.HandleCityGrowth(gameData);
		}
		MsgCityCantGrow warning = Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityCantGrow>());
		Assert.Equal("Seat can't grow any larger without an Aqueduct.", warning.Text());
	}

	[Fact]
	public void AWonderBegunIsNewsForThoseWhoKnowTheBuilder() {
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder());
		city.SetItemBeingProduced(wonder);
		MsgWonderStarted started = Assert.Single(EngineStorage.messagesToUI.OfType<MsgWonderStarted>());
		Assert.Null(started.TextFor(us));
		Player other = gameData.players.First(p => p != us && !p.isBarbarians);
		other.EnsureRelationshipExists(us);
		Assert.Equal($"Word has reached us that the {us.civilization.noun} have begun work on {wonder.name} in Seat.", started.TextFor(other));

		// Not again while it carries on with it.
		city.SetItemBeingProduced(wonder);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgWonderStarted>());
	}

	[Fact]
	public void ADealRunningOutIsReported() {
		Player other = gameData.players.First(p => p != us && !p.isBarbarians);
		us.EnsureRelationshipExists(other);
		PlayerRelationship.RegisterMultiTurnDeal(us, other, new MultiTurnDeal(DealType.Gold, DealSubType.GoldPerTurn,
			DealDetails.Outbound, 3, null, 1, gameData.turn));
		gameData.turn += 5;
		PlayerRelationship.CheckForObsoleteDeals(us, gameData.players, gameData.turn);
		MsgDealExpired expired = Assert.Single(EngineStorage.messagesToUI.OfType<MsgDealExpired>());
		Assert.Equal($"Our gold per turn with the {other.civilization.noun} has run out.", expired.Text(gameData));
	}
}
