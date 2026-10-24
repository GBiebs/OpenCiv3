using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI;

// The AI's use of pillaging and the draft (see PlayerAI.MaybePillage and
// DraftForDefense): a unit with moves left on a warring civ's improved land,
// with no enemy next to it, pillages; a threatened city short of defenders
// drafts one.
public class RaidAndDraftAITest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public RaidAndDraftAITest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
	}

	private MapUnit Spawn(Player owner, string type, Tile tile) {
		return gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == type), tile);
	}

	// A road on a tile inside the borders of a new (empty) city of `owner`'s
	// next to it, far from anyone.
	private (Tile road, City city) RoadOf(Player owner) {
		Tile tile = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity()
			&& t.owningCity == null && gameData.mapUnits.All(u => u.location.DistanceTo(t) > 4)
			&& t.neighbors.Values.Any(n => Tile.IsTileValid(n) && n.IsLand() && !n.HasCity() && n.baseTerrainType.allowCities && n.overlayTerrainType.allowCities));
		Tile site = tile.neighbors.Values.First(n => Tile.IsTileValid(n) && n.IsLand() && !n.HasCity() && n.baseTerrainType.allowCities && n.overlayTerrainType.allowCities);
		City city = CityInteractions.BuildCity(site, owner, "Roadside");
		tile.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "road"));
		Assert.Equal(owner, tile.owningCity?.owner);
		return (tile, city);
	}

	private static bool HasRoad(Tile tile) => tile.overlays.GetManMadeImprovements().Any(i => i.key == "road");

	[Fact]
	public void AtWarAUnitWithMovesLeftPillagesTheirLand() {
		(Tile road, _) = RoadOf(them);
		us.DeclareWarOn(them, gameData.turn);
		MapUnit raider = Spawn(us, "Swordsman", road);

		C7Engine.PlayerAI.MaybePillage(raider, us);

		Assert.False(HasRoad(road));
		Assert.False(raider.movementPoints.canMove);
	}

	[Fact]
	public void NoRaidsNextToTheEnemyInPeaceOrWithoutMoves() {
		(Tile road, City theirCity) = RoadOf(them);
		MapUnit raider = Spawn(us, "Swordsman", road);

		// In peace.
		C7Engine.PlayerAI.MaybePillage(raider, us);
		Assert.True(HasRoad(road));

		// Without moves.
		us.DeclareWarOn(them, gameData.turn);
		raider.movementPoints.onConsumeAll();
		C7Engine.PlayerAI.MaybePillage(raider, us);
		Assert.True(HasRoad(road));

		// Next to an enemy unit, which it should fight instead.
		raider.OnBeginTurn();
		Spawn(them, "Spearman", theirCity.location);
		C7Engine.PlayerAI.MaybePillage(raider, us);
		Assert.True(HasRoad(road));
	}

	[Fact]
	public void NoRaidsOnOurOwnLand() {
		(Tile road, _) = RoadOf(us);
		us.DeclareWarOn(them, gameData.turn);

		C7Engine.PlayerAI.MaybePillage(Spawn(us, "Swordsman", road), us);

		Assert.True(HasRoad(road));
	}

	// A city of ours of size 8, with Nationalism, and an enemy Warrior two
	// tiles away.
	private City ThreatenedCity() {
		City city = CityInteractions.BuildCity(us.units[0].location, us, "Frontier");
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < 8) {
			city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = laborer });
		}
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == "Bronze Working").id);
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == "Nationalism").id);
		Tile near = city.location.GetTilesWithinRankDistance(2).First(t => t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity()
			&& t.DistanceTo(city.location) == 2);
		Spawn(them, "Warrior", near);
		return city;
	}

	private int Defenders(City city) => city.location.unitsOnTile.Count(u => u.owner == us && u.CanDefendOnLand());

	[Fact]
	public void AThreatenedCityShortOfDefendersDrafts() {
		City city = ThreatenedCity();
		us.DeclareWarOn(them, gameData.turn);

		C7Engine.PlayerAI.DraftForDefense(us, gameData);

		Assert.Equal(1, Defenders(city));
		Assert.Equal(7, city.residents.Count);
		Assert.Equal("Conscript", city.location.unitsOnTile.Single(u => u.owner == us && u.CanDefendOnLand()).experienceLevelKey);
	}

	[Fact]
	public async System.Threading.Tasks.Task TheAIsTurnDraftsForAThreatenedCity() {
		City city = ThreatenedCity();
		us.DeclareWarOn(them, gameData.turn);

		await C7Engine.PlayerAI.PlayTurn(us, gameData);

		Assert.Equal(7, city.residents.Count);
		Assert.Contains(us.units, u => u.experienceLevelKey == "Conscript");
	}

	[Fact]
	public void NoDraftInPeaceWithoutAThreatOrWithEnoughDefenders() {
		City city = ThreatenedCity();

		C7Engine.PlayerAI.DraftForDefense(us, gameData);
		Assert.Equal(8, city.residents.Count);

		us.DeclareWarOn(them, gameData.turn);
		Spawn(us, "Spearman", city.location);
		Spawn(us, "Spearman", city.location);
		C7Engine.PlayerAI.DraftForDefense(us, gameData);
		Assert.Equal(8, city.residents.Count);

		foreach (MapUnit defender in city.location.unitsOnTile.Where(u => u.owner == us).ToList()) {
			defender.RemoveFromPlay();
		}
		foreach (MapUnit enemy in them.units.Where(u => u.location.DistanceTo(city.location) <= 2).ToList()) {
			enemy.RemoveFromPlay();
		}
		C7Engine.PlayerAI.DraftForDefense(us, gameData);
		Assert.Equal(8, city.residents.Count);
	}
}
