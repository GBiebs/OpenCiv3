using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The Nuclear Plant's fresh water requirement (City.HasFreshWaterInRadius)
// and meltdowns (Meltdown).
public class NuclearPlantTest : IClassFixture<SaveGameFixture>, System.IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public NuclearPlantTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => p.isHuman);
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	// Makes every random draw come out as the given number (or the lowest
	// allowed).
	private class FixedRandom(int value) : System.Random {
		public int draws;
		protected override double Sample() {
			++draws;
			return 0;
		}
		public override int Next(int maxValue) {
			++draws;
			return System.Math.Min(value, maxValue - 1);
		}
		public override int Next(int minValue, int maxValue) {
			++draws;
			return minValue;
		}
	}

	private T WithRandom<T>(FixedRandom random, System.Func<T> action) {
		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = random;
		try {
			return action();
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	private Building Building(string name) => gameData.Buildings.Single(b => b.name == name);

	private City FoundCity(int size) {
		Tile tile = us.units.First(u => u.unitType.isSettler).location;
		City city = CityInteractions.BuildCity(tile, us, us.GetNextCityName());
		while (city.residents.Count < size) {
			city.AddCitizen(new CityResident() {
				city = city,
				citizenType = city.residents[0].citizenType,
				tileWorked = city.residents[0].tileWorked,
			});
		}
		return city;
	}

	// Takes away all rivers and lakes from the map.
	private void DryTheMap() {
		foreach (Tile t in gameData.map.tiles) {
			t.riverNorth = t.riverNortheast = t.riverEast = t.riverSoutheast = false;
			t.riverSouth = t.riverSouthwest = t.riverWest = t.riverNorthwest = false;
			t.isFreshWater = false;
		}
	}

	private List<string> Popups() {
		List<string> result = new();
		while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI msg)) {
			if (msg is MsgShowDomesticAdvisorPopup popup) {
				result.Add(popup.message);
			}
		}
		return result;
	}

	[Fact]
	public void OnlyTheNuclearPlantNeedsFreshWaterAndCanMeltDown() {
		Assert.Equal(["Nuclear Plant"], gameData.Buildings.Where(b => b.canMeltdown).Select(b => b.name));
		using System.Text.Json.JsonDocument ruleset = JsonUtils.LoadBaseRuleset();
		List<string> nearWater = new();
		foreach (System.Text.Json.JsonElement b in ruleset.RootElement.GetProperty("buildings").EnumerateArray()) {
			if (b.TryGetProperty("flags", out System.Text.Json.JsonElement flags)
				&& flags.EnumerateArray().Any(f => f.GetString() == "mustBeNearWater")) {
				nearWater.Add(b.GetProperty("name").GetString());
			}
		}
		Assert.Equal(["Nuclear Plant"], nearWater);
	}

	[Fact]
	public void ANuclearPlantNeedsFreshWaterInTheCitysRadius() {
		City city = FoundCity(3);
		Building plant = Building("Nuclear Plant");
		us.knownTechs.Add(plant.requiredTech.id);
		city.AddBuilding(Building("Factory"));
		HashSet<Resource> resources = gameData.Resources.ToHashSet();
		DryTheMap();

		Assert.False(city.HasFreshWaterInRadius());
		Assert.False(plant.CanProduce(city, resources));

		// A river two tiles away, not next to the city, is in its radius.
		Tile riverTile = city.location.GetTilesWithinRankDistance(2).First(t => t != Tile.NONE && city.location.RankDistanceTo(t) == 2);
		riverTile.riverNorth = true;
		Assert.True(city.HasFreshWaterInRadius());
		Assert.True(plant.CanProduce(city, resources));
	}

	[Fact]
	public void ALakeInTheRadiusIsFreshWater() {
		City city = FoundCity(3);
		DryTheMap();
		Tile lake = city.location.GetTilesWithinRankDistance(2).First(t => t != Tile.NONE && t != city.location);
		lake.baseTerrainType = gameData.terrainTypes.Single(t => t.Key == "coast");
		lake.isFreshWater = true;
		Assert.True(city.HasFreshWaterInRadius());
	}

	[Fact]
	public void AMeltdownPollutesTheLandAroundAndHalvesTheCity() {
		City city = FoundCity(9);
		Building plant = Building("Nuclear Plant");
		city.AddBuilding(plant);
		foreach (Tile t in city.location.neighbors.Values) {
			if (t != Tile.NONE && t.HasPollution()) {
				t.ClearDamage();
			}
		}
		Popups();

		List<Tile> polluted = Meltdown.MeltDown(gameData, city);

		List<Tile> land = city.location.neighbors.Values.Where(t => t != Tile.NONE && !t.IsWater()).Distinct().ToList();
		Assert.Equal(land.Count, polluted.Count);
		Assert.All(land, t => Assert.True(t.HasPollution()));
		Assert.False(city.location.HasPollution());
		// 9 halved, rounded in the city's favour.
		Assert.Equal(5, city.residents.Count);
		// Nothing is destroyed, the plant included.
		Assert.True(city.HasEffectiveBuilding(plant));
		Assert.Equal([$"The Nuclear Plant in {city.name} has malfunctioned! Thousands are dead, and pollution has crippled production!"], Popups());
	}

	// Rand(100) < 10 for each plant (Civ3 binary,
	// doc/perez-findings/Civ3_Findings.md, Round 2b).
	[Fact]
	public void TheChanceIsTenPercentPerPlant() {
		City city = FoundCity(8);
		city.AddBuilding(Building("Nuclear Plant"));

		FixedRandom safe = new(10);
		Assert.False(WithRandom(safe, () => Meltdown.MaybeMeltDown(gameData, city)));
		Assert.Equal(1, safe.draws);
		Assert.Equal(8, city.residents.Count);

		FixedRandom random = new(9);
		Assert.True(WithRandom(random, () => Meltdown.MaybeMeltDown(gameData, city)));
		Assert.Equal(4, city.residents.Count);
		Assert.Equal(10, Meltdown.ChancePercent);
	}

	[Fact]
	public void ACityWithoutAPlantDrawsNoRandomNumber() {
		City city = FoundCity(8);
		FixedRandom random = new(0);
		Assert.False(WithRandom(random, () => Meltdown.MaybeMeltDown(gameData, city)));
		Assert.Equal(0, random.draws);
		Assert.Equal(8, city.residents.Count);
	}

	// A city of 16 under the starting government, with no luxuries, riots.
	private City RiotingCityWithPlant() {
		City city = FoundCity(16);
		city.AddBuilding(Building("Nuclear Plant"));
		Assert.Equal(City.Mood.Unhappy, city.RecalculateCitizenMoods(gameData));
		return city;
	}

	[Fact]
	public void APlantNeverMeltsDownTheTurnDisorderStarts() {
		City city = RiotingCityWithPlant();
		city.isInCivilDisorder = false;
		WithRandom(new FixedRandom(0), () => {
			us.RecalculateCitizenMoods(gameData, goIntoDisorderIfUnhappy: true);
			return 0;
		});
		Assert.True(city.isInCivilDisorder);
		Assert.Equal(16, city.residents.Count);
	}

	[Fact]
	public void APlantCanMeltDownWhenDisorderLastsAWholeTurn() {
		City city = RiotingCityWithPlant();
		city.isInCivilDisorder = true;
		WithRandom(new FixedRandom(0), () => {
			us.RecalculateCitizenMoods(gameData, goIntoDisorderIfUnhappy: true);
			return 0;
		});
		Assert.Equal(8, city.residents.Count);
	}
}
