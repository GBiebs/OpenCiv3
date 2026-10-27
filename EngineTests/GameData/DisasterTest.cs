using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Civ3's disasters: nuclear meltdowns, disease, volcanic eruptions, global
// warming and the plague (Civilopedia "Pollution", "Disease", "Volcanic
// Eruptions", "Plague"; CivFanatics for the meltdown's chance). Each
// undocumented number is a rule, which the tests set to make the outcome
// certain.
public class DisasterTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public DisasterTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Ground Zero");
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private TerrainType Terrain(string key) => gameData.terrainTypes.Find(t => t.Key == key);
	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);
	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);
	private TerrainImprovement Improvement(string key) => gameData.terrainImprovements.Find(i => i.key == key);

	private void Size(City c, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(t => t.IsDefaultCitizen);
		while (c.residents.Count < size) {
			c.AddCitizen(new CityResident { city = c, nationality = c.owner.civilization, citizenType = laborer });
		}
	}

	private static void Make(Tile tile, TerrainType baseTerrain, TerrainType overlay = null) {
		tile.baseTerrainType = baseTerrain;
		tile.overlayTerrainType = overlay ?? baseTerrain;
		tile.Resource = Resource.NONE;
		tile.ResourceKey = Resource.NONE.Key;
	}

	private Tile EmptyLandNear(Tile center, int distance) {
		return gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(center) == distance && gameData.cities.All(c => c == city || c.location.DistanceTo(t) >= 2));
	}

	private void LearnTheCure() {
		Tech sanitation = gameData.techs.First(t => t.HasFlag(SaveTech.Flag.DisablesDiseases));
		us.knownTechs.Add(sanitation.id);
	}

	// --- Meltdowns ---

	[Fact]
	public void ANuclearPlantMeltsDownAfterAWholeTurnOfDisorder() {
		Size(city, 11);
		city.AddBuilding(Building("Factory"), gameData);
		city.AddBuilding(Building("Nuclear Plant"), gameData);
		Assert.True(Building("Nuclear Plant").canMeltdown);
		gameData.rules.MeltdownChancePercent = 100;

		// Not on the turn the disorder starts.
		city.turnsInDisorder = 1;
		Pollution.CheckMeltdowns(gameData, us);
		Assert.Equal(11, city.residents.Count);

		city.turnsInDisorder = 2;
		Pollution.CheckMeltdowns(gameData, us);
		Assert.Equal(6, city.residents.Count);
		Assert.All(city.location.neighbors.Values.Where(t => t.IsLand()), t => Assert.True(t.HasPollution()));
		Assert.False(city.location.HasPollution());
		Assert.Contains(city.GetBuildings(), cb => cb.building.name == "Nuclear Plant");
		Assert.Equal(city, Assert.Single(EngineStorage.messagesToUI.OfType<MsgMeltdown>()).city);

		// No plant, no meltdown; and the chance is a rule.
		city.turnsInDisorder = 0;
		City other = CityInteractions.BuildCity(EmptyLandNear(city.location, 5), us, "Coal Town");
		Size(other, 8);
		other.turnsInDisorder = 5;
		Pollution.CheckMeltdowns(gameData, us);
		Assert.Equal(8, other.residents.Count);
		gameData.rules.MeltdownChancePercent = 0;
		city.turnsInDisorder = 5;
		Pollution.CheckMeltdowns(gameData, us);
		Assert.Equal(6, city.residents.Count);
	}

	[Fact]
	public void TurnsOfDisorderAreCountedAndSaved() {
		Size(city, 14);
		us.luxuryRate = 0;
		us.RecalculateCitizenMoods(gameData, goIntoDisorderIfUnhappy: true);
		Assert.True(city.isInCivilDisorder);
		Assert.Equal(1, city.turnsInDisorder);
		us.RecalculateCitizenMoods(gameData, goIntoDisorderIfUnhappy: true);
		Assert.Equal(2, city.turnsInDisorder);
		// Checking moods during the turn doesn't count.
		us.RecalculateCitizenMoods(gameData);
		us.RecalculateCitizenMoods(gameData, goIntoDisorderIfUnhappy: true);
		Assert.Equal(3, city.turnsInDisorder);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(3, reloaded.cities.Find(c => c.id == city.id).turnsInDisorder);

		city.RemoveCitizens(12);
		us.RecalculateCitizenMoods(gameData, goIntoDisorderIfUnhappy: true);
		Assert.Equal(0, city.turnsInDisorder);
	}

	// --- Disease ---

	[Fact]
	public void TheRulesetsUnhealthyTerrain() {
		Assert.Equal(["flood plain", "jungle", "marsh"], gameData.terrainTypes.Where(t => t.causesDisease).Select(t => t.Key).Order());
		Assert.Equal(["flood plain"], gameData.terrainTypes.Where(t => t.diseaseCuredByTech).Select(t => t.Key));
		Assert.All(gameData.terrainTypes.Where(t => t.causesDisease), t => Assert.Equal(50, t.diseaseStrength));
	}

	[Fact]
	public void CitizensWorkingUnhealthyLandFallIll() {
		Size(city, 3);
		Assert.Equal(0, Disease.CityChance(city, gameData));
		Tile jungle = EmptyLandNear(city.location, 1);
		Make(jungle, Terrain("grassland"), Terrain("jungle"));
		CityResident worker = city.residents[0];
		if (worker.tileWorked != null) {
			worker.tileWorked.personWorkingTile = null;
		}
		worker.tileWorked = jungle;
		jungle.personWorkingTile = worker;
		// Strength 50 × 10% = 5% for each citizen working an unhealthy tile.
		Assert.Equal(0.05, Disease.CityChance(city, gameData), 9);

		gameData.rules.CityDiseaseChancePerStrength = 200;
		Disease.UpdateForNewTurn(gameData, us);
		Assert.Equal(2, city.residents.Count);
		MsgDisease told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgDisease>());
		Assert.Equal((city, "Jungle"), (told.city, told.terrain));

		// Never the last citizen.
		City hamlet = CityInteractions.BuildCity(EmptyLandNear(city.location, 5), us, "Hamlet");
		Tile marsh = hamlet.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		Make(marsh, Terrain("grassland"), Terrain("marsh"));
		hamlet.residents[0].tileWorked.personWorkingTile = null;
		hamlet.residents[0].tileWorked = marsh;
		Disease.UpdateForNewTurn(gameData, us);
		Assert.Single(hamlet.residents);
	}

	// "Floodplains continue to be risky until you discover sanitation";
	// jungle and marsh until they're cleared.
	[Fact]
	public void SanitationCuresFloodPlainsButNotJungle() {
		Tile floodPlain = EmptyLandNear(city.location, 2);
		Make(floodPlain, Terrain("flood plain"));
		Tile jungle = EmptyLandNear(city.location, 3);
		Make(jungle, Terrain("grassland"), Terrain("jungle"));
		Tile grass = EmptyLandNear(city.location, 4);
		Make(grass, Terrain("grassland"));
		Assert.True(Disease.IsDiseased(floodPlain, us));
		Assert.True(Disease.IsDiseased(jungle, us));
		Assert.False(Disease.IsDiseased(grass, us));
		LearnTheCure();
		Assert.False(Disease.IsDiseased(floodPlain, us));
		Assert.True(Disease.IsDiseased(jungle, us));
	}

	// "Military units that fortify in these terrains are susceptible."
	[Fact]
	public void FortifiedSoldiersOutInTheJungleCanDie() {
		Tile jungle = EmptyLandNear(city.location, 2);
		Make(jungle, Terrain("grassland"), Terrain("jungle"));
		MapUnit spearman = gameData.SpawnUnit(us, Type("Spearman"), jungle);
		MapUnit worker = gameData.SpawnUnit(us, Type("Worker"), jungle);
		Assert.Equal(0, Disease.UnitChance(spearman, gameData));
		spearman.isFortified = true;
		worker.isFortified = true;
		// Strength 50 × 2% = 1% a turn.
		Assert.Equal(0.01, Disease.UnitChance(spearman, gameData), 9);
		Assert.Equal(0, Disease.UnitChance(worker, gameData));
		MapUnit garrison = gameData.SpawnUnit(us, Type("Spearman"), city.location);
		garrison.isFortified = true;
		Make(city.location, Terrain("flood plain"));
		Assert.Equal(0, Disease.UnitChance(garrison, gameData));

		gameData.rules.UnitDiseaseChancePerStrength = 200;
		Disease.UpdateForNewTurn(gameData, us);
		Assert.DoesNotContain(spearman, gameData.mapUnits);
		Assert.Contains(worker, gameData.mapUnits);
		Assert.Contains(garrison, gameData.mapUnits);
		MsgDisease told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgDisease>());
		Assert.Equal((spearman, jungle), (told.unit, told.tile));
		Assert.Equal("Our Spearman has died of disease from the jungle!", told.Text());
	}

	// --- Volcanoes ---

	[Fact]
	public void AVolcanoSmokesThenErupts() {
		Tile volcano = EmptyLandNear(city.location, 1);
		Make(volcano, Terrain("volcano"));
		Size(city, 6);
		List<Tile> around = volcano.neighbors.Values.Where(t => t.IsLand() && !t.HasCity() && !t.IsVolcano()).ToList();
		Tile camp = around.First();
		Make(camp, Terrain("grassland"));
		MapUnit warrior = gameData.SpawnUnit(us, Type("Warrior"), camp);
		Tile farm = around.Last();
		Make(farm, Terrain("grassland"));
		farm.overlays.Add(Improvement(Tile.TileOverlays.ROAD));
		farm.overlays.Add(Improvement("irrigation"));
		MapUnit garrison = gameData.SpawnUnit(us, Type("Warrior"), city.location);
		us.tileKnowledge.AddTilesToKnown(volcano);

		gameData.rules.VolcanoEruptionPeriod = 1;
		gameData.rules.VolcanoWarningPercent = 100;
		gameData.rules.VolcanoWarningTurns = 1;
		gameData.rules.VolcanoTileHitPercent = 100;
		Assert.Equal(1, Volcanoes.EruptionChance(gameData));
		Volcanoes.UpdateForNewTurn(gameData);
		Assert.Equal(gameData.turn + 1, volcano.volcanoEruptsOnTurn);
		Assert.Contains(warrior, gameData.mapUnits);
		Assert.Equal(MsgVolcano.Event.Active, Assert.Single(EngineStorage.messagesToUI.OfType<MsgVolcano>().Where(m => m.playerId == us.id && m.tile == volcano)).what);

		// Smoking survives a save.
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(gameData.turn + 1, reloaded.map.tileAt(volcano.XCoordinate, volcano.YCoordinate).volcanoEruptsOnTurn);

		++gameData.turn;
		Volcanoes.UpdateForNewTurn(gameData);
		Assert.Equal(-1, volcano.volcanoEruptsOnTurn);
		Assert.DoesNotContain(warrior, gameData.mapUnits);
		Assert.Empty(farm.overlays.GetManMadeImprovements());
		Assert.All(around, t => Assert.True(t.HasPollution()));
		// The city is torched, half its people dead; its garrison sheltered.
		Assert.Equal(3, city.residents.Count);
		Assert.Contains(garrison, gameData.mapUnits);
		List<MsgVolcano.Event> told = EngineStorage.messagesToUI.OfType<MsgVolcano>().Where(m => m.playerId == us.id && around.Append(volcano).Append(city.location).Contains(m.tile)).Select(m => m.what).ToList();
		Assert.Equal([MsgVolcano.Event.Active, MsgVolcano.Event.Erupted], told.Take(2));
		Assert.Contains(MsgVolcano.Event.KilledUnits, told);
		Assert.Contains(MsgVolcano.Event.TorchedCity, told);
	}

	[Fact]
	public void EachTurnsYearsAreTheChanceOfAnEruption() {
		gameData.turn = 1;
		int years = Volcanoes.YearsThisTurn(gameData);
		Assert.True(years > 1);
		Assert.Equal((double)years / 5000, Volcanoes.EruptionChance(gameData), 9);
		gameData.rules.VolcanoEruptionPeriod = 0;
		Assert.Equal(0, Volcanoes.EruptionChance(gameData));
	}

	// --- Global warming ---

	[Fact]
	public void PollutionBeyondTheThresholdBringsWarming() {
		Assert.Equal(0, GlobalWarming.Chance(gameData));
		int threshold = GlobalWarming.Threshold(gameData);
		Assert.True(threshold > 0);
		TerrainImprovement pollution = Improvement(Tile.TileOverlays.POLLUTION);
		foreach (Tile t in gameData.map.tiles.Where(t => t.IsLand() && t.overlays.CanAdd(t, pollution)).Take(threshold + 10)) {
			t.overlays.Add(pollution);
		}
		Assert.Equal(0.10, GlobalWarming.Chance(gameData), 9);
	}

	[Fact]
	public void WarmingDriesTheLandAndClearsTheForests() {
		Tile plains = EmptyLandNear(city.location, 3);
		Make(plains, Terrain("plains"));
		foreach (Tile n in plains.neighbors.Values.Where(n => n.baseTerrainType.Key == "tundra")) {
			Make(n, Terrain("grassland"));
		}
		Assert.True(GlobalWarming.Change(gameData, plains));
		Assert.Equal(("desert", "desert"), (plains.baseTerrainType.Key, plains.overlayTerrainType.Key));
		MsgGlobalWarming told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgGlobalWarming>());
		Assert.Equal("Global Warming has turned Plains into Desert!", told.Text());
		// The tile and those below it get new textures.
		Civ3ExtraInfo before = new() { BaseTerrainFileID = plains.ExtraInfo.BaseTerrainFileID, BaseTerrainImageID = plains.ExtraInfo.BaseTerrainImageID };
		TerrainTextureFiles.AssignTextureDetails(new Random(1), gameData.terrainTypes, plains);
		Assert.Equal((before.BaseTerrainFileID, before.BaseTerrainImageID), (plains.ExtraInfo.BaseTerrainFileID, plains.ExtraInfo.BaseTerrainImageID));

		Tile forest = EmptyLandNear(city.location, 4);
		Make(forest, Terrain("grassland"), Terrain("forest"));
		Assert.True(GlobalWarming.Change(gameData, forest));
		Assert.Equal(("grassland", "grassland"), (forest.baseTerrainType.Key, forest.overlayTerrainType.Key));
		Assert.Equal("Global Warming has destroyed Forest!", EngineStorage.messagesToUI.OfType<MsgGlobalWarming>().Last().Text());
		Assert.True(GlobalWarming.Change(gameData, forest));
		Assert.Equal("plains", forest.overlayTerrainType.Key);

		// Spared: tundra, marsh (rising coastlines aren't done), cities.
		Tile tundra = EmptyLandNear(city.location, 5);
		Make(tundra, Terrain("tundra"));
		Assert.False(GlobalWarming.Change(gameData, tundra));
		Tile marsh = EmptyLandNear(city.location, 6);
		Make(marsh, Terrain("grassland"), Terrain("marsh"));
		Assert.Null(GlobalWarming.ResultFor(marsh, gameData));
		Make(city.location, Terrain("grassland"));
		Assert.Null(GlobalWarming.ResultFor(city.location, gameData));
	}

	[Fact]
	public void WarmingStrikesWhereThePollutionIs() {
		Tile grass = EmptyLandNear(city.location, 3);
		Make(grass, Terrain("grassland"));
		foreach (Tile n in grass.neighbors.Values.Where(n => n.baseTerrainType.Key == "tundra")) {
			Make(n, Terrain("grassland"));
		}
		grass.overlays.Add(Improvement(Tile.TileOverlays.POLLUTION));
		gameData.rules.GlobalWarmingThresholdPerThousandLandTiles = 0;
		gameData.rules.GlobalWarmingChancePerExcessTile = 100;
		GlobalWarming.UpdateForNewTurn(gameData);
		Assert.Equal("plains", grass.overlayTerrainType.Key);
	}

	// --- Plague ---

	private void PlagueRules(bool enabled = true) {
		gameData.rules.plague = new PlagueRules {
			enabled = enabled,
			name = "Black Death",
			earliestStartYear = -100000,
			chancePercent = 100,
			durationYears = 30,
			strength = 100,
			graceYears = 200,
			maxOutbreaks = 2,
		};
	}

	[Fact]
	public void TheStandardRulesHaveNoPlague() {
		Assert.False(gameData.rules.plague.enabled);
		Plague.UpdateForNewTurn(gameData);
		Assert.False(Plague.Active(gameData));
		PlagueRules(enabled: false);
		Plague.UpdateForNewTurn(gameData);
		Assert.False(Plague.Active(gameData));
	}

	[Fact]
	public void BigWalledTradingCitiesSufferMostAndSanitationHelps() {
		PlagueRules();
		gameData.rules.plague.strength = 80;
		Size(city, 3);
		Assert.Equal(0.2, Plague.CityChance(gameData, city), 9);
		Size(city, 8);
		city.AddBuilding(Building("Walls"), gameData);
		Assert.Equal(0.6, Plague.CityChance(gameData, city), 9);
		LearnTheCure();
		Assert.Equal(0.3, Plague.CityChance(gameData, city), 9);
	}

	[Fact]
	public void AnOutbreakKillsThenEndsAndComesBackAfterTheGracePeriod() {
		PlagueRules();
		Size(city, 4);
		MapUnit warrior = gameData.SpawnUnit(us, Type("Warrior"), city.location);
		gameData.turn = 10;
		gameData.rules.plague.strength = 0;
		Plague.UpdateForNewTurn(gameData);
		Assert.True(Plague.Active(gameData));
		Assert.Equal(1, gameData.plague.outbreaks);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgPlague>());
		// A strike certain to kill the units too.
		Plague.Strike(gameData, city, 4);
		MsgPlague told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgPlague>());
		Assert.StartsWith("Horrors! Black Death strikes Ground Zero!", told.Text());
		Assert.Equal(3, city.residents.Count);
		Assert.DoesNotContain(warrior, gameData.mapUnits);
		Assert.Contains("Warrior has been killed by Black Death!", told.Text());

		// It ends once its years are up; the grace period keeps it away.
		int year = gameData.timeOptions.GetAbsoluteYear(gameData.turn);
		while (gameData.timeOptions.GetAbsoluteYear(gameData.turn) < year + 30) {
			++gameData.turn;
		}
		gameData.rules.plague.chancePercent = 0;
		Plague.UpdateForNewTurn(gameData);
		Assert.False(Plague.Active(gameData));
		gameData.rules.plague.chancePercent = 100;
		Assert.False(Plague.CanStart(gameData, gameData.timeOptions.GetAbsoluteYear(gameData.turn)));
		Assert.True(Plague.CanStart(gameData, gameData.plague.endedYear.Value + 200));

		// Saved, and at most the rules' outbreaks.
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal((1, gameData.plague.endedYear), (reloaded.plague.outbreaks, reloaded.plague.endedYear));
		gameData.plague.outbreaks = 2;
		Assert.False(Plague.CanStart(gameData, gameData.plague.endedYear.Value + 200));
	}

	// --- From Civ3's files ---

	[SkippableFact]
	public void Civ3sDataComesAlong() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// A random-map Civ3 game with the stock rules (the save tests download it).
		string file = Path.Combine(PathUtils.getDataPath("saves"), "12345.SAV");
		Skip.If(!File.Exists(file), "The sample save isn't downloaded.");
		SaveGame save = ImportCiv3.ImportSav(file, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.Equal(["flood plain", "jungle", "marsh"], save.TerrainTypes.Where(t => t.causesDisease).Select(t => t.Key).Order());
		Assert.Equal(["flood plain"], save.TerrainTypes.Where(t => t.diseaseCuredByTech).Select(t => t.Key));
		Dictionary<string, string> warming = save.TerrainTypes.Where(t => t.globalWarmingResult != "").ToDictionary(t => t.Key, t => t.globalWarmingResult);
		Assert.Equal(new Dictionary<string, string> {
			["plains"] = "desert",
			["grassland"] = "plains",
			["forest"] = "base",
			["jungle"] = "base",
			["marsh"] = "coast",
		}, warming);
		Assert.Equal(5000, save.Rules.VolcanoEruptionPeriod);
		Assert.False(save.Rules.plague.enabled);
		Assert.Contains(save.Buildings, b => b.name == "Nuclear Plant" && b.flags.Contains(SaveBuilding.Flag.CanMeltdown));
	}

	// The turn's disasters run at its end.
	[Fact]
	public async Task TheTurnRunsThem() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		Tile volcano = game.map.tiles.First(t => t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0);
		volcano.baseTerrainType = volcano.overlayTerrainType = game.terrainTypes.Find(t => t.Key == "volcano");
		volcano.volcanoEruptsOnTurn = game.turn + 1;

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();
		Assert.Equal(-1, volcano.volcanoEruptsOnTurn);
	}
}
