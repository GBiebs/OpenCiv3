using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Pillaging, as in Civ3: one order destroys a railroad (leaving the road),
// then the road with any irrigation or mine, then a fortress (a barricade is
// first reduced to a fortress). Bombarding a tile's improvements uses the
// same order.
public class PillageTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile tile;

	public PillageTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		tile = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity()
			&& t.owningCity == null && gameData.mapUnits.All(u => u.location.DistanceTo(t) > 4));
	}

	private TerrainImprovement Improvement(string key) {
		return gameData.terrainImprovements.Find(i => i.key == key);
	}

	private List<string> Keys(IEnumerable<TerrainImprovement> improvements) {
		return improvements.Select(i => i.key).Order().ToList();
	}

	private void Improve(params string[] keys) {
		foreach (string key in keys) {
			tile.overlays.Add(Improvement(key));
		}
	}

	private MapUnit Warrior(Player owner) {
		return gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == "Warrior"), tile);
	}

	// Puts the tile inside one of `owner`'s cities' borders.
	private void GiveTileTo(Player owner) {
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.DistanceTo(tile) == 1);
		CityInteractions.BuildCity(site, owner, "Owner");
		Assert.Equal(owner, tile.owningCity?.owner);
	}

	[Fact]
	public void ImprovementsArePillagedInCiv3sOrder() {
		Improve("railroad", "irrigation", "fortress");

		Assert.Equal(["railroad"], Keys(tile.overlays.PillageNext()));
		Assert.Equal("road", tile.overlays.ImprovementAtLayer(TerrainImprovement.Layer.Roads).key);
		Assert.Equal(["irrigation", "road"], Keys(tile.overlays.PillageNext()));
		Assert.Equal(["fortress"], Keys(tile.overlays.PillageNext()));
		Assert.Empty(tile.overlays.PillageNext());
		Assert.False(tile.overlays.HasBeenImproved());
	}

	[Fact]
	public void ABarricadeIsReducedToAFortress() {
		Improve("barricade");

		Assert.Equal(["barricade"], Keys(tile.overlays.PillageNext()));
		Assert.Equal("fortress", tile.overlays.ImprovementAtLayer(TerrainImprovement.Layer.Holdings).key);
	}

	[Fact]
	public void PillagingEnemyLandTakesTheUnitsTurnAndTellsTheOwner() {
		GiveTileTo(them);
		us.DeclareWarOn(them, gameData.turn);
		Improve("road", "mine");
		MapUnit warrior = Warrior(us);
		Assert.Contains(UnitAction.Pillage, warrior.GetAvailableActions());

		Assert.Equal(["mine", "road"], Keys(warrior.Pillage()));

		Assert.False(tile.overlays.HasBeenImproved());
		Assert.False(warrior.movementPoints.canMove);
		MsgTilePillaged message = Assert.Single(EngineStorage.messagesToUI.OfType<MsgTilePillaged>());
		Assert.Equal("Enemy troops pillaged our road and mine!", message.TextFor(them.id));
		Assert.Equal("We pillaged the road and mine!", message.TextFor(us.id));
	}

	[Fact]
	public void TheLandOfACivAtPeaceCannotBePillaged() {
		GiveTileTo(them);
		Improve("road");
		MapUnit warrior = Warrior(us);
		Assert.False(PlayerRelationship.AtWar(us, them));

		Assert.False(warrior.CanPillage());
		Assert.Empty(warrior.Pillage());
		Assert.True(tile.overlays.HasBeenImproved());
		Assert.DoesNotContain(UnitAction.Pillage, warrior.GetAvailableActions());
	}

	[Fact]
	public void OurOwnAndUnclaimedLandCanBePillaged() {
		Improve("road");
		Assert.True(Warrior(us).CanPillage());

		GiveTileTo(us);
		Assert.True(Warrior(us).CanPillage());
	}

	[Fact]
	public void PillagingNeedsTheOrderMovesAndSomethingToDestroy() {
		MapUnit warrior = Warrior(us);
		Assert.False(warrior.CanPillage()); // nothing to pillage

		Improve("road");
		MapUnit settler = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Settler"), tile);
		Assert.False(settler.CanPillage());

		warrior.movementPoints.onConsumeAll();
		Assert.False(warrior.CanPillage());
	}

	[Fact]
	public void TheCiv3RulesetSaysWhoCanPillage() {
		Assert.Contains(UnitAction.Pillage, gameData.unitPrototypes.Find(u => u.name == "Warrior").actions);
		Assert.Contains(UnitAction.Pillage, gameData.unitPrototypes.Find(u => u.name == "Tank").actions);
		Assert.DoesNotContain(UnitAction.Pillage, gameData.unitPrototypes.Find(u => u.name == "Worker").actions);
	}
}
