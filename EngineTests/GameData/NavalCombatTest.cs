using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI.UnitAI;
using C7GameData;
using C7GameData.AIData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Naval combat (Civilopedia): only amphibious units (Marines, Berserks)
// attack from ships; the Privateer attacks and is attacked without war;
// submarines are seen only by subs, Destroyers and AEGIS Cruisers; stealth
// attackers pick their target in a stack; a Coastal Fortress shoots at
// passing enemy ships.
public class NavalCombatTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	// A coast tile next to land, and that land tile.
	private readonly Tile water;
	private readonly Tile shore;

	public NavalCombatTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.EnsureRelationshipExists(them);
		water = gameData.map.tiles.First(t => t.IsCoast() && t.unitsOnTile.Count == 0 && t.neighbors.Values.All(Tile.IsTileValid)
			&& t.neighbors.Values.Count(n => n.IsLand() && !n.HasCity() && n.unitsOnTile.Count == 0) > 0
			&& t.neighbors.Values.Count(n => n.IsWater() && n.unitsOnTile.Count == 0) >= 3
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 4));
		shore = water.neighbors.Values.First(n => n.IsLand() && !n.HasCity() && n.unitsOnTile.Count == 0);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, Type(type), tile);

	private MapUnit Aboard(Player owner, string type, MapUnit ship) {
		MapUnit unit = Spawn(owner, type, ship.location);
		unit.loadedOnUnitId = ship.id;
		return unit;
	}

	// ---------------------------------------------------------------- amphibious

	[Fact]
	public void OnlyAmphibiousUnitsAttackFromShips() {
		us.DeclareWarOn(them, gameData.turn);
		MapUnit transport = Spawn(us, "Transport", water);
		MapUnit marine = Aboard(us, "Marine", transport);
		MapUnit rifleman = Aboard(us, "Rifleman", transport);
		Spawn(them, "Warrior", shore);

		Assert.True(marine.CanEnter(shore, out MapUnit.Intent intent));
		Assert.Equal(MapUnit.Intent.Fight, intent);
		Assert.False(rifleman.CanEnter(shore, out intent));
		Assert.Equal(MapUnit.Intent.Disabled, intent);

		// Empty land: anyone lands.
		Tile empty = water.neighbors.Values.FirstOrDefault(n => n.IsLand() && n != shore && !n.HasCity() && n.unitsOnTile.Count == 0);
		if (empty != null) {
			Assert.True(rifleman.CanEnter(empty));
		}

		// From land, anyone fights.
		MapUnit ashore = Spawn(us, "Rifleman", shore.neighbors.Values.First(n => n.IsLand() && n != shore && !n.HasCity() && n.unitsOnTile.Count == 0));
		Assert.True(ashore.CanEnter(shore, out intent));
		Assert.Equal(MapUnit.Intent.Fight, intent);
	}

	[Fact]
	public async Task AMarineStormsTheBeach() {
		us.DeclareWarOn(them, gameData.turn);
		MapUnit transport = Spawn(us, "Transport", water);
		MapUnit marine = Aboard(us, "Marine", transport);
		marine.unitType.attack = 1000;
		Spawn(them, "Warrior", shore);
		Assert.True(await marine.Move(water.DirectionTo(shore)));
		Assert.Empty(them.units.Where(u => u.location == shore));
	}

	[Fact]
	public void OnlyAmphibiousUnitsTakeACityFromShips() {
		us.DeclareWarOn(them, gameData.turn);
		City city = CityInteractions.BuildCity(shore, them, "Harbor Town");
		MapUnit transport = Spawn(us, "Transport", water);
		MapUnit rifleman = Aboard(us, "Rifleman", transport);
		MapUnit marine = Aboard(us, "Marine", transport);
		Assert.False(rifleman.CanEnter(city.location));
		Assert.True(marine.CanEnter(city.location));
	}

	// ---------------------------------------------------------------- privateers

	[Fact]
	public async Task APrivateerAttacksWithoutWar() {
		Tile next = water.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0);
		MapUnit privateer = Spawn(us, "Privateer", water);
		privateer.unitType.attack = 1000;
		MapUnit galleon = Spawn(them, "Galleon", next);
		Assert.True(us.IsAtPeaceWith(them));

		Assert.True(privateer.CanEnter(next, out MapUnit.Intent intent));
		Assert.Equal(MapUnit.Intent.Fight, intent);
		await privateer.Move(water.DirectionTo(next));
		Assert.DoesNotContain(galleon, them.units);
		Assert.True(us.IsAtPeaceWith(them));

		// Another ship of ours would have to declare war.
		MapUnit frigate = Spawn(us, "Frigate", water);
		Spawn(them, "Galleon", next.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0 && n.DistanceTo(water) == 1 && n != next));
		Tile theirs = them.units.Last().location;
		Assert.False(frigate.CanEnter(theirs, out intent));
		Assert.Equal(MapUnit.Intent.WarDeclaration, intent);
	}

	[Fact]
	public void AnyoneMayAttackAPrivateer() {
		Tile next = water.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0);
		Spawn(them, "Privateer", next);
		MapUnit frigate = Spawn(us, "Frigate", water);
		Assert.True(frigate.CanEnter(next, out MapUnit.Intent intent));
		Assert.Equal(MapUnit.Intent.Fight, intent);
	}

	[Fact]
	public void APrivateerDoesntRaidCitiesOrLand() {
		City city = CityInteractions.BuildCity(shore, them, "Port");
		Spawn(them, "Galleon", shore);
		MapUnit privateer = Spawn(us, "Privateer", water);
		Assert.False(Naval.FightsWithoutWar(privateer, city.location));
	}

	// ---------------------------------------------------------------- submarines

	[Fact]
	public void SubmarinesAreSeenOnlyByThoseThatCan() {
		MapUnit sub = Spawn(them, "Submarine", water);
		Assert.True(Naval.IsVisibleTo(sub, them));
		Assert.False(Naval.IsVisibleTo(sub, us));

		// A Frigate next to it doesn't see it; a Destroyer does.
		Tile next = water.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0);
		Spawn(us, "Frigate", next);
		Assert.False(Naval.IsVisibleTo(sub, us));
		MapUnit destroyer = Spawn(us, "Destroyer", next);
		Assert.True(Naval.IsVisibleTo(sub, us));

		// Two tiles off: only with radar (the AEGIS Cruiser).
		Tile far = gameData.map.tiles.First(t => t.IsWater() && t.DistanceTo(water) == 2);
		gameData.TeleportUnit(destroyer, far);
		Assert.False(Naval.IsVisibleTo(sub, us));
		Spawn(us, "AEGIS Cruiser", far);
		Assert.True(Naval.IsVisibleTo(sub, us));

		// Other ships are always seen.
		Assert.True(Naval.IsVisibleTo(Spawn(them, "Frigate", water), us));
	}

	[Fact]
	public void TheAIDoesntHuntWhatItCantSee() {
		us.DeclareWarOn(them, gameData.turn);
		Tile next = water.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0);
		MapUnit sub = Spawn(them, "Submarine", next);
		sub.unitType.defense = 0;
		MapUnit frigate = Spawn(us, "Frigate", water);
		us.tileKnowledge.AddTilesToKnown(water);
		Assert.True(us.tileKnowledge.isTileKnown(next));
		CombatAIData blind = CombatAI.MakeAiData(frigate, us);
		Assert.True(blind == null || blind.destination != next);

		Spawn(us, "Destroyer", water);
		CombatAIData seeing = CombatAI.MakeAiData(frigate, us);
		Assert.NotNull(seeing);
		Assert.Equal(next, seeing.destination);
	}

	// ---------------------------------------------------------------- stealth attack

	[Fact]
	public async Task TheAIPicksItsStealthTarget() {
		us.DeclareWarOn(them, gameData.turn);
		Tile next = water.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0);
		MapUnit battleship = Spawn(them, "Battleship", next);
		MapUnit transport = Spawn(them, "Transport", next);
		MapUnit sub = Spawn(us, "Submarine", water);
		Assert.Equal(battleship, next.FindTopDefender(sub));
		Assert.Equal([battleship, transport], Naval.StealthTargets(sub, next));
		Assert.Equal(transport, Naval.PickStealthTarget(sub, Naval.StealthTargets(sub, next)));

		sub.unitType.attack = 1000;
		await sub.Move(water.DirectionTo(next));
		Assert.Contains(battleship, them.units);
		Assert.DoesNotContain(transport, them.units);
	}

	[Fact]
	public async Task AHumanChoosesTheTarget() {
		us.isHuman = true;
		us.DeclareWarOn(them, gameData.turn);
		Tile next = water.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0);
		MapUnit battleship = Spawn(them, "Battleship", next);
		MapUnit transport = Spawn(them, "Transport", next);
		MapUnit sub = Spawn(us, "Submarine", water);
		sub.unitType.attack = 1000;

		Task<bool> move = sub.Move(water.DirectionTo(next));
		MsgChooseStealthTarget ask = Assert.Single(EngineStorage.messagesToUI.OfType<MsgChooseStealthTarget>());
		Assert.Equal(sub, ask.attacker);
		Assert.Equal(2, ask.targets.Count);
		new MsgStealthTarget(battleship.id).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(move.Wait(5000));
		Assert.DoesNotContain(battleship, them.units);
		Assert.Contains(transport, them.units);

		// Or calls it off.
		MapUnit another = Spawn(them, "Battleship", next);
		sub.movementPoints.reset(sub.unitType.movement);
		EngineStorage.messagesToUI.Clear();
		move = sub.Move(water.DirectionTo(next));
		new MsgStealthTarget(null).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(move.Wait(5000));
		Assert.False(move.Result);
		Assert.Contains(another, them.units);
		Assert.Contains(transport, them.units);
		await move;
	}

	[Fact]
	public void OnlyItsTargetTypesAndOnlyStealthAttackers() {
		Tile next = water.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0);
		Spawn(them, "Battleship", next);
		MapUnit destroyer = Spawn(us, "Destroyer", water);
		destroyer.unitType.stealthTargets.Add("Battleship");
		Assert.Empty(Naval.StealthTargets(destroyer, next));
		MapUnit sub = Spawn(us, "Submarine", water);
		sub.unitType.stealthTargets.Remove("Battleship");
		Assert.Empty(Naval.StealthTargets(sub, next));
	}

	// ---------------------------------------------------------------- coastal fortress

	[Fact]
	public void ACoastalFortressShootsAtPassingShips() {
		City city = CityInteractions.BuildCity(shore, them, "Fortress");
		city.AddBuilding(gameData.Buildings.Find(b => b.name == "Coastal Fortress"), gameData);
		Assert.Equal(8, ZoneOfControl.NavalPower(city));
		// Two water tiles next to the city, next to each other.
		Tile from = shore.neighbors.Values.First(t => t.IsWater() && t.neighbors.Values.Any(n => n.IsWater() && n.DistanceTo(shore) == 1 && n != t));
		Tile to = from.neighbors.Values.First(n => n.IsWater() && n.DistanceTo(shore) == 1 && n != from);

		MapUnit galley = Spawn(us, "Galley", from);
		Assert.Null(ZoneOfControl.CoastalFortress(galley, from, to));
		us.DeclareWarOn(them, gameData.turn);
		Assert.Equal(city, ZoneOfControl.CoastalFortress(galley, from, to));

		// The zone of control's odds, A / (16 D + A): a galley (defense 1) is hit a third of the time, never sunk.
		int hits = 0;
		for (int i = 0; i < 600; i++) {
			galley.hitPointsRemaining = 3;
			if (ZoneOfControl.ApplyCoastalFortress(galley, from, to) != null) {
				hits++;
				Assert.Equal(2, galley.hitPointsRemaining);
			}
		}
		Assert.InRange(hits, 150, 250);
		// Never at the last hit point.
		galley.hitPointsRemaining = 1;
		for (int i = 0; i < 50; i++) {
			Assert.Null(ZoneOfControl.ApplyCoastalFortress(galley, from, to));
		}

		// Land units and cities without one don't count.
		Assert.Null(ZoneOfControl.CoastalFortress(Spawn(us, "Warrior", shore.neighbors.Values.First(t => t.IsLand())), from, to));
	}
}

// The naval abilities from Civ3's PRTO.
public class NavalCombatImportTest {
	[SkippableFact]
	public void TheAbilitiesComeFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "9 WWII in the Pacific.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		BiqData biq = BiqData.LoadFile(scenario);
		for (int i = 0; i < biq.Prto.Length; i++) {
			QueryCiv3.Biq.PRTO prto = biq.Prto[i];
			SaveUnitPrototype unit = save.UnitPrototypes.Find(u => u.name == prto.Name);
			Assert.Equal(prto.Amphibious, unit.flags.Contains(SaveUnitPrototype.Flag.Amphibious));
			Assert.Equal(prto.Submarine, unit.flags.Contains(SaveUnitPrototype.Flag.Submarine));
			Assert.Equal(prto.CanSeeSubmarines, unit.flags.Contains(SaveUnitPrototype.Flag.CanSeeSubmarines));
			Assert.Equal(prto.HiddenNationality, unit.flags.Contains(SaveUnitPrototype.Flag.HiddenNationality));
			Assert.Equal(prto.StealthAttack, unit.flags.Contains(SaveUnitPrototype.Flag.StealthAttack));
		}
		Assert.Contains(biq.Prto, p => p.Submarine);
		int sub = Array.FindIndex(biq.Prto, p => p.Submarine && biq.PrtoPrto[Array.IndexOf(biq.Prto, p)].Length > 0);
		Assert.Equal(biq.PrtoPrto[sub].Select(t => biq.Prto[t].Name).Distinct().OrderBy(n => n, StringComparer.Ordinal),
			save.UnitPrototypes.Find(u => u.name == biq.Prto[sub].Name).stealthTargets);
	}
}
