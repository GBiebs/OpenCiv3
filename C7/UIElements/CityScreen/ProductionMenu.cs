using System.Collections.Generic;
using Godot;
using C7GameData;
using C7Engine;
using System;

[Tool]
[GlobalClass]
public partial class ProductionMenu : Civ3TextureRect {
	private Dictionary<TreeItem, IProducible> itemMapping = new();
	// The queue's rows: which place in the queue each one is.
	private Dictionary<TreeItem, int> queueMapping = new();

	Tree tree;
	Theme fontTheme = new();

	public ProductionMenu() { }

	public override void _Ready() {
		this.Texture = TextureLoader.Load("city_screen.production_queue");

		// Load the font we'll use.
		FontFile font = ResourceLoader.Load<FontFile>("res://Fonts/NotoSans-Regular.ttf", null, ResourceLoader.CacheMode.Ignore);
		font.FixedSize = 10;
		fontTheme.DefaultFont = font;
	}

	// An item's name, with a unit's attack (bombard), defense and moves.
	public static string ItemText(IProducible option) {
		string text = $"{option.name}";
		if (option is UnitPrototype proto) {
			string attackDesc = (proto.bombard > 0) ? $"{proto.attack}({proto.bombard})" : proto.attack.ToString();
			text += $" {attackDesc}.{proto.defense}.{proto.movement}";
		}
		return text;
	}

	// The city's queue comes first, under its own heading (a click takes
	// an item off it), then everything the city can build.
	public void AddItems(GameData gameData, City city, Action<IProducible> chooseProduction, Action<int> unqueue = null) {
		if (tree != null) {
			itemMapping.Clear();
			queueMapping.Clear();
			RemoveChild(tree);
			tree = null;
		}
		tree = new();

		// Set up the tree of items. We use a tree so we get a scroll bar and
		// other niceties.
		AddChild(tree);
		tree.Columns = 2;
		tree.Size = new Vector2(203, 360);
		tree.SetColumnExpand(0, true);
		tree.SetColumnExpand(1, false);
		tree.SetColumnCustomMinimumWidth(1, 50);
		TradingTree.ConfigureTreeTheme(tree, fontTheme);

		TreeItem root = TradingTree.CreateTreeRoot(tree);

		if (city.productionQueue.Count > 0) {
			TreeItem heading = tree.CreateItem(root);
			heading.SetText(0, "Queued (click to remove):");
			heading.SetSelectable(0, false);
			heading.SetSelectable(1, false);
			for (int i = 0; i < city.productionQueue.Count; i++) {
				TreeItem queued = tree.CreateItem(root);
				queued.SetText(0, $"{i + 1}. {ItemText(city.productionQueue[i])}");
				queued.SetIcon(0, RightClickChooseProductionMenu.GetProducibleIcon(city.productionQueue[i], city.owner));
				queued.SetCustomMinimumHeight(28);
				queueMapping[queued] = i;
			}
			foreach (string line in new[] { "Shift+click: add", "Shift+Delete: clear" }) {
				TreeItem hint = tree.CreateItem(root);
				hint.SetText(0, line);
				hint.SetSelectable(0, false);
				hint.SetSelectable(1, false);
			}
		}

		foreach (IProducible option in city.ListProductionOptions(gameData)) {
			int buildTime = city.TurnsToProduce(option);

			TreeItem child = tree.CreateItem(root);
			child.SetText(0, ItemText(option));
			child.SetText(1, $"{buildTime} turns");
			child.SetIcon(0, RightClickChooseProductionMenu.GetProducibleIcon(option, city.owner));
			child.SetCustomMinimumHeight(40);
			child.SetAutowrapMode(0, TextServer.AutowrapMode.WordSmart);
			tree.SetColumnTitleAlignment(1, HorizontalAlignment.Right);
			itemMapping[child] = option;
		}

		// We only want clickable behavior, not selectable behavior.
		tree.ItemSelected += () => {
			TreeItem ti = tree.GetSelected();
			ti.Deselect(0);
			if (queueMapping.TryGetValue(ti, out int place)) {
				unqueue?.Invoke(place);
			} else {
				chooseProduction(itemMapping[ti]);
			}
		};
	}
}
