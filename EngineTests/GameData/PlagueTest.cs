using System;
using System.IO;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// A scenario's plague (see Plague): off in the standard rules, and when a
// scenario permits it, outbreaks from its earliest start that strike cities.
public class PlagueTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public PlagueTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler));
		// One human, so that it isn't a multiplayer game.
		foreach (Player p in gameData.players) {
			p.isHuman = p == us;
		}
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private void PermitPlague(int strength = 100, int start = -4000, int variance = 0, int duration = 100, int grace = 200, int max = 2) {
		Rules r = gameData.rules;
		r.PermitPlagues = true;
		r.PlagueName = "The Black Death";
		r.PlagueStrength = strength;
		r.PlagueEarliestStart = start;
		r.PlagueVariance = variance;
		r.PlagueDuration = duration;
		r.PlagueGracePeriod = grace;
		r.PlagueMaxOccurrences = max;
	}

	private City BigCity(int size) {
		City city = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Pestville");
		Grow(city, size);
		return city;
	}

	private static void Grow(City city, int size) {
		while (city.residents.Count < size) {
			city.AddCitizen(new CityResident { city = city, citizenType = city.residents[0].citizenType, tileWorked = city.residents[0].tileWorked });
		}
	}

	[Fact]
	public void TheStandardRulesHaveNoPlague() {
		Assert.False(gameData.rules.PermitPlagues);
		City city = BigCity(8);
		for (int i = 0; i < 50; ++i) {
			Assert.Empty(Plague.ProcessEndOfRound(gameData));
		}
		Assert.Equal(0, gameData.plague.outbreaks);
		Assert.Equal(8, city.residents.Count);
	}

	[Fact]
	public void AnOutbreakComesAtTheEarliestStartAndStrikesCities() {
		City city = BigCity(8);
		int year = Plague.Year(gameData, gameData.turn);
		PermitPlague(start: year + 1);

		Plague.ProcessEndOfRound(gameData);
		Assert.False(Plague.Active(gameData));

		gameData.turn++;
		int struck = 0;
		for (int i = 0; i < 20 && city.residents.Count > 1; ++i) {
			struck += Plague.ProcessEndOfRound(gameData).Count;
			Assert.True(Plague.Active(gameData));
		}
		Assert.Equal(1, gameData.plague.outbreaks);
		Assert.True(struck > 0);
		// A strike costs a citizen each time, never the last.
		Assert.Equal(Math.Max(1, 8 - struck), city.residents.Count);
		bool told = false;
		while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI m)) {
			told |= m is MsgShowDomesticAdvisorPopup p && p.message == "Horrors! The Black Death strikes Pestville!";
		}
		Assert.True(told);
	}

	[Fact]
	public void AnOutbreakEndsAfterItsDurationAndTheNextWaitsAGracePeriod() {
		BigCity(1);
		int year = Plague.Year(gameData, gameData.turn);
		PermitPlague(start: year, duration: 1, grace: 400, max: 2);

		Plague.ProcessEndOfRound(gameData);
		Assert.True(Plague.Active(gameData));
		gameData.turn++;
		Plague.ProcessEndOfRound(gameData);
		Assert.False(Plague.Active(gameData));
		int ended = Plague.Year(gameData, gameData.turn);
		// A grace period of 400 years, ±50%.
		Assert.InRange(gameData.plague.nextStartYear.Value, ended + 200, ended + 600);

		// The second and last outbreak, then no more.
		gameData.plague.nextStartYear = ended;
		Plague.ProcessEndOfRound(gameData);
		Assert.Equal(2, gameData.plague.outbreaks);
		for (int i = 0; i < 3; ++i) {
			gameData.turn++;
			Plague.ProcessEndOfRound(gameData);
		}
		Assert.False(Plague.Active(gameData));
		gameData.plague.nextStartYear = ended;
		gameData.turn++;
		Plague.ProcessEndOfRound(gameData);
		Assert.Equal(2, gameData.plague.outbreaks);
		Assert.False(Plague.Active(gameData));
	}

	[Fact]
	public void BiggerWalledAndTradingCitiesAreStruckMoreAndSanitationHalvesIt() {
		City city = BigCity(1);
		PermitPlague(strength: 80);
		int small = Plague.ChancePerThousand(gameData, city);
		Assert.Equal(200, small);
		Grow(city, gameData.rules.MaximumLevel1CitySize + 1);
		int big = Plague.ChancePerThousand(gameData, city);
		Assert.Equal(400, big);

		Tech sanitation = Disease.CuringTech(gameData);
		Assert.NotNull(sanitation);
		us.knownTechs.Add(sanitation.id);
		Assert.Equal(200, Plague.ChancePerThousand(gameData, city));
	}

	[Fact]
	public void ThereIsNoPlagueInMultiplayerGames() {
		City city = BigCity(8);
		PermitPlague(start: Plague.Year(gameData, gameData.turn));
		foreach (Player p in gameData.players.Where(p => !p.isBarbarians)) {
			p.isHuman = true;
		}
		for (int i = 0; i < 10; ++i) {
			Plague.ProcessEndOfRound(gameData);
		}
		Assert.Equal(0, gameData.plague.outbreaks);
		Assert.Equal(8, city.residents.Count);
	}

	[Fact]
	public void AnOutbreakSurvivesASave() {
		PermitPlague(start: Plague.Year(gameData, gameData.turn));
		Plague.ProcessEndOfRound(gameData);
		Assert.True(Plague.Active(gameData));

		C7GameData.GameData loaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(loaded);
		Assert.True(loaded.rules.PermitPlagues);
		Assert.Equal("The Black Death", loaded.rules.PlagueName);
		Assert.Equal(1, loaded.plague.outbreaks);
		Assert.Equal(gameData.plague.endsYear, loaded.plague.endsYear);
	}

	// The scenarios' plague settings come from their BIQ GAME sections:
	// Middle Ages has the Black Death, Fall of Rome a plague, Mesopotamia
	// none.
	[SkippableFact]
	public void ScenariosPlagueSettingsAreImported() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		Rules middleAges = ImportScenario("4 Middle Ages.biq").Rules;
		Assert.True(middleAges.PermitPlagues);
		Assert.Equal("Black Death", middleAges.PlagueName);
		Assert.Equal((1346, 50, 50, 100, 100, 1), (middleAges.PlagueEarliestStart, middleAges.PlagueVariance, middleAges.PlagueDuration,
			middleAges.PlagueStrength, middleAges.PlagueGracePeriod, middleAges.PlagueMaxOccurrences));
		Rules fallOfRome = ImportScenario("3 Fall of Rome.biq").Rules;
		Assert.True(fallOfRome.PermitPlagues);
		Assert.Equal((350, 20, 10, 30, 50, 4), (fallOfRome.PlagueEarliestStart, fallOfRome.PlagueVariance, fallOfRome.PlagueDuration,
			fallOfRome.PlagueStrength, fallOfRome.PlagueGracePeriod, fallOfRome.PlagueMaxOccurrences));
		Assert.False(ImportScenario("1 Mesopotamia.biq").Rules.PermitPlagues);
	}

	private static SaveGame ImportScenario(string name) {
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		string scenarios = Path.Join(conquests, "Conquests");
		return ImportCiv3.ImportBiq(Path.Join(scenarios, name), PathUtils.defaultBicPath, modPath => {
			if (string.IsNullOrEmpty(modPath)) {
				return Path.GetFullPath(Path.Combine(conquests, "Text", "PediaIcons.txt"));
			}
			if (!OperatingSystem.IsWindows()) {
				modPath = modPath.Replace("\\conquests\\", "/Conquests/");
			}
			return Path.GetFullPath(Path.Combine(scenarios, modPath, "Text", "PediaIcons.txt"));
		});
	}
}
