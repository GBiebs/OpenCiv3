using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Trading Civ3's goods: maps, introductions, resources and gold per turn
// (deals that run 20 turns), cities and workers.
public class TradingTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City ourCapital;
	private readonly City theirCapital;

	public TradingTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		ourCapital = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		theirCapital = CityInteractions.BuildCity(them.units[0].location, them, "Abroad");
		us.EnsureRelationshipExists(them);
		gameData.turn = 30;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private DealValueRules Values => gameData.rules.dealValues;
	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);

	[Fact]
	public void AWorldMapShowsEveryTileTheGiverKnows() {
		List<Tile> news = them.tileKnowledge.knownTiles.Where(t => !us.tileKnowledge.isTileKnown(t)).ToList();
		Assert.NotEmpty(news);
		TradeOffer map = new() { worldMap = true };
		Assert.Equal(news.Count / Values.mapTilesPerGold, map.GoldEquivalentFor(gameData, us, them));
		Assert.Equal(0, map.GoldEquivalentFor(gameData, them, them));

		us.ExecuteDeal(gameData, them, map, new TradeOffer());
		Assert.All(news, t => Assert.True(us.tileKnowledge.isTileKnown(t)));
		Assert.Equal(0, map.GoldEquivalentFor(gameData, us, them));
	}

	[Fact]
	public void ATerritoryMapShowsTheGiversLand() {
		List<Tile> land = them.tileKnowledge.OwnedTiles();
		Assert.NotEmpty(land);
		us.ExecuteDeal(gameData, them, new TradeOffer { territoryMap = true }, new TradeOffer());
		Assert.All(land, t => Assert.True(us.tileKnowledge.isTileKnown(t)));
	}

	[Fact]
	public void AnIntroductionMakesContact() {
		Player third = gameData.players.First(p => !p.isBarbarians && p != us && p != them);
		them.EnsureRelationshipExists(third);
		Assert.Contains(third, Trading.Introductions(gameData, them, us));
		TradeOffer introduction = new() { contacts = [third.id] };
		Assert.Equal(Values.contactGold, introduction.GoldEquivalentFor(gameData, us, them));

		us.ExecuteDeal(gameData, them, introduction, new TradeOffer());
		Assert.True(us.playerRelationships.ContainsKey(third.id));
		Assert.False(PlayerRelationship.AtWar(us, third));
		Assert.DoesNotContain(third, Trading.Introductions(gameData, them, us));
	}

	// Wines at their capital (one, or two with a vineyard beside it), and
	// harbors in both capitals so they trade.
	private Resource Wines(bool two = false) {
		Resource wines = gameData.Resources.Find(r => r.Name == "Wines");
		theirCapital.location.Resource = wines;
		if (two) {
			Tile vineyard = theirCapital.location.neighbors.Values.First(t => t.IsLand() && t.OwningPlayer() == them && !t.HasCity());
			vineyard.Resource = wines;
			vineyard.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "road"));
		}
		ourCapital.AddBuilding(Building("Harbor"), gameData);
		theirCapital.AddBuilding(Building("Harbor"), gameData);
		gameData.InvalidateCachedTradeNetwork();
		return wines;
	}

	[Fact]
	public void AResourceDealGivesOneOfTheGiversToTheReceiver() {
		Resource wines = Wines(two: true);
		Assert.Equal(2, Trading.ResourceCount(gameData, them, wines));
		Assert.Equal(0, Trading.ResourceCount(gameData, us, wines));
		TradeOffer luxury = new() { resources = [wines.Key] };
		Assert.Equal(Values.luxuryResourceGold, luxury.GoldEquivalentFor(gameData, us, them));
		Assert.Equal(Values.luxuryResourceGold * Values.spareResourcePercent / 100, luxury.GoldEquivalentFor(gameData, them, them));

		us.ExecuteDeal(gameData, them, luxury, new TradeOffer());
		Assert.Equal(1, Trading.ResourceCount(gameData, them, wines));
		Assert.Equal(1, Trading.ResourceCount(gameData, us, wines));
		Assert.Contains(wines, ourCapital.GetLuxuries(gameData).Keys);
		Assert.Contains(them.playerRelationships[us.id].multiTurnDeals, d => d.dealSubType == DealSubType.LuxuryPerTurn
			&& d.dealDetails == DealDetails.Outbound && d.resourcePerTurn == wines.Key);
	}

	[Fact]
	public void GivingAwayYourOnlyOneLeavesYouWithout() {
		Resource wines = Wines();
		TradeOffer luxury = new() { resources = [wines.Key] };
		Assert.Equal(Values.luxuryResourceGold, luxury.GoldEquivalentFor(gameData, them, them));
		us.ExecuteDeal(gameData, them, luxury, new TradeOffer());
		Assert.Equal(0, Trading.ResourceCount(gameData, them, wines));
		Assert.Equal(1, Trading.ResourceCount(gameData, us, wines));
	}

	[Fact]
	public void NoResourceReachesCapitalsThatDontTrade() {
		Resource wines = Wines();
		ourCapital.RemoveBuilding(ourCapital.constructed_buildings.First(cb => cb.building.name == "Harbor"));
		us.ExecuteDeal(gameData, them, new TradeOffer { resources = [wines.Key] }, new TradeOffer());
		Assert.Equal(0, Trading.ResourceCount(gameData, us, wines));
		Assert.Equal(1, Trading.ResourceCount(gameData, them, wines));
	}

	[Fact]
	public void GoldPerTurnIsPaidForTheDealsDurationThenEndsForBoth() {
		TradeOffer tribute = new() { goldPerTurn = 5 };
		Assert.Equal(5 * gameData.rules.DefaultDealDuration, tribute.GoldEquivalentFor(gameData, us, them));
		us.ExecuteDeal(gameData, them, tribute, new TradeOffer());
		Assert.Equal(5, us.AggregateFlows().fromOtherCivs);
		Assert.Equal(5, them.AggregateFlows().toOtherCivs);
		// The receiver's copy starts the same turn.
		MultiTurnDeal inbound = us.playerRelationships[them.id].multiTurnDeals.Single(d => d.dealSubType == DealSubType.GoldPerTurn);
		Assert.Equal(DealDetails.Inbound, inbound.dealDetails);
		Assert.Equal(gameData.turn, inbound.turnStartDeal);

		gameData.turn += gameData.rules.DefaultDealDuration;
		// One side's check ends it for both.
		PlayerRelationship.CheckForObsoleteDeals(them, gameData.players, gameData.turn);
		Assert.Equal(0, us.AggregateFlows().fromOtherCivs);
		Assert.Equal(0, them.AggregateFlows().toOtherCivs);
	}

	[Fact]
	public void PromisesFromADealBreakerAreWorthNothing() {
		TradeOffer tribute = new() { goldPerTurn = 5 };
		Player third = gameData.players.First(p => !p.isBarbarians && p != us && p != them);
		third.EnsureRelationshipExists(them);
		third.playerRelationships[them.id].brokenDeals = 1;
		Assert.True(Reputation.Tarnished(gameData, them));
		Assert.Equal(0, tribute.GoldEquivalentFor(gameData, us, them));
		// Still a cost to the one paying.
		Assert.Equal(5 * gameData.rules.DefaultDealDuration, tribute.GoldEquivalentFor(gameData, them, them));
	}

	[Fact]
	public void ACityChangesHandsAndItsGarrisonGoesHome() {
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		City town = CityInteractions.BuildCity(site, them, "Town");
		MapUnit guard = gameData.SpawnUnit(them, gameData.unitPrototypes.Find(u => u.name == "Warrior"), site);
		Assert.DoesNotContain(theirCapital, Trading.TradeableCities(them));
		Assert.Contains(town, Trading.TradeableCities(them));

		TradeOffer city = new() { cities = [town.id] };
		int worth = WarRecord.CityValue(gameData, town) * gameData.rules.ShieldValueInGold;
		Assert.Equal(worth, city.GoldEquivalentFor(gameData, us, them));
		Assert.Equal(worth * Values.cityGiveFactor, city.GoldEquivalentFor(gameData, them, them));

		us.ExecuteDeal(gameData, them, city, new TradeOffer());
		Assert.Equal(us, town.owner);
		Assert.Equal(theirCapital.location, guard.location);
		Assert.Contains(guard, theirCapital.location.unitsOnTile);
		Assert.DoesNotContain(guard, site.unitsOnTile);
	}

	[Fact]
	public void AWorkerGoesToItsNewOwnersCapital() {
		MapUnit worker = gameData.SpawnUnit(them, gameData.unitPrototypes.Find(u => u.name == "Worker"), theirCapital.location);
		Assert.Contains(worker, Trading.TradeableWorkers(them));
		TradeOffer sale = new() { workers = [worker.id] };
		Assert.Equal(worker.unitType.shieldCost * gameData.rules.ShieldValueInGold, sale.GoldEquivalentFor(gameData, us, them));
		int before = us.playerRelationships[them.id].attitudePenalty;

		us.ExecuteDeal(gameData, them, sale, new TradeOffer { gold = 10 });
		Assert.Equal(us, worker.owner);
		Assert.Equal(ourCapital.location, worker.location);
		Assert.Contains(worker, us.units);
		// Civ3's AI minds being sold workers.
		Assert.Equal(before + gameData.rules.attitude.soldUsWorkers, us.playerRelationships[them.id].attitudePenalty);
	}

	[Fact]
	public void AIsSwapMapsAndSpareResources() {
		foreach (Player p in new[] { us, them }) {
			p.knownTechs.Add(gameData.techs.First(t => t.flags.Contains(SaveTech.Flag.EnablesMapTrading)).id);
		}
		Resource wines = Wines(two: true);
		// Two of something for us to spare.
		Resource furs = gameData.Resources.Find(r => r.Name == "Furs");
		ourCapital.location.Resource = furs;
		Tile trapline = ourCapital.location.neighbors.Values.First(t => t.IsLand() && t.OwningPlayer() == us && !t.HasCity());
		trapline.Resource = furs;
		trapline.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "road"));
		gameData.InvalidateCachedTradeNetwork();
		List<Tile> theirNews = them.tileKnowledge.knownTiles.Where(t => !us.tileKnowledge.isTileKnown(t)).ToList();

		TradeAI.SwapGoods(us, gameData);
		Assert.All(theirNews, t => Assert.True(us.tileKnowledge.isTileKnown(t)));
		Assert.Equal(1, Trading.ResourceCount(gameData, us, wines));
		Assert.Equal(1, Trading.ResourceCount(gameData, them, furs));
	}

	[Fact]
	public void NoSwapsWithoutTheAdvanceOrAtWar() {
		List<Tile> theirNews = them.tileKnowledge.knownTiles.Where(t => !us.tileKnowledge.isTileKnown(t)).ToList();
		TradeAI.SwapGoods(us, gameData);
		Assert.Contains(theirNews, t => !us.tileKnowledge.isTileKnown(t));

		foreach (Player p in new[] { us, them }) {
			p.knownTechs.Add(gameData.techs.First(t => t.flags.Contains(SaveTech.Flag.EnablesMapTrading)).id);
		}
		us.DeclareWarOn(them, gameData.turn);
		TradeAI.SwapGoods(us, gameData);
		Assert.Contains(theirNews, t => !us.tileKnowledge.isTileKnown(t));
	}
}
