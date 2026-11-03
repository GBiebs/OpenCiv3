using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Hotseat: news for a human who isn't playing waits for their turn
// (MessageRecipients); what's part of the playing human's own flow, and
// prompts, which hand over the UI instead, are never held.
public class MessageRecipientsTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player playing;
	private readonly Player waiting;
	private readonly Player ai;

	public MessageRecipientsTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		playing = gameData.players.First(p => p.isHuman);
		Player[] others = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(waiting, ai) = (others[0], others[1]);
		waiting.isHuman = true;
	}

	private City CityOf(Player owner) => CityInteractions.BuildCity(owner.units[0].location, owner, "Town");

	[Fact]
	public void NewsForTheWaitingHumanIsHeldForThem() {
		City theirs = CityOf(waiting);
		MsgCityCaptured captured = new(theirs, ai.id, waiting.id, 0, [], 0);
		Assert.Equal([waiting.id], MessageRecipients.HeldFor(captured, gameData, playing.id));
		Assert.Equal([waiting.id], MessageRecipients.HeldFor(new MsgGoldenAge(waiting.id, true), gameData, playing.id));
	}

	[Fact]
	public void NewsForThePlayingHumanIsntHeld() {
		Assert.Empty(MessageRecipients.HeldFor(new MsgGoldenAge(playing.id, true), gameData, playing.id));
		Assert.Empty(MessageRecipients.HeldFor(new MsgGoldenAge(ai.id, true), gameData, playing.id));
	}

	// Both humans are concerned: the one playing sees it now, the other later.
	[Fact]
	public void NewsForBothIsHeldForTheOther() {
		City theirs = CityOf(waiting);
		MsgCityCaptured captured = new(theirs, playing.id, waiting.id, 0, [], 0);
		Assert.Equal([waiting.id], MessageRecipients.HeldFor(captured, gameData, playing.id));
	}

	[Fact]
	public void NewsForEveryoneIsHeldForEveryOtherHuman() {
		Assert.Equal([waiting.id], MessageRecipients.HeldFor(new MsgPeaceTreatySigned(ai, playing), gameData, playing.id));
	}

	// The playing human's own flow, and prompts (handed over first).
	[Fact]
	public void PromptsAndTheirOwnFlowArentHeld() {
		Assert.Empty(MessageRecipients.HeldFor(new MsgNameCity(waiting.id, "Town"), gameData, playing.id));
		Assert.Empty(MessageRecipients.HeldFor(new MsgTributeDemanded(ai, waiting, new TradeOffer()), gameData, playing.id));
	}

	[Fact]
	public void NothingIsHeldForADefeatedHumanOrWithOneHuman() {
		waiting.defeated = true;
		Assert.Empty(MessageRecipients.HeldFor(new MsgGoldenAge(waiting.id, true), gameData, playing.id));
		waiting.defeated = false;
		waiting.isHuman = false;
		Assert.Empty(MessageRecipients.HeldFor(new MsgPeaceTreatySigned(ai, waiting), gameData, playing.id));
	}
}
