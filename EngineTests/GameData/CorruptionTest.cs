using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's corruption, as Firaxis described it (via alexman, "Everything About
// Corruption: C3C Edition"): a distance part, da = 0.5^Ni * min(Gd * t * d,
// MaxD) / MaxD, where t is 1 on the capital's trade network and 5/4 off it,
// and a rank part against the optimal city number Nopt = OCN * (L/100 *
// (1 + c + ...) + 0.25 * Ni), where c is 0.25 for a commercial civ and Ni
// the city's courthouses and police stations.
public class CorruptionTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City capital;

	public CorruptionTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.civilization.traits.Remove(Civilization.Trait.Commercial);
		capital = CityInteractions.BuildCity(us.units[0].location, us, "Capital");
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && !t.hasBarbarianCamp;
	}

	private City Town(Func<Tile, bool> where) {
		Tile site = gameData.map.tiles.First(t => IsEmptyLand(t) && gameData.cities.All(c => c.location.DistanceTo(t) > 3) && where(t));
		return CityInteractions.BuildCity(site, us, "Town");
	}

	// The rank part of corruption for a city of this rank.
	private static float RankPart(int rank, float nOpt) {
		return rank < nOpt ? rank / (2 * nOpt) : (2 * rank - nOpt) / (2 * nOpt);
	}

	private float NOpt() => Math.Max(1, us.GetAdjustedOptimalCityNumber(gameData));

	[Fact]
	public void ACourthouseHalvesTheDistancePartAndAddsAQuarterOfTheMapsOptimalNumber() {
		City town = Town(t => t.DistanceTo(capital.location) >= 8 && t.DistanceTo(capital.location) <= 12);
		us.DoCorruptionCalculations(gameData);
		float distancePart = town.corruption - RankPart(town.rankIndex, NOpt());
		Assert.True(distancePart > 0 && town.corruption < .9f);

		town.AddBuilding(gameData.Buildings.Find(b => b.name == "Courthouse"), gameData);
		us.DoCorruptionCalculations(gameData);

		float expected = distancePart / 2 + RankPart(town.rankIndex, NOpt() + .25f * gameData.map.optimalNumberOfCities);
		Assert.Equal(expected, town.corruption, 4);
	}

	[Fact]
	public void ACommercialCivHasAQuarterMoreOptimalCities() {
		float baseNumber = gameData.map.optimalNumberOfCities * gameData.gameDifficulty.PercentageOfOptimalCities / 100f;
		Assert.Equal((int)baseNumber, us.GetAdjustedOptimalCityNumber(gameData));

		us.civilization.traits.Add(Civilization.Trait.Commercial);
		Assert.Equal((int)(baseNumber * 1.25f), us.GetAdjustedOptimalCityNumber(gameData));
	}

	[Fact]
	public void ACityOnTheCapitalsRoadsCountsAsNearer() {
		// A town four tiles east of the capital, over land.
		List<Tile> between = [];
		Tile tile = capital.location;
		for (int i = 0; i < 3; i++) {
			tile = tile.neighbors[TileDirection.EAST];
			between.Add(tile);
		}
		Tile site = tile.neighbors[TileDirection.EAST];
		Assert.True(between.All(IsEmptyLand) && IsEmptyLand(site), "the fixture's capital needs land to its east");
		City town = CityInteractions.BuildCity(site, us, "Town");
		us.DoCorruptionCalculations(gameData);
		Assert.False(gameData.GetTradeNetwork().ConnectedToCapital(us, town));
		float rankPart = RankPart(town.rankIndex, NOpt());
		float distancePart = town.corruption - rankPart;
		Assert.True(distancePart > 0);

		TerrainImprovement road = gameData.terrainImprovements.Find(i => i.key == "road");
		foreach (Tile t in between) {
			t.overlays.Add(road);
		}
		us.DoCorruptionCalculations(gameData);

		Assert.True(gameData.GetTradeNetwork().ConnectedToCapital(us, town));
		Assert.Equal(distancePart * 4 / 5 + rankPart, town.corruption, 4);
	}

	[Fact]
	public void TheCapitalIsTheCityWithThePalaceNotTheFirstCity() {
		City town = Town(t => t.DistanceTo(capital.location) >= 8);
		Assert.True(gameData.GetTradeNetwork().ConnectedToCapital(us, capital));
		Assert.False(gameData.GetTradeNetwork().ConnectedToCapital(us, town));

		capital.capital = false;
		town.capital = true;
		Assert.True(gameData.GetTradeNetwork().ConnectedToCapital(us, town));
		Assert.False(gameData.GetTradeNetwork().ConnectedToCapital(us, capital));
	}
}
