using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's mobilization (Civilopedia "Mobilization"): with Nationalism a civ can
// put its economy on a war footing. Its cities build only military units and
// military improvements, a city building a military unit makes a shield more
// on every tile that makes one, and it can't return to normalcy until it
// signs a peace treaty.
public class MobilizationTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City city;

	public MobilizationTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Arsenal");
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < 4) {
			CityResident resident = new() { city = city, nationality = us.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
		foreach (string tech in new[] { "Ceremonial Burial", "Bronze Working", "Currency" }) {
			Learn(tech);
		}
	}

	private void Learn(string tech) {
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == tech).id);
	}

	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);
	private UnitPrototype Unit(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private bool CanBuild(IProducible item) => item.CanProduce(city, city.GetAccessibleResources(gameData));

	[Fact]
	public void MobilizingNeedsNationalism() {
		Assert.True(gameData.techs.Find(t => t.Name == "Nationalism").HasFlag(SaveTech.Flag.EnablesMobilization));
		Assert.False(us.CanMobilize());
		Assert.False(us.SetMobilized(true, gameData));
		Assert.False(us.mobilized);

		Learn("Nationalism");
		Assert.True(us.SetMobilized(true, gameData));
		Assert.True(us.mobilized);
		Assert.False(us.CanMobilize());
	}

	[Fact]
	public void MobilizedCitiesBuildOnlyMilitaryUnitsAndImprovements() {
		Learn("Nationalism");
		Inflow wealth = gameData.Inflows.First();
		Assert.True(CanBuild(Building("Temple")));
		Assert.True(CanBuild(wealth));

		us.SetMobilized(true, gameData);

		Assert.False(CanBuild(Building("Temple")));
		Assert.False(CanBuild(Building("Marketplace")));
		Assert.False(CanBuild(wealth));
		Assert.True(CanBuild(Building("Barracks")));
		// Nationalism brings the Rifleman (and makes the Spearman obsolete).
		Assert.True(CanBuild(Unit("Rifleman")));
		// Workers and settlers can still be built (without the bonus).
		Assert.True(CanBuild(Unit("Worker")));
	}

	[Fact]
	public void ACityBuildingAnImprovementTurnsToSomethingItMayBuild() {
		Learn("Nationalism");
		city.SetItemBeingProduced(Building("Temple"));

		us.SetMobilized(true, gameData);

		Assert.NotEqual(Building("Temple"), city.itemBeingProduced);
		Assert.True(CanBuild(city.itemBeingProduced));
	}

	[Fact]
	public void MilitaryUnitsGetAShieldMoreOnEveryTileMakingShields() {
		Learn("Nationalism");
		List<Tile> tiles = [city.location, .. city.residents.Select(r => r.tileWorked)];
		int tilesMakingShields = tiles.Count(t => t.ProductionYield(city).yield > 0);
		Assert.True(tilesMakingShields > 0);
		city.SetItemBeingProduced(Unit("Spearman"));
		int normal = Total(city.CurrentProductionYield());

		us.SetMobilized(true, gameData);
		Assert.Equal(normal + tilesMakingShields, Total(city.CurrentProductionYield()));

		city.SetItemBeingProduced(Unit("Worker"));
		Assert.Equal(normal, Total(city.CurrentProductionYield()));
		city.SetItemBeingProduced(Building("Barracks"));
		Assert.Equal(normal, Total(city.CurrentProductionYield()));
	}

	private static int Total(CorruptableValue value) => value.useful + value.corrupt;

	[Fact]
	public void OnlyAPeaceTreatySignedWhileMobilizedEndsIt() {
		Learn("Nationalism");
		us.DeclareWarOn(them, gameData.turn);
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);

		// That peace came before mobilizing, so it doesn't count.
		us.SetMobilized(true, gameData);
		Assert.False(us.CanDemobilize());
		Assert.False(us.SetMobilized(false, gameData));
		Assert.True(us.mobilized);

		us.DeclareWarOn(them, gameData.turn);
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.True(us.CanDemobilize());
		Assert.True(us.SetMobilized(false, gameData));
		Assert.False(us.mobilized);
		Assert.True(CanBuild(Building("Temple")));
	}

	// ---------------------------------------------------------------- the AI

	private void Strengthen(Player player, int warriors) {
		for (int i = 0; i < warriors; i++) {
			gameData.SpawnUnit(player, Unit("Warrior"), player.units[0].location);
		}
	}

	[Fact]
	public void TheAIMobilizesForAWarItIsntWinning() {
		Learn("Nationalism");
		us.DeclareWarOn(them, gameData.turn);
		Assert.Equal(PeaceAI.Standing.Even, PeaceAI.StandingIn(us, them));

		gameData.turn += MobilizationAI.MobilizeAfterWarTurns - 1;
		MobilizationAI.Manage(us, gameData);
		Assert.False(us.mobilized);

		gameData.turn++;
		MobilizationAI.Manage(us, gameData);
		Assert.True(us.mobilized);
	}

	[Fact]
	public async Task AnAITurnMobilizes() {
		Learn("Nationalism");
		us.DeclareWarOn(them, gameData.turn);
		gameData.turn += MobilizationAI.MobilizeAfterWarTurns;
		await PlayerAI.PlayTurn(us, gameData);
		Assert.True(us.mobilized);
	}

	[Fact]
	public void NotForAWarItsWinning() {
		Learn("Nationalism");
		us.DeclareWarOn(them, gameData.turn);
		Strengthen(us, 3 * them.units.Count + 10);
		Assert.Equal(PeaceAI.Standing.Winning, PeaceAI.StandingIn(us, them));
		gameData.turn += MobilizationAI.MobilizeAfterWarTurns;
		MobilizationAI.Manage(us, gameData);
		Assert.False(us.mobilized);

		// Nor without Nationalism.
		Strengthen(them, 10 * us.units.Count);
		us.knownTechs.Remove(gameData.techs.Find(t => t.Name == "Nationalism").id);
		MobilizationAI.Manage(us, gameData);
		Assert.False(us.mobilized);
	}

	[Fact]
	public void TheAIReturnsToNormalcyWhenAtWarWithNoOne() {
		Learn("Nationalism");
		Player third = gameData.players.First(p => !p.isHuman && !p.isBarbarians && p != us && p != them);
		us.DeclareWarOn(them, gameData.turn);
		us.DeclareWarOn(third, gameData.turn);
		gameData.turn += MobilizationAI.MobilizeAfterWarTurns;
		MobilizationAI.Manage(us, gameData);
		Assert.True(us.mobilized);

		// Still at war with one of them.
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		MobilizationAI.Manage(us, gameData);
		Assert.True(us.mobilized);

		// The barbarians don't count.
		PlayerRelationship.SignPeaceAfterWar(us, third, gameData);
		Assert.False(us.IsAtPeaceWith(gameData.players.Find(p => p.isBarbarians)));
		MobilizationAI.Manage(us, gameData);
		Assert.False(us.mobilized);
	}

	[Fact]
	public void MobilizationIsSaved() {
		Learn("Nationalism");
		us.SetMobilized(true, gameData);
		us.signedPeaceSinceMobilizing = true;

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);

		Player loaded = reloaded.players.Find(p => p.id == us.id);
		Assert.True(loaded.mobilized);
		Assert.True(loaded.signedPeaceSinceMobilizing);
		Assert.False(reloaded.players.Find(p => p.id == them.id).mobilized);
	}
}
