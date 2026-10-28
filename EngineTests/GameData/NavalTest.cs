using System;
using System.IO;
using System.Linq;
using C7Engine;
using C7Engine.Pathing;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Ships that sink at sea: a galley ending its turn in sea or ocean, or a
// caravel in ocean, sinks half the time (a quarter for a Seafaring civ),
// with its cargo; with the Great Lighthouse, galleys are safe at sea, never
// in ocean.
public class NavalTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Tile coast;
	private readonly Tile sea;
	private readonly Tile ocean;

	public NavalTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.civilization.traits.Remove(Civilization.Trait.Seafaring);
		coast = gameData.map.tiles.First(t => t.IsCoast() && t.unitsOnTile.Count == 0);
		sea = gameData.map.tiles.First(t => t.IsSea() && t.unitsOnTile.Count == 0);
		ocean = gameData.map.tiles.First(t => t.baseTerrainType.isOcean() && t.unitsOnTile.Count == 0);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private void GiveUsTheGreatLighthouse() {
		City city = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Port");
		city.AddBuilding(gameData.Buildings.Find(b => b.HasWonderFlag(SaveBuilding.WonderFlag.SafeSeaTravel)), gameData);
	}

	[Fact]
	public void GalleysAndCaravelsAreInDangerOutOfTheirWaters() {
		UnitPrototype galley = Type("Galley");
		UnitPrototype caravel = Type("Caravel");
		Assert.False(Naval.InDanger(galley, coast, safeAtSea: false));
		Assert.True(Naval.InDanger(galley, sea, safeAtSea: false));
		Assert.True(Naval.InDanger(galley, ocean, safeAtSea: false));
		Assert.False(Naval.InDanger(caravel, sea, safeAtSea: false));
		Assert.True(Naval.InDanger(caravel, ocean, safeAtSea: false));
		Assert.False(Naval.InDanger(Type("Frigate"), ocean, safeAtSea: false));

		// The Great Lighthouse: safe at sea, not in ocean.
		Assert.False(Naval.InDanger(galley, sea, safeAtSea: true));
		Assert.True(Naval.InDanger(galley, ocean, safeAtSea: true));
	}

	[Fact]
	public void AShipInDangerSinksWithItsCargo() {
		us.rules.naval.sinkPercent = 100;
		MapUnit galley = gameData.SpawnUnit(us, Type("Galley"), sea);
		MapUnit warrior = gameData.SpawnUnit(us, Type("Warrior"), sea);
		warrior.loadedOnUnitId = galley.id;
		MapUnit safe = gameData.SpawnUnit(us, Type("Galley"), coast);

		TurnHandling.OnEndTurn(us);
		Assert.DoesNotContain(galley, us.units);
		Assert.DoesNotContain(warrior, us.units);
		Assert.Contains(safe, us.units);
		MsgShipSank sank = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShipSank>());
		Assert.Equal(us.id, sank.playerId);
		Assert.Equal(sea, sank.location);
		Assert.Equal(1, sank.cargo);
	}

	[Fact]
	public void TheChanceIsTheRules() {
		us.rules.naval.sinkPercent = 0;
		MapUnit galley = gameData.SpawnUnit(us, Type("Galley"), ocean);
		Naval.SinkShips(gameData, us);
		Assert.Contains(galley, us.units);

		// A Seafaring civ has its own chance.
		us.rules.naval.sinkPercent = 100;
		us.rules.naval.seafaringSinkPercent = 0;
		us.civilization.traits.Add(Civilization.Trait.Seafaring);
		Naval.SinkShips(gameData, us);
		Assert.Contains(galley, us.units);
		Assert.Equal(0, Naval.SinkPercent(us));
		us.civilization.traits.Remove(Civilization.Trait.Seafaring);
		Assert.Equal(100, Naval.SinkPercent(us));
	}

	[Fact]
	public void HalfSinkAndAQuarterForSeafarers() {
		Assert.Equal(50, Naval.SinkPercent(us));
		us.civilization.traits.Add(Civilization.Trait.Seafaring);
		Assert.Equal(25, Naval.SinkPercent(us));
		us.civilization.traits.Remove(Civilization.Trait.Seafaring);

		int sank = 0;
		for (int i = 0; i < 400; i++) {
			gameData.SpawnUnit(us, Type("Curragh"), ocean);
		}
		int before = us.units.Count;
		Naval.SinkShips(gameData, us);
		sank = before - us.units.Count;
		Assert.InRange(sank, 160, 240);
	}

	[Fact]
	public void TheGreatLighthouseKeepsGalleysSafeAtSea() {
		us.rules.naval.sinkPercent = 100;
		GiveUsTheGreatLighthouse();
		MapUnit atSea = gameData.SpawnUnit(us, Type("Galley"), sea);
		MapUnit inOcean = gameData.SpawnUnit(us, Type("Galley"), ocean);
		Naval.SinkShips(gameData, us);
		Assert.Contains(atSea, us.units);
		Assert.DoesNotContain(inOcean, us.units);
	}

	[Fact]
	public void TheAIKeepsItsGalleysOutOfDanger() {
		MapUnit galley = gameData.SpawnUnit(us, Type("Galley"), coast);
		Tile seaNext = coast.neighbors.Values.FirstOrDefault(t => t.IsSea());
		Assert.NotNull(seaNext);
		Assert.DoesNotContain(new UnitWalker(galley).getEdges(coast), e => e.current == seaNext);

		// Ships that don't sink, and a human's galleys, go anywhere.
		MapUnit frigate = gameData.SpawnUnit(us, Type("Frigate"), coast);
		Assert.Contains(new UnitWalker(frigate).getEdges(coast), e => e.current == seaNext);
		Player human = gameData.players.First(p => p.isHuman);
		human.tileKnowledge.AddTilesToKnown(seaNext);
		Assert.True(human.HasExploredTile(seaNext));
		MapUnit theirs = gameData.SpawnUnit(human, Type("Galley"), coast);
		Assert.Contains(new UnitWalker(theirs).getEdges(coast), e => e.current == seaNext);
	}
}

// The sinking flags come from Civ3's PRTO.
public class NavalImportTest {
	[SkippableFact]
	public void ShipsThatSinkComeFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "6 Age of Discovery.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		BiqData biq = BiqData.LoadFile(scenario);
		foreach (QueryCiv3.Biq.PRTO prto in biq.Prto) {
			SaveUnitPrototype unit = save.UnitPrototypes.Find(u => u.name == prto.Name);
			Assert.Equal(prto.SinkInSea, unit.flags.Contains(SaveUnitPrototype.Flag.SinksInSea));
			Assert.Equal(prto.SinkInOcean, unit.flags.Contains(SaveUnitPrototype.Flag.SinksInOcean));
		}
		Assert.Contains(biq.Prto, p => p.SinkInOcean);
	}
}
