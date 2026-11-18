using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.Editor;

// Phase 7: Civ3's Capture the Flag game mode, which the editor's flag unit,
// "Capture the Unit", "Auto Place Capture Units", "Allow anyone to capture
// any flag" and "Respawn flag unit on capture" set up.
public class CaptureTheFlagTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public CaptureTheFlagTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] civs = gameData.players.Where(p => !p.isBarbarians && !p.isHuman).Take(2).ToArray();
		us = civs[0];
		them = civs[1];
		gameData.victoryConditions ??= new VictoryConditions();
		gameData.victoryConditions.CaptureTheFlag = true;
		gameData.victoryConditions.CaptureFlagVictoryPoints = 1000;
		gameData.victoryConditions.GoldForCapture = 50;
		gameData.rules.FlagUnitType = Flag.name;
		us.EnsureRelationshipExists(them);
		PlayerRelationship.DeclareWar(us, them, false, gameData.turn + 10);
	}

	private UnitPrototype Flag => gameData.unitPrototypes.First(p => p.name == "Princess");
	private UnitPrototype Warrior => gameData.unitPrototypes.First(p => p.name == "Warrior");

	private static bool IsEmptyLand(Tile t) {
		return t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.IsImpassable();
	}

	private City Capital(Player owner) {
		Tile tile = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities() && t.OwningPlayer() == null
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity()));
		City city = CityInteractions.BuildCity(tile, owner, $"Capital {owner.cities.Count}");
		city.capital = true;
		return city;
	}

	// Two empty land tiles next to each other, far from cities.
	private (Tile from, TileDirection dir, Tile to) AdjacentLand() {
		foreach (Tile from in gameData.map.tiles.Where(t => IsEmptyLand(t) && t.OwningPlayer() == null)) {
			foreach ((TileDirection dir, Tile to) in from.neighbors) {
				if (to != Tile.NONE && IsEmptyLand(to) && to.OwningPlayer() == null) {
					return (from, dir, to);
				}
			}
		}
		throw new System.Exception("No adjacent land");
	}

	private MapUnit FlagOf(Player owner, Tile at) {
		MapUnit flag = gameData.SpawnUnit(owner, Flag, at);
		flag.nationality = owner.civilization;
		return flag;
	}

	[Fact]
	public void EachCivsFlagIsPlacedInItsCapitalAtTheStart() {
		City ours = Capital(us);
		City theirs = Capital(them);
		gameData.turn = 0;
		CaptureTheFlag.PlaceFlags(gameData);
		Assert.Contains(ours.location.unitsOnTile, u => u.unitType.isFlagUnit && u.owner == us && u.nationality == us.civilization);
		Assert.Contains(theirs.location.unitsOnTile, u => u.unitType.isFlagUnit && u.owner == them);
		// Not twice.
		CaptureTheFlag.PlaceFlags(gameData);
		Assert.Single(us.units, u => u.unitType.isFlagUnit);

		// Not without auto-placed capture units.
		City another = Capital(them);
		them.units.Where(u => u.unitType.isFlagUnit).ToList().ForEach(gameData.RemoveUnit);
		gameData.victoryConditions.AutoPlaceCaptureUnits = false;
		CaptureTheFlag.PlaceFlags(gameData);
		Assert.DoesNotContain(them.units, u => u.unitType.isFlagUnit);
	}

	[Fact]
	public async Task AFlagIsTakenCarriedAndBroughtHome() {
		City theirs = Capital(them);
		City ours = Capital(us);
		(Tile from, TileDirection dir, Tile to) = AdjacentLand();
		MapUnit flag = FlagOf(them, to);
		MapUnit warrior = gameData.SpawnUnit(us, Warrior, from);

		Assert.True(await warrior.Move(dir));
		Assert.Equal(us, flag.owner);
		Assert.Equal(them.civilization, flag.nationality);
		Assert.True(flag.IsLoadedIn(warrior));

		// Carried along, then into our capital.
		int gold = us.gold;
		warrior.location.unitsOnTile.Remove(warrior);
		flag.location.unitsOnTile.Remove(flag);
		Tile next = ours.location.neighbors.Values.First(t => t != Tile.NONE && IsEmptyLand(t));
		warrior.location = next;
		flag.location = next;
		next.unitsOnTile.Add(warrior);
		next.unitsOnTile.Add(flag);
		TileDirection home = next.neighbors.First(kv => kv.Value == ours.location).Key;
		warrior.movementPoints.reset(warrior.MaxMovementPoints());
		Assert.True(await warrior.Move(home));
		Assert.Equal(ours.location, warrior.location);
		Assert.Equal(1000, us.victoryPoints);
		Assert.Equal(gold + 50, us.gold);
		Assert.Equal(1, us.flagsCaptured);
		Assert.DoesNotContain(us.units, u => u.unitType.isFlagUnit);
		// It comes back in its civ's capital.
		Assert.Contains(theirs.location.unitsOnTile, u => u.unitType.isFlagUnit && u.owner == them);

		// Without Victory Point Scoring, that wins.
		CaptureTheFlagVictory victory = new();
		Assert.True(victory.HasVictory(victory.Evaluate(us, gameData)));
		Assert.False(victory.HasVictory(victory.Evaluate(them, gameData)));
	}

	[Fact]
	public void WithoutRespawningTheFlagIsGone() {
		City theirs = Capital(them);
		City ours = Capital(us);
		gameData.victoryConditions.RespawnFlagUnits = false;
		MapUnit warrior = gameData.SpawnUnit(us, Warrior, ours.location);
		MapUnit flag = FlagOf(them, ours.location);
		CaptureTheFlag.Capture(gameData, flag, warrior);
		CaptureTheFlag.OnEnterTile(gameData, warrior, ours.location);
		Assert.DoesNotContain(gameData.mapUnits, u => u.unitType.isFlagUnit);
	}

	[Fact]
	public void ACivMayTakeItsOwnFlagOnlyIfAnyoneMayTakeAny() {
		MapUnit ownFlag = FlagOf(us, AdjacentLand().to);
		Assert.False(CaptureTheFlag.MayCapture(gameData, us, ownFlag));
		Assert.True(CaptureTheFlag.MayCapture(gameData, them, ownFlag));
		gameData.victoryConditions.AnyoneCapturesAnyFlag = true;
		Assert.True(CaptureTheFlag.MayCapture(gameData, us, ownFlag));
		// Nor in a game without Capture the Flag.
		gameData.victoryConditions.CaptureTheFlag = false;
		Assert.False(CaptureTheFlag.MayCapture(gameData, them, ownFlag));
	}

	[Fact]
	public void TheOptionsAreSavedAndTheGameTypeWins() {
		VictoryConditions vc = gameData.victoryConditions;
		vc.AnyoneCapturesAnyFlag = true;
		vc.RespawnFlagUnits = false;
		vc.AutoPlaceCaptureUnits = false;
		us.flagsCaptured = 2;
		SaveGame saved = SaveGame.FromJSON(SaveGame.FromGameData(gameData).ToCompactJSON());
		Assert.True(saved.VictoryConditions.AnyoneCapturesAnyFlag);
		Assert.False(saved.VictoryConditions.RespawnFlagUnits);
		Assert.False(saved.VictoryConditions.AutoPlaceCaptureUnits);
		Assert.Equal(2, saved.Players.First(p => p.id == us.id).flagsCaptured);

		saved.VictoryConditions.VictoryLocations = false;
		Assert.Contains(saved.ToGameData(fixture.behaviors).victories, v => v is CaptureTheFlagVictory);
	}

	[SkippableFact]
	public void AgeOfDiscoverysFlagOptionsAreImported() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		string file = Path.Join(conquests, "Scenarios", "6 MP Age of Discovery.biq");
		Skip.If(!File.Exists(file), "No Age of Discovery");
		SaveGame save = ScenarioFiles.Open(file, PathUtils.defaultBicPath, ScenarioCorpusTest.PediaIconsFor(conquests, Path.GetDirectoryName(file)));
		VictoryConditions vc = save.VictoryConditions;
		Assert.True(vc.CaptureTheFlag);
		Assert.True(vc.AnyoneCapturesAnyFlag);
		Assert.False(vc.RespawnFlagUnits);
		Assert.Equal(200, vc.GoldForCapture);
		Assert.True(save.Scenario.anyoneCapturesAnyFlag);
		Assert.False(save.Scenario.respawnFlagUnits);
	}
}
