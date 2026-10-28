using C7Engine;
using C7GameData;
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using ConvertCiv3Media;

public partial class TradeOfferUi : Tree {
	TreeItem peaceTreaty;
	TreeItem lumpSumGold;
	List<TreeItem> techs = new();
	Dictionary<TreeItem, Agreement> agreementItems = new();
	Dictionary<TreeItem, DealGood> goodItems = new();
	TreeItem goldPerTurn;
	TradeOffer currentOffer;
	List<Tech> tradeableTechs;
	int playerGold;

	public TradeOfferUi(Theme fontTheme, List<Tech> tradeableTechs, int playerGold,
						TradeOffer currentOffer, bool requiresPeaceTreaty,
						List<(Agreement agreement, string text)> agreements,
						List<DealGood> goods,
						HorizontalAlignment alignment) {
		this.currentOffer = currentOffer;
		this.tradeableTechs = tradeableTechs;
		this.playerGold = playerGold;
		this.Columns = 1;
		this.AllowRmbSelect = true;
		TradingTree.ConfigureTreeTheme(this, fontTheme);
		TreeItem root = TradingTree.CreateTreeRoot(this);

		// Match the size of the texture used in the deal screen.
		this.Size = new Vector2(190, 100);

		if (requiresPeaceTreaty) {
			peaceTreaty = this.CreateItem(root);
			peaceTreaty.SetTextAlignment(0, alignment);
			peaceTreaty.SetText(0, "Peace Treaty");
		}

		// Add tree items for all the possible things that could be traded - we
		// determine whether they're visible based on the current offer in
		// RefreshUiForOffer.
		foreach ((Agreement agreement, string text) in agreements) {
			TreeItem item = this.CreateItem(root);
			item.SetTextAlignment(0, alignment);
			item.SetText(0, text);
			agreementItems[item] = agreement;
		}

		foreach (DealGood good in goods) {
			TreeItem item = this.CreateItem(root);
			item.SetTextAlignment(0, alignment);
			item.SetText(0, good.text);
			goodItems[item] = good;
		}

		goldPerTurn = this.CreateItem(root);
		goldPerTurn.SetTextAlignment(0, alignment);

		lumpSumGold = this.CreateItem(root);
		lumpSumGold.SetTextAlignment(0, alignment);

		foreach (Tech tech in tradeableTechs) {
			TreeItem child = this.CreateItem(root);
			child.SetTextAlignment(0, alignment);
			child.SetText(0, tech.Name);
			techs.Add(child);
		}

		RefreshUiForOffer();
	}

	public void HandleClicks(TradeOfferUi other, Action offerUpdated) {
		this.ItemMouseSelected += (Vector2 mousePos, long mouseButtonIndex) => {
			TreeItem ti = this.GetSelected();
			ti.Deselect(0);

			// Handle techs being clicked on.
			if (techs.Contains(ti)) {
				Tech t = tradeableTechs.Find(x => x.Name == ti.GetText(0));
				currentOffer.techs.Remove(t);
			}
			if (ti == lumpSumGold && mouseButtonIndex != 2) {
				currentOffer.gold = null;
			}

			if (goodItems.TryGetValue(ti, out DealGood good)) {
				good.remove(currentOffer);
			}
			if (ti == goldPerTurn) {
				currentOffer.goldPerTurn = null;
			}

			// An agreement comes off both sides of the table.
			if (agreementItems.TryGetValue(ti, out Agreement agreement)) {
				currentOffer.agreements.RemoveAll(a => a.SameAs(agreement));
				other.currentOffer.agreements.RemoveAll(a => a.SameAs(agreement));
			}

			// Allow right clicking the gold amount to change it.
			if (ti == lumpSumGold && mouseButtonIndex == 2) {
				var handleTextInput = (string input) => {
					int gold = 0;
					bool parsed = int.TryParse(input, out gold);
					if (!parsed) {
						return;
					}
					if (gold > playerGold || gold <= 0) {
						GetParent<DealScreen>().GetParent<Diplomacy>().popupOverlay
							.ShowPopup(new InformationalPopup("Insufficient gold"),
									   PopupOverlay.PopupCategory.Advisor);
						return;
					}

					currentOffer.gold = gold;
					offerUpdated();
					RefreshUiForOffer();
				};

				GetParent<DealScreen>().GetParent<Diplomacy>().popupOverlay
					.ShowPopup(new TextDialog("Enter amount...",
											"Gold: ", "" + currentOffer.gold.Value,
											BoxContainer.AlignmentMode.Center,
											handleTextInput),
								PopupOverlay.PopupCategory.Advisor);
			}

			if (ti == peaceTreaty) {
				// If we click the peace treaty off the trading table, remove
				// everything else too.
				if (currentOffer.partOfPeaceTreaty) {
					currentOffer.Clear();
					other.currentOffer.Clear();
				} else {
					currentOffer.partOfPeaceTreaty = true;
				}
			}

			offerUpdated();
			RefreshUiForOffer();
		};
	}

	public void RefreshUiForOffer() {
		if (currentOffer.gold.HasValue) {
			lumpSumGold.SetText(0, $"{currentOffer.gold.Value} gold");
			lumpSumGold.Visible = true;
		} else {
			lumpSumGold.Visible = false;
		}

		foreach (TreeItem ti in techs) {
			ti.Visible = currentOffer.techs.Any(x => x.Name == ti.GetText(0));
		}

		if (peaceTreaty != null) {
			peaceTreaty.Visible = currentOffer.partOfPeaceTreaty;
		}

		foreach ((TreeItem ti, Agreement agreement) in agreementItems) {
			ti.Visible = currentOffer.agreements.Any(a => a.SameAs(agreement));
		}

		foreach ((TreeItem ti, DealGood good) in goodItems) {
			ti.Visible = good.isIn(currentOffer);
		}
		goldPerTurn.Visible = currentOffer.goldPerTurn.HasValue;
		if (currentOffer.goldPerTurn.HasValue) {
			goldPerTurn.SetText(0, $"{currentOffer.goldPerTurn.Value} gold per turn");
		}
	}
}
