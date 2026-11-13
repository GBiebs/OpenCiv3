using System.Collections.Generic;
using C7GameData;
using Xunit;

namespace EngineTests.GameData;

public class AdvisorReportsTest {

	[Theory]
	[InlineData(0, "Fledgling")]
	[InlineData(9, "Fledgling")]
	[InlineData(10, "Weak")]
	[InlineData(999, "Fragile")]
	[InlineData(1000, "Solid")]
	[InlineData(50000, "Strong")]
	[InlineData(100000, "Glorious")]
	[InlineData(5000000, "Glorious")]
	public void CultureLevel_TakesTenTimesTheCultureForEachLevel(int culture, string expected) {
		Assert.Equal(expected, CultureReport.CultureLevel(new Rules(), culture));
	}

	[Theory]
	[InlineData(100, 300, "in awe of")]
	[InlineData(100, 299, "admirers of")]
	[InlineData(100, 100, "impressed with")]
	[InlineData(100, 75, "unimpressed by")]
	[InlineData(100, 50, "dismissive of")]
	[InlineData(100, 33, "disdainful of")]
	[InlineData(100, 0, "disdainful of")]
	[InlineData(0, 0, "impressed with")]
	public void OpinionOf_ComparesOurCultureToTheirs(int theirs, int ours, string expected) {
		Assert.Equal(expected, CultureReport.OpinionOf(new Rules(), theirs, ours));
	}

	[Fact]
	public void Article_UsesAnBeforeVowels() {
		Assert.Equal("an", CultureReport.Article("Admired"));
		Assert.Equal("a", CultureReport.Article("Solid"));
	}

	private static (Player us, Player them) TwoPlayersAtPeace() {
		Player us = new() { civilization = new Civilization("Rome"), id = ID.FromString("player-1") };
		Player them = new() { civilization = new Civilization("Greece"), id = ID.FromString("player-2") };
		us.playerRelationships[them.id] = new PlayerRelationship { multiTurnDeals = { MultiTurnDeal.DEFAULT_PEACE } };
		them.playerRelationships[us.id] = new PlayerRelationship { multiTurnDeals = { MultiTurnDeal.DEFAULT_PEACE } };
		return (us, them);
	}

	[Fact]
	public void StatusWith_CanTradeAtPeaceWithoutDeals() {
		(Player us, Player them) = TwoPlayersAtPeace();
		Assert.Equal(TradeStatus.CanTradeWith, TradeReport.StatusWith(us, them));
	}

	[Fact]
	public void StatusWith_CannotTradeAtWar() {
		(Player us, Player them) = TwoPlayersAtPeace();
		us.playerRelationships[them.id].multiTurnDeals.Clear();
		Assert.Equal(TradeStatus.CannotTradeWith, TradeReport.StatusWith(us, them));
	}

	[Fact]
	public void StatusWith_CannotTradeUnderAnEmbargoEitherWay() {
		(Player us, Player them) = TwoPlayersAtPeace();
		Player third = new() { civilization = new Civilization("Egypt"), id = ID.FromString("player-3") };
		third.playerRelationships[them.id] = new PlayerRelationship();
		third.playerRelationships[them.id].multiTurnDeals.Add(new MultiTurnDeal(DealType.Embargo,
			DealSubType.TradeEmbargo, DealDetails.Exchange, againstPlayer: us.id));
		// Them joining an embargo against us.
		them.playerRelationships[third.id] = new PlayerRelationship();
		them.playerRelationships[third.id].multiTurnDeals.Add(new MultiTurnDeal(DealType.Embargo,
			DealSubType.TradeEmbargo, DealDetails.Exchange, againstPlayer: us.id));

		Assert.Equal(TradeStatus.CannotTradeWith, TradeReport.StatusWith(us, them));
		Assert.True(TradeReport.HasEmbargoAgainst(them, us));
		Assert.False(TradeReport.HasEmbargoAgainst(us, them));
	}

	[Fact]
	public void ExcessResources_NeedsTwoWithOneNotAlreadyTraded() {
		Player them = new() { civilization = new Civilization("Greece"), id = ID.FromString("player-2") };
		Resource wine = new() { Key = "wine", Name = "Wine", Category = ResourceCategory.LUXURY };
		Resource silk = new() { Key = "silk", Name = "Silk", Category = ResourceCategory.LUXURY };
		Resource iron = new() { Key = "iron", Name = "Iron", Category = ResourceCategory.STRATEGIC };
		Resource gems = new() { Key = "gems", Name = "Gems", Category = ResourceCategory.LUXURY };
		Dictionary<Resource, int> available = new() { [wine] = 1, [silk] = 2, [iron] = 3, [gems] = 2 };
		// Both gems already go to other civs; one of the silks does.
		List<ResourceDeal> outbound = [new(gems, them, 10), new(gems, them, 10), new(silk, them, 10)];

		List<(Resource resource, int count)> excess = TradeReport.ExcessResources(available, outbound);

		Assert.Equal([(silk, 2), (iron, 3)], excess);
	}

	[Fact]
	public void ResourceDeals_ListsTheResourcesComingInAndGoingOut() {
		(Player us, Player them) = TwoPlayersAtPeace();
		Resource wine = new() { Key = "wine", Name = "Wine", Category = ResourceCategory.LUXURY };
		Resource iron = new() { Key = "iron", Name = "Iron", Category = ResourceCategory.STRATEGIC };
		us.playerRelationships[them.id].multiTurnDeals.Add(new MultiTurnDeal(DealType.Luxury,
			DealSubType.LuxuryPerTurn, DealDetails.Inbound, resourcePerTurn: "wine", dealDuration: 20, turnStartDeal: 5));
		us.playerRelationships[them.id].multiTurnDeals.Add(new MultiTurnDeal(DealType.Resource,
			DealSubType.ResourcePerTurn, DealDetails.Outbound, resourcePerTurn: "iron", dealDuration: 20, turnStartDeal: 5));

		C7GameData.GameData gameData = new() { turn = 10 };
		gameData.players.AddRange(new List<Player> { us, them });
		gameData.Resources.AddRange(new List<Resource> { wine, iron });

		List<ResourceDeal> imports = TradeReport.ResourceDeals(gameData, us, DealDetails.Inbound);
		List<ResourceDeal> exports = TradeReport.ResourceDeals(gameData, us, DealDetails.Outbound);

		Assert.Equal(new ResourceDeal(wine, them, 15), Assert.Single(imports));
		Assert.Equal(new ResourceDeal(iron, them, 15), Assert.Single(exports));
		Assert.Equal(TradeStatus.TradingWith, TradeReport.StatusWith(us, them));
	}

	// The military advisor lists a rival's army in full with an embassy or a
	// spy ("The Military Advisor gets a complete list of their forces from
	// an embassy", Civ3 manual), and otherwise only what our troops see.
	[Fact]
	public void KnownUnits_AreAllWithAnEmbassyOrSpyAndOtherwiseThoseInSight() {
		(Player us, Player them) = TwoPlayersAtPeace();
		UnitPrototype warrior = new() { name = "Warrior", attack = 1, defense = 1 };
		Tile seen = new(ID.None("seen"));
		Tile unseen = new(ID.None("unseen"));
		MapUnit inSight = new(ID.FromString("unit-1")) { unitType = warrior, owner = them, location = seen };
		MapUnit outOfSight = new(ID.FromString("unit-2")) { unitType = warrior, owner = them, location = unseen };
		them.units.Add(inSight);
		them.units.Add(outOfSight);
		us.tileKnowledge.knownTiles.Add(seen);
		us.tileKnowledge.knownTiles.Add(unseen);
		us.tileKnowledge.Peek([seen]);

		Assert.Equal([inSight], MilitaryReport.KnownUnits(us, them));
		Assert.Equal("We only know what our troops report.", MilitaryReport.Intelligence(us, them));

		us.playerRelationships[them.id].hasEmbassy = true;
		Assert.Equal([inSight, outOfSight], MilitaryReport.KnownUnits(us, them));
		Assert.Equal("We have: An Embassy", MilitaryReport.Intelligence(us, them));

		us.playerRelationships[them.id].hasEmbassy = false;
		us.playerRelationships[them.id].hasSpy = true;
		Assert.Equal([inSight, outOfSight], MilitaryReport.KnownUnits(us, them));
		Assert.Equal("We have: A Spy", MilitaryReport.Intelligence(us, them));

		us.playerRelationships[them.id].hasEmbassy = true;
		Assert.Equal("We have: An Embassy, A Spy", MilitaryReport.Intelligence(us, them));
		Assert.Equal("", MilitaryReport.Intelligence(us, us));
	}
}
