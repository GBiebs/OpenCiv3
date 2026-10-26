using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The great wonders' effects, as the Conquests Civilopedia gives them.
public class WonderEffectsTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public WonderEffectsTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Wonders");
	}

	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);

	private void Add(string name) => city.AddBuilding(Building(name), gameData);

	private void MakeObsolete(string name) => us.knownTechs.Add(Building(name).renderedObsoleteBy.id);

	private MapUnit Spawn(string type) => gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == type), city.location);

	// "All naval unit movement rates are increased by one" (the Great
	// Lighthouse, and Magellan's Voyage).
	[Fact]
	public void LighthouseAndMagellansSpeedUpShips() {
		MapUnit galley = Spawn("Galley");
		MapUnit warrior = Spawn("Warrior");
		int galleyMoves = galley.unitType.movement;

		Add("The Great Lighthouse");
		Assert.Equal(galleyMoves + 1, galley.MovementPerTurn());
		Add("Magellan's Voyage");
		Assert.Equal(galleyMoves + 2, galley.MovementPerTurn());
		Assert.Equal(warrior.unitType.movement, warrior.MovementPerTurn());

		// The moves come with the turn.
		galley.OnBeginTurn();
		Assert.Equal(galleyMoves + 2, galley.movementPoints.remaining);

		MakeObsolete("The Great Lighthouse");
		Assert.Equal(galleyMoves + 1, galley.MovementPerTurn());
	}
}
