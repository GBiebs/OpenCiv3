using Godot;
using System;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7.Map;
using C7.Textures;
using C7Engine;
using C7Engine.AI;


// Handles the city screen, where citizens can be assigned and other details of
// the city can bee seen.
[Tool]
public partial class CityScreen : Control {
	private ILogger log = LogManager.ForContext<CityScreen>();
	public TileAssignmentLayer tileAssignmentLayer;
	public MapView mapView;
	public List<CitizenType> citizenTypes;
	private List<TextureButton> popHeads = new();
	private List<TextureRect> popHeadEffects = new();

	private const int POP_HEAD_OFFSET_Y = 433;
	private const string LUXURIES = "luxuries";
	private const string TAXES = "taxes";
	private const string RESEARCH = "research";
	private const string CORRUPTION = "corruption";
	private const string CONSTRUCTION = "construction";

	[Export] private TextureRect background;
	[Export] private VBoxContainer existingBuildings;
	[Export] private Label culturePerTurn;
	[Export] private Label totalCulture;
	[Export] private Label cityName;

	[Export] private TextureButton productionButton;
	[Export] private TextureButton close;
	[Export] private TextureButton previousCity;
	[Export] private TextureButton nextCity;

	[Export] private ProductionMenu productionMenu;

	[Export] private HBoxContainer strategicResources;
	[Export] private VBoxContainer luxuriesContainer;

	[Export] Label productionLabel;
	[Export] Label completeInLabel;
	[Export] Label growthInLabel;
	[Export] Label foodLabel;
	[Export] Label granaryLabel;
	[Export] GridContainer shieldsInBoxContainer;
	[Export] GridContainer foodInBoxContainer;
	[Export] GridContainer foodInGranaryContainer;

	[Export] Control shieldRowContainer;
	[Export] Control foodRowContainer;

	Theme yieldDetailsFontTheme = new();
	FontFile yieldDetailsFont = new();

	private Label foodDetails;
	private Label productionDetails;
	private BaseButton draftButton;
	private BaseButton governorButton;

	// Civ3's commerce on the three green bars, each with its count beside
	// it: taxes, science (and at its start, in red, what corruption takes),
	// luxuries.
	private sealed class CommerceRow {
		public Control row;
		public Control icons;
		public Label count;
		public Label lost;
	}
	private CommerceRow taxesRow, scienceRow, luxuriesRow;

	// The production box (the scene's grid's place): every shield the same
	// size, in columns of up to ShieldBoxRows, the columns closer together
	// (overlapping in the end) as the cost grows.
	private Control shieldBox;
	private const int ShieldBoxRows = 5;
	private static readonly Vector2 ShieldBoxSpacing = new(18, 20);
	private ImageTexture taxTexture, scienceTexture, luxuryTexture, corruptGoldTexture;
	// (Their icons scaled to sit inside the 21-pixel bars.)
	private const float CommerceIconHeight = 18;

	// Civ3's city screen shows the city at one zoom every time; the map
	// goes back to the player's own view (zoom and place) on leaving.
	private const float CityZoom = 1.0f;
	private (float zoom, Vector2 location)? viewBefore;

	private ImageTexture shieldTexture;
	private ImageTexture emptyShieldTexture;
	private ImageTexture corruptShieldTexture;
	private ImageTexture foodTexture;
	private ImageTexture emptyFoodTexture;
	private ImageTexture noFoodTexture;
	private ImageTexture eatenFoodTexture;

	private ImageTexture smileyFaceTexture;
	private ImageTexture goodShieldTexture;
	private ImageTexture beakerTexture;
	private ImageTexture goodGoldTexture;
	private ImageTexture wastedGoldTexture;

	private Dictionary<string, ImageTexture> effectIcons = new();

	// Another civ's city (an investigation, see Espionage): shown, but
	// nothing can be changed.
	private bool readOnly = false;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready() {
		background.Texture = TextureLoader.Load("city_screen.background");

		// The close button.
		TextureLoader.SetButtonTextures(close, "city_screen.buttons.close");
		close.Pressed += Hide;

		TextureLoader.SetButtonTextures(previousCity, "city_screen.buttons.previous");
		previousCity.Pressed += SwitchToPreviousCity;

		TextureLoader.SetButtonTextures(nextCity, "city_screen.buttons.next");
		nextCity.Pressed += SwitchToNextCity;

		// Load the font we'll use for the details.
		//
		// We skip the cache so that we can change the size without affecting other
		// code using the same font.
		yieldDetailsFont = ResourceLoader.Load<FontFile>("res://Fonts/NotoSans-Regular.ttf", null, ResourceLoader.CacheMode.Ignore);
		yieldDetailsFont.FixedSize = 20;

		yieldDetailsFontTheme.DefaultFont = yieldDetailsFont;
		yieldDetailsFontTheme.SetColor("font_color", "Label", Colors.Black);
		yieldDetailsFontTheme.SetFontSize("font_size", "Label", 20);

		foodDetails = new Label() {
			OffsetLeft = 290,
			OffsetTop = 567,
			Theme = yieldDetailsFontTheme,
		};
		background.AddChild(foodDetails);

		productionDetails = new Label() {
			OffsetLeft = 290,
			OffsetTop = 520,
			Theme = yieldDetailsFontTheme,
		};
		background.AddChild(productionDetails);

		taxTexture = TextureLoader.Load("icons.commerce");
		scienceTexture = TextureLoader.Load("icons.commerce_science");
		luxuryTexture = TextureLoader.Load("icons.commerce_luxury");
		corruptGoldTexture = TextureLoader.Load("icons.penalty_commerce");
		// (The green bars' tops in the background.)
		taxesRow = AddCommerceRow(626);
		scienceRow = AddCommerceRow(657);
		luxuriesRow = AddCommerceRow(690);

		TextureLoader.SetButtonTextures(productionButton, "city_screen.buttons.production");
		productionButton.Pressed += () => { this.productionMenu.Visible = !this.productionMenu.Visible; };

		// Civ3's governor and draft buttons at the end of the production
		// bar, each saying what it does when hovered.
		governorButton = ArtButton("city_screen.buttons.governor", "Gov", new Vector2(838, 520));
		governorButton.Pressed += () => {
			City city = tileAssignmentLayer.city;
			if (city != null) {
				mapView.game.ShowGovernor(city);
			}
		};
		background.AddChild(governorButton);

		// Drafts a citizen into a conscript unit.
		draftButton = ArtButton("city_screen.buttons.draft", "Draft", new Vector2(869, 524));
		draftButton.Pressed += () => {
			City city = tileAssignmentLayer.city;
			if (city != null) {
				new MsgDraft(city, city.owner.id).send();
			}
		};
		background.AddChild(draftButton);

		shieldTexture = TextureLoader.Load("icons.good_shield");
		emptyShieldTexture = TextureLoader.Load("icons.empty_shield");
		corruptShieldTexture = TextureLoader.Load("icons.wasted_shield");
		foodTexture = TextureLoader.Load("icons.full_food");
		emptyFoodTexture = TextureLoader.Load("icons.empty_food");
		noFoodTexture = TextureLoader.Load("icons.no_food");
		eatenFoodTexture = TextureLoader.Load("icons.eaten_food");
		// effects
		smileyFaceTexture = TextureLoader.Load("icons.effect_smiley_face");
		goodGoldTexture = TextureLoader.Load("icons.effect_good_gold");
		beakerTexture = TextureLoader.Load("icons.effect_beaker");
		wastedGoldTexture = TextureLoader.Load("icons.effect_wasted_gold");
		goodShieldTexture = TextureLoader.Load("icons.effect_shield");

		effectIcons = new() {
			{LUXURIES,  smileyFaceTexture},
			{TAXES,  goodGoldTexture},
			{RESEARCH,  beakerTexture},
			{CORRUPTION,  wastedGoldTexture},
			{CONSTRUCTION,  goodShieldTexture}
		};

		shieldBox = new Control { Position = new Vector2(904, 626), Size = new Vector2(116, 138), MouseFilter = MouseFilterEnum.Ignore };
		background.AddChild(shieldBox);
		shieldsInBoxContainer.Visible = false;
		RenderShieldBox(shieldCost: 30, shieldsInBox: 15);
		RenderShieldRow(goodShields: 10, corruptShields: 3);
		RenderFoodBox(foodNeededToGrow: 20, foodStored: 10, foodLostPerTurn: 2, hasGranary: true);
		RenderFoodRow(foodEatenPerTurn: 3, foodSurplus: 1);

		Hidden += OnExit;
	}

	public override void _UnhandledInput(InputEvent @event) {
		// Only capture mouse events if we're visible.
		if (!this.Visible) {
			return;
		}

		// If we left clicked on a tile, handle reassigning a citizen accordingly.
		if (@event is InputEventMouseButton eventMouseButton) {
			if (eventMouseButton.ButtonIndex == MouseButton.Left) {
				GetViewport().SetInputAsHandled();
				if (eventMouseButton.IsPressed()) {
					EngineStorage.ReadGameData((GameData gameData) => {
						if (productionMenu != null) {
							productionMenu.Visible = false;
						}
						if (readOnly) {
							return;
						}

						Tile tile = mapView.tileOnScreenAt(gameData.map, eventMouseButton.Position);
						if (tile != null) {
							// (The screen shows it once the order is done: Refresh.)
							new MsgWorkTile(tileAssignmentLayer.city.id, tile).send();
						}
					});
				}
			}
		}
	}

	// An order changed this city: show it again.
	public void Refresh(ID cityId) {
		City city = tileAssignmentLayer.city;
		if (!Visible || city == null || city.id != cityId) {
			return;
		}
		EngineStorage.ReadGameData((GameData gameData) => {
			RenderPopHeads(city);
			RenderCulture(city);
			RenderFoodDetails(city);
			RenderCommerceDetails(city);
			RenderProductionDetails(gameData, city);
		});
	}

	private void OnShowCityScreen(ParameterWrapper<City> city) {
		EngineStorage.ReadGameData((GameData gameData) => {
			OnShowCityScreenLocked(gameData, city);
		});
	}

	private void OnShowCityScreenLocked(GameData gameData, ParameterWrapper<City> city) {
		readOnly = city.Value.owner.id != EngineStorage.viewerID;
		productionButton.Disabled = readOnly;
		previousCity.Visible = !readOnly;
		nextCity.Visible = !readOnly;
		this.Show();
		// The production list only when the production box is clicked.
		productionMenu.Hide();
		// (Kept from the first city shown, not the next or previous one.)
		viewBefore ??= (mapView.cameraZoom, mapView.cameraLocation);
		mapView.cameraZoom = CityZoom;
		mapView.centerCameraOnTile(city.Value.location.neighbors[TileDirection.SOUTH]);
		tileAssignmentLayer.city = city.Value;
		cityName.Text = city.Value.name;
		RenderPopHeads(city.Value);
		RenderCulture(city.Value);
		RenderFoodDetails(city.Value);
		RenderCommerceDetails(city.Value);
		RenderProductionDetails(gameData, city.Value);
		RenderExistingBuildings(city.Value);
		RenderDraftButton(gameData, city.Value);
		RenderGovernorButton(city.Value);
		RenderStrategicResources(gameData, city.Value);
		RenderLuxuries(gameData, city.Value);
	}

	private void OnExit() {
		productionMenu.Hide();
		tileAssignmentLayer.city = null;
		if (viewBefore is (float zoom, Vector2 location)) {
			mapView.cameraZoom = zoom;
			mapView.cameraLocation = location;
			viewBefore = null;
		}
	}

	// A button with Civ3's art, or a small labeled one where the art isn't
	// there (the standalone art has none).
	private static BaseButton ArtButton(string art, string text, Vector2 position) {
		try {
			TextureButton button = new() { Position = position };
			TextureLoader.SetButtonTextures(button, art);
			return button;
		} catch (Exception) {
			return new Button { Text = text, Position = position };
		}
	}

	// (A disabled button keeps its art: dimmed, its tooltip saying why.)
	private static void Enable(BaseButton button, bool enabled) {
		button.Disabled = !enabled;
		button.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.45f);
	}

	private void RenderStrategicResources(GameData gameData, City city) {
		Dictionary<C7GameData.Resource, int> resourceCounter = city.GetStrategicResources(gameData);

		foreach (var child in strategicResources.GetChildren()) {
			strategicResources.RemoveChild(child);
		}

		foreach ((C7GameData.Resource resource, int count) in resourceCounter) {
			VBoxContainer resourceContainer = new();
			resourceContainer.AddThemeConstantOverride("separation", 0);

			var texture = (ImageTexture)TextureLoader.Load("resources.large", resource, useCache: true).Duplicate();
			texture.SetSizeOverride(new(45, 45));

			TextureRect resourceRect = new() {
				Texture = texture,
			};

			Label resourceLabel = new() {
				Text = count.ToString(),
				HorizontalAlignment = HorizontalAlignment.Center
			};

			resourceContainer.AddChild(resourceRect);
			resourceContainer.AddChild(resourceLabel);

			strategicResources.AddChild(resourceContainer);
		}
	}

	private void RenderLuxuries(GameData gameData, City city) {
		Dictionary<C7GameData.Resource, int> resourceCounter = city.GetLuxuries(gameData);

		foreach (var child in luxuriesContainer.GetChildren()) {
			luxuriesContainer.RemoveChild(child);
		}

		foreach ((C7GameData.Resource resource, int count) in resourceCounter) {
			HBoxContainer resourceContainer = new();

			Label resourceCount = new() {
				Text = "(" + count.ToString() + ")"
			};

			var texture = TextureLoader.Load("resources.small", resource, useCache: true);

			TextureRect resourceRect = new() {
				Texture = texture,
			};

			resourceContainer.AddChild(resourceCount);
			resourceContainer.AddChild(resourceRect);

			luxuriesContainer.AddChild(resourceContainer);
		}
	}

	// The button is disabled, with the reason as its tooltip, when the city
	// can't draft.
	private void RenderDraftButton(GameData gameData, City city) {
		string reason = readOnly ? "Not our city" : city.WhyCannotDraft(gameData);
		Enable(draftButton, reason == null);
		draftButton.TooltipText = reason == null ? $"Draft a citizen into a conscript {city.DraftableUnit(gameData).name}" : $"Draft: {reason}";
	}

	private void RenderGovernorButton(City city) {
		bool ours = !readOnly && city.owner.isHuman;
		Enable(governorButton, ours);
		governorButton.TooltipText = ours ? $"Governor: how {city.name} is run (citizens, moods, production)" : "Governor: not our city";
	}

	private void RenderExistingBuildings(City city) {
		foreach (var node in existingBuildings.GetChildren()) {
			existingBuildings.RemoveChild(node);
		}

		// Clicking a building offers to sell it, as in Civ3.
		foreach (CityBuilding building in city.GetBuildings()) {
			Button button = new() {
				Text = building.building.name,
				Flat = true,
				Alignment = HorizontalAlignment.Left,
				TooltipText = readOnly ? "" : "Click to sell",
				Disabled = readOnly,
			};
			Building b = building.building;
			button.Pressed += () => new MsgDisplaySellBuildingPopup(city, b).sendFromScreens();
			existingBuildings.AddChild(button);
		}
	}

	private void RenderFoodDetails(City city) {
		string growthStr;
		int foodLostPerTurn = 0;
		int foodSurplus = 0;
		int turnsUntilGrowth = city.TurnsUntilGrowth();
		if (turnsUntilGrowth == int.MaxValue) {
			growthStr = "Not growing.";
		} else if (turnsUntilGrowth == int.MinValue) {
			growthStr = "Starving!";
			foodLostPerTurn = Math.Abs(city.FoodGrowthPerTurn());
		} else {
			growthStr = $"Growth in {turnsUntilGrowth} turns.";
			foodSurplus = city.FoodGrowthPerTurn();
		}
		growthInLabel.Text = growthStr;
		foodLabel.Text = $"{city.CurrentFoodYield()} per turn";

		RenderFoodBox(city.FoodNeededToGrow(), city.foodStored, foodLostPerTurn, hasGranary: city.HasGranary());
		RenderFoodRow(city.FoodConsumedPerTurn(), foodSurplus);
	}

	private void RenderFoodRow(int foodEatenPerTurn, int foodSurplus) {
		foreach (Node child in foodRowContainer.GetChildren()) {
			foodRowContainer.RemoveChild(child);
			child.QueueFree();
		}

		int width = (int)foodRowContainer.Size.X;
		int iconWidth = eatenFoodTexture.GetWidth();
		int spacerWidth = foodSurplus > 0 ? 100 : 0;
		int spacePerIcon = (width - spacerWidth) / (foodEatenPerTurn + foodSurplus);

		int xOffset = 0;
		for (int i = 0; i < foodEatenPerTurn; ++i) {
			TextureRect icon = new() { Texture = eatenFoodTexture };
			foodRowContainer.AddChild(icon);
			icon.SetPosition(new Vector2(xOffset, 0));
			xOffset += Math.Min(spacePerIcon, iconWidth + 10);
		}

		xOffset = width - iconWidth;
		for (int i = 0; i < foodSurplus; ++i) {
			TextureRect icon = new() { Texture = foodTexture };
			foodRowContainer.AddChild(icon);
			icon.SetPosition(new Vector2(xOffset, 0));
			xOffset -= Math.Min(spacePerIcon, iconWidth + 10);
		}
	}

	private void RenderFoodBox(int foodNeededToGrow, int foodStored, int foodLostPerTurn, bool hasGranary) {
		if (hasGranary && foodStored >= foodNeededToGrow / 2) {
			RenderFoodBoxWithGranary(foodNeededToGrow, foodStored, foodLostPerTurn);
		} else {
			RenderFoodBoxNoGranary(foodNeededToGrow, foodStored, foodLostPerTurn);
		}
	}

	private void RenderFoodBoxNoGranary(int foodNeededToGrow, int foodStored, int foodLostPerTurn) {
		foreach (Node child in foodInBoxContainer.GetChildren()) {
			foodInBoxContainer.RemoveChild(child);
			child.QueueFree();
		}
		foreach (Node child in foodInGranaryContainer.GetChildren()) {
			foodInGranaryContainer.RemoveChild(child);
			child.QueueFree();
		}
		granaryLabel.Visible = false;
		foodInGranaryContainer.Visible = false;

		int width = 120;
		int height = 180;

		// Hardcode the common 20/40/60 sizes, but support custom rules as well.
		if (foodNeededToGrow == 20) {
			foodInBoxContainer.Columns = 2;
		} else if (foodNeededToGrow == 40) {
			foodInBoxContainer.Columns = 4;
		} else if (foodNeededToGrow == 60) {
			foodInBoxContainer.Columns = 6;
		} else {
			foodInBoxContainer.Columns = (int)Math.Ceiling(Math.Sqrt(foodNeededToGrow));
		}

		int itemsPerColumn = (int)Math.Ceiling((float)foodNeededToGrow / foodInBoxContainer.Columns);
		int iconSize = Math.Min(height / itemsPerColumn, width / foodInBoxContainer.Columns);

		int nonEmptySquares = 0;
		for (int i = 0; i < Math.Min(foodNeededToGrow, foodStored) - foodLostPerTurn; ++i) {
			foodInBoxContainer.AddChild(new TextureRect() {
				Texture = foodTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new Vector2(iconSize, iconSize),
			});
			++nonEmptySquares;
		}
		for (int i = 0; i < foodLostPerTurn; ++i) {
			foodInBoxContainer.AddChild(new TextureRect() {
				Texture = noFoodTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new Vector2(iconSize, iconSize),
			});
			++nonEmptySquares;
		}
		for (int i = 0; i < foodNeededToGrow - nonEmptySquares; ++i) {
			foodInBoxContainer.AddChild(new TextureRect() {
				Texture = emptyFoodTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new Vector2(iconSize, iconSize)
			});
		}
	}

	private void RenderFoodBoxWithGranary(int foodNeededToGrow, int foodStored, int foodLostPerTurn) {
		foreach (Node child in foodInBoxContainer.GetChildren()) {
			foodInBoxContainer.RemoveChild(child);
			child.QueueFree();
		}
		foreach (Node child in foodInGranaryContainer.GetChildren()) {
			foodInGranaryContainer.RemoveChild(child);
			child.QueueFree();
		}
		granaryLabel.Visible = true;
		foodInGranaryContainer.Visible = true;

		int width = 120;
		int height = 80;

		// Hardcode the common 20/40/60 sizes, but support custom rules as well.
		if (foodNeededToGrow == 20) {
			foodInBoxContainer.Columns = 2;
		} else if (foodNeededToGrow == 40) {
			foodInBoxContainer.Columns = 4;
		} else if (foodNeededToGrow == 60) {
			foodInBoxContainer.Columns = 6;
		} else {
			foodInBoxContainer.Columns = (int)Math.Ceiling(Math.Sqrt(foodNeededToGrow));
		}
		foodInGranaryContainer.Columns = foodInBoxContainer.Columns;

		int itemsPerColumn = (int)Math.Ceiling((float)foodNeededToGrow / foodInBoxContainer.Columns) / 2;
		int iconSize = Math.Min(height / itemsPerColumn, width / foodInBoxContainer.Columns);

		// Start by filling the granary.
		int foodInGranary = foodNeededToGrow / 2;
		if (foodInGranary > foodStored) { throw new Exception($"not enough food {foodInGranary} {foodStored}"); }

		int foodLostInGranaryPerTurn = Math.Max(0, foodLostPerTurn - (foodStored - foodInGranary));

		for (int i = 0; i < foodInGranary - foodLostInGranaryPerTurn; ++i) {
			foodInGranaryContainer.AddChild(new TextureRect() {
				Texture = foodTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new Vector2(iconSize, iconSize),
			});
		}
		for (int i = 0; i < foodLostInGranaryPerTurn; ++i) {
			foodInGranaryContainer.AddChild(new TextureRect() {
				Texture = noFoodTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new Vector2(iconSize, iconSize),
			});
		}

		// Now fill the rest of the box.
		foodStored -= foodInGranary;
		foodLostPerTurn -= foodLostInGranaryPerTurn;

		for (int i = 0; i < foodStored - foodLostPerTurn; ++i) {
			foodInBoxContainer.AddChild(new TextureRect() {
				Texture = foodTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new Vector2(iconSize, iconSize),
			});
		}
		for (int i = 0; i < foodLostPerTurn; ++i) {
			foodInBoxContainer.AddChild(new TextureRect() {
				Texture = noFoodTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new Vector2(iconSize, iconSize),
			});
		}

		for (int i = 0; i < foodNeededToGrow / 2 - foodStored; ++i) {
			foodInBoxContainer.AddChild(new TextureRect() {
				Texture = emptyFoodTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new Vector2(iconSize, iconSize)
			});
		}
	}

	private void RenderCommerceDetails(City city) {
		CommerceBreakdown breakdown = city.CurrentCommerceYield();
		RenderCommerceRow(taxesRow, taxTexture, breakdown.taxes, $"Taxes: {breakdown.taxes} gold a turn");
		RenderCommerceRow(scienceRow, scienceTexture, breakdown.beakers, $"Science: {breakdown.beakers} beakers a turn"
			+ (breakdown.corrupted > 0 ? $"\nCorruption: {breakdown.corrupted} commerce lost a turn (in red, on the left)" : ""), breakdown.corrupted);
		RenderCommerceRow(luxuriesRow, luxuryTexture, breakdown.happiness, $"Luxuries: {breakdown.happiness} a turn");
	}

	// A green bar (`top` its top edge): its icons and count, and the
	// tooltip over both.
	private CommerceRow AddCommerceRow(int top) {
		CommerceRow row = new() {
			row = new Control { Position = new Vector2(289, top - 4), Size = new Vector2(479, 29), MouseFilter = MouseFilterEnum.Stop },
			icons = new Control { Size = new Vector2(385, 29), MouseFilter = MouseFilterEnum.Ignore },
			count = new Label { Theme = yieldDetailsFontTheme, MouseFilter = MouseFilterEnum.Ignore },
			lost = new Label { Theme = yieldDetailsFontTheme, MouseFilter = MouseFilterEnum.Ignore },
		};
		row.lost.AddThemeColorOverride("font_color", new Color(0.75f, 0.05f, 0.05f));
		HBoxContainer counts = new() { Position = new Vector2(401, 0), MouseFilter = MouseFilterEnum.Ignore };
		counts.AddThemeConstantOverride("separation", 10);
		counts.AddChild(row.count);
		counts.AddChild(row.lost);
		row.row.AddChild(row.icons);
		row.row.AddChild(counts);
		background.AddChild(row.row);
		return row;
	}

	// As Civ3's, and the shield row: `lost` red coins from the bar's left
	// end, `amount` icons up to its tip, squeezed together when they
	// wouldn't fit.
	private void RenderCommerceRow(CommerceRow row, ImageTexture icon, int amount, string tooltip, int lost = 0) {
		foreach (Node child in row.icons.GetChildren()) {
			row.icons.RemoveChild(child);
			child.QueueFree();
		}
		static Vector2 Fitted(ImageTexture texture) => texture.GetSize() * Math.Min(1, CommerceIconHeight / texture.GetHeight());
		Vector2 lostSize = Fitted(corruptGoldTexture), iconSize = Fitted(icon);
		float room = row.icons.Size.X - 10;
		float width = lost * lostSize.X + amount * iconSize.X;
		float squeeze = lost + amount > 1 && width > room ? (width - room) / (lost + amount - 1) : 0;
		void Place(ImageTexture texture, Vector2 size, float x) {
			TextureRect rect = new() {
				Texture = texture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.Scale,
				// (Smoothed: shrunk pixel art is choppy otherwise.)
				TextureFilter = TextureFilterEnum.Linear,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			rect.Position = new Vector2(x, (row.icons.Size.Y - size.Y) / 2);
			rect.Size = size;
			row.icons.AddChild(rect);
		}
		for (int i = 0; i < lost; ++i) {
			Place(corruptGoldTexture, lostSize, i * (lostSize.X - squeeze));
		}
		for (int i = 1; i <= amount; ++i) {
			Place(icon, iconSize, room - iconSize.X - (i - 1) * (iconSize.X - squeeze));
		}
		row.count.Text = amount.ToString();
		row.lost.Text = lost.ToString();
		row.lost.Visible = lost > 0;
		row.row.TooltipText = tooltip;
	}

	private void RenderProductionDetails(GameData gameData, City city) {
		CorruptableValue shields = city.CurrentProductionYield();
		RenderShieldBox(city.owner.ShieldCost(city.itemBeingProduced), city.shieldsStored);
		RenderShieldRow(shields.useful, shields.corrupt);
		productionLabel.Text = $"PRODUCTION: {shields.useful + shields.corrupt} per turn";
		completeInLabel.Text = city.TurnsUntilProductionFinished() == int.MaxValue ? "--" : $"Complete in {city.TurnsUntilProductionFinished()} turns";

		foreach (Node child in productionButton.GetChildren()) {
			child.QueueFree();
		}

		int marginTop = 35;

		if (city.itemBeingProduced is UnitPrototype proto) {
			var unit = proto.GetInstance(gameData.GenerateID(proto.name), proto, city.owner);
			AnimationManager animationManager = mapView.game.animationController.civ3AnimData.forUnit(unit, MapUnit.AnimatedAction.DEFAULT).animationManager;
			ShaderMaterial material = PlayerTextureUtil.GetShaderMaterialForUnit(city.owner.GetPlayerColor());
			(ImageTexture baseImage, ImageTexture imageTint) = animationManager.GetAnimationFrameAndTintTextures(unit);

			// Add the base sprite.
			Sprite2D baseImageSprite = new();
			baseImageSprite.Texture = baseImage;
			baseImageSprite.Position = new Vector2(productionButton.TextureNormal.GetWidth() / 2.0f, marginTop);
			productionButton.AddChild(baseImageSprite);

			// Add the tint sprite, hooking up the shader.
			Sprite2D imageTintSprite = new Sprite2D();
			imageTintSprite.Texture = imageTint;
			imageTintSprite.Material = material;
			imageTintSprite.Position = baseImageSprite.Position;
			productionButton.AddChild(imageTintSprite);
		} else if (city.itemBeingProduced is Building b) {
			Sprite2D icon = new();
			icon.Texture = TextureLoader.Load("building_icons.large", b, useCache: true);
			icon.Position = new Vector2(productionButton.TextureNormal.GetWidth() / 2.0f, marginTop);
			productionButton.AddChild(icon);
		} else if (city.itemBeingProduced is Inflow inflow) {
			Sprite2D icon = new();
			icon.Texture = TextureLoader.Load("building_icons.large", inflow, useCache: true);
			icon.Position = new Vector2(productionButton.TextureNormal.GetWidth() / 2.0f, marginTop);
			productionButton.AddChild(icon);
		}

		Label productionButtonLabel = new();
		productionButton.AddChild(productionButtonLabel);
		productionButtonLabel.SetPosition(new Vector2(0, 65));
		productionButtonLabel.SetTextAndCenterLabel($"{city.itemBeingProduced.name}");

		productionMenu.AddItems(gameData, city, (IProducible p) => {
			// (Chosen: the list goes until the production box is clicked.)
			productionMenu.Hide();
			new MsgChooseProduction(city.id, p.name).send();
		});
	}

	private void RenderShieldRow(int goodShields, int corruptShields) {
		foreach (Node child in shieldRowContainer.GetChildren()) {
			shieldRowContainer.RemoveChild(child);
			child.QueueFree();
		}

		int width = (int)shieldRowContainer.Size.X;
		int iconWidth = shieldTexture.GetWidth();
		int spacerWidth = corruptShields > 0 ? 100 : 0;
		int spacePerIcon = (width - spacerWidth) / (goodShields + corruptShields);

		int xOffset = 0;
		for (int i = 0; i < corruptShields; ++i) {
			TextureRect icon = new() { Texture = corruptShieldTexture };
			shieldRowContainer.AddChild(icon);
			icon.SetPosition(new Vector2(xOffset, 0));
			xOffset += Math.Min(spacePerIcon, iconWidth);
		}

		xOffset = width - iconWidth;
		for (int i = 0; i < goodShields; ++i) {
			TextureRect icon = new() { Texture = shieldTexture };
			shieldRowContainer.AddChild(icon);
			icon.SetPosition(new Vector2(xOffset, 0));
			xOffset -= Math.Min(spacePerIcon, iconWidth);
		}
	}

	// The shields in the box, then the empty ones, down each column and
	// across, centered in the box.
	private void RenderShieldBox(int shieldCost, int shieldsInBox) {
		foreach (Node child in shieldBox.GetChildren()) {
			shieldBox.RemoveChild(child);
			child.QueueFree();
		}
		if (shieldCost <= 0) {
			return;
		}

		Vector2 icon = shieldTexture.GetSize();
		int rows = Math.Min(ShieldBoxRows, shieldCost);
		int columns = (shieldCost + rows - 1) / rows;
		float across = columns == 1 ? 0 : Math.Min(ShieldBoxSpacing.X, (shieldBox.Size.X - icon.X) / (columns - 1));
		Vector2 used = new((columns - 1) * across + icon.X, (rows - 1) * ShieldBoxSpacing.Y + icon.Y);
		Vector2 start = ((shieldBox.Size - used) / 2).Floor();
		for (int i = 0; i < shieldCost; ++i) {
			shieldBox.AddChild(new TextureRect {
				Texture = i < shieldsInBox ? shieldTexture : emptyShieldTexture,
				Position = start + new Vector2(i / rows * across, i % rows * ShieldBoxSpacing.Y),
				MouseFilter = MouseFilterEnum.Ignore,
			});
		}
	}

	private void RenderCulture(City city) {
		culturePerTurn.Text = $"{city.GetCulturePerTurn()}/turn";

		int nextCultureExpansion = (int)Math.Pow(10, city.GetBorderExpansionLevel());
		totalCulture.Text = $"Total: {city.GetCulture()}/{nextCultureExpansion}";
	}

	private void RenderPopHeads(City city) {
		// Reset any old heads.
		foreach (TextureButton head in popHeads) {
			background.RemoveChild(head);
			head.QueueFree();
		}
		// Reset any old head effects.
		foreach (TextureRect effect in popHeadEffects) {
			background.RemoveChild(effect);
			effect.QueueFree();
		}

		popHeads.Clear();
		popHeadEffects.Clear();

		int eraNum = city.owner.EraIndex();

		// The pop head textures are 50 x 50, but have a 1px border on all sides
		//
		// The texture file has 16 rows of the default citizen, in groups of 4
		// per era (content, happy, resisting, unhappy). There are 10 columns,
		// the first 5 are male heads of different regions for civs, the other 5
		// are female heads.
		//
		// After the 16 rows of default citizens there is one row per specialist
		// type, and again 10 columns per row. This time they are
		// (ancient, middle, industrial, modern, blank) for male and female heads
		//
		// TODO: handle per-civ regions
		// TODO: handle male/female citizens

		// Start by splitting the default residents from the specialists, since
		// they are spaced apart in the UI.
		List<CityResident> happyResidents =
			city.residents.FindAll(x => x.citizenType.IsDefaultCitizen && !x.resisting && x.mood == CityResident.Mood.Happy);
		List<CityResident> contentResidents =
			city.residents.FindAll(x => x.citizenType.IsDefaultCitizen && !x.resisting && x.mood == CityResident.Mood.Content);
		List<CityResident> unhappyResidents =
			city.residents.FindAll(x => x.citizenType.IsDefaultCitizen && !x.resisting && x.mood == CityResident.Mood.Unhappy);
		// Resisters are shown last among the laborers, with their own head.
		unhappyResidents.AddRange(city.residents.FindAll(x => x.resisting));
		List<CityResident> specialists = city.residents.FindAll(x => !x.citizenType.IsDefaultCitizen);

		// Leave a 1 head gap if we have specialists.
		int width = city.residents.Count * PopHead.HEAD_SIZE;
		if (specialists.Count > 0) {
			width += PopHead.HEAD_SIZE;
		}

		// Leave a 1 head gap between each section of moods.
		int numMoodsPresent = (happyResidents.Count > 0 ? 1 : 0)
			+ (contentResidents.Count > 0 ? 1: 0)
			+ (unhappyResidents.Count > 0 ? 1: 0);
		width += (numMoodsPresent - 1) * PopHead.HEAD_SIZE;

		// Track the x position of each head so that we're centered in the screen
		int xPos = background.Texture.GetWidth() / 2 + -width / 2;

		// Add each of the default citizens. These are buttons with the idea that
		// we can eventually support clicking on the heads to view details, such
		// as the reason for unhappiness.
		foreach (CityResident cr in happyResidents) {
			xPos = AddDefaultCitizen(cr, xPos, eraNum);
		}
		if (happyResidents.Count > 0 && (contentResidents.Count > 0 || unhappyResidents.Count > 0)) {
			xPos += PopHead.HEAD_SIZE;
		}
		foreach (CityResident cr in contentResidents) {
			xPos = AddDefaultCitizen(cr, xPos, eraNum);
		}
		if (contentResidents.Count > 0 && unhappyResidents.Count > 0) {
			xPos += PopHead.HEAD_SIZE;
		}
		foreach (CityResident cr in unhappyResidents) {
			xPos = AddDefaultCitizen(cr, xPos, eraNum);
		}

		// Add space before specialists.
		xPos += PopHead.HEAD_SIZE;

		// Add each of the specialists.
		//
		// TODO: Render the specialist effect (like a smiley for entertainers)
		// in the corner of the head.
		foreach (CityResident cr in specialists) {
			TextureButton tb = new();
			tb.TextureNormal = PopHead.GetTexture(cr, eraNum);
			tb.SetPosition(new Vector2(xPos, POP_HEAD_OFFSET_Y));
			tb.Disabled = readOnly;

			List<CitizenType> specialistTypes = null;
			EngineStorage.ReadGameData((GameData gameData) => { specialistTypes = city.owner.GetKnownSpecialists(gameData); });

			int resident = city.residents.IndexOf(cr);
			tb.Pressed += () => new MsgCycleSpecialist(city.id, resident).send();

			background.AddChild(tb);

			float iconOffset = 0.0f;
			foreach (KeyValuePair<string, int> entry in GetSpecialistEffectInfo(cr)) {
				if (!effectIcons.TryGetValue(entry.Key, out ImageTexture texture)) continue;
				for (int i = 0; i < entry.Value; i++) {
					TextureRect icon = new();
					icon.Texture = texture;

					float iconHalfWidth = icon.Texture.GetWidth() / 2.0f;
					float iconXPos = tb.Position.X + iconHalfWidth * i + iconOffset;
					float iconYPos = tb.Position.Y + PopHead.HEAD_SIZE - 10;

					icon.SetPosition(new Vector2(iconXPos, iconYPos));
					background.AddChild(icon);
					popHeadEffects.Add(icon);

					// If multiple effects are applicable we need to
					// calculate the total offset that comes before each type.
					// The offset is only calculated on the last iteration
					// because we don't want any extra offset between effects of the same type.
					if (i == entry.Value - 1) {
						iconOffset += iconHalfWidth * entry.Value;
					}
				}
			}

			popHeads.Add(tb);
			xPos += PopHead.HEAD_SIZE;
		}
	}

	private void SwitchToNextCity() {
		EngineStorage.ReadGameData((GameData gameData) => {
			City currentCity = tileAssignmentLayer.city;
			List<City> cities = currentCity.owner.cities;
			City nextCity = cities[(cities.IndexOf(currentCity) + 1) % cities.Count];
			Game.RefreshMoods(nextCity, gameData);
			OnShowCityScreenLocked(gameData, new ParameterWrapper<City>(nextCity));
		});
	}

	private void SwitchToPreviousCity() {
		EngineStorage.ReadGameData((GameData gameData) => {
			City currentCity = tileAssignmentLayer.city;
			List<City> cities = currentCity.owner.cities;
			City previousCity = cities[(cities.IndexOf(currentCity) + cities.Count - 1) % cities.Count];
			Game.RefreshMoods(previousCity, gameData);
			OnShowCityScreenLocked(gameData, new ParameterWrapper<City>(previousCity));
		});
	}

	private int AddDefaultCitizen(CityResident cr, int xPos, int eraNum) {
		TextureButton tb = new();
		tb.TextureNormal = PopHead.GetTexture(cr, eraNum);
		tb.Disabled = readOnly;
		tb.SetPosition(new Vector2(xPos, POP_HEAD_OFFSET_Y));
		background.AddChild(tb);
		popHeads.Add(tb);
		return xPos + PopHead.HEAD_SIZE;
	}

	private Dictionary<string, int> GetSpecialistEffectInfo(CityResident cityResident) {
		Dictionary<string, int> specialistEffectInfo = new();
		specialistEffectInfo.TryAdd(LUXURIES, cityResident.citizenType.Luxuries);
		specialistEffectInfo.TryAdd(TAXES, cityResident.citizenType.Taxes);
		specialistEffectInfo.TryAdd(RESEARCH, cityResident.citizenType.Research);
		specialistEffectInfo.TryAdd(CORRUPTION, cityResident.citizenType.Corruption);
		specialistEffectInfo.TryAdd(CONSTRUCTION, cityResident.citizenType.Construction);

		return specialistEffectInfo;
	}
}
