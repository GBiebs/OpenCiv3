using System;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// A building stops working once its owner knows the advance that makes it
// obsolete, and Civ3's Secret Police HQ outside Communism ("Changing from
// Communism will cause the Secret Police HQ bonuses to stop."). Culture
// they keep making (Civ3's obsolete wonders do).
public class ObsoleteBuildingTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;
	private readonly Tech obsoleting;

	public ObsoleteBuildingTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Seat");
		obsoleting = gameData.techs.First(t => !us.knownTechs.Contains(t.id));
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private Building Find(string name) => gameData.Buildings.Find(b => b.name == name);

	// The building goes obsolete with an advance we then learn.
	private void MakeObsolete(Building building) {
		building.renderedObsoleteBy = obsoleting;
		us.knownTechs.Add(obsoleting.id);
	}

	[Fact]
	public void ObsoleteWallsAndGranariesDoNothing() {
		city.AddBuilding(Find("Walls"), gameData);
		city.AddBuilding(Find("Granary"), gameData);
		Assert.True(city.HasWalls());
		Assert.True(city.HasGranary());

		MakeObsolete(Find("Walls"));
		MakeObsolete(Find("Granary"));
		Assert.False(city.HasWalls());
		Assert.False(city.HasGranary());
	}

	[Fact]
	public void AnObsoleteCourthouseNoLongerCutsCorruption() {
		Building courthouse = Find("Courthouse");
		city.AddBuilding(courthouse, gameData);
		Assert.Contains(city.WorkingBuildings(), cb => cb.building == courthouse);
		MakeObsolete(courthouse);
		Assert.DoesNotContain(city.WorkingBuildings(), cb => cb.building == courthouse);
	}

	[Fact]
	public void ObsoleteBuildingsKeepMakingCulture() {
		Building temple = Find("Temple");
		city.AddBuilding(temple, gameData);
		int culture = city.GetCulturePerTurnRaw();
		Assert.True(culture > 0);
		MakeObsolete(temple);
		Assert.Equal(culture, city.GetCulturePerTurnRaw());
	}

	[Fact]
	public void TheSecretPoliceHQWorksOnlyUnderCommunism() {
		Building hq = Find("Secret Police HQ");
		Government communism = hq.requiredGovernment;
		Assert.Equal("Communism", communism.name);
		Government other = gameData.governments.First(g => g != communism && g.name != "Anarchy");

		Player them = gameData.players.First(p => p != us && !p.isBarbarians);
		City far = CityInteractions.BuildCity(them.units.First(u => u.unitType.isSettler).location, us, "Far");
		far.AddBuilding(hq, gameData);

		us.government = communism;
		Assert.True(hq.Works(us));
		int optimalUnderCommunism = us.GetAdjustedOptimalCityNumber(gameData);
		us.DoCorruptionCalculations(gameData);
		Assert.Contains(far, us.citiesWithCorruptionWonders);

		// The same empire as if it had no HQ: Communism's own factor only.
		hq.requiredGovernment = null;
		far.constructed_buildings.RemoveAll(cb => cb.building == hq);
		int optimalWithoutIt = us.GetAdjustedOptimalCityNumber(gameData);
		Assert.True(optimalUnderCommunism > optimalWithoutIt);
		far.AddBuilding(hq, gameData);
		hq.requiredGovernment = communism;

		us.government = other;
		Assert.False(hq.Works(us));
		us.DoCorruptionCalculations(gameData);
		Assert.DoesNotContain(far, us.citiesWithCorruptionWonders);
	}
}
