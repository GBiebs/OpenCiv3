using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Online;

// Online, every game must change alike, so what the screens used to change
// themselves is an order now (played in every game), and the citizens'
// moods are worked out by the engine, never by a screen.
public class ScreenChangesTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly ID controller = EngineStorage.uiControllerID;

	public ScreenChangesTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	public void Dispose() {
		EngineStorage.AbandonWork();
		EngineStorage.animationsEnabled = true;
		EngineStorage.uiControllerID = controller;
	}

	private (C7GameData.GameData, Player human, Player computer) Load() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.AbandonWork();
		Player human = gameData.players.First(p => p.isHuman);
		Player computer = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		EngineStorage.uiControllerID = human.id;
		return (gameData, human, computer);
	}

	private static long seq;

	private static void Play(Player from, MessageToEngine order) {
		EngineStorage.EnqueueSequenced(++seq, 7, from.id, order);
		for (int i = 0; i < 10_000 && (EngineStorage.sequencedOrders.Count > 0 || !EngineStorage.OrdersDone()); i++) {
			EngineStorage.ProcessNextMessageToEngine();
			EngineStorage.messagesToUI.Clear();
		}
	}

	[Fact]
	public void ContactAndWarAreOrders() {
		(C7GameData.GameData gameData, Player human, Player computer) = Load();
		Assert.False(human.playerRelationships.ContainsKey(computer.id));
		Play(human, new MsgMakeContact(computer.id));
		Assert.True(human.IsAtPeaceWith(computer));
		Play(human, new MsgDeclareWar(computer.id));
		Assert.False(human.IsAtPeaceWith(computer));
	}

	// A gift the computer takes; a deal it wouldn't take isn't made.
	[Fact]
	public void TradingWithAComputerIsAnOrder() {
		(C7GameData.GameData gameData, Player human, Player computer) = Load();
		Play(human, new MsgMakeContact(computer.id));
		human.gold = 100;
		int theirs = computer.gold;
		Play(human, new MsgTradeWithComputer(computer.id, new TradeOffer { gold = 10 }, new TradeOffer()));
		Assert.Equal(90, human.gold);
		Assert.Equal(theirs + 10, computer.gold);
		computer.gold = 1000;
		Play(human, new MsgTradeWithComputer(computer.id, new TradeOffer(), new TradeOffer { gold = 1000 }));
		Assert.Equal(90, human.gold);
	}

	// The luxury rate and a specialist change the moods in the engine (in
	// every game), and reading a city's mood changes nothing.
	[Fact]
	public void TheEngineWorksOutTheMoods() {
		(C7GameData.GameData gameData, Player human, _) = Load();
		City city = CityInteractions.BuildCity(human.units[0].location, human, "Uruk");
		string Moods() => string.Join(",", city.residents.Select(r => r.mood));
		city.RecalculateCitizenMoods(gameData);
		string fresh = Moods();
		foreach (CityResident resident in city.residents) {
			resident.mood = CityResident.Mood.Unhappy;
		}
		Assert.Equal(City.Mood.Unhappy, city.CurrentMood());
		Assert.NotEqual(fresh, Moods());

		Play(human, new MsgChangeSliders(human.id, MsgChangeSliders.DomesticPolicyChoice.MoreLuxury));
		Play(human, new MsgChangeSliders(human.id, MsgChangeSliders.DomesticPolicyChoice.LessLuxury));
		Assert.Equal(fresh, Moods());

		// (A specialist has no mood: a second citizen shows it.)
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		city.AddCitizen(new CityResident { city = city, nationality = human.civilization, citizenType = laborer });
		city.residents[0].citizenType = human.GetKnownSpecialists(gameData)[0];
		// (Whatever the mood should be, one of the two placeholders is wrong:
		// cycling the specialist puts it right.)
		foreach (CityResident.Mood placeholder in new[] { CityResident.Mood.Happy, CityResident.Mood.Unhappy }) {
			city.residents[1].mood = placeholder;
			Assert.True(CityInteractions.CycleSpecialist(gameData, city, 0));
			string afterCycling = Moods();
			city.RecalculateCitizenMoods(gameData);
			Assert.Equal(Moods(), afterCycling);
		}
	}
}
