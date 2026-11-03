using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;
using static C7GameData.TradeReport;

namespace EngineTests.GameData;

// The trade advisor: our resources (own, imported, exported), the civs we
// trade with or could, our cities off the capital's network, and the advice.
public class TradeReportTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City ourCapital;
	private readonly City theirCapital;

	public TradeReportTest(SaveGameFixture fixture) {
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

	private Resource Luxury(string name) => gameData.Resources.Find(r => r.Name == name);

	// Harbors in both capitals, so they trade (see TradingTest).
	private void Harbors() {
		Building harbor = gameData.Buildings.Find(b => b.name == "Harbor");
		ourCapital.AddBuilding(harbor, gameData);
		theirCapital.AddBuilding(harbor, gameData);
		gameData.InvalidateCachedTradeNetwork();
	}

	private Partner PartnerFor(Player civ) => Partners(gameData, us).Single(p => p.civ == civ);

	[Fact]
	public void OurResourcesOwnImportedAndExported() {
		Resource furs = Luxury("Furs"), wines = Luxury("Wines");
		ourCapital.location.Resource = furs;
		theirCapital.location.Resource = wines;
		Harbors();
		ResourceRow Row(Resource r) => Resources(gameData, us).Single(row => row.resource == r);
		Assert.Equal(new ResourceRow(furs, 1, 0, 0), Row(furs));
		Assert.Equal(new ResourceRow(wines, 0, 0, 0), Row(wines));

		Trading.Give(gameData, them, us, new TradeOffer { resources = [wines.Key] });
		Trading.Give(gameData, us, them, new TradeOffer { resources = [furs.Key] });
		gameData.InvalidateCachedTradeNetwork();
		Assert.Equal(new ResourceRow(wines, 0, 1, 0), Row(wines));
		Assert.Equal(new ResourceRow(furs, 1, 0, 1), Row(furs));
	}

	[Fact]
	public void TheCivsWeTradeWithOrCould() {
		Partner none = PartnerFor(them);
		Assert.Equal(Standing.CannotTrade, none.standing);
		Assert.Equal("No trade route joins our capitals.", none.reason);

		Harbors();
		Assert.Equal(Standing.CanTrade, PartnerFor(them).standing);
		Trading.Give(gameData, them, us, new TradeOffer { goldPerTurn = 3 });
		Assert.Equal(Standing.Trading, PartnerFor(them).standing);

		us.DeclareWarOn(them, gameData.turn);
		Partner war = PartnerFor(them);
		Assert.Equal(Standing.CannotTrade, war.standing);
		Assert.Equal("We are at war.", war.reason);
	}

	[Fact]
	public void CitiesOffTheCapitalsNetwork() {
		Tile site = gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.OwningPlayer() == null
			&& t.DistanceTo(ourCapital.location) is >= 4 and <= 8).First();
		City far = CityInteractions.BuildCity(site, us, "Faraway");
		gameData.InvalidateCachedTradeNetwork();
		Assert.Equal([far], Unconnected(gameData, us));

		AdvisorAdvice.Advice advice = AdvisorAdvice.Trade(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Sad, advice.mood);
		Assert.Equal("Faraway isn't joined to our capital, so it gets none of our resources.", advice.text);
	}

	[Fact]
	public void TheTradeAdvisorsAdvice() {
		Assert.Equal("We have no one to trade with yet.", AdvisorAdvice.Trade(gameData, us).text);

		// They have Wines, which we lack.
		Resource wines = Luxury("Wines");
		theirCapital.location.Resource = wines;
		Harbors();
		AdvisorAdvice.Advice advice = AdvisorAdvice.Trade(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Surprised, advice.mood);
		Assert.Equal($"The {them.civilization.noun} could trade us Wines.", advice.text);
		Assert.Equal([wines], TheyCouldSell(gameData, us, them));

		// Two Furs of our own: one to spare.
		theirCapital.location.Resource = Resource.NONE;
		Resource furs = Luxury("Furs");
		ourCapital.location.Resource = furs;
		Tile trapline = ourCapital.location.neighbors.Values.First(t => t.IsLand() && t.OwningPlayer() == us && !t.HasCity());
		trapline.Resource = furs;
		trapline.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "road"));
		gameData.InvalidateCachedTradeNetwork();
		Assert.Equal("We have Furs to spare for trade.", AdvisorAdvice.Trade(gameData, us).text);
	}
}
