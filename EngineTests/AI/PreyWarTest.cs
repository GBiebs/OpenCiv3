using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI;

// The prey-war switch (see PreyWar): an Advanced AI whose plan names a
// rival as prey (the Target stance RulePlanner.PlanPeace sets once land
// runs out) gets a war campaign on it at once, and declares once the stack
// has gathered. Off, or for a Standard AI, the plan's stance alone starts
// nothing: Civ3's plotting rolls decide, and here they all fail.
public sealed class PreyWarTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Random originalRandom;
	private City ours;
	private City theirs;

	// Every roll fails, so Civ3's war plotting starts nothing by itself.
	private class HighRandom : Random {
		protected override double Sample() => 0.999999;
	}

	public PreyWarTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		originalRandom = gameData.random;
		gameData.random = new HighRandom();
		Player[] civs = gameData.players.Where(p => !p.isBarbarians && !p.isHuman).Take(2).ToArray();
		us = civs[0];
		them = civs[1];
		us.EnsureRelationshipExists(them);
		them.EnsureRelationshipExists(us);
		OneCityEach();
		foreach (Tile t in gameData.map.tiles) {
			us.tileKnowledge.knownTiles.Add(t);
		}
	}

	public void Dispose() {
		gameData.random = originalRandom;
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private City BuildCity(Player owner, Func<Tile, bool> where) {
		Tile tile = gameData.map.tiles.First(t => t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp
			&& !t.IsImpassable() && t.IsAllowCities() && t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity()) && where(t));
		return CityInteractions.BuildCity(tile, owner, $"City {owner.cities.Count}");
	}

	// One city each on the same landmass, theirs 5 to 10 tiles from ours,
	// and none of our old units.
	private void OneCityEach() {
		foreach (City c in us.cities.ToList()) {
			CityInteractions.DestroyCity(c);
		}
		foreach (MapUnit u in us.units.ToList()) {
			us.units.Remove(u);
			u.location.unitsOnTile.Remove(u);
		}
		ours = BuildCity(us, _ => true);
		List<City> old = them.cities.ToList();
		theirs = BuildCity(them, t => t.continent == ours.location.continent && t.DistanceTo(ours.location) >= 5 && t.DistanceTo(ours.location) <= 10);
		foreach (City c in old) {
			CityInteractions.DestroyCity(c);
		}
	}

	// The plan RulePlanner.PlanPeace makes with no room left and a much
	// weaker neighbour: them as the target.
	private void PlanToTakeThem(AiType type) {
		us.aiType = type;
		us.strategicPlan = new StrategicPlan { source = "rules", setTurn = gameData.turn, aggressionShift = 1 };
		us.strategicPlan.stances[them.id.ToString()] = RivalStance.Target;
	}

	// A defender to stay home and enough attackers for the stack, in our
	// city.
	private void Army(int attackers) {
		UnitPrototype spearman = gameData.unitPrototypes.First(p => p.name == "Spearman");
		UnitPrototype archer = gameData.unitPrototypes.First(p => p.name == "Archer");
		gameData.SpawnUnit(us, spearman, ours.location).currentAI = null;
		for (int i = 0; i < attackers; ++i) {
			gameData.SpawnUnit(us, archer, ours.location).currentAI = null;
		}
	}

	private bool AtWar() => us.playerRelationships[them.id].AtWar();

	[Fact]
	public async Task OffThePreyPlanStartsNothing() {
		PlanToTakeThem(AiType.Advanced);
		Army(PreyWar.MinAttackers + 2);
		await WarCampaign.PlayTurn(us, gameData);
		Assert.False(WarCampaign.HasCampaign(us));
		Assert.False(AtWar());
	}

	[Fact]
	public async Task AStandardAiWithTheSwitchStartsNothing() {
		PlanToTakeThem(AiType.Standard);
		Army(PreyWar.MinAttackers + 2);
		AiExperiments.Enable(us, PreyWar.Switch);
		Assert.False(PreyWar.On(us));
		await WarCampaign.PlayTurn(us, gameData);
		Assert.False(WarCampaign.HasCampaign(us));
		Assert.False(AtWar());
	}

	[Fact]
	public async Task OnThePreyGetsACampaignThatWaitsForItsStack() {
		PlanToTakeThem(AiType.Advanced);
		AiExperiments.Enable(us, PreyWar.Switch);
		Army(2);
		await WarCampaign.PlayTurn(us, gameData);
		Assert.True(WarCampaign.HasPreyCampaign(us));
		Assert.True(WarCampaign.IsPlannedTarget(us, them));
		// Two archers are not the stack it wants: no war yet.
		Assert.False(AtWar());
		Assert.True(WarCampaign.WantsAttackers(us));
	}

	[Fact]
	public async Task OnTheGatheredStackDeclaresWar() {
		PlanToTakeThem(AiType.Advanced);
		AiExperiments.Enable(us, PreyWar.Switch);
		Army(PreyWar.MinAttackers + 2);
		await WarCampaign.PlayTurn(us, gameData);
		Assert.True(AtWar());
		Assert.True(WarCampaign.HasCampaignAgainst(us, them));
	}

	[Fact]
	public async Task OnNoCampaignInThePeaceCooldown() {
		PlanToTakeThem(AiType.Advanced);
		AiExperiments.Enable(us, PreyWar.Switch);
		Army(PreyWar.MinAttackers + 2);
		gameData.turn = 50;
		us.playerRelationships[them.id].peaceSignedTurn = gameData.turn - 1;
		them.playerRelationships[us.id].peaceSignedTurn = gameData.turn - 1;
		us.strategicPlan.setTurn = gameData.turn;
		Assert.Null(PreyWar.Prey(us, gameData));
		await WarCampaign.PlayTurn(us, gameData);
		Assert.False(WarCampaign.HasCampaign(us));
		Assert.False(AtWar());
	}

	[Fact]
	public async Task OnAFriendStanceCallsOffTheGathering() {
		PlanToTakeThem(AiType.Advanced);
		AiExperiments.Enable(us, PreyWar.Switch);
		Army(2);
		await WarCampaign.PlayTurn(us, gameData);
		Assert.True(WarCampaign.HasPreyCampaign(us));
		us.strategicPlan.stances[them.id.ToString()] = RivalStance.Friend;
		await WarCampaign.PlayTurn(us, gameData);
		Assert.False(WarCampaign.HasCampaign(us));
		Assert.False(AtWar());
	}

	// Their city grown with their own people, and with culture they made
	// in it: by Civ3's flip formula, that many nationals in a city that is
	// their capital, and remembers more of their culture than ours, make it
	// likely to flip back to them.
	private void FillTheirCity(int people) {
		theirs.perPlayerCulture[them] = 1000;
		CityResident template = theirs.residents[0];
		for (int i = 0; i < people; ++i) {
			theirs.AddCitizen(new CityResident { city = theirs, nationality = them.civilization, citizenType = template.citizenType });
		}
	}

	[Fact]
	public void FlipChanceRisesWithTheirPeopleAndFallsWithTheGarrison() {
		double small = PreyWar.FlipChanceIfTaken(gameData, us, them, theirs, 1);
		FillTheirCity(15);
		double big = PreyWar.FlipChanceIfTaken(gameData, us, them, theirs, 1);
		double held = PreyWar.FlipChanceIfTaken(gameData, us, them, theirs, PreyWar.MaxFlipGarrison);
		Assert.True(big > small);
		Assert.True(held < big);
		Assert.True(held > PreyWar.MaxTargetFlipChance);
		Assert.False(PreyWar.WorthKeeping(gameData, us, theirs));
		Assert.True(PreyWar.KeepChance(gameData, us, theirs) < 0.5);
	}

	[Fact]
	public void ASmallCityIsWorthKeepingWithFewUnits() {
		Assert.True(PreyWar.WorthKeeping(gameData, us, theirs));
		Assert.True(PreyWar.GarrisonAgainstFlip(gameData, us, them, theirs) <= PreyWar.MaxFlipGarrison);
	}

	[Fact]
	public async Task OnACityLikelyToFlipBackIsNoTarget() {
		PlanToTakeThem(AiType.Advanced);
		AiExperiments.Enable(us, PreyWar.Switch);
		Army(PreyWar.MinAttackers + 2);
		FillTheirCity(15);
		await WarCampaign.PlayTurn(us, gameData);
		Assert.False(WarCampaign.HasCampaign(us));
		Assert.False(AtWar());
	}

	private static void Teleport(MapUnit u, Tile t) {
		u.location.unitsOnTile.Remove(u);
		t.unitsOnTile.Add(u);
		u.location = t;
	}

	// The stack that takes a city leaves enough of itself there to hold it
	// against a flip back, not just the one defender other campaigns leave.
	[Fact]
	public async Task OnATakenCityKeepsEnoughToHoldItAgainstAFlip() {
		PlanToTakeThem(AiType.Advanced);
		AiExperiments.Enable(us, PreyWar.Switch);
		Army(PreyWar.MinAttackers + 2);
		// A second city near it, so that they live on once this one falls,
		// with their palace near.
		BuildCity(them, t => t.continent == theirs.location.continent && t.DistanceTo(theirs.location) is >= 3 and <= 4
			&& t.DistanceTo(ours.location) > theirs.location.DistanceTo(ours.location));
		while (PreyWar.GarrisonAgainstFlip(gameData, us, them, theirs) < PreyWar.MaxFlipGarrison && theirs.residents.Count < 40) {
			FillTheirCity(1);
		}
		Assert.True(PreyWar.WorthKeeping(gameData, us, theirs));
		await WarCampaign.PlayTurn(us, gameData);
		Assert.True(AtWar());

		// The stack storms the city.
		foreach (MapUnit u in theirs.location.unitsOnTile.ToList()) {
			gameData.RemoveUnit(u);
		}
		CityInteractions.CaptureCity(theirs, us);
		foreach (MapUnit u in us.units.Where(u => u.currentAI is WarCampaign.MemberAI).ToList()) {
			Teleport(u, theirs.location);
		}
		await WarCampaign.PlayTurn(us, gameData);

		int want = PreyWar.GarrisonAgainstFlip(gameData, us, them, theirs);
		int held = theirs.location.unitsOnTile.Count(u => u.owner == us && u.currentAI is not WarCampaign.MemberAI);
		Assert.InRange(want, 2, PreyWar.MaxFlipGarrison);
		Assert.True(held >= want, $"held {held}, wanted {want}");
	}
}
