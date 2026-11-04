using Godot;
using System;
using C7GameData;
using C7Engine;
using Serilog;
using ConvertCiv3Media;
using System.Collections.Generic;


// A container for all the diplomacy-related screens that aren't simple popups.
public partial class Diplomacy : CenterContainer {
	private ILogger log = LogManager.ForContext<Diplomacy>();

	[Export]
	public PopupOverlay popupOverlay;

	private TalkScreen talkScreen;
	private DealScreen dealScreen;

	public override void _Ready() {
		this.Hide();
		Hidden += () => { new MsgDiplomacyCompleted().send(); };
	}

	private void RemoveOtherScreens() {
		if (talkScreen != null) {
			RemoveChild(talkScreen);
			talkScreen = null;
		}
		if (dealScreen != null) {
			RemoveChild(dealScreen);
			dealScreen = null;
		}
	}

	public void ShowDealScreenForPlayer(ID humanPlayer, ID opponentPlayer) {
		ShowDealScreenForPlayer(humanPlayer, opponentPlayer, new TradeOffer(), new TradeOffer());
	}

	public void ShowDealScreenForPlayer(ID humanPlayer, ID opponentPlayer, TradeOffer humanGives, TradeOffer humanWants) {
		RemoveOtherScreens();

		dealScreen = new DealScreen(humanPlayer, opponentPlayer, humanGives, humanWants);
		AddChild(dealScreen);
		this.Show();
	}

	// Hotseat and play by email: the deal on the table with another human
	// (HumanDiplomacy), what's no longer possible taken off, or a new one.
	public void ShowHumanDealScreen(ID humanPlayer, ID opponentPlayer) {
		RemoveOtherScreens();

		GameData gd = EngineStorage.gameData;
		Player us = gd.GetPlayer(humanPlayer);
		Player them = gd.GetPlayer(opponentPlayer);
		HumanDeal pending = HumanDiplomacy.Between(gd, us, them);
		TradeOffer weGive = new();
		TradeOffer theyGive = new();
		bool changed = false;
		if (pending != null) {
			(weGive, theyGive) = HumanDiplomacy.Tables(gd, pending, us, them, out changed);
		}
		dealScreen = new DealScreen(humanPlayer, opponentPlayer, weGive, theyGive, pending, changed);
		AddChild(dealScreen);
		this.Show();
	}

	public void ShowTalkScreenForPlayer(ID humanPlayer, ID opponentPlayer) {
		RemoveOtherScreens();

		GameData gd = EngineStorage.gameData;

		Player opponent = gd.players.Find(x => x.id == opponentPlayer);
		Player human = gd.players.Find(x => x.id == humanPlayer);
		if (opponent.isHuman) {
			// Another human may have asked our envoys to stay away (though
			// their own deal can still be answered).
			int refused = HumanDiplomacy.EnvoysRefusedFor(gd, human, opponent);
			if (refused > 0 && HumanDiplomacy.Between(gd, human, opponent)?.to != human.id) {
				new MsgDiplomacyNews(human.id, $"The {opponent.civilization.noun} have asked us not to send envoys for {refused} more turns.", good: false).send();
				return;
			}
		} else if (!opponent.WillAcceptCommunicationFrom(human, gd.turn)) {
			new MsgRefuseContact(opponentPlayer).send();
			return;
		}

		talkScreen = new TalkScreen(humanPlayer, opponentPlayer);
		AddChild(talkScreen);
		this.Show();
	}

	// The leader of `player`, with how they feel about `towards`.
	public void AddLeaderHeadAndLabel(TextureRect node, Player player, Player towards, Theme fontTheme) {
		ColorRect headBackground = new();
		headBackground.Color = Colors.Black;
		headBackground.Size = new Vector2(200, 240);
		headBackground.SetPosition(new Vector2(512 - 100, 59));
		node.AddChild(headBackground);

		TextureRect leaderHead = new();
		leaderHead.Texture = TextureLoader.Load("leader_heads", player);
		leaderHead.Scale = new Vector2(1.7f, 1.7f);
		leaderHead.SetPosition(new Vector2(512 - (115 * 1.7f) / 2, 59 + 120 - (115 * 1.7f) / 2));
		node.AddChild(leaderHead);

		// (A human's attitude is theirs to show.)
		string civNameText = player.isHuman
			? player.civilization.name
			: $"{player.civilization.name} ({Attitude.Of(EngineStorage.gameData, player, towards)})";
		Label civName = new();
		civName.SetPosition(new Vector2(0, 330));
		node.AddChild(civName);
		civName.Theme = fontTheme;
		civName.SetTextAndCenterLabel(civNameText);
	}
}
