using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI.UnitAI;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI;

// The AI at sea (SeaAI): ferries ship settlers and attackers across the
// water, warships go after enemy ships they're likely to beat, and the AI
// builds what that needs.
public class SeaAITest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public SeaAITest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] ais = gameData.players.Where(p => !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.isHuman = false;
		them.isHuman = false;
		foreach (Tile t in gameData.map.tiles) {
			us.tileKnowledge.AddTilesToKnown(t, recomputeActiveTiles: false);
		}
		us.tileKnowledge.AddTilesToKnown(gameData.map.tiles[0]);
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, Type(type), tile);

	// The land continents, biggest first.
	private List<IGrouping<int, Tile>> Continents() {
		return gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand()).GroupBy(t => t.continent)
			.OrderByDescending(g => g.Count()).ToList();
	}

	// A coastal city site (by the sea, no city near) on the continent.
	private Tile PortSite(IEnumerable<Tile> continent) {
		return continent.First(t => t.IsAllowCities() && t.NeighborsOcean() && t.unitsOnTile.Count == 0 && !t.hasGoodyHut
			&& !t.hasBarbarianCamp && gameData.cities.All(c => c.location.DistanceTo(t) > 4));
	}

	// A coastal site on another continent near enough for a ferry trip.
	private Tile OverseasSite(Tile port) {
		return gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && t.continent != port.continent && t.IsAllowCities()
				&& t.NeighborsOcean() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasGoodyHut && !t.hasBarbarianCamp)
			.OrderBy(t => t.DistanceTo(port)).First();
	}

	private async Task PlayUnitTurns(int turns) {
		for (int i = 0; i < turns; i++) {
			foreach (MapUnit u in us.units.ToList()) {
				u.OnBeginTurn();
			}
			await PlayerAI.DoUnitActions(us);
			gameData.turn++;
		}
	}

	[Fact]
	public void FerriesAndWarships() {
		Assert.True(SeaAI.IsFerryType(Type("Galley")));
		Assert.True(SeaAI.IsFerryType(Type("Transport")));
		Assert.False(SeaAI.IsFerryType(Type("Carrier")));
		Assert.False(SeaAI.IsFerryType(Type("Nuclear Submarine")));
		Assert.False(SeaAI.IsFerryType(Type("Frigate")));
		Assert.True(SeaAI.IsWarshipType(Type("Frigate")));
		Assert.False(SeaAI.IsWarshipType(Type("Galley")));
	}

	[Fact]
	public async Task AFerryShipsASettlerToANewContinent() {
		Tile portSite = PortSite(Continents()[0]);
		City port = CityInteractions.BuildCity(portSite, us, "Port");
		Tile site = OverseasSite(portSite);
		Spawn(us, "Transport", portSite);
		MapUnit settler = Spawn(us, "Settler", portSite);
		settler.currentAI = new OverseasAI(new OverseasAIData { destination = site, port = portSite });

		await PlayUnitTurns(25);

		// Ashore, it founds its city on the site it was shipped for.
		Assert.Contains(us.cities, c => c.location == site);
		Assert.True(SeaAI.landings > 0);
	}

	[Fact]
	public void ASettlerWithNoRoomAtHomeIsShipped() {
		// The smallest island with room for a port: no second city fits.
		IGrouping<int, Tile> island = Continents().Where(g => g.Count() <= 6 && g.Any(t => t.IsAllowCities() && t.NeighborsOcean()))
			.OrderBy(g => g.Count()).First();
		Tile portSite = PortSite(island);
		CityInteractions.BuildCity(portSite, us, "Isle");
		Spawn(us, "Transport", portSite);
		MapUnit settler = Spawn(us, "Settler", portSite);

		C7GameData.UnitAI job = PlayerAI.GetAIForUnit(settler, us);
		OverseasAI overseas = Assert.IsType<OverseasAI>(job);
		Assert.Same(portSite, overseas.data.port);
		Assert.NotEqual(island.Key, overseas.data.destination.continent);
	}

	[Fact]
	public void AttackersWithNothingToFightAtHomeAreShippedToTheEnemy() {
		// (No barbarian camp near: a unit goes for one first.)
		Tile portSite = Continents()[0].First(t => t.IsAllowCities() && t.NeighborsOcean() && t.unitsOnTile.Count == 0 && !t.hasGoodyHut
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 4) && gameData.map.barbarianCamps.All(c => c.DistanceTo(t) > 4));
		CityInteractions.BuildCity(portSite, us, "Port");
		Tile enemySite = OverseasSite(portSite);
		City enemy = CityInteractions.BuildCity(enemySite, them, "Enemy");
		us.DeclareWarOn(them, gameData.turn);
		Assert.True(PlayerRelationship.AtWar(us, them));
		foreach (MapUnit u in them.units.ToList()) {
			gameData.RemoveUnit(u);
		}
		for (int i = 0; i < 3; i++) {
			Spawn(us, "Spearman", portSite);
		}
		Spawn(us, "Transport", portSite);
		MapUnit swordsman = Spawn(us, "Swordsman", portSite);

		OverseasAI overseas = Assert.IsType<OverseasAI>(PlayerAI.GetAIForUnit(swordsman, us));
		Assert.Same(enemy.location, overseas.data.destination);

		// Not without a way to ship it: no ferry, none known.
		foreach (MapUnit ship in us.units.Where(SeaAI.IsFerry).ToList()) {
			gameData.RemoveUnit(ship);
		}
		foreach (UnitPrototype t in gameData.unitPrototypes.Where(SeaAI.IsFerryType)) {
			us.knownTechs.Remove(t.requiredTech?.id);
		}
		Assert.IsNotType<OverseasAI>(PlayerAI.GetAIForUnit(swordsman, us));
	}

	// A war that ends while the attackers are at sea: the trip is off, and
	// the land of a civ at peace with us is no place to go ashore (its
	// border guards would send them home, again and again: found by the
	// soak).
	[Fact]
	public async Task ATripIsCalledOffWhenTheWarEnds() {
		Tile portSite = PortSite(Continents()[0]);
		CityInteractions.BuildCity(portSite, us, "Port");
		City enemy = CityInteractions.BuildCity(OverseasSite(portSite), them, "Enemy");
		us.DeclareWarOn(them, gameData.turn);
		MapUnit transport = Spawn(us, "Transport", portSite);
		MapUnit swordsman = Spawn(us, "Swordsman", portSite);
		OverseasAI trip = new(new OverseasAIData { destination = enemy.location, port = portSite, attack = true });
		Assert.True(SeaAI.StillWorthIt(us, trip.data));
		Tile theirs = enemy.location.neighbors.Values.First(n => n.IsLand() && n.OwningPlayer() == them && n.unitsOnTile.Count == 0);
		Assert.True(SeaAI.IsFreeLand(theirs, us));

		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.False(SeaAI.StillWorthIt(us, trip.data));
		Assert.False(SeaAI.IsFreeLand(theirs, us));

		// Aboard, the unit stays on: the ferry takes it home.
		swordsman.BoardTransport(transport);
		swordsman.Wake();
		swordsman.currentAI = trip;
		await swordsman.currentAI.PlayTurn(us, swordsman);
		Assert.Null(trip.data.destination);
		Assert.True(swordsman.IsLoaded());
	}

	[Fact]
	public void WarshipsAttackShipsTheyCanBeat() {
		Tile portSite = PortSite(Continents()[0]);
		CityInteractions.BuildCity(portSite, us, "Port");
		us.DeclareWarOn(them, gameData.turn);
		Tile sea = portSite.neighbors.Values.First(n => !n.IsLand() && !n.isFreshWater);
		Tile prey = sea.neighbors.Values.First(n => !n.IsLand() && n != portSite && n.unitsOnTile.Count == 0);
		MapUnit galley = Spawn(them, "Galley", prey);

		MapUnit battleship = Spawn(us, "Battleship", sea);
		Assert.IsType<CombatAI>(PlayerAI.GetAIForUnit(battleship, us));
		Assert.Same(prey, SeaAI.WarshipAttack(battleship, us).destination);

		// A boat that would lose stays clear and goes to port.
		gameData.RemoveUnit(galley);
		Spawn(them, "Battleship", prey);
		MapUnit curragh = Spawn(us, "Curragh", sea);
		Assert.IsNotType<CombatAI>(PlayerAI.GetAIForUnit(curragh, us));
	}

	[Fact]
	public void TheAIBuildsAFerryForUnitsWaitingInPort() {
		Tile portSite = PortSite(Continents()[0]);
		City port = CityInteractions.BuildCity(portSite, us, "Port");
		foreach (UnitPrototype t in gameData.unitPrototypes.Where(t => SeaAI.IsFerryType(t) && t.requiredTech != null)) {
			us.knownTechs.Add(t.requiredTech.id);
		}
		for (int i = 0; i < 2; i++) {
			Spawn(us, "Spearman", portSite);
		}
		MapUnit settler = Spawn(us, "Settler", portSite);
		settler.currentAI = new OverseasAI(new OverseasAIData { destination = OverseasSite(portSite), port = portSite });
		Assert.True(SeaAI.FerryNeeded(us, portSite.continent));

		int ferries = Enumerable.Range(0, 20).Count(_ => ChooseProducible.Choose(port, us) is UnitPrototype t && SeaAI.IsFerryType(t));
		Assert.True(ferries >= 15, $"a ferry {ferries} times in 20");
	}

	[Fact]
	public void WarsOverseasOnlyWithAWayToShipAnArmy() {
		Tile portSite = PortSite(Continents()[0]);
		CityInteractions.BuildCity(portSite, us, "Port");
		Player other = gameData.players.First(p => p != us && p != them && !p.isBarbarians);
		foreach (City c in other.cities.ToList()) {
			CityInteractions.DestroyCity(c);
		}
		CityInteractions.BuildCity(OverseasSite(portSite), other, "Far");
		foreach (City c in us.cities.Where(c => c.location.continent != portSite.continent).ToList()) {
			CityInteractions.DestroyCity(c);
		}
		foreach (UnitPrototype t in gameData.unitPrototypes.Where(SeaAI.IsFerryType)) {
			us.knownTechs.Remove(t.requiredTech?.id);
		}
		foreach (MapUnit ship in us.units.Where(SeaAI.IsFerry).ToList()) {
			gameData.RemoveUnit(ship);
		}
		Assert.Equal(int.MaxValue, C7GameData.AIData.WarPriority.DistanceToClosestCity(us, other));

		Spawn(us, "Transport", portSite);
		Assert.InRange(C7GameData.AIData.WarPriority.DistanceToClosestCity(us, other), 1, 1000);
	}
}
