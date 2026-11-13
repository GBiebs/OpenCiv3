using System.Linq;
using C7Engine;
using C7Engine.AI.UnitAI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Scientific great leaders: rolled for by the first civ to research a tech,
// and used to finish anything a city builds, great wonders included.
public class ScientificLeaderTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City capital;

	public ScientificLeaderTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && !p.isHuman).Take(2).ToArray();
		us = civs[0];
		them = civs[1];
		us.civilization.traits.Remove(Civilization.Trait.Scientific);
		// So there is a wonder to build: the Pyramids, which need no coast.
		us.knownTechs.Add(BuildingNamed("The Pyramids").requiredTech.id);
		capital = us.cities.FirstOrDefault(c => c.capital) ?? us.cities.FirstOrDefault() ?? BuildCity(us);
	}

	private class FixedRandom(double sample) : System.Random {
		protected override double Sample() => sample;
	}

	private T WithRandom<T>(double sample, System.Func<T> action) {
		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = new FixedRandom(sample);
		try {
			return action();
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	private static bool IsEmptyLand(Tile t) {
		return t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.IsImpassable();
	}

	// Away from everyone else's units and cities, so it isn't threatened
	// (see WonderPlanner.IsThreatened).
	private City BuildCity(Player owner) {
		Tile tile = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities() && t.OwningPlayer() == null
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity())
			&& !gameData.players.Where(p => p != owner).Any(p => p.units.Any(u => u.location.DistanceTo(t) <= 8)
				|| p.cities.Any(c => c.location.DistanceTo(t) <= 8)));
		return CityInteractions.BuildCity(tile, owner, $"City {owner.cities.Count}");
	}

	private Building BuildingNamed(string name) {
		return gameData.Buildings.Single(b => b.name == name);
	}

	private MapUnit SpawnScientificLeader(Tile tile) {
		MapUnit leader = gameData.SpawnUnit(us, gameData.unitPrototypes.Single(p => p.name == "Leader"), tile);
		leader.isScientificLeader = true;
		return leader;
	}

	// Finishes researching a tech no civ knows yet, rolling the sample.
	private Tech ResearchNewTech(double sample) {
		Tech tech = us.GetAvailableTechsToResearch(gameData.techs)
			.First(t => !gameData.players.Any(p => p.knownTechs.Contains(t.id)));
		us.SetCurrentlyResearchedTech(tech.id);
		us.turnsResearched = 1000;
		WithRandom(sample, () => { us.CompletePaidResearch(gameData); return 0; });
		Assert.Contains(tech.id, us.knownTechs);
		return tech;
	}

	private MapUnit ScientificLeader() => us.units.SingleOrDefault(u => u.IsScientificLeader());

	[Fact]
	public void ChancesAreThreeAndFivePercent() {
		Assert.Equal(0.03, MapUnit.ScientificLeaderChanceFor(us));
		us.civilization.traits.Add(Civilization.Trait.Scientific);
		Assert.Equal(0.05, MapUnit.ScientificLeaderChanceFor(us));
	}

	[Fact]
	public void BeingFirstToResearchATechCanBringOne() {
		ResearchNewTech(0.029);

		MapUnit leader = ScientificLeader();
		Assert.NotNull(leader);
		Assert.Equal(capital.location, leader.location);
		Assert.Equal("Scientific Leader", leader.name);
	}

	[Theory]
	[InlineData(false, 0.031, false)]
	[InlineData(true, 0.031, true)]
	[InlineData(true, 0.049, true)]
	[InlineData(true, 0.051, false)]
	public void ScientificCivsHaveTheBetterChance(bool scientific, double roll, bool expected) {
		if (scientific) {
			us.civilization.traits.Add(Civilization.Trait.Scientific);
		}
		ResearchNewTech(roll);
		Assert.Equal(expected, ScientificLeader() != null);
	}

	[Fact]
	public void TechsOthersKnowBringNone() {
		Tech tech = us.GetAvailableTechsToResearch(gameData.techs).First();
		them.knownTechs.Add(tech.id);
		us.SetCurrentlyResearchedTech(tech.id);
		us.turnsResearched = 1000;
		WithRandom(0.0, () => { us.CompletePaidResearch(gameData); return 0; });

		Assert.Contains(tech.id, us.knownTechs);
		Assert.Null(ScientificLeader());
	}

	[Fact]
	public void TechsGainedOtherwiseBringNone() {
		Tech tech = us.GetAvailableTechsToResearch(gameData.techs)
			.First(t => !gameData.players.Any(p => p.knownTechs.Contains(t.id)));
		WithRandom(0.0, () => { us.AcquireTech(gameData, tech); return 0; });
		Assert.Null(ScientificLeader());
	}

	// Philosophy's free tech counts as a first discovery (AutomatedTeller,
	// CFC thread 211436); other free techs, as Theory of Evolution's, don't.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void OnlyPhilosophysFreeTechCanBringOne(bool philosophy) {
		Tech tech = us.GetAvailableTechsToResearch(gameData.techs)
			.First(t => !gameData.players.Any(p => p.knownTechs.Contains(t.id)));
		us.freeTechsRemaining = 1;
		us.bonusFreeTechsRemaining = philosophy ? 1 : 0;
		WithRandom(0.0, () => { us.SetCurrentlyResearchedTech(tech.id); return 0; });
		Assert.Contains(tech.id, us.knownTechs);
		Assert.Equal(philosophy, ScientificLeader() != null);
		Assert.Equal(0, us.bonusFreeTechsRemaining);
	}

	[Fact]
	public void BeingFirstToABonusTechGrantsABonusFreeTech() {
		Tech bonus = gameData.techs.Single(t => t.BonusTechToFirstCivThatResearches);
		foreach (Player p in gameData.players) {
			p.knownTechs.Remove(bonus.id);
		}
		// A human, so that the free tech waits for their choice.
		us.isHuman = true;
		us.SetCurrentlyResearchedTech(null);
		us.ResearchQueue.Clear();
		us.AcquireTech(gameData, bonus);
		Assert.Equal(1, us.freeTechsRemaining);
		Assert.Equal(1, us.bonusFreeTechsRemaining);
		Assert.Equal(1, SaveGame.FromGameData(gameData).ToGameData(fixture.behaviors).players.Single(p => p.id == us.id).bonusFreeTechsRemaining);
	}

	[Fact]
	public void TheRuleCanTurnThemOff() {
		gameData.rules.ScientificLeaders = false;
		ResearchNewTech(0.0);
		Assert.Null(ScientificLeader());
	}

	[Fact]
	public void TheRuleIsOnByDefaultAndSaved() {
		Assert.True(new Rules().ScientificLeaders);
		gameData.rules.ScientificLeaders = false;
		C7GameData.GameData loaded = SaveGame.FromGameData(gameData).ToGameData(fixture.behaviors);
		Assert.False(loaded.rules.ScientificLeaders);
	}

	[Fact]
	public void AScientificLeaderSurvivesASave() {
		MapUnit leader = SpawnScientificLeader(capital.location);
		C7GameData.GameData loaded = SaveGame.FromGameData(gameData).ToGameData(fixture.behaviors);
		Assert.True(loaded.mapUnits.Single(u => u.id == leader.id).IsScientificLeader());
	}

	[Fact]
	public void ItFinishesAGreatWonder() {
		MapUnit leader = SpawnScientificLeader(capital.location);
		Building wonder = BuildingNamed("The Great Wall");
		capital.SetItemBeingProduced(wonder);

		Assert.Null(leader.HurryProductionBlocker());
		Assert.Contains(UnitAction.HurryBuilding, leader.GetAvailableActions());
		Assert.True(leader.HurryProductionAsLeader());

		Assert.Equal(us.ShieldCost(wonder), capital.shieldsStored);
		Assert.DoesNotContain(leader, gameData.mapUnits);
		capital.HandleCityProduction(gameData);
		Assert.Contains(wonder.name, gameData.GreatWondersBuilt);
	}

	// It can hurry anything, not only wonders (owner's decision, 2026-10-12).
	[Fact]
	public void ItFinishesAnImprovement() {
		MapUnit leader = SpawnScientificLeader(capital.location);
		Building temple = BuildingNamed("Temple");
		capital.SetItemBeingProduced(temple);
		capital.SetStoredShields(0);

		Assert.Null(leader.HurryProductionBlocker());
		Assert.True(leader.HurryProductionAsLeader());
		Assert.Equal(us.ShieldCost(temple), capital.shieldsStored);
		Assert.DoesNotContain(leader, gameData.mapUnits);
	}

	[Fact]
	public void ItFinishesAUnit() {
		MapUnit leader = SpawnScientificLeader(capital.location);
		UnitPrototype catapult = gameData.unitPrototypes.Single(p => p.name == "Catapult");
		capital.SetItemBeingProduced(catapult);
		capital.SetStoredShields(0);

		Assert.Null(leader.HurryProductionBlocker());
		Assert.Contains(UnitAction.HurryBuilding, leader.GetAvailableActions());
		Assert.True(leader.HurryProductionAsLeader());
		Assert.Equal(us.ShieldCost(catapult), capital.shieldsStored);
		Assert.DoesNotContain(leader, gameData.mapUnits);
	}

	// A military leader still can't hurry a unit.
	[Fact]
	public void AMilitaryLeaderCantFinishAUnit() {
		MapUnit leader = gameData.SpawnUnit(us, gameData.unitPrototypes.Single(p => p.name == "Leader"), capital.location);
		capital.SetItemBeingProduced(gameData.unitPrototypes.Single(p => p.name == "Catapult"));
		capital.SetStoredShields(0);
		Assert.NotNull(leader.HurryProductionBlocker());
		Assert.False(leader.HurryProductionAsLeader());
	}

	[Fact]
	public void ItCantFormAnArmy() {
		MapUnit leader = SpawnScientificLeader(capital.location);
		Assert.False(leader.CanFormArmy());
		Assert.DoesNotContain(UnitAction.BuildArmy, leader.GetAvailableActions());
	}

	// A scientific leader doesn't stop an elite unit producing a military one.
	[Fact]
	public void ItDoesntCountAgainstTheMilitaryLeaderLimit() {
		SpawnScientificLeader(capital.location);
		Tile tile = capital.location.neighbors.Values.First(t => t != Tile.NONE && t.IsLand());
		MapUnit tank = gameData.SpawnUnit(us, gameData.unitPrototypes.Single(p => p.name == "Tank"), tile);
		tank.experienceLevel = gameData.experienceLevels.Last();
		tank.experienceLevelKey = tank.experienceLevel.key;
		MapUnit enemy = gameData.SpawnUnit(them, gameData.unitPrototypes.Single(p => p.name == "Warrior"), tile);
		Assert.True(tank.CouldProduceLeaderBeating(enemy, wasElite: true));
	}

	// The AI finishes the costliest wonder it could build with its leader,
	// switching the city to it.
	[Fact]
	public void TheAIUsesItOnItsCostliestWonder() {
		capital.SetItemBeingProduced(BuildingNamed("Temple"));
		(City city, Building wonder) = PlayerAI.BestWonderForScientificLeader(us);
		Assert.NotNull(wonder);
		Assert.True(City.IsWonder(wonder));
		foreach (IProducible p in city.ListProductionOptions(gameData)) {
			if (City.IsWonder(p)) {
				Assert.True(us.ShieldCost(p) <= us.ShieldCost(wonder));
			}
		}

		MapUnit leader = SpawnScientificLeader(city.location);
		Assert.True(PlayerAI.UseLeaderInCity(leader, us));

		Assert.Equal(wonder, city.itemBeingProduced);
		Assert.Equal(us.ShieldCost(wonder), city.shieldsStored);
		Assert.DoesNotContain(leader, gameData.mapUnits);
	}

	// Elsewhere, it heads for that city.
	[Fact]
	public void TheAISendsItToTheCityWithTheWonder() {
		(City city, _) = PlayerAI.BestWonderForScientificLeader(us);
		Assert.NotNull(city);
		Tile away = gameData.map.tiles.First(t => IsEmptyLand(t) && t.DistanceTo(city.location) is > 1 and < 4);
		MapUnit leader = SpawnScientificLeader(away);

		Assert.False(PlayerAI.UseLeaderInCity(leader, us));
		DefenderAI ai = Assert.IsType<DefenderAI>(leader.currentAI);
		Assert.Equal(city.location, ai.data.destination);
	}

	// With no wonder it could build, the AI finishes the costliest thing
	// it can elsewhere: here, the unit a city is building, which costs more
	// than any improvement it would switch to.
	[Fact]
	public void WithNoWonderTheAIFinishesTheCostliestBuild() {
		foreach (City c in us.cities) {
			c.receivedForestShields = true;
			c.SetItemBeingProduced(BuildingNamed("Temple"));
			c.SetStoredShields(us.ShieldCost(BuildingNamed("Temple")));
		}
		Assert.Null(PlayerAI.BestWonderForScientificLeader(us).Item1);
		UnitPrototype costliest = gameData.unitPrototypes.Where(p => p.shieldCost > 0)
			.OrderByDescending(p => us.ShieldCost(p)).First();
		capital.SetItemBeingProduced(costliest);
		capital.SetStoredShields(0);

		(City city, IProducible item) = PlayerAI.BestBuildForScientificLeader(us);
		Assert.Equal(capital, city);
		Assert.Equal(costliest, item);

		MapUnit leader = SpawnScientificLeader(capital.location);
		Assert.True(PlayerAI.UseLeaderInCity(leader, us));
		Assert.Equal(costliest, capital.itemBeingProduced);
		Assert.Equal(us.ShieldCost(costliest), capital.shieldsStored);
		Assert.DoesNotContain(leader, gameData.mapUnits);
	}

	// A leader in a safe city of ours where it
	// would save at least half as much as at the empire's best target is
	// used there, rather than wander after a best target that keeps
	// changing.
	[Fact]
	public void TheAIUsesItWhereItIsWhenThatSavesEnough() {
		City here = BuildCity(us);
		foreach (City c in us.cities) {
			c.receivedForestShields = true;
			c.SetItemBeingProduced(BuildingNamed("Temple"));
			c.SetStoredShields(us.ShieldCost(BuildingNamed("Temple")));
		}
		UnitPrototype costliest = gameData.unitPrototypes.Where(p => p.shieldCost > 0)
			.OrderByDescending(p => us.ShieldCost(p)).First();
		UnitPrototype cheaper = gameData.unitPrototypes.Where(p => us.ShieldCost(p) < us.ShieldCost(costliest)
			&& 2 * us.ShieldCost(p) >= us.ShieldCost(costliest)).First();
		capital.SetItemBeingProduced(costliest);
		capital.SetStoredShields(0);
		here.SetItemBeingProduced(cheaper);
		here.SetStoredShields(0);
		Assert.Equal(capital, PlayerAI.BestBuildForScientificLeader(us).Item1);

		MapUnit leader = SpawnScientificLeader(here.location);
		Assert.True(PlayerAI.UseLeaderInCity(leader, us));
		Assert.Equal(us.ShieldCost(cheaper), here.shieldsStored);
		Assert.Equal(0, capital.shieldsStored);
	}

	// With nothing to finish anywhere, it waits in the capital.
	[Fact]
	public void WithNothingToFinishTheAIKeepsItInTheCapital() {
		foreach (City c in us.cities) {
			c.receivedForestShields = true;
			c.hurriedThisTurn = true;
			c.SetItemBeingProduced(BuildingNamed("Temple"));
			c.SetStoredShields(us.ShieldCost(BuildingNamed("Temple")));
		}
		(City city, IProducible item) = PlayerAI.BestBuildForScientificLeader(us);
		Assert.Equal(us.cities.FirstOrDefault(c => c.capital), city);
		Assert.Null(item);

		MapUnit leader = SpawnScientificLeader(capital.location);
		Assert.False(PlayerAI.UseLeaderInCity(leader, us));
		Assert.Contains(leader, gameData.mapUnits);
	}

	// "Simply move the scientific leader to your capital and press the
	// "Increase Scientific Output" button" / "When the leader does either of
	// these actions he disappears" (Conquests Civilopedia, GCON_Leaders); an
	// Age of Science is "a 20-turn period" (GCON_Age_of_Science).
	[Fact]
	public void InTheCapitalItStartsAnAgeOfScience() {
		Assert.True(capital.IsCapital());
		City other = BuildCity(us);
		MapUnit elsewhere = SpawnScientificLeader(other.location);
		Assert.DoesNotContain(UnitAction.ScienceAge, elsewhere.GetAvailableActions());
		Assert.False(elsewhere.StartScienceAge());

		MapUnit leader = SpawnScientificLeader(capital.location);
		Assert.Contains(UnitAction.ScienceAge, leader.GetAvailableActions());
		Assert.True(leader.StartScienceAge());

		Assert.DoesNotContain(leader, gameData.mapUnits);
		Assert.Equal(Player.ScienceAgeTurns, us.scienceAgeTurnsRemaining);
		// One at a time.
		Assert.DoesNotContain(UnitAction.ScienceAge, SpawnScientificLeader(capital.location).GetAvailableActions());

		C7GameData.GameData loaded = SaveGame.FromGameData(gameData).ToGameData(fixture.behaviors);
		Assert.Equal(Player.ScienceAgeTurns, loaded.players.Single(p => p.id == us.id).scienceAgeTurnsRemaining);

		for (int i = 0; i < Player.ScienceAgeTurns; i++) {
			Assert.True(us.InScienceAge);
			us.AdvanceGoldenAge();
		}
		Assert.False(us.InScienceAge);
	}

	// The age's research is "25% more productive" (GCON_Age_of_Science),
	// the intended "_Total_Bulbs = (_Total_Bulbs * 1.25);" that the shipped
	// 1.22 executable leaves out (Antal1987,
	// https://forums.civfanatics.com/threads/patch-framework-for-c3c-v1-22.526966/).
	[Fact]
	public void AnAgeOfScienceAddsAQuarterToResearch() {
		Assert.Equal(100, us.WithScienceAgeBonus(100));
		us.scienceAgeTurnsRemaining = 5;
		Assert.Equal(125, us.WithScienceAgeBonus(100));
		Assert.Equal(12, us.WithScienceAgeBonus(10));
	}
}
