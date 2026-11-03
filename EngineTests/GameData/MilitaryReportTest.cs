using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;
using static C7GameData.MilitaryReport;

namespace EngineTests.GameData;

// The military advisor: our units by type and by city, the upgrades the
// gold covers, what we know of a rival's forces, and the advice.
public class MilitaryReportTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City city;
	private readonly UnitPrototype oldType;
	private readonly UnitPrototype newType;

	public MilitaryReportTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Garrison");
		us.EnsureRelationshipExists(them);
		// A ground unit whose direct upgrade needs no resource (see UpgradeTest).
		(oldType, newType) = gameData.unitPrototypes
			.Where(u => u.IsLandUnit() && u.actions.Contains(UnitAction.Upgrade) && u.producibleBy.Contains(us.civilization))
			.SelectMany(u => u.upgradesTo.Select(t => (u, t)))
			.First(pair => pair.t.requiredResources.Count == 0 && pair.t.producibleBy.Contains(us.civilization));
		us.knownTechs.Add(newType.requiredTech.id);
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	[Fact]
	public void OurUnitsByTypeWithTheUpgradesTheGoldCovers() {
		foreach (MapUnit unit in us.units.ToList()) {
			gameData.RemoveUnit(unit);
		}
		for (int i = 0; i < 3; i++) {
			gameData.SpawnUnit(us, oldType, city.location);
		}
		gameData.SpawnUnit(us, oldType, city.location.neighbors.Values.First(t => t.IsLand()));
		city.AddBuilding(gameData.Buildings.First(b => b.providesVeteranGroundUnits && !b.IsGreatWonder()), gameData);
		int price = us.units[0].UpgradeOffer().Value.cost;

		// The one outside the city can't upgrade; the gold covers two of the
		// three inside.
		us.gold = price * 2 + 1;
		UnitGroup group = Assert.Single(ByType(gameData, us));
		Assert.Equal(oldType, group.type);
		Assert.Equal(4, group.units.Count);
		Assert.Equal(2, group.upgradable);
		Assert.Equal(price * 2, group.upgradeCost);

		AdvisorAdvice.Advice advice = AdvisorAdvice.Military(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Surprised, advice.mood);
		Assert.Equal("2 of our units can be upgraded.", advice.text);
	}

	[Fact]
	public void OurUnitsByCityThenInTheField() {
		foreach (MapUnit unit in us.units.ToList()) {
			gameData.RemoveUnit(unit);
		}
		MapUnit inside = gameData.SpawnUnit(us, Type("Spearman"), city.location);
		MapUnit outside = gameData.SpawnUnit(us, Type("Warrior"), city.location.neighbors.Values.First(t => t.IsLand()));
		List<(City city, List<MapUnit> units)> byCity = ByCity(us);
		Assert.Equal(2, byCity.Count);
		Assert.Equal(city, byCity[0].city);
		Assert.Equal([inside], byCity[0].units);
		Assert.Null(byCity[1].city);
		Assert.Equal([outside], byCity[1].units);
	}

	[Fact]
	public void WhatWeKnowOfTheirForces() {
		Tile near = us.units[0].location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		gameData.SpawnUnit(them, Type("Spearman"), near);
		gameData.SpawnUnit(them, Type("Spearman"), near);
		Tile far = gameData.map.tiles.First(t => t.IsLand() && !t.HasCity() && !us.tileKnowledge.isActiveTile(t));
		gameData.SpawnUnit(them, Type("Swordsman"), far);
		us.tileKnowledge.RecomputeActiveTiles();

		Intelligence seen = On(us, them);
		Assert.False(seen.spy);
		Assert.Equal(2, seen.seen);
		Assert.Equal([(Type("Spearman"), 2)], seen.types);

		us.playerRelationships[them.id].hasSpy = true;
		Intelligence all = On(us, them);
		Assert.True(all.spy);
		Assert.Equal(Type("Swordsman"), all.types[0].type);
		Assert.Equal(them.units.Count(u => u.IsCombatUnit()), all.seen);
	}

	[Fact]
	public void TheMilitaryAdvisorsAdvice() {
		// The city has no defender: worse at war.
		foreach (MapUnit unit in us.units.Where(u => u.location == city.location).ToList()) {
			gameData.RemoveUnit(unit);
		}
		AdvisorAdvice.Advice advice = AdvisorAdvice.Military(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Sad, advice.mood);
		Assert.Equal("Garrison is without a defender!", advice.text);
		us.DeclareWarOn(them, gameData.turn);
		Assert.Equal(AdvisorAdvice.Mood.Angry, AdvisorAdvice.Military(gameData, us).mood);

		gameData.SpawnUnit(us, Type("Spearman"), city.location);
		Assert.Empty(Undefended(us));
		Assert.Equal(AdvisorAdvice.Mood.Happy, AdvisorAdvice.Military(gameData, us).mood);

		// Units beyond the free ones cost gold.
		for (int i = 0; i < 12; i++) {
			gameData.SpawnUnit(us, Type("Warrior"), city.location);
		}
		(_, _, int cost) = us.TotalUnitsAllowedUnitsAndSupportCost();
		Assert.True(cost > 0);
		Assert.Equal($"Our units cost us {cost} gold a turn beyond what our cities support for free.", AdvisorAdvice.Military(gameData, us).text);
	}
}
