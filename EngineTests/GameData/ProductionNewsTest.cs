using System;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// What a human hears when production finishes (Civ3, script.txt
// IMPROVEMENT_COMPLETE, CITYPRODUCE, WONDERPRODUCE, WONDERCHANGE): a unit is
// built again and named; after an improvement the advisor's pick goes ahead
// and the player is asked; everyone else hears of a great wonder; a city
// that was building that wonder switches and its human is told.
public class ProductionNewsTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City city;
	private readonly City theirs;

	public ProductionNewsTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		us = ais[0];
		them = ais[1];
		us.isHuman = true;
		city = CityInteractions.BuildCity(us.units[0].location, us, "Seat");
		theirs = CityInteractions.BuildCity(them.units[0].location, them, "There");
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private void Finish(City c, IProducible item) {
		c.SetItemBeingProduced(item);
		c.SetStoredShields(c.owner.ShieldCost(item));
		c.HandleCityProduction(gameData);
	}

	private Building Named(string name) => gameData.Buildings.Find(b => b.name == name);

	private MsgProductionComplete Heard() => Assert.Single(EngineStorage.messagesToUI.OfType<MsgProductionComplete>());

	[Fact]
	public void AFinishedImprovementAsksWhatNext() {
		Building barracks = Named("Barracks");
		Finish(city, barracks);

		MsgProductionComplete news = Heard();
		Assert.Equal(us.id, news.playerId);
		Assert.Same(city, news.city);
		Assert.Same(barracks, news.produced);
		Assert.True(news.askWhatNext);
		Assert.Equal("Seat builds Barracks.", news.Text());
		// The advisor's pick is already under way.
		Assert.NotSame(barracks, city.itemBeingProduced);
		Assert.Contains($"It is now working on {city.itemBeingProduced.name}.", NewsWhileAway.Describe(news, us));
	}

	[Fact]
	public void AFinishedUnitIsBuiltAgainWithoutAsking() {
		UnitPrototype warrior = gameData.unitPrototypes.Find(u => u.name == "Warrior");
		Finish(city, warrior);

		MsgProductionComplete news = Heard();
		Assert.False(news.askWhatNext);
		Assert.Same(warrior, city.itemBeingProduced);
		Assert.Equal("Seat builds Warrior.", news.Text());
	}

	[Fact]
	public void AGovernorChoosesWithoutAsking() {
		city.governor.manageProduction = true;
		Finish(city, Named("Barracks"));

		Assert.False(Heard().askWhatNext);
	}

	[Fact]
	public void TheComputersHearNothing() {
		Finish(theirs, Named("Barracks"));

		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgProductionComplete>());
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgWonderCompleted>());
	}

	[Fact]
	public void AGreatWonderIsNewsForEveryoneAndSwitchesItsRivals() {
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder() && b.spaceshipPart == null);
		city.SetItemBeingProduced(wonder);
		city.SetStoredShields(10);
		Finish(theirs, wonder);

		MsgWonderCompleted done = Assert.Single(EngineStorage.messagesToUI.OfType<MsgWonderCompleted>());
		Assert.Same(them, done.builder);
		Assert.Same(theirs, done.city);
		Assert.True(MessageRecipients.ForEveryone(done));
		Assert.Null(done.TextFor(them));
		us.playerRelationships.Remove(them.id);
		Assert.Equal($"Word has reached us that a distant civilization has completed {wonder.name}.", done.TextFor(us));
		us.EnsureRelationshipExists(them);
		Assert.Equal($"Word has reached us that the {them.civilization.adjective} city of There has completed {wonder.name}.", done.TextFor(us));

		// Our city was building it: it has switched, and we're told.
		MsgProductionSwitched switched = Assert.Single(EngineStorage.messagesToUI.OfType<MsgProductionSwitched>());
		Assert.Equal(us.id, switched.playerId);
		Assert.Same(city, switched.city);
		Assert.Same(wonder, switched.lost);
		Assert.NotSame(wonder, city.itemBeingProduced);
		Assert.Same(city.itemBeingProduced, switched.now);
		Assert.Equal([us.id], MessageRecipients.Of(switched));
	}

	[Fact]
	public void OurOwnGreatWonderIsAnnouncedAndAsksWhatNext() {
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder() && b.spaceshipPart == null);
		Finish(city, wonder);

		MsgProductionComplete news = Heard();
		Assert.True(news.IsGreatWonder);
		Assert.True(news.askWhatNext);
		Assert.Equal($"We have completed {wonder.name} in Seat!", news.Text());
		Assert.Null(Assert.Single(EngineStorage.messagesToUI.OfType<MsgWonderCompleted>()).TextFor(us));
	}
}
