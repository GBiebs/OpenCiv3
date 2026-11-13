using System;
using System.Linq;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Disease from unhealthy terrain (see Disease).
public class DiseaseTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player player;
	private readonly City city;
	private readonly TerrainType floodPlain;
	private readonly TerrainType jungle;
	private readonly TerrainType grassland;

	public DiseaseTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		player = gameData.players.First(p => p.isHuman);
		Tile tile = player.units.First(u => u.unitType.isSettler).location;
		city = CityInteractions.BuildCity(tile, player, player.GetNextCityName());
		floodPlain = Terrain("flood plain");
		jungle = Terrain("jungle");
		grassland = Terrain("grassland");
		city.location.overlayTerrainType = grassland;
		player.knownTechs.Remove(SanitationTech().id);
		// Only what each test puts there: no other unhealthy terrain nearby.
		foreach (Tile t in city.location.GetTilesWithinRankDistance(2)) {
			if (t != Tile.NONE && t.overlayTerrainType.causesDisease) {
				t.overlayTerrainType = grassland;
			}
		}
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private TerrainType Terrain(string key) => gameData.terrainTypes.Single(t => t.Key == key);

	private Tech SanitationTech() => gameData.techs.Single(t => t.HasFlag(SaveTech.Flag.DisablesDiseases));

	// Grows the city, every new citizen working a grassland tile.
	private void GrowTo(int size) {
		while (city.residents.Count < size) {
			CityResident r = new() {
				city = city,
				nationality = player.civilization,
				citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen),
			};
			city.AddCitizen(r);
			CityTileAssignmentAI.AssignNewCitizenToTile(gameData, r);
			if (r.tileWorked != Tile.NONE) {
				r.tileWorked.overlayTerrainType = grassland;
			}
		}
	}

	private CityResident Working() => city.residents.First(r => r.tileWorked != Tile.NONE && r.tileWorked != city.location);

	[Fact]
	public void TheRulesetHasConquestsDiseaseTerrains() {
		Assert.True(floodPlain.causesDisease);
		Assert.True(floodPlain.diseaseCanBeCured);
		Assert.True(jungle.causesDisease);
		Assert.False(jungle.diseaseCanBeCured);
		Assert.True(Terrain("marsh").causesDisease);
		Assert.False(Terrain("marsh").diseaseCanBeCured);
		Assert.False(grassland.causesDisease);
		Assert.False(Terrain("volcano").causesDisease);
		Assert.Equal(50, floodPlain.diseaseStrength);
		Assert.Equal("Sanitation", SanitationTech().Name);
	}

	// conquests.biq's terrain flags have 0xCC filler above the real bits.
	[Fact]
	public void TheImportReadsTheBiqDiseaseFlags() {
		TerrainType imported = TerrainType.ImportFromCiv3(4, new QueryCiv3.Biq.TERR { TerrainFlags = unchecked((int)0xCCCCCCCC), DiseaseStrength = 50 });
		Assert.True(imported.causesDisease);
		Assert.True(imported.diseaseCanBeCured);
		Assert.Equal(50, imported.diseaseStrength);

		TerrainType jungleImport = TerrainType.ImportFromCiv3(8, new QueryCiv3.Biq.TERR { TerrainFlags = unchecked((int)0xCCCCCCC4), DiseaseStrength = 50 });
		Assert.True(jungleImport.causesDisease);
		Assert.False(jungleImport.diseaseCanBeCured);

		TerrainType volcanoImport = TerrainType.ImportFromCiv3(10, new QueryCiv3.Biq.TERR { TerrainFlags = unchecked((int)0xCCCCCCC2), DiseaseStrength = 50 });
		Assert.False(volcanoImport.causesDisease);
	}

	[Fact]
	public void TheDiseaseFieldsSurviveASave() {
		floodPlain.diseaseStrength = 70;
		SaveGame save = SaveGame.FromGameData(gameData);
		SaveGame loaded = SaveGame.FromJSON(save.ToCompactJSON());
		floodPlain.diseaseStrength = 50;
		TerrainType reloaded = loaded.TerrainTypes.Single(t => t.Key == "flood plain");
		Assert.True(reloaded.causesDisease);
		Assert.True(reloaded.diseaseCanBeCured);
		Assert.Equal(70, reloaded.diseaseStrength);
	}

	// The Civ3 binary's roll: Rand(0x300 - strength * 512 / 100), 512 at
	// strength 50, for any unhealthy terrain in the radius, the strongest
	// counting.
	[Fact]
	public void UnhealthyTerrainInTheRadiusGivesTheCityARoll() {
		GrowTo(3);
		Assert.Equal(0, Disease.IllnessRange(city));
		city.location.overlayTerrainType = floodPlain;
		Assert.Equal(512, Disease.IllnessRange(city));
		// More unhealthy tiles don't add up.
		Working().tileWorked.overlayTerrainType = jungle;
		Assert.Equal(512, Disease.IllnessRange(city));
		// The strongest counts.
		jungle.diseaseStrength = 100;
		try {
			Assert.Equal(0x300 - 512, Disease.IllnessRange(city));
		} finally {
			jungle.diseaseStrength = 50;
		}
	}

	// Sanitation's flag cures flood plains, as intended, not tech number 8
	// (Writing) as in the shipped game (owner's decision, 2026-10-20).
	[Fact]
	public void SanitationCuresFloodPlainsButNotJungle() {
		Tech cure = Disease.CuringTech(gameData);
		Assert.Equal("Sanitation", cure.Name);
		GrowTo(3);
		city.location.overlayTerrainType = floodPlain;
		Tech writing = gameData.techs[8];
		Assert.Equal("Writing", writing.Name);
		player.knownTechs.Add(writing.id);
		Assert.True(Disease.IsUnhealthy(floodPlain, player));
		Assert.Equal(512, Disease.IllnessRange(city));
		player.knownTechs.Add(cure.id);
		Assert.False(Disease.IsUnhealthy(floodPlain, player));
		Assert.Equal(0, Disease.IllnessRange(city));
		Assert.True(Disease.IsUnhealthy(jungle, player));
	}

	// Not working the unhealthy tile doesn't protect the city.
	[Fact]
	public void UnworkedUnhealthyTilesCount() {
		GrowTo(2);
		Tile unworked = city.location.neighbors.Values.First(t => t != Tile.NONE && !t.IsWater() && t.personWorkingTile == null);
		unworked.overlayTerrainType = jungle;
		Assert.Equal(512, Disease.IllnessRange(city));
	}

	[Fact]
	public void DiseaseKillsACitizenWorkingTheUnhealthyTile() {
		GrowTo(3);
		CityResident sick = Working();
		sick.tileWorked.overlayTerrainType = jungle;
		Tile jungleTile = sick.tileWorked;

		Disease.Strike(city);

		Assert.Equal(2, city.residents.Count);
		Assert.DoesNotContain(sick, city.residents);
		Assert.Null(jungleTile.personWorkingTile);
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgShowDomesticAdvisorPopup>(), m => m.message == $"Disease strikes {city.name}.");
	}

	// A city falls ill when Rand(512) < size, loses a citizen that turn
	// and every turn it stays ill, and recovers when Rand(128) > size.
	[Fact]
	public void AnIllCityLosesACitizenEveryTurnUntilItRecovers() {
		GrowTo(6);
		city.location.overlayTerrainType = floodPlain;
		// A roll at its size spares it.
		C7GameData.GameData.rng = new FixedRandom(6);
		Assert.False(Disease.MaybeStrike(city, 10));
		Assert.False(city.ill);
		Assert.Equal(6, city.residents.Count);

		// A roll below its size: it falls ill and loses a citizen.
		C7GameData.GameData.rng = new FixedRandom(5);
		Assert.True(Disease.MaybeStrike(city, 10));
		Assert.True(city.ill);
		Assert.Equal(5, city.residents.Count);
		Assert.Equal(10, city.lastDiseaseTurn);

		// Ill, it loses another whatever the roll, and a recovery roll not
		// above its new size (4) keeps it ill.
		C7GameData.GameData.rng = new FixedRandom(4);
		Assert.True(Disease.MaybeStrike(city, 11));
		Assert.Equal(4, city.residents.Count);
		Assert.True(city.ill);

		// A recovery roll above its size cures it, after the citizen is lost.
		C7GameData.GameData.rng = new FixedRandom(4);
		Assert.True(Disease.MaybeStrike(city, 12));
		Assert.Equal(3, city.residents.Count);
		Assert.False(city.ill);
	}

	// Healthy terrain doesn't cure an ill city: it recovers only by the roll.
	[Fact]
	public void AnIllCityStaysIllWithoutUnhealthyTerrain() {
		GrowTo(4);
		city.ill = true;
		C7GameData.GameData.rng = new FixedRandom(0);
		Assert.True(Disease.MaybeStrike(city, 1));
		Assert.Equal(3, city.residents.Count);
		Assert.True(city.ill);
	}

	[Fact]
	public void IllnessSurvivesASave() {
		city.ill = true;
		SaveGame save = SaveGame.FromGameData(gameData);
		SaveGame loaded = SaveGame.FromJSON(save.ToCompactJSON());
		SaveCity saved = loaded.Cities.Single(c => c.name == city.name);
		Assert.True(saved.ill);
		Assert.False(saved.diseaseStrikesNextTurn);
	}

	// An older save's city that was to lose a second citizen loads ill.
	[Fact]
	public void AnOlderSavesSecondStrikeLoadsAsIllness() {
		SaveGame save = SaveGame.FromGameData(gameData);
		SaveCity saved = save.Cities.Single(c => c.name == city.name);
		saved.ill = false;
		saved.diseaseStrikesNextTurn = true;
		C7GameData.GameData loaded = save.ToGameData(fixture.behaviors);
		Assert.True(loaded.cities.Single(c => c.name == city.name).ill);
	}

	[Fact]
	public void TheLastStrikeSurvivesASave() {
		city.lastDiseaseTurn = 42;
		SaveGame save = SaveGame.FromGameData(gameData);
		SaveGame loaded = SaveGame.FromJSON(save.ToCompactJSON());
		Assert.Equal(42, loaded.Cities.Single(c => c.name == city.name).lastDiseaseTurn);
	}

	// A size 1 city may fall ill, but doesn't shrink.
	[Fact]
	public void ASizeOneCityNeverShrinks() {
		Assert.Single(city.residents);
		city.location.overlayTerrainType = floodPlain;
		C7GameData.GameData.rng = new FixedRandom(0);
		Assert.False(Disease.MaybeStrike(city, 1));
		Assert.True(city.ill);
		Assert.False(Disease.MaybeStrike(city, 2));
		Assert.Single(city.residents);
	}

	[Fact]
	public void AHealthyCityIsNeverStruckNorDrawsARandomNumber() {
		GrowTo(3);
		C7GameData.GameData.rng = new Random(7);
		int expected = new Random(7).Next();
		Assert.False(Disease.MaybeStrike(city, 1));
		Assert.Equal(3, city.residents.Count);
		Assert.Equal(expected, C7GameData.GameData.rng.Next());
	}

	// A size 3 city falls ill 3/512 of its healthy turns, and an outbreak
	// almost always costs two citizens (one each turn, then recovery at
	// 124/128).
	[Fact]
	public void IllnessStrikesAtTheBinarysRate() {
		GrowTo(3);
		city.location.overlayTerrainType = floodPlain;
		C7GameData.GameData.rng = new GameRandom(1234);
		int outbreaks = 0;
		int healthyTurns = 0;
		int lost = 0;
		for (int turn = 0; turn < 40000; ++turn) {
			bool wasIll = city.ill;
			if (!wasIll) {
				++healthyTurns;
			}
			int before = city.residents.Count;
			Disease.MaybeStrike(city, turn);
			lost += before - city.residents.Count;
			if (!wasIll && city.ill) {
				++outbreaks;
			}
			if (!city.ill) {
				GrowTo(3);
			}
		}
		double expected = healthyTurns * 3.0 / 512;
		Assert.InRange(outbreaks, expected * 0.8, expected * 1.2);
		Assert.InRange((double)lost / outbreaks, 2.0, 2.1);
	}

	private Tile JungleNextToTheCity() {
		Tile tile = city.location.neighbors.Values.First(t => t != Tile.NONE && t.IsLand() && !t.HasCity());
		tile.overlayTerrainType = jungle;
		return tile;
	}

	// Only fortified units die of disease, and not workers.
	[Fact]
	public void OnlyFortifiedUnitsInJungleCanDie() {
		Tile tile = JungleNextToTheCity();
		MapUnit unit = gameData.SpawnUnit(player, gameData.unitPrototypes.Single(p => p.name == "Warrior"), tile);
		Assert.False(Disease.CanStrikeUnit(unit));
		unit.isFortified = true;
		Assert.True(Disease.CanStrikeUnit(unit));

		MapUnit worker = gameData.SpawnUnit(player, gameData.unitPrototypes.Single(p => p.name == "Worker"), tile);
		worker.isFortified = true;
		Assert.False(Disease.CanStrikeUnit(worker));

		tile.overlayTerrainType = grassland;
		Assert.False(Disease.CanStrikeUnit(unit));
		tile.overlayTerrainType = jungle;

		// Flood plains and marsh don't kill units, only jungle (Civ3
		// binary, Unit_StartOfTurn).
		tile.overlayTerrainType = floodPlain;
		Assert.False(Disease.CanStrikeUnit(unit));
		tile.overlayTerrainType = Terrain("marsh");
		Assert.False(Disease.CanStrikeUnit(unit));
		tile.overlayTerrainType = jungle;

		// A roll of 0 in 1000 kills it.
		C7GameData.GameData.rng = new FixedRandom(0);
		Assert.Contains(unit, Disease.StrikeFortifiedUnits(player));
		Assert.DoesNotContain(unit, player.units);
		Assert.Contains(worker, player.units);
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgShowMilitaryAdvisorPopup>(), m => m.message == "Our Warrior has died of disease from the jungle!");
	}

	// The chance is 1 in 1000 whatever the jungle's disease strength.
	[Fact]
	public void ARollAboveZeroSparesTheUnit() {
		MapUnit unit = gameData.SpawnUnit(player, gameData.unitPrototypes.Single(p => p.name == "Warrior"), JungleNextToTheCity());
		unit.isFortified = true;
		jungle.diseaseStrength = 1000;
		C7GameData.GameData.rng = new FixedRandom(1);
		try {
			Assert.Empty(Disease.StrikeFortifiedUnits(player));
		} finally {
			jungle.diseaseStrength = 50;
		}
		Assert.Contains(unit, player.units);
	}

	// Rolls the given number for every Next(max).
	private class FixedRandom(int roll) : Random {
		public override int Next(int maxValue) => Math.Min(roll, maxValue - 1);
		protected override double Sample() => 0.0;
	}
}
