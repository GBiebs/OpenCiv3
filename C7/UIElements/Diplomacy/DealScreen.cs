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

	// Hotseat and play by email: the opponent is another human
	// (HumanDiplomacy), whose goods we don't see; the deal on the table
	// between us, if any, and whether some of it had to come off.
	bool withHuman;
	HumanDeal pending;
	bool tableChanged;

	// Online, live (Civ3's multiplayer diplomacy, LiveTalks): both at the
	// table at once, each putting up what they give; the other's half as
	// they last sent it, who has accepted, our face, and a word between us.
	private readonly C7Engine.Online.OnlineGame online;
	private bool live => online != null;
	private string theirTerms = "";
	private bool weAccepted;
	private bool theyAccepted;
	private int ourMood = C7Engine.Online.LiveTalks.Neutral;
	private TextureRect opponentHead;
	private VBoxContainer talkLines;
	private ScrollContainer talkScroll;

	public DealScreen(ID humanPlayer, ID opponentPlayer, TradeOffer humanGives, TradeOffer humanWants) {
		this.humanPlayerId = humanPlayer;
		this.opponentPlayerId = opponentPlayer;
		this.humanOffer = humanGives;
		this.opponentOffer = humanWants;
	}

	public DealScreen(ID humanPlayer, ID opponentPlayer, TradeOffer humanGives, TradeOffer humanWants, HumanDeal pending, bool tableChanged)
		: this(humanPlayer, opponentPlayer, humanGives, humanWants) {
		this.withHuman = true;
		this.pending = pending;
		this.tableChanged = tableChanged;
	}

	public DealScreen(ID humanPlayer, ID opponentPlayer, C7Engine.Online.OnlineGame online)
		: this(humanPlayer, opponentPlayer, new TradeOffer(), new TradeOffer()) {
		this.withHuman = true;
		this.online = online;
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
			opponentHead = GetParent<Diplomacy>().AddLeaderHeadAndLabel(this, opponentPlayer, humanPlayer, fontTheme);

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
				playersAtWar, agreements, opponentGoods, showsGoods: !withHuman);
			AddChild(opponentTree);
			opponentTree.Position = new Vector2(45, 220);
			// (Live, their goods are theirs to put up.)
			opponentTree.Visible = !live;

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
				// Live, their half is as they sent it (an agreement we click
				// goes on ours).
				if (live) {
					CopyInto(C7Engine.Online.LiveTalks.Read(theirTerms, gD), opponentOffer);
				}
				opponentTree.RefreshUiForOffer();
				humanTree.RefreshUiForOffer();
				opponentOfferUi.RefreshUiForOffer();
				humanOfferUi.RefreshUiForOffer();
			};
			syncLive = syncUis;

			// Link up the tree components appropriately (live, only our side
			// is ours to change, and each change goes to them at once).
			if (!live) {
				opponentTree.HandleClicks(humanTree, () => {
					syncUis();
					UpdateText();
				});
			}
			humanTree.HandleClicks(opponentTree, () => {
				syncUis();
				UpdateText();
			});

			humanOfferUi.HandleClicks(opponentOfferUi, () => {
				syncUis();
				UpdateText();
			});
			if (!live) {
				opponentOfferUi.HandleClicks(humanOfferUi, () => {
					syncUis();
					UpdateText();
				});
			}

			if (live) {
				AddLiveControls(opponentPlayer);
				return;
			}

			if (withHuman) {
				AddHumanButtons(opponentPlayer);
				return;
			}

			// Add the buttons at the bottom.
			acceptDeal = new();
			acceptDeal.Text = "\"Will you accept this deal?\"";
			acceptDeal.SetPosition(new Vector2(512 - 205, 650));
			acceptDeal.Theme = fontTheme;
			acceptDeal.Pressed += AttemptDeal;
			AddChild(acceptDeal);

			// Demand what's on their side, giving nothing.
			Button demand = new();
			demand.Text = "\"Give me this, or else!\"";
			demand.SetPosition(new Vector2(512 + 20, 650));
			demand.Theme = fontTheme;
			demand.Pressed += DemandTribute;
			demand.Visible = !playersAtWar;
			AddChild(demand);

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

	// With another human: agree to the table (done if they agreed to it
	// too, otherwise sent to them), send it without agreeing yet, turn it
	// down (or take ours back), or leave it as it is for now.
	private void AddHumanButtons(Player opponentPlayer) {
		acceptDeal = new();
		acceptDeal.SetPosition(new Vector2(512 - 205, 650));
		acceptDeal.Theme = fontTheme;
		acceptDeal.Pressed += () => AnswerHuman(HumanDealAnswer.Accept);
		AddChild(acceptDeal);

		Button send = new();
		send.Text = "\"What do you say to this?\"";
		send.SetPosition(new Vector2(512 + 20, 650));
		send.Theme = fontTheme;
		send.Pressed += () => AnswerHuman(HumanDealAnswer.Send);
		AddChild(send);

		Button goodbye = new();
		goodbye.Text = "\"Never mind...\"";
		goodbye.SetPosition(new Vector2(512 - 205, 670));
		goodbye.Theme = fontTheme;
		goodbye.Pressed += () => { GetParent<Diplomacy>().Hide(); };
		AddChild(goodbye);

		if (pending != null) {
			Button decline = new();
			decline.Text = pending.to == humanPlayerId ? "\"We do not accept.\"" : "\"We withdraw our offer.\"";
			decline.SetPosition(new Vector2(512 + 20, 670));
			decline.Theme = fontTheme;
			decline.Pressed += () => AnswerHuman(HumanDealAnswer.Decline);
			AddChild(decline);
		}

		opponentNoun = opponentPlayer.civilization.noun;
		opponentResponse = new();
		// (Wrapping before the size, or the label doesn't wrap.)
		opponentResponse.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		opponentResponse.SetPosition(new Vector2(512 - 205, 368));
		opponentResponse.Size = new Vector2(410, 70);
		opponentResponse.Theme = fontTheme;
		AddChild(opponentResponse);
		UpdateHumanAcceptText();
	}

	private string opponentNoun;

	// "We accept." once it's the table they agreed to: the trade is done.
	// What the table is, for us.
	private void UpdateHumanAcceptText() {
		bool asTheySentIt = pending != null && !tableChanged
			&& pending.fromGives.SameAs(DealTerms.Of(pending.from == humanPlayerId ? humanOffer : opponentOffer))
			&& pending.toGives.SameAs(DealTerms.Of(pending.from == humanPlayerId ? opponentOffer : humanOffer));
		bool completes = asTheySentIt && pending.to == humanPlayerId && pending.fromAccepted;
		acceptDeal.Text = completes ? "\"We accept.\"" : "\"Will you accept this deal?\"";
		opponentResponse.Text = pending == null
			? $"What the {opponentNoun} have is hidden from us. We put our offer on the table, and they can add to their side: they will see it on their turn."
			: !asTheySentIt && pending.from != humanPlayerId
			? $"We have changed the deal: it goes back to the {opponentNoun} for their answer."
				+ (tableChanged ? " (Some of it was no longer possible, and was taken off the table.)" : "")
			: pending.from == humanPlayerId
			? $"Our offer is on its way to the {opponentNoun}. They haven't answered yet."
			: pending.fromAccepted
			? $"The {opponentNoun} have accepted this deal."
			: $"The {opponentNoun} haven't accepted this deal yet: change it, or accept it and send it back.";
	}

	private void AnswerHuman(HumanDealAnswer answer) {
		if (answer != HumanDealAnswer.Decline && humanOffer.IsEmpty() && opponentOffer.IsEmpty()) {
			opponentResponse.Text = "There is nothing on the table.";
			return;
		}
		new MsgHumanDeal(opponentPlayerId, answer, humanOffer, opponentOffer).send();
		GetParent<Diplomacy>().Hide();
	}

	private void AttemptDeal() {
		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
			Player humanPlayer = gD.players.Find(x => x.id == humanPlayerId);

			// If the deal is acceptable, make it (an order: every game makes
			// it) and go back to the previous screen.
			if (opponentPlayer.WouldAcceptDealFrom(gD, humanPlayer, humanOffer, opponentOffer)) {
				new MsgTradeWithComputer(opponentPlayerId, DealTerms.Of(humanOffer).ToOffer(gD), DealTerms.Of(opponentOffer).ToOffer(gD)).send();

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

	// The AI gives in if it's outmatched (see TributeAI), and minds being
	// asked either way (an order: every game asks).
	private void DemandTribute() {
		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
			Player humanPlayer = gD.players.Find(x => x.id == humanPlayerId);
			if (opponentOffer.IsEmpty()) {
				return;
			}
			bool pays = TributeAI.WouldPay(opponentPlayer, humanPlayer);
			// (A copy: the table here is cleared before the order is played.)
			new MsgDemandTribute(opponentPlayerId, DealTerms.Of(opponentOffer).ToOffer(gD)).send();
			if (pays) {
				opponentResponse.Text = "\"We will give you what you request. But we do this out of a love of peace, not fear.\"";
				opponentOffer.Clear();
				humanOffer.Clear();
				opponentTree.RefreshUiForOffer();
				opponentOfferUi.RefreshUiForOffer();
			} else {
				opponentResponse.Text = "\"We will not be intimidated!\"";
			}
		});
	}

	private void UpdateText() {
		if (live) {
			// Our half changed: it goes to them, and neither of us has accepted
			// it any more.
			weAccepted = false;
			SendTable();
			UpdateLiveText();
			return;
		}
		if (withHuman) {
			UpdateHumanAcceptText();
			return;
		}
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
	private System.Action syncLive;

	// Live: Accept (Civ3's "Do Not Accept" once we have, while they
	// haven't), Cancel, our face (Civ3's emote buttons), and a word at the
	// table where their goods would be.
	private void AddLiveControls(Player opponentPlayer) {
		opponentNoun = opponentPlayer.civilization.noun;
		acceptDeal = new() { Position = new Vector2(512 - 205, 650), Theme = fontTheme };
		acceptDeal.Pressed += () => {
			if (!weAccepted && humanOffer.IsEmpty() && opponentOffer.IsEmpty()) {
				opponentResponse.Text = "There is nothing on the table.";
				return;
			}
			weAccepted = !weAccepted;
			SendTable();
			UpdateLiveText();
		};
		AddChild(acceptDeal);

		Button cancel = new() { Text = "Cancel", Position = new Vector2(512 + 20, 650), Theme = fontTheme };
		// (Leaving the table ends the talk: Diplomacy.)
		cancel.Pressed += () => GetParent<Diplomacy>().Hide();
		AddChild(cancel);

		ButtonGroup faces = new();
		int x = 512 - 205;
		foreach ((string label, int mood) in new[] { ("Pleased", C7Engine.Online.LiveTalks.Pleased), ("Neutral", C7Engine.Online.LiveTalks.Neutral), ("Angry", C7Engine.Online.LiveTalks.Angry) }) {
			Button face = new() { Text = label, Position = new Vector2(x, 675), Theme = fontTheme, ToggleMode = true, ButtonGroup = faces, ButtonPressed = mood == ourMood };
			face.TooltipText = $"The face our leader shows the {opponentNoun}.";
			face.Pressed += () => {
				ourMood = mood;
				SendTable();
			};
			AddChild(face);
			x += 95;
		}

		opponentResponse = new();
		// (Wrapping before the size, or the label doesn't wrap.)
		opponentResponse.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		opponentResponse.SetPosition(new Vector2(512 - 205, 368));
		opponentResponse.Size = new Vector2(410, 70);
		opponentResponse.Theme = fontTheme;
		AddChild(opponentResponse);

		// (Inside the parchment their goods would fill: wrapped, so set before the size.)
		Label talking = new() { Text = $"At the table with the {opponentNoun} (only they see this):", AutowrapMode = TextServer.AutowrapMode.WordSmart, Theme = fontTheme };
		talking.Position = new Vector2(45, 222);
		talking.Size = new Vector2(195, 40);
		AddChild(talking);
		talkScroll = new ScrollContainer { Position = new Vector2(45, 264), Size = new Vector2(195, 316), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		talkLines = new VBoxContainer { CustomMinimumSize = new Vector2(185, 0) };
		talkScroll.AddChild(talkLines);
		AddChild(talkScroll);
		LineEdit say = new() { Position = new Vector2(45, 585), Size = new Vector2(195, 28), MaxLength = C7Engine.Online.Chat.MaxLength, PlaceholderText = "Say something (Enter)", Theme = fontTheme };
		say.TextSubmitted += text => {
			text = text.Trim();
			say.Clear();
			if (text != "") {
				online.Talk(new C7Engine.Online.TalkSay { with = opponentPlayerId.ToString(), text = text });
				LiveLine($"We: {text}");
			}
		};
		AddChild(say);
		UpdateLiveText();
	}

	// Our half of the table, whether we accept it as it stands, our face.
	private void SendTable() {
		online.Talk(new C7Engine.Online.TalkTable {
			with = opponentPlayerId.ToString(),
			terms = C7Engine.Online.LiveTalks.Write(humanOffer),
			accepted = weAccepted,
			mood = ourMood,
		});
	}

	// What the host says of the table: their half, who has accepted, their
	// face.
	public void LiveUpdate(C7Engine.Online.TalkState state) {
		theirTerms = state.theirTerms ?? "";
		theyAccepted = state.theyAccepted;
		weAccepted = state.weAccepted;
		Player opponent = EngineStorage.gameData.GetPlayer(opponentPlayerId);
		if (opponentHead != null && opponent != null) {
			opponentHead.Texture = TextureLoader.Load("leader_heads", new LeaderPortrait(opponent, state.theirMood), useCache: true);
		}
		syncLive?.Invoke();
		UpdateLiveText();
	}

	public void LiveLine(string text) {
		if (talkLines == null) {
			return;
		}
		Label line = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(180, 0), Theme = fontTheme };
		talkLines.AddChild(line);
		// (To the newest, once it's laid out.)
		Callable.From(() => talkScroll.ScrollVertical = (int)talkScroll.GetVScrollBar().MaxValue).CallDeferred();
	}

	private void UpdateLiveText() {
		if (acceptDeal == null || opponentResponse == null) {
			return;
		}
		acceptDeal.Text = weAccepted ? "Do Not Accept" : "Accept";
		opponentResponse.Text = theyAccepted
			? $"The {opponentNoun} have accepted this deal: accept it too, or change it."
			: weAccepted
			? $"We have accepted this deal. Waiting for the {opponentNoun}..."
			: $"We put up what we give, the {opponentNoun} what they give. When both of us accept the table as it stands, the trade is done.";
	}

	// (Their half arrives as a new offer; the screen's parts keep this one.)
	private static void CopyInto(TradeOffer from, TradeOffer to) {
		to.partOfPeaceTreaty = from.partOfPeaceTreaty;
		to.gold = from.gold;
		to.techs = new(from.techs);
		to.agreements = new(from.agreements);
		to.worldMap = from.worldMap;
		to.territoryMap = from.territoryMap;
		to.contacts = new(from.contacts);
		to.resources = new(from.resources);
		to.goldPerTurn = from.goldPerTurn;
		to.cities = new(from.cities);
		to.workers = new(from.workers);
	}
}
