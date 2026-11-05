using C7Engine;
using C7GameData;
using Xunit;

namespace EngineTests.Online;

// Online, which computers show a message to the UI: every game plays the
// same orders, so each one's engine sends every message, and each
// computer shows only what concerns its own player.
public class MessageFilterTest {
	private static readonly ID us = ID.FromString("player-2");
	private static readonly ID them = ID.FromString("player-3");
	private static readonly ID third = ID.FromString("player-4");

	[Fact]
	public void TheActingPlayersOwnConfirmationsShowOnlyOnTheirComputer() {
		// (Sent by the engine while it plays their order.)
		MessageToUI moved = new MsgShowTemporaryPopup("This unit has already moved.", null);
		Assert.True(MessageRecipients.ForComputerOf(moved, local: them, acting: them));
		Assert.False(MessageRecipients.ForComputerOf(moved, local: us, acting: them));
	}

	[Fact]
	public void NewsShowsOnTheComputersOfThoseItConcerns() {
		MessageToUI captured = new MsgCityCaptured(null, them, us, 0, [], 0);
		Assert.True(MessageRecipients.ForComputerOf(captured, local: us, acting: them));
		Assert.True(MessageRecipients.ForComputerOf(captured, local: them, acting: them));
		Assert.False(MessageRecipients.ForComputerOf(captured, local: third, acting: them));
	}

	[Fact]
	public void NewsForEveryoneShowsOnEveryComputer() {
		MessageToUI war = new MsgWarDeclarationNotification(null, null);
		Assert.True(MessageRecipients.ForComputerOf(war, local: third, acting: them));
	}

	[Fact]
	public void WhatAComputersOwnScreensSendShowsThereWhoeverIsPlaying() {
		EngineStorage.messagesToUI.Clear();
		// The menu, opened while another plays.
		MessageToUI menu = new MsgGameMainMenu();
		menu.sendFromScreens();
		Assert.True(menu.fromScreens);
		Assert.True(MessageRecipients.ForComputerOf(menu, local: us, acting: them));
		Assert.Contains(menu, EngineStorage.messagesToUI);

		// The same message from the engine is the acting player's.
		MessageToUI fromEngine = new MsgGameMainMenu();
		fromEngine.send();
		Assert.False(fromEngine.fromScreens);
		Assert.False(MessageRecipients.ForComputerOf(fromEngine, local: us, acting: them));
		EngineStorage.messagesToUI.Clear();
	}

	[Fact]
	public void TurnsAndHandOversAreLeftToTheUiToSortOut() {
		Assert.True(MessageRecipients.ForComputerOf(new MsgStartTurn(them), local: us, acting: them));
		Assert.True(MessageRecipients.ForComputerOf(new MsgHandOver(them), local: us, acting: them));
	}
}
