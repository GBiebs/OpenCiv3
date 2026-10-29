using System;
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

// What transports carry (Civilopedia): a carrier aircraft (they board by
// re-basing), a missile sub tactical missiles, which fire from it at sea,
// and a helicopter foot soldiers boarding in a city, which it drops within
// its operational range.
public class CarriersTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City port;
	private readonly City abroad;

	public CarriersTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		abroad = CityInteractions.BuildCity(them.units.First(u => u.unitType.isSettler).location, them, "Abroad");
		Tile coast = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(abroad.location) is > 6 and < 12 && t.neighbors.Values.Any(n => Tile.IsTileValid(n) && n.IsWater()));
		port = CityInteractions.BuildCity(coast, us, "Port");
		foreach (Tech t in gameData.techs) us.knownTechs.Add(t.id);
		gameData.GreatWondersBuilt.Add("The Manhattan Project");
		us.DeclareWarOn(them, gameData.turn);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private MapUnit Spawn(string type, Tile tile, Player owner = null) => gameData.SpawnUnit(owner ?? us, gameData.unitPrototypes.Find(u => u.name == type), tile);

	private bool Boards(MapUnit unit, MapUnit transport) => unit.GetTransportsToBoard(unit.location).Contains(transport);

	[Fact]
	public void EachTransportTakesItsKind() {
		Tile at = port.location;
		MapUnit sub = Spawn("Nuclear Submarine", at), carrier = Spawn("Carrier", at), helicopter = Spawn("Helicopter", at), transport = Spawn("Transport", at);
		MapUnit warrior = Spawn("Warrior", at), tank = Spawn("Tank", at), nuke = Spawn("Tactical Nuke", at), cruise = Spawn("Cruise Missile", at);

		// A missile sub: tactical missiles only.
		Assert.True(Boards(nuke, sub));
		Assert.False(Boards(warrior, sub));
		Assert.False(Boards(cruise, sub));
		// A carrier: no land units (planes re-base onto it).
		Assert.False(Boards(warrior, carrier));
		Assert.False(Boards(nuke, carrier));
		// A helicopter: foot soldiers, in a city.
		Assert.True(Boards(warrior, helicopter));
		Assert.False(Boards(tank, helicopter));
		// A transport: land units.
		Assert.True(Boards(tank, transport));
		Assert.True(Boards(warrior, transport));

		Tile field = port.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		gameData.TeleportUnit(helicopter, field);
		gameData.TeleportUnit(warrior, field);
		Assert.False(Boards(warrior, helicopter));
	}

	[Fact]
	public void ANukeFiresFromItsSubmarine() {
		MapUnit sub = Spawn("Nuclear Submarine", port.location);
		MapUnit nuke = Spawn("Tactical Nuke", port.location);
		nuke.BoardTransport(sub);
		Assert.True(nuke.IsLoadedIn(sub));

		// Out at sea within reach of their city, the nuke goes with the sub.
		Tile sea = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsWater() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(abroad.location) <= nuke.unitType.bombardRange);
		gameData.TeleportUnit(sub, sea);
		Assert.Equal(sea, nuke.location);
		Assert.True(nuke.CanBombardTile(abroad.location));
		Assert.Equal(Nuclear.Outcome.Detonated, Nuclear.Strike(gameData, nuke, abroad.location));
		Assert.DoesNotContain(nuke, us.units);
		Assert.Empty(sub.Passengers());
	}

	[Fact]
	public void HelicoptersDropTheirSoldiers() {
		MapUnit helicopter = Spawn("Helicopter", port.location);
		MapUnit first = Spawn("Rifleman", port.location), second = Spawn("Marine", port.location);
		Assert.False(Air.CanAirdropNow(helicopter));
		first.BoardTransport(helicopter);
		second.BoardTransport(helicopter);
		Assert.True(Air.CanAirdropNow(helicopter));
		Assert.Contains(UnitAction.Airdrop, helicopter.GetAvailableActions());

		int range = helicopter.unitType.operationalRange;
		Tile target = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(port.location) == range);
		Tile tooFar = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(port.location) == range + 1);
		Assert.False(Air.CanAirdrop(helicopter, tooFar));
		Assert.False(Air.CanAirdrop(helicopter, abroad.location));
		Tile guarded = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(port.location) == range - 1);
		Assert.True(Air.CanAirdrop(helicopter, guarded));
		Spawn("Warrior", guarded, them);
		Assert.False(Air.CanAirdrop(helicopter, guarded));

		Assert.True(Air.Airdrop(gameData, helicopter, target));
		Assert.All([first, second], s => {
			Assert.Equal(target, s.location);
			Assert.False(s.IsLoaded());
			Assert.False(s.movementPoints.canMove);
		});
		Assert.Equal(port.location, helicopter.location);
		Assert.False(helicopter.movementPoints.canMove);
	}

	[Fact]
	public async Task HelicoptersCarryThemWhenTheyReBase() {
		City other = CityInteractions.BuildCity(gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& t.unitsOnTile.Count == 0 && t.DistanceTo(port.location) is >= 4 and <= 6 && gameData.cities.All(c => c.location.DistanceTo(t) >= 4)), us, "Other");
		MapUnit helicopter = Spawn("Helicopter", port.location);
		MapUnit soldier = Spawn("Rifleman", port.location);
		soldier.BoardTransport(helicopter);
		await Air.Fly(gameData, helicopter, Air.Mission.Rebase, other.location);
		Assert.Equal(other.location, soldier.location);
		Assert.True(soldier.IsLoadedIn(helicopter));
	}
}

// The foot soldier and tactical missile flags from Civ3's PRTO.
public class CarriersImportTest {
	[SkippableFact]
	public void TheFlagsComeFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "9 WWII in the Pacific.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		int foot = 0, missiles = 0;
		foreach (QueryCiv3.Biq.PRTO prto in BiqData.LoadFile(scenario).Prto) {
			SaveUnitPrototype unit = save.UnitPrototypes.Find(u => u.name == prto.Name);
			Assert.Equal(prto.FootSoldier, unit.flags.Contains(SaveUnitPrototype.Flag.FootSoldier));
			Assert.Equal(prto.TacticalMissile, unit.flags.Contains(SaveUnitPrototype.Flag.TacticalMissile));
			foot += prto.FootSoldier ? 1 : 0;
			missiles += prto.TacticalMissile ? 1 : 0;
		}
		Assert.True(foot > 0 && missiles > 0);
	}
}
