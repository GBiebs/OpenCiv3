using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;
using static C7GameData.ForeignAffairs;

namespace EngineTests.GameData;

// The foreign advisor: the lines between the civs' portraits, our treaties
// and deals with one civ, the best of its units we know about, and the
// advice.
public class ForeignAffairsTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Player third;
	private readonly Player stranger;

	public ForeignAffairsTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(4).ToArray();
		(us, them, third, stranger) = (ais[0], ais[1], ais[2], ais[3]);
		foreach (Player p in ais.Take(3)) {
			CityInteractions.BuildCity(p.units[0].location, p, $"{p.civilization.name} home");
		}
		us.EnsureRelationshipExists(them);
		us.EnsureRelationshipExists(third);
		them.EnsureRelationshipExists(third);
		Embassies.Open(us, them);
		foreach (Player p in new[] { us, them }) {
			foreach (string tech in new[] { "Writing", "Map Making", "Nationalism" }) {
				p.knownTechs.Add(gameData.techs.Find(t => t.Name == tech).id);
			}
		}
		gameData.turn = 30;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private void Sign(DealSubType type, Player against = null) {
		Agreements.Sign(gameData, us, them, new Agreement { type = type, against = against?.id });
	}

	[Fact]
	public void TheLinesBetweenTwoCivs() {
		Assert.Empty(Between(us, stranger));
		Assert.Equal([Relation.Peace], Between(us, them));

		Sign(DealSubType.RightOfPassage);
		Sign(DealSubType.MutualProtectionPact);
		Assert.Equal([Relation.Peace, Relation.MutualProtection, Relation.RightOfPassage], Between(us, them));
		Assert.Equal(Between(us, them), Between(them, us));

		// An embargo we signed against the third civ is a line between us
		// and it.
		Sign(DealSubType.TradeEmbargo, third);
		Assert.Contains(Relation.Embargo, Between(us, third));
		Assert.DoesNotContain(Relation.Embargo, Between(us, them));

		them.DeclareWarOn(third, gameData.turn);
		Assert.Equal(Relation.War, Between(them, third)[0]);
	}

	[Fact]
	public void OurTreatiesWithACiv() {
		string noun = them.civilization.noun;
		Assert.Equal([$"We are at peace with the {noun}."], Treaties(gameData, us, them));

		Sign(DealSubType.RightOfPassage);
		them.DeclareWarOn(third, gameData.turn);
		List<string> lines = Treaties(gameData, us, them);
		Assert.Equal($"We are at peace with the {noun}.", lines[0]);
		Assert.Contains($"Right of Passage ({gameData.rules.DefaultDealDuration} turns left)", lines);
		Assert.Contains($"They are at war with the {third.civilization.noun}.", lines);

		us.DeclareWarOn(them, gameData.turn);
		Assert.Equal($"We are at war with the {noun}.", Treaties(gameData, us, them)[0]);
		Assert.Empty(Treaties(gameData, us, stranger));
	}

	[Fact]
	public void OurDealsWithACiv() {
		Assert.Empty(Deals(gameData, us, them));
		Resource resource = gameData.Resources.First(r => r.Category == ResourceCategory.LUXURY);
		Trading.Give(gameData, us, them, new TradeOffer { goldPerTurn = 5 });
		Trading.Give(gameData, them, us, new TradeOffer { resources = [resource.Key] });

		int turns = gameData.rules.DefaultDealDuration;
		Assert.Equal([$"They give us {resource.Name} ({turns} turns left)", $"We give them 5 gold a turn ({turns} turns left)"],
			Deals(gameData, us, them).Order());
		Assert.Equal([$"They give us 5 gold a turn ({turns} turns left)", $"We give them {resource.Name} ({turns} turns left)"],
			Deals(gameData, them, us).Order());
	}

	[Fact]
	public void TheBestOfTheirUnitsWeKnowAbout() {
		Assert.Null(BestKnownUnit(us, them));
		UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);
		// A Spearman next to our unit, a stronger Swordsman far away.
		Tile near = us.units[0].location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		gameData.SpawnUnit(them, Type("Spearman"), near);
		Tile far = gameData.map.tiles.First(t => t.IsLand() && !t.HasCity() && !us.tileKnowledge.isActiveTile(t));
		gameData.SpawnUnit(them, Type("Swordsman"), far);
		us.tileKnowledge.RecomputeActiveTiles();
		Assert.Equal("Spearman", BestKnownUnit(us, them).name);

		// A spy shows all of them.
		us.playerRelationships[them.id].hasSpy = true;
		Assert.Equal("Swordsman", BestKnownUnit(us, them).name);
	}

	[Fact]
	public void TheForeignAdvisorsAdvice() {
		AdvisorAdvice.Advice none = AdvisorAdvice.Foreign(gameData, stranger);
		Assert.Equal(AdvisorAdvice.Mood.Surprised, none.mood);

		Assert.Equal(AdvisorAdvice.Mood.Happy, AdvisorAdvice.Foreign(gameData, us).mood);

		them.playerRelationships[us.id].attitudePenalty = 100;
		AdvisorAdvice.Advice upset = AdvisorAdvice.Foreign(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Sad, upset.mood);
		Assert.Equal($"The {them.civilization.noun} are furious with us.", upset.text);

		us.DeclareWarOn(third, gameData.turn);
		AdvisorAdvice.Advice war = AdvisorAdvice.Foreign(gameData, us);
		Assert.Equal(AdvisorAdvice.Mood.Angry, war.mood);
		Assert.Equal($"We are at war with the {third.civilization.noun}!", war.text);
	}
}
