using C7Engine;
using C7Engine.AI;
using C7GameData;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static C7GameData.PlayerRelationship;

// At a high level the deal screen has 4 parts; 2 "TradingTree"s per player, and
// 2 "TradeOfferUi"s per player.
//
// The trading tree is the far left and right parts of the screen, where sections
// can be collapsed (like technology, gold, etc). The trade offer ui is the part
// in the middle, where we show what is actually being offered.
//
// The basic idea is to have all the possible items present in both components,
// and then use the shared TradeOffer object to determine what is visible. So
// when a technology is clicked in the trading tree, it is added to the
// TradeOffer object, and then we refresh the UIs of the trading tree and the
// trade offer ui to swap the visibility.
//
// Things that aren't yet implemented:
//  - Gifts
//  - Demands
//  - What do you need for ...
//  - What will you give me for ...
//  - Per-turn deals
//  - Interesting messages from the opponent player
public partial class DealScreen : TextureRect {
	private ID humanPlayerId;
	private ID opponentPlayerId;

	Theme fontTheme = new();
	FontFile font = new();

	TradingTree opponentTree;
	TradingTree humanTree;

	TradeOfferUi opponentOfferUi;
	TradeOfferUi humanOfferUi;

	Button acceptDeal;
	Label opponentResponse;

	TradeOffer opponentOffer = new();
	TradeOffer humanOffer = new();

	public DealScreen(ID humanPlayer, ID opponentPlayer, TradeOffer humanGives, TradeOffer humanWants) {
		this.humanPlayerId = humanPlayer;
		this.opponentPlayerId = opponentPlayer;
		this.humanOffer = humanGives;
		this.opponentOffer = humanWants;
	}

	public override void _Ready() {
		this.CreateUI();
	}

	private void CreateUI() {
		// Load the font we'll use.
		//
		// We skip the cache so that we can change the size without affecting other
		// code using the same font.
		font = ResourceLoader.Load<FontFile>("res://Fonts/NotoSans-Regular.ttf", null, ResourceLoader.CacheMode.Ignore);
		font.FixedSize = 13;
		fontTheme.DefaultFont = font;

		Theme blueFontTheme = new();
		blueFontTheme.DefaultFont = font;
		blueFontTheme.SetColor("font_color", "Label", Colors.Blue);

		this.Texture = TextureLoader.Load("diplomacy.deal");

		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
			Player humanPlayer = gD.players.Find(x => x.id == humanPlayerId);
			bool playersAtWar = AtWar(humanPlayer, opponentPlayer);
			GetParent<Diplomacy>().AddLeaderHeadAndLabel(this, opponentPlayer, humanPlayer, fontTheme);

			// Figure out which technologies can be traded by each player, if any.
			List<Tech> techsOpponentCanTrade = gD.techs.FindAll(x => {
				return opponentPlayer.knownTechs.Contains(x.id) && !humanPlayer.knownTechs.Contains(x.id);
			});
			List<Tech> techsHumanCanTrade = gD.techs.FindAll(x => {
				return humanPlayer.knownTechs.Contains(x.id) && !opponentPlayer.knownTechs.Contains(x.id);
			});

			// The agreements we could sign, and any the opponent proposes.
			List<Agreement> possible = Agreements.Available(gD, humanPlayer, opponentPlayer);
			possible.AddRange(Player.AgreementsIn(humanOffer, opponentOffer).Where(a => !possible.Any(p => p.SameAs(a))));
			List<(Agreement, string)> agreements = possible.Select(a => (a, a.Describe(gD))).ToList();
			List<DealGood> opponentGoods = DealGood.Of(gD, opponentPlayer, humanPlayer);
			List<DealGood> humanGoods = DealGood.Of(gD, humanPlayer, opponentPlayer);

			// Left hand side UI components.
			opponentTree = new TradingTree(fontTheme, opponentPlayer.gold, techsOpponentCanTrade, opponentOffer,
				playersAtWar, agreements, opponentGoods);
			AddChild(opponentTree);
			opponentTree.Position = new Vector2(45, 220);

			Label weWant = new();
			weWant.Text = "We Want";
			weWant.SetPosition(new Vector2(320, 440));
			weWant.Theme = blueFontTheme;
			AddChild(weWant);

			opponentOfferUi = new(fontTheme, techsOpponentCanTrade, opponentPlayer.gold, opponentOffer, playersAtWar,
				agreements, opponentGoods, HorizontalAlignment.Left);
			AddChild(opponentOfferUi);
			opponentOfferUi.Position = new Vector2(314, 453);

			// Right hand side UI components.
			humanTree = new TradingTree(fontTheme, humanPlayer.gold, techsHumanCanTrade, humanOffer, playersAtWar, agreements, humanGoods);
			AddChild(humanTree);
			humanTree.Position = new Vector2(789, 220);

			Label weOffer = new();
			weOffer.Text = "We Offer";
			weOffer.SetPosition(new Vector2(660, 440));
			weOffer.Theme = blueFontTheme;
			AddChild(weOffer);

			humanOfferUi = new(fontTheme, techsHumanCanTrade, humanPlayer.gold, humanOffer, playersAtWar,
				agreements, humanGoods, HorizontalAlignment.Right);
			AddChild(humanOfferUi);
			humanOfferUi.Position = new Vector2(527, 453);

			var syncUis = () => {
				opponentTree.RefreshUiForOffer();
				humanTree.RefreshUiForOffer();
				opponentOfferUi.RefreshUiForOffer();
				humanOfferUi.RefreshUiForOffer();
			};

			// Link up the tree components appropriately.
			opponentTree.HandleClicks(humanTree, () => {
				syncUis();
				UpdateText();
			});
			humanTree.HandleClicks(opponentTree, () => {
				syncUis();
				UpdateText();
			});

			humanOfferUi.HandleClicks(opponentOfferUi, () => {
				syncUis();
				UpdateText();
			});
			opponentOfferUi.HandleClicks(humanOfferUi, () => {
				syncUis();
				UpdateText();
			});

			// Add the buttons at the bottom.
			acceptDeal = new();
			acceptDeal.Text = "\"Will you accept this deal?\"";
			acceptDeal.SetPosition(new Vector2(512 - 205, 650));
			acceptDeal.Theme = fontTheme;
			acceptDeal.Pressed += AttemptDeal;
			AddChild(acceptDeal);

			Button goodbye = new();
			goodbye.Text = "\"Never mind...\"";
			goodbye.SetPosition(new Vector2(512 - 205, 670));
			goodbye.Theme = fontTheme;
			goodbye.Pressed += () => { GetParent<Diplomacy>().Hide(); };
			AddChild(goodbye);

			// And the text from the opponent, if they reject a deal.
			opponentResponse = new();
			// An offer of peace or an agreement from the AI comes with its terms.
			opponentResponse.Text = opponentOffer.partOfPeaceTreaty
				? "\"This war is pointless for both of us. Will you accept these terms?\""
				: opponentOffer.agreements.Count > 0
				? $"\"Will you agree to this {opponentOffer.agreements[0].Describe(gD)}?\""
				: "\"Let's get down to business...\"";
			opponentResponse.SetPosition(new Vector2(512 - 205, 345));
			opponentResponse.Theme = fontTheme;
			AddChild(opponentResponse);
		});
	}

	private void AttemptDeal() {
		// TODO: This seems to be only usage of UIGameDataAccess that mutates
		// state - can this be a message instead?
		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
			Player humanPlayer = gD.players.Find(x => x.id == humanPlayerId);

			// If the deal is acceptable, execute it and go back to the previous
			// screen.
			if (opponentPlayer.WouldAcceptDealFrom(gD, humanPlayer, humanOffer, opponentOffer)) {
				opponentPlayer.ExecuteDeal(gD, humanPlayer, humanOffer, opponentOffer);

				GetParent<Diplomacy>().ShowTalkScreenForPlayer(humanPlayerId, opponentPlayerId);
			} else if (humanOffer.partOfPeaceTreaty && PeaceAI.PeaceValue(gD, opponentPlayer, humanPlayer) is null) {
				opponentResponse.Text = "\"Not likely! This war shall go on.\"";
			} else if (!humanOffer.partOfPeaceTreaty && Agreements.Embargoed(opponentPlayer, humanPlayer)) {
				opponentResponse.Text = "\"We have agreed not to trade with you.\"";
			} else if (Player.AgreementsIn(humanOffer, opponentOffer).Any(a => !AgreementAI.WouldSign(gD, opponentPlayer, humanPlayer, a))) {
				opponentResponse.Text = "\"We cannot agree to that at this time.\"";
			} else {
				opponentResponse.Text = "\"That is not enough.\"";
			}
		});
	}

	private void UpdateText() {
		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
			Player humanPlayer = gD.players.Find(x => x.id == humanPlayerId);

			int theirGoldValue = opponentOffer.GoldEquivalentFor(gD, opponentPlayer, opponentPlayer);
			int ourGoldValue = humanOffer.GoldEquivalentFor(gD, opponentPlayer, humanPlayer);
			opponentResponse.Text =
				$"\"I value my offer at {theirGoldValue} gold and I value your offer at {ourGoldValue} gold\"";
			// At war, peace is what matters.
			if (humanOffer.partOfPeaceTreaty && AtWar(humanPlayer, opponentPlayer)) {
				opponentResponse.Text = PeaceAI.PeaceValue(gD, opponentPlayer, humanPlayer) switch {
					null => "\"Not likely! This war shall go on.\"",
					< 0 and int price => $"\"Give us {-price} gold's worth and we'll call it a tie.\"",
					_ => "\"This war is pointless for both of us. Let us end it.\"",
				};
			}
		});
	}
}
