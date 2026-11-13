using C7Engine;
using C7GameData;
using Godot;
using Resource = C7GameData.Resource;
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
// A computer opponent answers the deal on the table as Civ3's does, in its
// words from diplomacy.txt, by how close the deal is to one it would take
// (see CounterOfferAI.Reaction), rather than showing what it is worth. The
// human may ask it what it wants for what is on its side, or what it would
// give for what is on the human's (CounterOfferAI.Counter and Exchange).
//
// Things that aren't yet implemented:
//  - Gifts
//  - Demands
public partial class DealScreen : TextureRect {
	private ID humanPlayerId;
	private ID opponentPlayerId;

	Theme fontTheme = new();
	FontFile font;

	TradingTree opponentTree;
	TradingTree humanTree;

	TradeOfferUi opponentOfferUi;
	TradeOfferUi humanOfferUi;

	Button acceptDeal;
	Label opponentResponse;

	// The "Active Deals" button swaps the negotiation for a list of the
	// deals already in force, and back.
	Button activeDealsButton;
	Control activeDealsPanel;
	readonly List<CanvasItem> negotiationControls = new();

	TradeOffer opponentOffer = new();
	TradeOffer humanOffer = new();

	// What the opponent says first, if it made the offer with words of its
	// own (see DealProposalAI.Greeting), or null.
	private readonly string greeting;

	// Civ3's "What do you want for $GIVE0?" and "Would you care to offer
	// something in exchange for $GIVE0?", shown while there is something on
	// the opponent's or the human's side, in peace with a computer player.
	Button askCounter;
	Button askExchange;

	// Which of its lines the opponent says, so its words change only when
	// its view of the deal does.
	private readonly int linePick = System.Random.Shared.Next(1000);

	public DealScreen(ID humanPlayer, ID opponentPlayer, TradeOffer humanGives, TradeOffer humanWants, string greeting = null) {
		this.humanPlayerId = humanPlayer;
		this.opponentPlayerId = opponentPlayer;
		this.humanOffer = humanGives;
		this.opponentOffer = humanWants;
		this.greeting = greeting;
	}

	public override void _Ready() {
		this.CreateUI();
	}

	private void CreateUI() {
		// Load the font we'll use, at a fixed size that doesn't affect other
		// code using the same font.
		font = FixedSizeFonts.Get("res://Fonts/NotoSans-Regular.ttf", 13);
		fontTheme.DefaultFont = font;

		Theme blueFontTheme = new();
		blueFontTheme.DefaultFont = font;
		blueFontTheme.SetColor("font_color", "Label", Colors.Blue);

		this.Texture = TextureLoader.Load("diplomacy.deal");

		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
			Player humanPlayer = gD.players.Find(x => x.id == humanPlayerId);
			bool playersAtWar = AtWar(humanPlayer, opponentPlayer);
			bool canSignRightOfPassage = !playersAtWar && RightOfPassage.CanSign(gD, humanPlayer, opponentPlayer);
			GetParent<Diplomacy>().AddLeaderHeadAndLabel(this, gD, opponentPlayer, humanPlayer, fontTheme);

			// Figure out which technologies can be traded by each player, if any.
			// A tech can only go to a player who could research it themselves.
			List<Tech> techsOpponentCanTrade = opponentPlayer.GetTechsTradableTo(humanPlayer, gD.techs);
			List<Tech> techsHumanCanTrade = humanPlayer.GetTechsTradableTo(opponentPlayer, gD.techs);
			List<City> citiesOpponentCanTrade = TradeOffer.TradableCities(opponentPlayer);
			List<City> citiesHumanCanTrade = TradeOffer.TradableCities(humanPlayer);
			// The workers in each capital (see TradeOffer.workers), and any
			// already on the table.
			List<MapUnit> workersOpponentCanTrade = TradeOffer.TradableWorkers(opponentPlayer)
				.Union(opponentOffer.workers).ToList();
			List<MapUnit> workersHumanCanTrade = TradeOffer.TradableWorkers(humanPlayer)
				.Union(humanOffer.workers).ToList();
			List<Resource> resourcesOpponentCanTrade = TradeOffer.TradableResources(gD, opponentPlayer, humanPlayer);
			List<Resource> resourcesHumanCanTrade = TradeOffer.TradableResources(gD, humanPlayer, opponentPlayer);
			// Resources go only between civs whose capitals are connected
			// (see TradeOffer.ProblemWithDeal); otherwise they show grayed out.
			bool capitalsConnected = gD.GetTradeNetwork().CapitalsConnected(humanPlayer, opponentPlayer);
			// Resources already on the table (as in an offer from an AI)
			// show even if no longer spare.
			AddOffered(gD, resourcesOpponentCanTrade, opponentOffer);
			AddOffered(gD, resourcesHumanCanTrade, humanOffer);

			// The pacts the two may sign (see Pacts), and any already on
			// the table.
			TradingTree.PactChoices pacts = new(
				!playersAtWar && Pacts.ProblemSigningMutualProtectionPact(gD, humanPlayer, opponentPlayer) == null,
				playersAtWar ? new() : Pacts.AllianceTargets(gD, humanPlayer, opponentPlayer),
				playersAtWar ? new() : Pacts.EmbargoTargets(gD, humanPlayer, opponentPlayer));
			void AddTarget(List<Player> targets, ID target) {
				Player p = target == null ? null : gD.GetPlayer(target);
				if (p != null && !targets.Contains(p)) {
					targets.Add(p);
				}
			}
			AddTarget(pacts.allianceTargets, TradeOffer.AllianceTarget(humanOffer, opponentOffer));
			AddTarget(pacts.embargoTargets, TradeOffer.EmbargoTarget(humanOffer, opponentOffer));

			// The maps and introductions each side may give, and any
			// already on the table.
			TradingTree.MapChoices MapChoicesFor(Player giver, Player receiver, TradeOffer offer) {
				List<Player> contacts = playersAtWar ? new() : TradeOffer.TradableContacts(gD, giver, receiver);
				foreach (ID id in offer.contacts) {
					AddTarget(contacts, id);
				}
				return new(!playersAtWar && TradeOffer.ProblemTradingMaps(gD, giver, receiver) == null, contacts);
			}
			TradingTree.MapChoices opponentMaps = MapChoicesFor(opponentPlayer, humanPlayer, opponentOffer);
			TradingTree.MapChoices humanMaps = MapChoicesFor(humanPlayer, opponentPlayer, humanOffer);

			// Left hand side UI components.
			opponentTree = new TradingTree(fontTheme, opponentPlayer.gold, techsOpponentCanTrade, citiesOpponentCanTrade,
				opponentOffer, playersAtWar, canSignRightOfPassage,
				TradeOffer.MaxGoldPerTurn(opponentPlayer), resourcesOpponentCanTrade, pacts, opponentMaps, capitalsConnected,
				workersOpponentCanTrade);
			AddChild(opponentTree);
			opponentTree.Position = new Vector2(45, 220);

			Label weWant = new();
			weWant.Text = "We Want";
			weWant.SetPosition(new Vector2(320, 440));
			weWant.Theme = blueFontTheme;
			AddChild(weWant);

			opponentOfferUi = new(fontTheme, techsOpponentCanTrade, citiesOpponentCanTrade, opponentPlayer.gold, opponentOffer, playersAtWar,
				HorizontalAlignment.Left, resourcesOpponentCanTrade, pacts, opponentMaps, workersOpponentCanTrade);
			AddChild(opponentOfferUi);
			opponentOfferUi.Position = new Vector2(314, 453);

			// Right hand side UI components.
			humanTree = new TradingTree(fontTheme, humanPlayer.gold, techsHumanCanTrade, citiesHumanCanTrade, humanOffer, playersAtWar,
				canSignRightOfPassage, TradeOffer.MaxGoldPerTurn(humanPlayer), resourcesHumanCanTrade, pacts, humanMaps, capitalsConnected,
				workersHumanCanTrade);
			AddChild(humanTree);
			humanTree.Position = new Vector2(789, 220);

			Label weOffer = new();
			weOffer.Text = "We Offer";
			weOffer.SetPosition(new Vector2(660, 440));
			weOffer.Theme = blueFontTheme;
			AddChild(weOffer);

			humanOfferUi = new(fontTheme, techsHumanCanTrade, citiesHumanCanTrade, humanPlayer.gold, humanOffer, playersAtWar,
				HorizontalAlignment.Right, resourcesHumanCanTrade, pacts, humanMaps, workersHumanCanTrade);
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
			acceptDeal.SetPosition(new Vector2(512 - 205, 630));
			acceptDeal.Theme = fontTheme;
			acceptDeal.Pressed += AttemptDeal;
			AddChild(acceptDeal);

			// At war, Civ3 lets the human ask an AI what it wants for peace
			// (Conquests diplomacy.txt, #USERASKFORCOUNTER: "What do you want
			// for $GIVE0?").
			if (playersAtWar && !opponentPlayer.isHuman) {
				Button askTerms = new();
				askTerms.Text = "\"What do you want for a peace treaty?\"";
				askTerms.SetPosition(new Vector2(512 - 205, 610));
				askTerms.Theme = fontTheme;
				askTerms.Pressed += () => { new MsgAskForPeaceTerms(opponentPlayer) { playerID = humanPlayerId }.send(); };
				AddChild(askTerms);
				negotiationControls.Add(askTerms);
			}

			// In peace, the human may ask for a counter-offer.
			if (!playersAtWar && !opponentPlayer.isHuman) {
				askCounter = new() { Theme = fontTheme, Visible = false };
				askCounter.SetPosition(new Vector2(512 - 205, 590));
				askCounter.Pressed += () => AskForCounterOffer(exchange: false);
				AddChild(askCounter);
				askExchange = new() { Theme = fontTheme, Visible = false };
				askExchange.SetPosition(new Vector2(512 - 205, 610));
				askExchange.Pressed += () => AskForCounterOffer(exchange: true);
				AddChild(askExchange);
			}

			Button goodbye = new();
			goodbye.Text = "\"Never mind...\"";
			goodbye.SetPosition(new Vector2(512 - 205, 650));
			goodbye.Theme = fontTheme;
			goodbye.Pressed += () => { GetParent<Diplomacy>().Hide(); };
			AddChild(goodbye);

			activeDealsButton = new();
			activeDealsButton.Text = "Active Deals";
			activeDealsButton.SetPosition(new Vector2(512 - 205, 670));
			activeDealsButton.Theme = fontTheme;
			activeDealsButton.Pressed += ToggleActiveDeals;
			AddChild(activeDealsButton);

			negotiationControls.AddRange(new CanvasItem[] {
				opponentTree, humanTree, weWant, weOffer, opponentOfferUi, humanOfferUi, acceptDeal,
			});
			activeDealsPanel = CreateActiveDealsPanel(gD, humanPlayer, opponentPlayer, blueFontTheme);
			activeDealsPanel.Visible = false;
			AddChild(activeDealsPanel);

			// And the text from the opponent, if they reject a deal.
			opponentResponse = new();
			opponentResponse.Text = greeting != null ? $"\"{greeting}\"" : "\"Let's get down to business...\"";
			opponentResponse.SetPosition(new Vector2(512 - 205, 345));
			opponentResponse.Theme = fontTheme;
			AddChild(opponentResponse);

			RefreshAskButtons(gD, opponentPlayer, humanPlayer);
		});
	}

	private void ToggleActiveDeals() {
		bool showDeals = !activeDealsPanel.Visible;
		activeDealsPanel.Visible = showDeals;
		foreach (CanvasItem control in negotiationControls) {
			control.Visible = !showDeals;
		}
		activeDealsButton.Text = showDeals ? "Back to the negotiation" : "Active Deals";
		EngineStorage.ReadGameData((GameData gD) => {
			RefreshAskButtons(gD, gD.GetPlayer(opponentPlayerId), gD.GetPlayer(humanPlayerId));
		});
	}

	// Shows "What do you want for $GIVE0?" while the opponent's side has
	// something on it, and "Would you care to offer something in exchange
	// for $GIVE0?" while ours has, in Civ3's words (diplomacy.txt,
	// #USERASKFORCOUNTER and #USERASKFOREXCHANGE).
	private void RefreshAskButtons(GameData gD, Player opponentPlayer, Player humanPlayer) {
		if (askCounter == null || opponentPlayer == null || humanPlayer == null) {
			return;
		}
		bool negotiating = !activeDealsPanel.Visible;
		askCounter.Visible = negotiating && !opponentOffer.IsEmpty();
		askExchange.Visible = negotiating && !humanOffer.IsEmpty();
		if (askCounter.Visible) {
			SetAskText(askCounter, gD, "USERASKFORCOUNTER", opponentPlayer, humanPlayer, opponentOffer);
		}
		if (askExchange.Visible) {
			SetAskText(askExchange, gD, "USERASKFOREXCHANGE", opponentPlayer, humanPlayer, humanOffer);
		}
	}

	// The width of the box the buttons are in, from their left edge, less
	// the margins of the button's text.
	private const float AskTextWidth = 392;

	private bool FitsAskButton(string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X <= AskTextWidth;

	// The words for an ask button, kept within the box: the line for the
	// opponent's mood if it fits, else the shortest of the label's lines,
	// else that line asking for "this", and if even that is too long, cut
	// short with the whole line in the tooltip.
	private void SetAskText(Button button, GameData gD, string key, Player opponentPlayer, Player humanPlayer, TradeOffer items) {
		string Quoted(string line, string give) => $"\"{line.Replace("$GIVE0", give)}\"";
		string name = ItemsName(items);
		string text = Quoted(DiplomacyLines.Say(gD, key, opponentPlayer, humanPlayer, linePick), name);
		string full = text;
		if (!FitsAskButton(text)) {
			string shortest = DiplomacyLines.SayInEveryMood(gD, key, opponentPlayer, humanPlayer, linePick)
				.OrderBy(l => l.Length).First();
			text = Quoted(shortest, name);
			if (!FitsAskButton(text)) {
				text = Quoted(shortest, "this");
			}
		}
		bool fits = FitsAskButton(text);
		button.ClipText = !fits;
		button.TextOverrunBehavior = fits ? TextServer.OverrunBehavior.NoTrimming : TextServer.OverrunBehavior.TrimEllipsis;
		button.CustomMinimumSize = fits ? Vector2.Zero : new Vector2(AskTextWidth + 8, 0);
		button.Size = Vector2.Zero;
		button.TooltipText = text == full ? "" : full;
		button.Text = text;
	}

	// What is on one side of the table, for $GIVE0: the item, if there is
	// one, or "this".
	private static string ItemsName(TradeOffer offer) {
		string items = offer.ToString();
		return items.Length == 0 || items.Contains(',') ? "this" : items;
	}

	private void AskForCounterOffer(bool exchange) {
		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.GetPlayer(opponentPlayerId);
			if (opponentPlayer == null) {
				return;
			}
			new MsgAskForCounterOffer(opponentPlayer, humanOffer, opponentOffer, exchange) { playerID = humanPlayerId }.send();
		});
	}

	// Lists the deals in force with the opponent across the boxes in the
	// middle of the screen: the heading in the top box, what each side gives
	// on its side of the table, and the agreements in the bottom box.
	private Control CreateActiveDealsPanel(GameData gD, Player humanPlayer, Player opponentPlayer, Theme headerTheme) {
		Theme blackFontTheme = new();
		blackFontTheme.DefaultFont = font;
		blackFontTheme.SetColor("font_color", "Label", Colors.Black);

		Control panel = new();
		panel.MouseFilter = MouseFilterEnum.Ignore;

		// A scrollable list over one of the boxes, for when there are more
		// deals than fit.
		VBoxContainer AddBox(Vector2 position, Vector2 size) {
			ScrollContainer scroll = new();
			scroll.SetPosition(position);
			scroll.Size = size;
			scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			panel.AddChild(scroll);

			VBoxContainer list = new();
			list.AddThemeConstantOverride("separation", 1);
			list.CustomMinimumSize = new Vector2(size.X, 0);
			scroll.AddChild(list);
			return list;
		}

		void AddLine(Container parent, string text, Theme theme, bool indent = false,
				HorizontalAlignment alignment = HorizontalAlignment.Left) {
			Label label = new();
			// Indent toward the middle, on whichever side the text is.
			string padding = indent ? "    " : "";
			label.Text = alignment == HorizontalAlignment.Right ? text + padding : padding + text;
			label.HorizontalAlignment = alignment;
			label.Theme = theme;
			parent.AddChild(label);
		}

		void AddSection(Container parent, string header, List<string> lines, HorizontalAlignment alignment) {
			if (lines.Count == 0) {
				return;
			}
			bool indent = alignment != HorizontalAlignment.Center;
			AddLine(parent, header, headerTheme, alignment: alignment);
			foreach (string line in lines) {
				AddLine(parent, line, blackFontTheme, indent, alignment);
			}
		}

		Label heading = new();
		heading.Text = $"Active deals with the {opponentPlayer.civilization.noun}";
		heading.Theme = headerTheme;
		heading.HorizontalAlignment = HorizontalAlignment.Center;
		heading.SetPosition(new Vector2(314, 393));
		heading.Size = new Vector2(400, 20);
		panel.AddChild(heading);

		VBoxContainer middle = AddBox(new Vector2(314, 440), new Vector2(400, 88));
		if (AtWar(humanPlayer, opponentPlayer)) {
			AddLine(middle, "We are at war.", blackFontTheme, true);
			return panel;
		}
		ActiveDeals deals = ActiveDeals.Between(gD, humanPlayer, opponentPlayer);
		if (deals.IsEmpty) {
			AddLine(middle, "We have no active deals.", blackFontTheme, true);
			return panel;
		}

		// What each side gives goes on its side of the table, as the offers
		// do: theirs on the left, ours on the right.
		HBoxContainer columns = new();
		columns.AddThemeConstantOverride("separation", 0);
		middle.AddChild(columns);
		VBoxContainer theyGive = new();
		VBoxContainer weGive = new();
		foreach (VBoxContainer column in new[] { theyGive, weGive }) {
			column.AddThemeConstantOverride("separation", 1);
			column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			columns.AddChild(column);
		}
		AddSection(theyGive, "They Give", deals.theyGive, HorizontalAlignment.Left);
		AddSection(weGive, "We Give", deals.weGive, HorizontalAlignment.Right);

		VBoxContainer bottom = AddBox(new Vector2(314, 560), new Vector2(400, 85));
		AddSection(bottom, "Agreements", deals.agreements, HorizontalAlignment.Center);
		return panel;
	}

	private void AttemptDeal() {
		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
			Player humanPlayer = gD.players.Find(x => x.id == humanPlayerId);

			// A human on another machine is asked over the network, and the
			// engine tells us their answer.
			if (LanSession.IsRemotePlayer(opponentPlayer)) {
				opponentResponse.Text = $"\"{opponentPlayer.civilization.leader} is considering the offer...\"";
				new MsgProposeDeal(opponentPlayer, humanOffer, opponentOffer).send();
				return;
			}

			// Another human player (in a hotseat game) decides for themselves
			// rather than having the AI decide for them. Playing by email
			// they aren't here to, and the engine turns the deal down.
			if (opponentPlayer.isHuman && !PlayByEmail.IsOn(EngineStorage.gameData)) {
				AskHumanOpponentToAccept(humanPlayer, opponentPlayer);
				return;
			}

			// The AI decides; if it accepts, the engine's answer takes us back
			// to the previous screen.
			new MsgProposeDeal(opponentPlayer, humanOffer, opponentOffer).send();
		});
	}

	private void AskHumanOpponentToAccept(Player humanPlayer, Player opponentPlayer) {
		string message =
			$"{opponentPlayer.civilization.leader}, please take the screen.\n" +
			DescribeDeal(humanPlayer, humanOffer, opponentOffer);

		opponentResponse.Text = $"\"{opponentPlayer.civilization.leader} is considering the offer...\"";
		GetParent<Diplomacy>().ShowScreenPopup(
			new ConfirmationPopup(message, "We accept.", "We refuse.",
				yesAction: () => {
					// The deal screen may have been replaced while the
					// popup was up.
					if (!IsInstanceValid(this) || !IsInsideTree()) {
						return;
					}
					new MsgProposeDeal(opponentPlayer, humanOffer, opponentOffer) { opponentAgreed = true }.send();
				},
				noAction: () => {
					if (!IsInstanceValid(this) || !IsInsideTree()) {
						return;
					}
					opponentResponse.Text = $"\"{opponentPlayer.civilization.leader} refused the offer.\"";
				}));
	}

	// Adds the resources in the offer that aren't in the list.
	private static void AddOffered(GameData gD, List<Resource> list, TradeOffer offer) {
		foreach (string key in offer.resources) {
			Resource r = gD.Resources.Find(x => x.Key == key);
			if (r != null && !list.Contains(r)) {
				list.Add(r);
			}
		}
	}

	public static string DescribeDeal(Player proposer, TradeOffer proposerGives, TradeOffer proposerWants) {
		static string Describe(TradeOffer offer) {
			string description = offer.ToString();
			return description == "" ? "nothing" : description.Replace(",", ", ");
		}

		return $"The {proposer.civilization.noun} offer: {Describe(proposerGives)}\n" +
			$"In return they want: {Describe(proposerWants)}";
	}

	public bool IsFor(ID opponent) => opponent == opponentPlayerId;

	// Shows what the opponent says, in quotes.
	public void ShowResponse(string text) {
		opponentResponse.Text = $"\"{text}\"";
	}

	// The engine's answer to a deal we proposed.
	public void OnDealResult(ID opponent, bool accepted) {
		if (opponent != opponentPlayerId) {
			return;
		}
		if (accepted) {
			GetParent<Diplomacy>().ShowTalkScreenForPlayer(humanPlayerId, opponentPlayerId);
		} else {
			EngineStorage.ReadGameData((GameData gD) => {
				Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
				if (opponentPlayer.isHuman) {
					opponentResponse.Text = $"\"{opponentPlayer.civilization.leader} refused the offer.\"";
				} else if ((humanOffer.partOfPeaceTreaty || opponentOffer.partOfPeaceTreaty)
						&& C7Engine.AI.PeaceAI.PeaceOutlook(gD, opponentPlayer, gD.GetPlayer(humanPlayerId)) >= C7Engine.AI.PeaceAI.WarOutlook.Winning) {
					// Peace turned down by an AI that thinks it's winning,
					// which wants more for it (see PeaceAI.WouldAcceptPeace).
					// Civ3's words (diplomacy.txt, #AINOPEACETREATY).
					opponentResponse.Text = $"\"Not likely! {opponentPlayer.civilization.name} is winning!\"";
				} else {
					// A computer player says no in Civ3's words, by how far
					// the deal is from one it would take. If it now looks
					// acceptable (things changed while it was considered),
					// a plain no (#AINEUTRALREJECT).
					C7Engine.AI.DealReaction reaction = C7Engine.AI.CounterOfferAI.Reaction(gD, opponentPlayer,
						gD.GetPlayer(humanPlayerId), humanOffer, opponentOffer);
					if (reaction == C7Engine.AI.DealReaction.Accept) {
						reaction = C7Engine.AI.DealReaction.NeutralReject;
					}
					ShowReaction(gD, opponentPlayer, reaction);
				}
			});
		}
	}

	private void ShowReaction(GameData gD, Player opponentPlayer, C7Engine.AI.DealReaction reaction) {
		ShowResponse(DiplomacyLines.Say(gD, DiplomacyLines.Key(reaction), opponentPlayer, gD.GetPlayer(humanPlayerId), linePick));
	}

	// The opponent's words as the deal on the table changes: Civ3's leader
	// says how close the deal is to one it would take (diplomacy.txt,
	// #AIWILLACCEPT, #AIWEAKREJECT, #AINEUTRALREJECT, #AISTRONGREJECT,
	// #AITOTALREJECT, as the foreign advisor's #DIPLOADVICETRADE_DEAL_*
	// lines grade it), not what it is worth.
	private void UpdateText() {
		EngineStorage.ReadGameData((GameData gD) => {
			Player opponentPlayer = gD.players.Find(x => x.id == opponentPlayerId);
			Player humanPlayer = gD.players.Find(x => x.id == humanPlayerId);
			RefreshAskButtons(gD, opponentPlayer, humanPlayer);

			if (opponentPlayer.isHuman || (humanOffer.IsEmpty() && opponentOffer.IsEmpty())) {
				opponentResponse.Text = "\"Let's get down to business...\"";
				return;
			}
			ShowReaction(gD, opponentPlayer, C7Engine.AI.CounterOfferAI.Reaction(gD, opponentPlayer, humanPlayer, humanOffer, opponentOffer));
		});
	}
}
