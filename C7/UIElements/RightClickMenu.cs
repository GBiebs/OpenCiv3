using System.Collections.Generic;
using Godot;
using C7GameData;
using C7Engine;
using System;
using System.Linq;

public partial class RightClickMenu : VBoxContainer {
	protected Game game;
	protected Vector2 position;

	protected RightClickMenu(Game game) : base() {
		this.game = game;

		// Set theme for menu node. TODO: This should be made moddable. I noticed in the Godot docs something about loading themes from files
		// but didn't look into how it works, but that's probably what we'll want to do.
		Color black = Color.Color8(0, 0, 0, 255);
		var theme = new Theme();
		theme.SetConstant("separation", "VBoxContainer", 0);
		theme.SetColor("font_color", "Button", black);
		theme.SetColor("font_hover_color", "Button", black);
		theme.SetColor("font_pressed_color", "Button", black);
		theme.SetColor("font_focus_color", "Button", black);
		theme.SetStylebox("normal", "Button", GetItemStyleBox(Color.Color8(255, 247, 222, 255)));
		theme.SetStylebox("hover", "Button", GetItemStyleBox(Color.Color8(255, 189, 107, 255)));
		theme.SetStylebox("pressed", "Button", GetItemStyleBox(Color.Color8(140, 200, 200, 255)));
		this.Theme = theme;

		this.Hide();

		// Add the menu as a child of "CanvasLayer" to ensure it's drawn overtop of the game map. "CanvasLayer" should have been named
		// something like "UILayer" since it's the layer that contains all UI elements. "CanvasLayer" is actually its type.
		game.GetNode("CanvasLayer").AddChild(this);
	}

	public void Open(Vector2 position) {
		// Must show the container first in order to update its RectSize
		this.Show();

		// Move "position" if the menu would extend past the right or bottom edges of the screen
		Vector2 offScreen = position + this.Size - GetViewportRect().Size;
		if (offScreen.X > 0) {
			position.X = Mathf.Max(0, position.X - offScreen.X);
		}
		if (offScreen.Y > 0) {
			position.Y = Mathf.Max(0, position.Y - offScreen.Y);
		}
		this.SetPosition(position);

		// Godot 4.2.1 does not have an accessor for the position, so store it ourselves.
		this.position = position;
	}

	public void CloseAndDelete() {
		this.QueueFree();
	}

	// The menus in the tree. The game polls its key actions, which an event
	// the menu accepts doesn't stop, so it checks this to leave keys like
	// Escape (which closes the menu) and the unit hotkeys alone while a menu
	// is open, and in the frame it closes.
	private static readonly HashSet<RightClickMenu> menusInTree = new();

	public static bool IsAnyOpen => menusInTree.Any(m => IsInstanceValid(m) && m.Visible);

	public override void _EnterTree() {
		menusInTree.Add(this);
		base._EnterTree();
	}

	public override void _ExitTree() {
		menusInTree.Remove(this);
		base._ExitTree();
	}

	private static StyleBoxFlat GetItemStyleBox(Color color) {
		return new StyleBoxFlat() {
			BgColor = color,
			ContentMarginLeft = 10f,
			ContentMarginTop = 0f,
			ContentMarginRight = 10f,
			ContentMarginBottom = 0f
		};
	}

	public Button AddItem(string text, System.Action action, Texture2D icon = null) {
		Button button = new Button();
		button.Text = text;
		if (icon != null) {
			button.Icon = icon;
		}
		button.Alignment = HorizontalAlignment.Left;
		if (action != null) {
			button.Pressed += action;
		}
		this.AddChild(button);
		return button;
	}

	// The "Abandon City" item, greyed out with the reason when the city
	// can't be abandoned, e.g. because it is the player's only one.
	// As in Civ3, the city menu ends with the Civilopedia entry for what the
	// city is building.
	protected void AddCivilopediaItem(City city) {
		object producing = city.itemBeingProduced;
		if (producing is not (UnitPrototype or Building)) {
			return;
		}
		string name = producing is UnitPrototype unit ? unit.name : ((Building)producing).name;
		AddTreeSeparator();
		AddItem($"View Civilopedia entry for {name}", () => {
			CloseAndDelete();
			Civilopedia.OpenFor(producing);
		});
	}

	protected void AddAbandonCityItem(City city) {
		string reason = null;
		EngineStorage.ReadGameData((GameData gameData) => {
			reason = CityInteractions.WhyCannotAbandon(city.owner, city);
		});
		if (reason != null) {
			Button item = AddItem($"Abandon City ({reason.TrimEnd('.')})", null);
			item.Disabled = true;
			return;
		}
		AddItem("Abandon City", () => {
			this.CloseAndDelete();
			new MsgDisplayAbandonCityPopup(city).send();
		});
	}

	protected void ShowRenameDialog(City city) {
		ConfirmationDialog dialog = new() { Title = $"Rename {city.name}", OkButtonText = "OK", CancelButtonText = "Cancel" };
		LineEdit nameEdit = new() {
			Text = city.name,
			MaxLength = City.MaxNameLength,
			CustomMinimumSize = new Vector2(260, 0),
		};
		dialog.AddChild(nameEdit);
		dialog.RegisterTextEnter(nameEdit);
		dialog.Confirmed += () => new MsgRenameCity(city, nameEdit.Text).send();
		dialog.VisibilityChanged += () => {
			if (!dialog.Visible) {
				dialog.QueueFree();
			}
		};
		game.AddChild(dialog);
		dialog.PopupCentered();
		nameEdit.GrabFocus();
	}

	// Civ3's rally point items: Set Rally Point and Set Continental Rally
	// Point ask for a tile on the map; Clear Continental Rally Point shows
	// once the city has a rally point.
	protected void AddRallyPointItems(City city) {
		Action<bool> pick = (bool continental) => {
			this.CloseAndDelete();
			game.PickTile(target => {
				string reason = null;
				EngineStorage.ReadGameData((GameData gameData) => reason = city.WhyCannotRallyAt(target));
				if (reason == null) {
					new MsgSetRallyPoint(city, target, continental).send();
				}
			});
		};
		AddItem("Set Rally Point", () => pick(false));
		AddItem("Set Continental Rally Point", () => pick(true));
		if (city.rallyPoint != null) {
			AddItem("Clear Continental Rally Point", () => {
				this.CloseAndDelete();
				new MsgSetRallyPoint(city, null, true).send();
			});
		}
	}

	// The items for one of the player's own cities, the same whether or not
	// units stand in it.
	protected void AddOwnCityItems(City city) {
		AddItem($"Zoom to {city.name}", () => {
			this.CloseAndDelete();
			EngineStorage.ReadGameData((GameData gameData) => {
				game.ShowCityScreenForCity(gameData, city);
			});
		});
		AddItem("Rename", () => {
			this.CloseAndDelete();
			ShowRenameDialog(city);
		});
		AddTreeSeparator();
		AddItem("Change Production", () => {
			// Close the first menu before opening the second menu.
			this.CloseAndDelete();
			new RightClickChooseProductionMenu(game, city).Open(this.position);
		});
		AddItem("Hurry Production", () => {
			this.CloseAndDelete();
			EngineStorage.ReadGameData((GameData gameData) => {
				City.HurryProductionDetails details = city.GetHurryProductionDetails();
				new MsgDisplayHurryProductionPopup(city, details).send();
			});
		});
		// Only once the advance that allows the draft is known.
		if (city.owner.HasTechEnabling(C7GameData.Save.SaveTech.Flag.EnablesConscription)) {
			AddItem("Draft Citizen", () => {
				this.CloseAndDelete();
				EngineStorage.ReadGameData((GameData gameData) => {
					new MsgDisplayDraftPopup(city, city.WhyCannotDraft(gameData), city.DraftableUnit(gameData)?.name).send();
				});
			});
		}
		AddItem("Contact Governor", () => {
			this.CloseAndDelete();
			EngineStorage.ReadGameData((GameData gameData) => game.ShowGovernor(city));
		});
		AddRallyPointItems(city);

		AddAbandonCityItem(city);
		AddCivilopediaItem(city);
	}

	protected void AddTreeSeparator() {
		var background = new ColorRect();
		background.Color = Color.Color8(255, 247, 222, 255);
		background.CustomMinimumSize = new Vector2(0, 12);
		background.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		background.MouseFilter = Control.MouseFilterEnum.Stop;

		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.MouseFilter = MouseFilterEnum.Stop;

		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_top", 5);
		margin.AddThemeConstantOverride("margin_bottom", 5);

		var line = new ColorRect();
		line.Color = Color.Color8(165, 165, 165, 255);
		line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		line.CustomMinimumSize = new Vector2(0, 2);
		line.MouseFilter = Control.MouseFilterEnum.Stop;

		margin.AddChild(line);
		background.AddChild(margin);

		this.AddChild(background);
	}

	public void RemoveAll() {
		foreach (Node child in this.GetChildren()) {
			child.QueueFree();
		}
	}

	public override void _Input(InputEvent @event) {
		bool mouseOverMenu = new Rect2(Vector2.Zero, this.Size).HasPoint(this.GetLocalMousePosition());
		bool escapeKeyWasPressed = (@event is InputEventKey keyEvent) && keyEvent.Pressed && keyEvent.Keycode == Godot.Key.Escape;
		bool mouseClickedOutsideMenu = (@event is InputEventMouseButton mouseButtonEvent) && mouseButtonEvent.IsPressed() && !mouseOverMenu;

		if (escapeKeyWasPressed || mouseClickedOutsideMenu) {
			this.AcceptEvent(); // Prevents other controls from receiving this event
			CloseAndDelete();
			// Eat all events other than mouse events while the cursor is over the menu. We want the menu to grab all input while it's open but we
			// must make sure not to block mouse events from reaching its child buttons. (This had me confused for a while since the Godot docs
			// say that events reach children before their parents, but the catch is that there are three phases of input processing. The "input"
			// phase, this function, then "gui input", and finally "unhandled input". If a control eats an event during the "input" phase it won't
			// proceed to the "gui input" phase where buttons actually respond to it.)
		} else if (!((@event is InputEventMouse) && mouseOverMenu)) {
			this.AcceptEvent();
		}
	}
}

public partial class RightClickTileMenu : RightClickMenu {
	public RightClickTileMenu(Game game, Tile tile) : base(game) {
		ResetItems(tile);
	}

	private bool isUnitFortified(MapUnit unit, Dictionary<ID, bool> uiStates) {
		if (uiStates is null || !uiStates.ContainsKey(unit.id)) {
			return unit.isFortified;
		}
		return uiStates[unit.id];
	}

	private bool isUnitLoadedOnTransport(MapUnit unit) => unit.IsLoaded();

	private string getUnitAction(MapUnit unit, bool isFortified) {
		if (unit.owner == game.controller) {
			return isFortified ? "Wake" : "Activate";
		}
		return "Contact";
	}

	private static StyleBoxFlat AltItemStyleBox(Color color) {
		return new StyleBoxFlat() {
			BgColor = color,
			ContentMarginLeft = 20f,
			ContentMarginTop = 2f,
			ContentMarginRight = 4f,
			ContentMarginBottom = 2f
		};
	}

	// uiUpdatedUnitStates maps unit guid to a boolean that is true if they were fortified
	// and false if they were selected in the previous action. This is to update the UI
	// since the actions update the engine asynchronously and otherwise the UI may not
	// reflect these changes immediately.
	public void ResetItems(Tile tile, Dictionary<ID, bool> uiUpdatedUnitStates = null) {
		RemoveAll();

		AddItem($"Terrain Info", () => {
			game.ShowTileInfo(tile);
			CloseAndDelete();
		});

		AddTreeSeparator();

		bool observerMode = false;
		EngineStorage.ReadGameData((GameData gameData) => {
			observerMode = gameData.observerMode;
		});

		int fortifiedCount = 0;
		// Invisible units the player doesn't detect aren't listed.
		List<MapUnit> shownUnits = observerMode ? tile.unitsOnTile : tile.UnitsSeenBy(game.controller);
		List<MapUnit> playerUnits = shownUnits.FindAll(unit => unit.owner.id == game.controller.id || observerMode);
		// Another player's transport is listed without what it carries.
		List<MapUnit> nonPlayerUnits = shownUnits.FindAll(unit => unit.owner.id != game.controller.id && !observerMode && !unit.IsLoaded());

		// Sort by transport group
		playerUnits = playerUnits
			.GroupBy(u => u.CanCarryUnits() ? u.id : u.loadedOnUnitId ?? ID.None("Other"))
			.SelectMany(g => g.OrderBy(u => u.CanCarryUnits() ? int.MinValue : 0))
			.ToList();

		int orderableCount = 0;
		foreach (MapUnit unit in playerUnits) {
			// A unit locked in an army takes no orders of its own, so it's
			// only listed, greyed out, to show what the army is made of.
			if (unit.IsLockedInArmy()) {
				var memberItem = AddItem(unit.Describe(), null);
				ApplyAltItemOverrides(memberItem);
				memberItem.Disabled = true;
				continue;
			}

			orderableCount++;
			bool isFortified = isUnitFortified(unit, uiUpdatedUnitStates);
			fortifiedCount += isFortified ? 1 : 0;
			string actionName = getUnitAction(unit, isFortified);
			var menuItem = AddItem($"{actionName} {unit.Describe()}", () => SelectUnit(unit.id, tile.XCoordinate, tile.YCoordinate));

			if (isUnitLoadedOnTransport(unit))
				ApplyAltItemOverrides(menuItem);
		}
		int unfortifiedCount = orderableCount - fortifiedCount;

		if (fortifiedCount > 1) {
			AddItem($"Wake All ({fortifiedCount} units)", () => ForAll(tile.XCoordinate, tile.YCoordinate, false));
		}
		// labels.txt's "Wake Transported": wakes the units asleep aboard our
		// transports here, to be moved one by one.
		List<MapUnit> asleepAboard = playerUnits.FindAll(u => u.IsLoaded() && !u.IsLockedInArmy()
			&& (isUnitFortified(u, uiUpdatedUnitStates) || (u.isSentried && uiUpdatedUnitStates?.ContainsKey(u.id) != true)));
		if (asleepAboard.Count > 0 && !observerMode) {
			AddItem($"Wake Transported ({asleepAboard.Count} {(asleepAboard.Count == 1 ? "unit" : "units")})", () => WakeTransported(tile, asleepAboard));
		}
		if (unfortifiedCount > 1) {
			AddItem($"Fortify All ({unfortifiedCount} units)", () => ForAll(tile.XCoordinate, tile.YCoordinate, true));
		}
		if (tile.cityAtTile?.owner == game.controller) {
			AddTreeSeparator();
			AddOwnCityItems(tile.cityAtTile);
		}

		// If we're looking at an enemy tile, then the behavior depends on whether the units
		// are in a city. We can see the full list of units outside of a city, but in a city
		// we can only see the top defender.
		if (nonPlayerUnits.Count > 0) {
			Action contactCiv = () => {
				this.CloseAndDelete();
				game.controller.EnsureRelationshipExists(nonPlayerUnits[0].owner);
				game.OnDiplomacySelected(new ParameterWrapper<ID>(nonPlayerUnits[0].owner.id));
			};

			// A hidden nationality unit, like a privateer, is named without
			// its civ, and gives no one to contact.
			string Whose(MapUnit unit) => unit.ShowsOwnerTo(game.controller) ? $"{(string.IsNullOrEmpty(unit.owner.civilization.adjective) ? unit.owner.civilization.noun : unit.owner.civilization.adjective)} " : "";
			if (tile.cityAtTile == null) {
				foreach (MapUnit unit in nonPlayerUnits) {
					AddItem($"{Whose(unit)}{unit.Describe()}", null);
				}
			} else {
				// TODO: This isn't necessarily the top unit, get that code to an accessible
				// location and then use it here.
				MapUnit unit = nonPlayerUnits[0];
				AddItem($"{Whose(unit)}{unit.Describe()}", null);
			}

			if (!nonPlayerUnits[0].owner.IsIndependent && nonPlayerUnits[0].ShowsOwnerTo(game.controller))
				AddItem($"Contact {nonPlayerUnits[0].owner.civilization.name}", contactCiv);
		}

		// Diplomatic and espionage missions against a foreign city.
		City foreignCity = tile.cityAtTile;
		if (foreignCity != null && foreignCity.owner != game.controller && !foreignCity.owner.IsIndependent
				&& PlayerRelationship.TryGetRelationship(game.controller, foreignCity.owner, out _)) {
			AddTreeSeparator();
			AddItem("Diplomatic Missions", () => {
				this.CloseAndDelete();
				game.ShowEspionageMissions(foreignCity.owner, foreignCity);
			});
		}

		// The Civilopedia entries of the kinds of units listed, at most a few.
		IEnumerable<MapUnit> listed = playerUnits.Concat(tile.cityAtTile == null ? nonPlayerUnits : nonPlayerUnits.Take(1));
		List<UnitPrototype> unitTypes = listed.Select(u => u.unitType).Where(t => t != null).Distinct().Take(3).ToList();
		if (unitTypes.Count > 0) {
			AddTreeSeparator();
			foreach (UnitPrototype unitType in unitTypes) {
				AddItem($"View Civilopedia entry for {unitType.name}", () => {
					CloseAndDelete();
					Civilopedia.OpenFor(unitType);
				});
			}
		}
	}

	private static void ApplyAltItemOverrides(Button menuItem) {
		var grey = Color.Color8(64, 64, 64, 255);
		menuItem.AddThemeColorOverride("font_color", grey);
		menuItem.AddThemeColorOverride("font_hover_color", grey);
		menuItem.AddThemeColorOverride("font_pressed_color", grey);
		menuItem.AddThemeColorOverride("font_focus_color", grey);
		menuItem.AddThemeColorOverride("font_disabled_color", Color.Color8(140, 140, 140, 255));
		menuItem.AddThemeStyleboxOverride("disabled", AltItemStyleBox(Color.Color8(255, 247, 222, 255)));
		menuItem.AddThemeStyleboxOverride("normal", AltItemStyleBox(Color.Color8(255, 247, 222, 255)));
		menuItem.AddThemeStyleboxOverride("hover", AltItemStyleBox(Color.Color8(255, 189, 107, 255)));
		menuItem.AddThemeStyleboxOverride("pressed", AltItemStyleBox(Color.Color8(140, 200, 200, 255)));
	}

	public void SelectUnit(ID id) {
		SelectUnit(id, null, null);
	}

	// Selects the unit, looking for it first on the tile the menu is for
	// (rather than among every unit in the game).
	public void SelectUnit(ID id, int? tileX, int? tileY) {
		EngineStorage.ReadGameData((GameData gameData) => {
			MapUnit toSelect = null;
			if (tileX.HasValue && tileY.HasValue) {
				Tile tile = gameData.map.tileAt(tileX.Value, tileY.Value);
				toSelect = tile?.unitsOnTile.Find(u => u.id == id);
			}
			toSelect ??= gameData.GetUnit(id);

			if (toSelect != null && toSelect.owner == game.controller && !toSelect.IsLockedInArmy()) {
				game.SelectUnit(toSelect);

				new MsgSetFortification(toSelect.id, false).send();
				ResetItems(toSelect.location, new Dictionary<ID, bool>() { { toSelect.id, false } });
			}
		});
		if (!Input.IsKeyPressed(Godot.Key.Shift)) {
			CloseAndDelete();
		}
	}

	private void WakeTransported(Tile tile, List<MapUnit> units) {
		Dictionary<ID, bool> modified = new();
		foreach (MapUnit unit in units) {
			new MsgSetFortification(unit.id, false).send();
			modified[unit.id] = false;
		}
		ResetItems(tile, modified);
	}

	public void ForAll(int tileX, int tileY, bool isFortify) {
		EngineStorage.ReadGameData((GameData gameData) => {
			bool hasSelectedUnit = false;
			Tile tile = gameData.map.tileAt(tileX, tileY);
			Dictionary<ID, bool> modified = new Dictionary<ID, bool>();
			foreach (MapUnit unit in tile.unitsOnTile) {
				if (unit.isFortified != isFortify && !unit.IsLockedInArmy()) {
					modified[unit.id] = isFortify;
					new MsgSetFortification(unit.id, isFortify).send();

					if (!hasSelectedUnit && !isFortify) {
						bool canMove = game.unitSelector.SetSelectedUnit(unit);
						hasSelectedUnit = true;
						if (!canMove) {
							new MsgShowTemporaryPopup("This unit has already moved.", tile).send();
						}
					}
				}
			}

			ResetItems(tile, modified);
		});
		if (!Input.IsKeyPressed(Godot.Key.Shift)) {
			CloseAndDelete();
		}
	}
}

// A right click menu for the player's city when there are no units.
public partial class RightClickCityMenu : RightClickMenu {
	public RightClickCityMenu(Game game, Tile tile) : base(game) {
		ResetItems(tile);
	}

	public void ResetItems(Tile tile) {
		RemoveAll();

		AddItem($"Terrain Info", () => {
			game.ShowTileInfo(tile);
			CloseAndDelete();
		});

		AddTreeSeparator();

		if (tile.cityAtTile?.owner == game.controller) {
			AddOwnCityItems(tile.cityAtTile);
		}
	}
}

public partial class RightClickChooseProductionMenu : RightClickMenu {
	private ID cityID;

	public static ImageTexture GetProducibleIcon(IProducible producible, Player player) {
		if (producible is UnitPrototype proto) {
			return TextureLoader.Load("unit_icons", new ItemContext(proto, player), useCache: true);
		} else if (producible is Building b) {
			return TextureLoader.Load("building_icons.small", b, useCache: true);
		} else if (producible is Inflow inflow) {
			return TextureLoader.Load("building_icons.small", inflow, useCache: true);
		} else {
			return null;
		}
	}

	public RightClickChooseProductionMenu(Game game, City city) : base(game) {
		cityID = city.id;
		EngineStorage.ReadGameData((GameData gameData) => {
			foreach (IProducible option in city.ListProductionOptions(gameData)) {
				int buildTime = city.TurnsToProduce(option);
				// Say how many shields switching would waste, or why it can't.
				string warning = city.ProductionChangeWarning(option);
				string text = warning == null ? $"{option.name} ({buildTime} turns)" : $"{option.name} ({buildTime} turns; {warning})";
				Button item = AddItem(text, () => ChooseProduction(option.name), GetProducibleIcon(option, city.owner));
				item.Disabled = !city.CanChangeProduction(option);
			}
		});
	}

	// Shift adds the item to the city's production queue instead.
	public void ChooseProduction(string producibleName) {
		if (Input.IsKeyPressed(Key.Shift)) {
			new MsgEnqueueProduction(cityID, producibleName).send();
		} else {
			new MsgChooseProduction(cityID, producibleName).send();
		}
		CloseAndDelete();
	}
}
