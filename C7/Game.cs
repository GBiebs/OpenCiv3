using Godot;
using System;
using System.Diagnostics;
using C7Engine;
using C7GameData;
using Serilog;
using C7.UIElements;
using C7Engine.Pathing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using C7.UIElements;
using C7.UIElements.Popups;
using static AdvisorHead;
using static C7GameData.MapUnit;

public class GotoInfo {
	public Tile destinationTile = null;
	public int moveCost = -1;
	public TilePath path = null;
	public HashSet<System.Numerics.Vector2> pathCoords;
	public bool attackingMove = false;
	public Player requiresWarDeclarationOnPlayer = null;
	public Intent intent = Intent.Disabled;
};

public class TileInfo {
	public Tile targetTile;
	public HashSet<Tile> coveredTiles = [];
	public HashSet<Tile> cityLabelsToHide = [];

	public TileInfo(Tile tile) {
		targetTile = tile;

		List<TileDirection> coverage = [TileDirection.SOUTHWEST, TileDirection.SOUTH, TileDirection.SOUTHEAST];
		foreach (var dir in coverage)
			if (TryNeighbor(tile, dir, out var neighbor))
				coveredTiles.Add(neighbor);

		List<TileDirection> labelCoverage = [TileDirection.NORTHWEST, TileDirection.NORTH, TileDirection.NORTHEAST];
		foreach (var dir in labelCoverage)
			if (TryNeighbor(tile, dir, out var neighbor))
				cityLabelsToHide.Add(neighbor);
	}

	private bool TryNeighbor(Tile tile, TileDirection dir, out Tile neighbor) {
		neighbor = tile.neighbors[dir];
		return neighbor != Tile.NONE;
	}

	public bool IsCovered(Tile tile) => tile == targetTile || coveredTiles.Contains(tile);

	public bool HasCityLabelToCover(Tile tile) => cityLabelsToHide.Contains(tile);
};

public class BombardInfo {
	public MapUnit bombardingUnit;
	public Tile mouseTile;
	public MapUnit.BombardTarget bombardTarget = MapUnit.BombardTarget.None;

	public BombardInfo(MapUnit bombardingUnit) {
		this.bombardingUnit = bombardingUnit;
	}

	public bool RequiresWarDeclaration(Tile tile, out Player player) {
		player = null;

		var bombarder = bombardingUnit.owner;

		if (bombardTarget == MapUnit.BombardTarget.None)
			return false;

		if (bombardTarget == MapUnit.BombardTarget.City) {
			player = tile.owningCity.owner;
			return bombarder.IsAtPeaceWith(player);
		}

		if (bombardTarget == MapUnit.BombardTarget.Unit) {
			player = tile.unitsOnTile.First().owner;
			return bombarder.IsAtPeaceWith(player);
		}

		if (bombardTarget == MapUnit.BombardTarget.Improvement) {
			player = tile.OwningPlayer();
			return player != null && player != bombarder && bombarder.IsAtPeaceWith(player);
		}

		throw new NotImplementedException($"A case is missing here. {tile} | {player} | {bombardTarget}");
	}
};

// An air mission, airdrop or airlift waiting for its target tile (see Air).
public class AirTargetInfo {
	public MapUnit unit;
	public string action;
	public Tile mouseTile;

	public AirTargetInfo(MapUnit unit, string action) {
		this.unit = unit;
		this.action = action;
	}

	public Air.Mission Mission() {
		return action switch {
			C7Action.UnitBombing => Air.Mission.Bombing,
			C7Action.UnitRecon => Air.Mission.Recon,
			C7Action.UnitPrecisionStrike => Air.Mission.PrecisionStrike,
			_ => Air.Mission.Rebase,
		};
	}

	public bool CanTarget(GameData gameData, Tile tile) {
		if (tile == null) {
			return false;
		}
		return action switch {
			C7Action.UnitAirdrop => Air.CanAirdrop(unit, tile),
			C7Action.UnitAirlift => Air.CanAirlift(gameData, unit, tile),
			_ => Air.CanFly(unit, Mission(), tile),
		};
	}

	// How far around the unit to draw the reachable tiles; re-basing and
	// airlifts reach too far to draw.
	public int GridRange() {
		return action is C7Action.UnitRebase or C7Action.UnitAirlift ? 0 : unit.unitType.operationalRange;
	}
}

public partial class Game : Node {
	private ILogger log = LogManager.ForContext<Game>();

	[Signal] public delegate void TurnEndedEventHandler();
	[Signal] public delegate void ShowSpecificAdvisorEventHandler();
	[Signal] public delegate void ShowGameViewEventHandler();
	[Signal] public delegate void ShowCityScreenEventHandler();

	[Signal] public delegate void PlayerTurnStartEventHandler();
	[Signal] public delegate void PlayerTurnEndEventHandler();
	[Signal] public delegate void GameInitializedEventHandler();
	[Signal] public delegate void InteractivePopUpEventHandler();

	[Signal] public delegate void UnitMovedEventHandler();

	[Export]
	Control Toolbar;
	private bool IsMovingCamera;
	private Vector2 OldPosition;

	// A left press with a unit selected (Civ3): once the button has been held
	// a moment, the route, the turns and the target show (gotoInfo) and
	// releasing moves the unit there. Only holding moves, so a quick click
	// can select another unit without its route flashing up first (user,
	// 2026-11-02, then a third shorter); on our own city two quick clicks
	// open it.
	private Tile movePressTile = null;
	private ulong movePressStartMs = 0;
	private const ulong HoldToMoveMs = 200;

	// Moving the mouse to an edge of the window scrolls the map that way.
	private const int EdgeScrollMargin = 3;
	private const float EdgeScrollSpeed = 900;

	[Export]
	private PopupOverlay popupOverlay;
	[Export]
	private CityScreen cityScreen;
	[Export]
	private Advisors advisor;
	[Export]
	private GameViews gameViews;
	[Export]
	private Diplomacy diplomacy;
	[Export]
	private Preferences preferences;
	[Export]
	private InteractablePopUpController interactablePopUpController;
	[Export]
	private DoubleClickHandler doubleClickHandler;
	[Export]
	public AnimationController animationController;
	[Export]
	public UnitSelector unitSelector;

	Stopwatch loadTimer = new Stopwatch();

	GlobalSingleton Global;
	Civ3FileDialog FileDialog;

	public Player controller; // Player that's controlling the UI.

	private MapView mapView;

	public enum GameState {
		PlayerTurn,
		ComputerTurn
	}
	public GameState CurrentState { get; private set; } = GameState.PlayerTurn;

	public MapUnit CurrentlySelectedUnit => unitSelector.CurrentlySelectedUnit;
	private bool HasCurrentlySelectedUnit() => CurrentlySelectedUnit != MapUnit.NONE;

	// When the game is in "goto" mode, the current destination and the cost of getting
	// there, in turns.
	//
	// Otherwise null.
	public GotoInfo gotoInfo = null;

	public BombardInfo bombardInfo = null;
	public AirTargetInfo airTargetInfo = null;

	public TileInfo tileInfo = null;

	// When in observer mode, the number of turns to play before prompting the
	// user to advance the turn manually. This allows for more rapid debugging
	// without pressing the spacebar repeatedly.
	public int turnsLeftToFastForward = 0;

	bool errorOnLoad = false;

	public override void _EnterTree() {
		loadTimer.Start();
	}

	// Called when the node enters the scene tree for the first time.
	// The catch should always catch any error, as it's the general catch
	// that gives an error if we fail to load for some reason.
	public override async void _Ready() {
		Global = GetNode<GlobalSingleton>("/root/GlobalSingleton");

		FileDialog = GetNode<Civ3FileDialog>("%LoadDialog");
		FileDialog.Canceled += OnResolved;
		FileDialog.FileSelected += path => OnResolved();

		advisor.VisibilityChanged += OnAdvisorVisibilityChanged;

		try {
			await InitializeGame();
			await StartGame();
		} catch (Exception ex) {
			errorOnLoad = true;
			string message = ex.Message;
			string[] stack = ex.StackTrace.Split("\r\n");   //for some reason it is returned with \r\n in the string as one line.  let's make it readable!
			foreach (string line in stack) {
				message = message + "\r\n" + line;
			}

			popupOverlay.ShowPopup(new ErrorMessage(message), PopupOverlay.PopupCategory.Advisor);
			log.Error(ex, "Unexpected error in Game.cs _Ready");
		}
	}

	public void ShowGovernor(City city) {
		popupOverlay.ShowPopup(new GovernorPopup(city), PopupOverlay.PopupCategory.Info);
	}

	public void ShowEspionage(City city) {
		popupOverlay.ShowPopup(new EspionagePopup(city, controller), PopupOverlay.PopupCategory.Info);
	}

	private async Task InitializeGame() {
		// Ensure we clear out our image caches, as scenarios and games will
		// use the same filenames but have different content for them.
		Util.ClearCaches();

		GameParams options = CreateGameParams();

		await CreateGameAndAssignPlayerController(options);

		foreach (var gameDataPlayer in EngineStorage.gameData.players) {
			if (TurnHandling.GetTurnNumber() == 0)
				if (gameDataPlayer.SitsOutFirstTurn())
					TurnHandling.InitTurnData(gameDataPlayer, true);
				else if (Global.SaveGame != null)
					TurnHandling.InitTurnData(gameDataPlayer, false);
		}

		InitializeMapView();
		InitializeAudio();
	}

	private void InitializeAudio() {
		GlobalAudio.manager.StopMusic();
		// TODO: switch to in-game playlist / music logic
	}

	private async Task StartGame() {
		log.Information("Now in game!");

		TurnHandling.OnBeginTurn();

		loadTimer.Stop();
		TimeSpan stopwatchElapsed = loadTimer.Elapsed;
		log.Information("Game scene load time: " + Convert.ToInt32(stopwatchElapsed.TotalMilliseconds) + " ms");

		EmitSignal(SignalName.GameInitialized);

		if (StatisticsCapture.ShotsDir != null) {
			StatisticsCapture.Run(this, gameViews);
		}

		if (Global.LoadGamePath == null) {
			new MsgNewGame(controller.id).send();
			await EngineStorage.WaitForMessageToEngine<MsgUiDisengaged>();
		}

		try {
			if (Global.LoadGamePath == null) {
				try {
					Autosave.ClearAutosaveDirectory();
				} catch {
				}
			}
			Autosave.PerformAutosave();
			if (Global.LoadGamePath == null) {
				try {
					int turn = TurnHandling.GetTurnNumber();
					if (turn == 0) {
						string autoDir = SaveNameGenerator.GetAutosaveDirectory();
						string pattern = "-Turn 000-";
						foreach (string f in System.IO.Directory.GetFiles(autoDir, "*.json")) {
							if (System.IO.Path.GetFileName(f).Contains(pattern)) {
								C7Engine.C7Settings.SetLastWorldSave(f);
								break;
							}
						}
					}
				} catch {
				}
			} else {
				try {
					int turn = TurnHandling.GetTurnNumber();
					if (turn == 0) {
						string autoDir = SaveNameGenerator.GetAutosaveDirectory();
						string pattern = "-Turn 000-";
						foreach (string f in System.IO.Directory.GetFiles(autoDir, "*.json")) {
							if (System.IO.Path.GetFileName(f).Contains(pattern)) {
								C7Engine.C7Settings.SetLastWorldSave(f);
								break;
							}
						}
					}
				} catch {
				}
			}
		} catch {
		}

		Global.ResetLoadGameFields();
	}

	private GameParams CreateGameParams() {
		return new(GamePaths.DefaultBicPath) {
			GetPediaIconsPath = (scenarioSearchPath) => {
				// When the game loading logic tries to load the PediaIcons file, set the
				// scenario search path and then use our Civ3MediaPath searching logic to
				// find the correct version of the file.
				//
				// This weird bit of indirection is necessary because the C7GameData project
				// can't depend on the C7 project without a circular dependency, and the
				// search logic has a Godot dependency, so it doesn't make sense to live
				// in the C7GameData project.
				//
				// This also helps ensure the weird stateful behavior of the Util class works,
				// since the search path/mod path is a static global variable - we want to
				// be sure it is always set properly, so doing it during game creation
				// is reasonable.
				Util.setModPath(scenarioSearchPath);
				log.Debug("RelativeModPath ", scenarioSearchPath);
				return Util.Civ3MediaPath("Text/PediaIcons.txt");
			},
			GetCivilopediaTextPath = (scenarioSearchPath) => {
				Util.setModPath(scenarioSearchPath);
				return Util.Civ3MediaPath("Text/Civilopedia.txt");
			},
			GameModeLoader = (config) => {
				Global.ActivateGameMode(config);
				return Global.GameMode.behaviors;
			},
		};
	}

	private async Task CreateGameAndAssignPlayerController(GameParams options) {
		// Initializes the game data and returns the "human" player
		if (Global.SaveGame != null) {
			controller = await CreateGame.createGame(Global.SaveGame, options.GameModeLoader);
		} else if (Global.LoadGamePath != null) {
			controller = await CreateGame.createGame(Global.LoadGamePath, options);
		} else {
			throw new InvalidOperationException("Save data was not set");
		}
	}

	private void InitializeMapView() {
		EngineStorage.ReadGameData((GameData gameData) => {
			GameMap map = gameData.map;

			Vector2? cameraLocation = null;
			if (mapView != null) {
				cameraLocation = mapView.cameraLocation;
				RemoveChild(mapView);
			}

			mapView = new MapView(this, map.numTilesWide, map.numTilesTall, map.wrapHorizontally, map.wrapVertically);
			AddChild(mapView);

			mapView.cameraZoom = (float)1.0;
			mapView.gridLayer.visible = PreferencesSettings.GetShowGrid();

			if (!cameraLocation.HasValue) {
				// Set initial camera location. If the UI controller has any cities, focus on their capital. Otherwise, focus on their
				// starting settler.
				if (controller.cities.Count > 0) {
					City capital = controller.cities.Find(c => c.IsCapital());
					if (capital != null)
						mapView.centerCameraOnTile(capital.location);
				} else {
					MapUnit startingSettler =
						controller.units.Find(u => u.unitType.actions.Contains(UnitAction.BuildCity));
					if (startingSettler != null)
						mapView.centerCameraOnTile(startingSettler.location);
				}
			} else {
				mapView.cameraLocation = cameraLocation.Value;
			}

			// Allow the city screen to control whether tile assignments
			// are visible and map UI locations back to map locations.
			cityScreen.tileAssignmentLayer = mapView.tileAssignmentLayer;
			cityScreen.mapView = mapView;
			cityScreen.citizenTypes = gameData.citizenTypes;

			// Allow the domestic advisor to trigger popups.
			advisor.domesticAdvisor.SetPopupOverlay(popupOverlay);
			advisor.scienceAdvisor.SetPopupOverlay(popupOverlay);
		});
	}

	public void HandleEngineMessage(MessageToUI msg) {
		GameData gameData = EngineStorage.gameData;

		switch (msg) {
			case MsgNewGame mNGP:
				EmitDawnOfCivilizationSignal(mNGP);
				break;
			case MsgStartTurn mST:
				OnPlayerStartTurn();
				break;
			case MsgGameMainMenu mMM:
				EmitGameMainMenuSignal();
				break;
			case MsgDiplomacyPopUp mDP:
				EmitDiplomacyPopUpSignal();
				break;
			case MsgScienceGuidance mSG:
				EmitScienceGuidanceSignal();
				break;
			case MsgShowCityScreen mSCS:
				ShowCityScreenForCity(gameData, mSCS.city);
				break;
			case MsgCityCreated mCC:
				ShowCityScreenForCity(gameData, mCC.city);
				break;
			case MsgCityDestroyed mCD:
				if (mCD.city.owner.id == controller.id)
					this.ensureLocationIsInView(mCD.city.location);
				this.mapView.cityLayer.UpdateAfterCityDestruction(mCD.city);
				break;
			case MsgCityRazed mCR:
				this.mapView.cityLayer.UpdateAfterCityDestruction(mCR.city);
				if (mCR.formerOwnerId == controller.id || mCR.razerId == controller.id) {
					this.ensureLocationIsInView(mCR.city.location);
					this.EmitCityRazedSignal(mCR);
				}
				break;
			case MsgCityFlipped mCF:
				this.mapView.cityLayer.UpdateAfterCityCaptured(mCF.city);
				if (mCF.toId == controller.id || mCF.fromId == controller.id) {
					this.ensureLocationIsInView(mCF.city.location);
					this.EmitCityFlippedSignal(mCF);
				}
				break;
			case MsgCityRebuffed mCR:
				this.mapView.cityLayer.UpdateAfterCityCaptured(mCR.city);
				break;
			case MsgCityCaptured mCC:
				// Redraw the city in its new owner's colors.
				this.mapView.cityLayer.UpdateAfterCityCaptured(mCC.city);
				if (mCC.conquerorId == controller.id || mCC.formerOwnerId == controller.id) {
					this.ensureLocationIsInView(mCC.city.location);
					this.EmitCityCapturedSignal(mCC);
				}
				break;
			case MsgCityRansacked mCRS:
				if (mCRS.city.owner.id == controller.id)
					this.ensureLocationIsInView(mCRS.city.location);
				this.EmitCityRansackedSignal(mCRS);
				break;
			case MsgCivilizationDestroyed mCivD:
				this.EmitCivilizationDestroyedSignal(mCivD);
				InterestingEvent();
				break;
			case MsgPlayerDefeated mPD:
				this.EmitDefeatSignal(mPD.playerId);
				InterestingEvent();
				break;
			case MsgBarbarianCampDispersed mBCD:
				if (mBCD.controllerId == controller.id)
					this.EmitBarbarianCampDispersedSignal(mBCD);
				break;
			case MsgBuildingSold mBS:
				if (mBS.playerId == controller.id)
					this.EmitBuildingSoldSignal(mBS);
				break;
			case MsgShowScienceAdvisor mSSA:
				EmitSignal(SignalName.ShowSpecificAdvisor, C7Action.ShowScienceAdvisor);
				break;
			case MsgPromptResearch:
				PromptForResearchIfNeeded();
				break;
			case MsgUpdateUiAfterDomesticChange mUUASC:
				// Ensure the citizen moods are correct before displaying them.
				foreach (City c in controller.cities) {
					c.RecalculateCitizenMoods(gameData);
				}
				EmitSignal(SignalName.ShowSpecificAdvisor, C7Action.ShowDomesticAdvisor);
				break;
			case MsgShowTradeOffer mSTO:
				diplomacy.ShowDealScreenForPlayer(
					mSTO.humanPlayer.id, mSTO.aiPlayer.id,
					humanGives: mSTO.aiWant,
					humanWants: mSTO.aiGive);
				break;
			case MsgDisplaySellBuildingPopup mDSBP:
				EmitSellBuildingSignal(mDSBP);
				break;
			case MsgDisplayHurryProductionPopup mDHPP:
				EmitHurryProductionSignal(mDHPP);
				break;
			case MsgDisplayStopWorkerActionPopup mDSWA:
				EmitConfirmStopWorkerActionSignal(mDSWA);
				break;
			// this should come before MsgWarDeclarationConfirmation
			// as this message class inherits from it
			// and otherwise MsgWarDeclarationConfirmation
			// will catch both before reaching this
			case MsgDiplomacyWarDeclarationConfirmation mDWDC:
				EmitDiplomaticWarDeclarationConfirmationSignal(mDWDC);
				InterestingEvent();
				break;
			case MsgWarDeclarationNotification mWDN:
				EmitWarDeclarationNotificationSignal(mWDN);
				InterestingEvent();
				break;
			case MsgWarDeclarationConfirmation mWD:
				EmitWarDeclarationConfirmationSignal(mWD);
				InterestingEvent();
				break;
			case MsgRefuseContact mRC:
				EmitRefuseContactSignal(mRC);
				break;
			case MsgCityRiotWarning mRW:
				if (mRW.city.owner.id == controller.id)
					this.ensureLocationIsInView(mRW.city.location);
				EmitCityRiotWarningSignal(mRW);
				break;
			case MsgSelectGovernment mSG:
				EmitSelectGovernmentSignal(mSG);
				break;
			case MsgChangeMobilization mCM:
				if (mCM.controllerId == controller.id)
					EmitChangeMobilizationSignal();
				break;
			case MsgSelectTransport mSelT:
				EmitSelectTransportSignal(mSelT);
				break;
			case MsgConfirmStartRevolution mSR:
				EmitStartARevolutionSignal(mSR);
				break;
			case MsgDescendIntoAnarchy mDIA:
				// Also sent when war weariness overthrows a government.
				if (mDIA.controllerId == controller.id)
					EmitDescendIntoAnarchySignal(mDIA);
				break;
			case MsgAlreadyInRevolution mAIR:
				EmitAlreadyInRevolutionSignal(mAIR);
				break;
			case MsgShowTemporaryPopup mSTP:
				Vector2 pos = mapView.screenLocationOfTile(mSTP.location, true);
				TemporaryPopup.Show(this, mSTP.message, pos);
				break;
			case MsgGoldenAge mGA:
				if (mGA.playerId == controller.id)
					EmitGoldenAgeSignal(mGA);
				break;
			case MsgAgeOfScience mAoS:
				if (mAoS.playerId == controller.id)
					EmitAgeOfScienceSignal(mAoS);
				break;
			case MsgTechsFromWonder mTW:
				if (mTW.playerId == controller.id)
					EmitTechsFromWonderSignal(mTW);
				break;
			case MsgCelebration mCel:
				if (mCel.playerId == controller.id) {
					TemporaryPopup.Show(this, mCel.Text(), mapView.screenLocationOfTile(mCel.city.location, true));
				}
				break;
			case MsgGoodyHut mGH:
				if (mGH.playerId == controller.id)
					EmitGoodyHutSignal(mGH);
				break;
			case MsgMeltdown mMd:
				if (mMd.playerId == controller.id)
					EmitBadNewsSignal(mMd.Text());
				break;
			case MsgEmbassyEstablished mEE:
				if (mEE.playerId == controller.id)
					EmitForeignAdvisorSignal(mEE.Text(), Mood.Happy);
				break;
			case MsgEspionageResult mER:
				if (mER.playerId != controller.id)
					break;
				// An investigation opens the city's screen, as in Civ3.
				if (mER.result.city != null) {
					ShowCityScreenForCity(gameData, mER.result.city);
				} else {
					EmitForeignAdvisorSignal(mER.result.text, mER.result.outcome == Espionage.Outcome.Succeeded ? Mood.Happy : Mood.Sad);
				}
				break;
			case MsgSpyCaught mSC:
				if (mSC.victimId == controller.id)
					EmitForeignAdvisorSignal(mSC.Text(), Mood.Happy);
				break;
			case MsgBorderWarning mBW:
				if (mBW.intruder == controller)
					EmitBorderWarningSignal(mBW);
				break;
			case MsgTributeDemanded mTD:
				if (mTD.victim == controller)
					EmitTributeDemandedSignal(mTD);
				break;
			case MsgNuclearAttack mNA:
				// News for everyone who knows the attacker.
				if (mNA.attacker == controller || controller.playerRelationships.ContainsKey(mNA.attacker.id))
					EmitMilitaryAdvisorSignal(mNA.outcome == Nuclear.Outcome.ShotDown
						? $"A {mNA.attacker.civilization.adjective} nuclear missile was shot down by a missile defense!"
						: $"The {mNA.attacker.civilization.noun} have launched a nuclear attack! {mNA.unitsKilled} units and {mNA.citizensKilled} citizens were lost.");
				break;
			case MsgAirMission mAM:
				if (mAM.playerId == controller.id && mAM.target != null && mAM.outcome is Air.Outcome.Intercepted or Air.Outcome.ShotDown)
					TemporaryPopup.Show(this, mAM.outcome == Air.Outcome.ShotDown
						? $"Our {mAM.plane} was shot down!"
						: $"Our {mAM.plane} was intercepted!", mapView.screenLocationOfTile(mAM.target, true));
				break;
			case MsgChooseStealthTarget mCST:
				if (mCST.playerId == controller.id)
					EmitChooseStealthTargetSignal(mCST);
				break;
			case MsgShipSank mSS:
				// Civ3's SHIPSUNK.
				if (mSS.playerId == controller.id)
					TemporaryPopup.Show(this, $"Our {mSS.ship} was lost in treacherous waters!", mapView.screenLocationOfTile(mSS.location, true));
				break;
			case MsgPrincessDelivered mPD:
				if (mPD.captor == controller)
					EmitForeignAdvisorSignal($"We have brought the {mPD.princess.adjective} princess to {mPD.capital.name}!", Mood.Happy);
				else if (mPD.princess == controller.civilization)
					EmitForeignAdvisorSignal($"The {mPD.captor.civilization.noun} have taken our princess to {mPD.capital.name}!", Mood.Sad);
				break;
			case MsgSpaceshipPart mSP:
				// News of other civs' spaceships, if we know them.
				if (mSP.owner != controller && controller.playerRelationships.ContainsKey(mSP.owner.id))
					EmitScienceAdvisorSignal($"The {mSP.owner.civilization.noun} have added {mSP.part.name} to their ship.", Mood.Surprised);
				break;
			case MsgSpaceshipComplete mSC:
				if (mSC.playerId == controller.id)
					EmitSpaceshipCompleteSignal();
				break;
			case MsgSpaceshipLost mSL:
				if (mSL.loser == controller)
					EmitScienceAdvisorSignal($"The {mSL.captor.civilization.noun} have destroyed our spaceship!", Mood.Angry);
				else if (mSL.captor == controller)
					EmitScienceAdvisorSignal($"We have destroyed the {mSL.loser.civilization.adjective} spaceship!", Mood.Happy);
				break;
			case MsgElectionOffer mEO:
				if (mEO.playerId == controller.id)
					EmitElectionOfferSignal();
				break;
			case MsgVoteRequest mVR:
				if (mVR.voterId == controller.id)
					EmitVoteRequestSignal(mVR);
				break;
			case MsgElectionResults mER:
				EmitElectionResultsSignal(mER);
				break;
			case MsgPeaceTreatySigned mPTS:
				// News of other civs' peace, if we know them both.
				if (mPTS.a != controller && mPTS.b != controller
					&& controller.playerRelationships.ContainsKey(mPTS.a.id) && controller.playerRelationships.ContainsKey(mPTS.b.id))
					EmitForeignAdvisorSignal(mPTS.Text(), Mood.Happy);
				break;
			case MsgDisease mDis:
				if (mDis.playerId == controller.id) {
					if (mDis.city != null) {
						EmitBadNewsSignal(mDis.Text());
					} else {
						TemporaryPopup.Show(this, mDis.Text(), mapView.screenLocationOfTile(mDis.tile, true));
					}
				}
				break;
			case MsgVolcano mVol:
				if (mVol.playerId == controller.id)
					TemporaryPopup.Show(this, mVol.Text(), mapView.screenLocationOfTile(mVol.tile, true));
				break;
			case MsgGlobalWarming mGW:
				foreach (Tile shown in TerrainTextureFiles.TilesShowing(mGW.tile)) {
					TextureLoader.Forget(shown);
				}
				if (mGW.knownBy.Contains(controller.id))
					TemporaryPopup.Show(this, mGW.Text(), mapView.screenLocationOfTile(mGW.tile, true));
				break;
			case MsgPlague mPlg:
				if (mPlg.playerId == controller.id)
					EmitBadNewsSignal(mPlg.Text());
				break;
			case MsgPollution mPol:
				if (mPol.playerId == controller.id) {
					TemporaryPopup.Show(this, mPol.Text(), mapView.screenLocationOfTile(mPol.city.location, true));
				}
				break;
			case MsgResistanceQuelled mRQ:
				if (mRQ.playerId == controller.id) {
					string resistanceText = mRQ.resistanceContinues
						? $"Our troops have quelled {mRQ.stopped} {(mRQ.stopped == 1 ? "resister" : "resisters")} in {mRQ.city.name}!"
						: $"The resistance in {mRQ.city.name} has ended!";
					TemporaryPopup.Show(this, resistanceText, mapView.screenLocationOfTile(mRQ.city.location, true));
				}
				break;
			case MsgTilePillaged mTP:
				if (mTP.TextFor(controller.id) is string pillagedText) {
					TemporaryPopup.Show(this, pillagedText, mapView.screenLocationOfTile(mTP.location, true));
				}
				break;
			case MsgUnitsCaptured mUC:
				if (mUC.TextFor(controller.id) is string capturedText) {
					TemporaryPopup.Show(this, capturedText, mapView.screenLocationOfTile(mUC.location, true));
				}
				break;
			case MsgLeaderBorn mLB:
				if (mLB.playerId == controller.id) {
					this.ensureLocationIsInView(mLB.location);
					EmitLeaderBornSignal(mLB);
					InterestingEvent();
				}
				break;
			case MsgUnitEnslaved mUE:
				if (mUE.TextFor(controller.id) is string enslavedText) {
					TemporaryPopup.Show(this, enslavedText, mapView.screenLocationOfTile(mUE.location, true));
				}
				break;
			case MsgUnitMoved mUUAAB:
				EmitSignal(SignalName.UnitMoved, new ParameterWrapper<MapUnit>(mUUAAB.Unit));
				break;
			case MsgTransportUnloaded mTU:
				// UnitMoved is enough to refresh UI
				EmitSignal(SignalName.UnitMoved, new ParameterWrapper<MapUnit>(mTU.Unit));
				break;
			case MsgDisplayAbandonCityPopup mDACP:
				if (mDACP.city.owner.id == controller.id)
					this.ensureLocationIsInView(mDACP.city.location);
				EmitConfirmAbandonCitySignal(mDACP);
				break;
			case MsgNameCity mNC:
				EmitNameCitySignal(mNC);
				break;
			case MsgDisbandUnitConfirmation mDUC:
				EmitDisbandUnitConfirmationSignal(mDUC);
				break;
			case MsgReplaceTerrainImprovementConfirmation mRTIC:
				EmitTerrainImprovementReplacementConfirmationSignal(mRTIC);
				break;
			case MsgVictory mV:
				EmitVictorySignal(mV);
				InterestingEvent();
				break;
		}
	}

	private void InterestingEvent() {
		// Break out of fast forward mode after interesting events.
		turnsLeftToFastForward = 0;
	}

	public override void _Process(double delta) {
		ProcessActions();
		UpdateMovePress();
		EdgeScroll(delta);
		UpdateMouseMode();

		if (!EngineStorage.HasPendingAnimations())
			EngineStorage.ProcessNextMessageToEngine();

		if (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI msg))
			HandleEngineMessage(msg);
	}

	// If "location" is not already near the center of the screen, moves the camera to bring it into view.
	public void ensureLocationIsInView(Tile location) {
		if (EngineStorage.gameData.observerMode)
			return;
		if (controller.tileKnowledge.isActiveTile(location) && location != Tile.NONE) {
			Vector2 relativeScreenLocation = mapView.screenLocationOfTile(location, true) / mapView.getVisibleAreaSize();
			if (relativeScreenLocation.DistanceTo(new Vector2((float)0.5, (float)0.5)) > 0.30)
				mapView.centerCameraOnTile(location);
		}
	}

	private void _onEndTurnButtonPressed() {
		if (CurrentState == GameState.PlayerTurn) {
			OnPlayerEndTurn();
		} else {
			log.Information("It's not your turn!");
		}
	}

	// Switching away from the science advisor commits the research chosen in it, which may
	// need the player's OK first. If so, navigate runs only once they give it.
	private void LeaveScienceAdvisorThen(System.Action navigate) {
		ScienceAdvisor science = advisor.scienceAdvisor;
		if (science.Visible) {
			if (popupOverlay.ShowingPopup || science.AskToConfirmSwitch(navigate)) {
				return;
			}
			science.CommitPendingResearch();
		}
		navigate();
	}

	// Runs any action a popup deferred until an advisor screen was closed.
	private void OnAdvisorVisibilityChanged() {
		if (!advisor.Visible) {
			popupOverlay.OnAdvisorClosed();
		}
	}

	// Prompts for a tech to research, or auto-picks one if prompts are disabled.
	private void PromptForResearchIfNeeded() {
		if (controller.cities.Count == 0 || popupOverlay.ShowingPopup) {
			return;
		}
		if (controller.GetAvailableTechsToResearch(EngineStorage.gameData.techs).Count == 0) {
			return;
		}

		if (controller.freeTechsRemaining > 0) {
			popupOverlay.ShowPopup(new ScienceSelection(controller, claimingFreeTech: true), PopupOverlay.PopupCategory.Info);
			return;
		}

		if (controller.currentlyResearchedTech != null) {
			return;
		}

		bool promptForResearch = PreferencesSettings.GetPromptForResearch();

		if (promptForResearch) {
			popupOverlay.ShowPopup(new ScienceSelection(controller), PopupOverlay.PopupCategory.Info);
		} else {
			PlayerAI.MaybePickTechToResearch(controller, EngineStorage.gameData.techs);
		}
	}

	private void OnPlayerStartTurn() {
		EngineStorage.ReadGameData((GameData gameData) => {
			// A defeated player has nothing to play: the defeat popup (from
			// MsgPlayerDefeated) offers the main menu or watching on, and input
			// stays enabled for it.
			if (IsHumanDefeated(gameData)) {
				CurrentState = GameState.PlayerTurn;
				return;
			}

			log.Information("Starting player turn");

			// TODO: Before we call this method to automatically end obsolete deals, we could make this more versatile.
			// For example unless we have a good reason, as a human, receiving luxuries, gpt,
			// or having an active RoP, doesn't hurt us.
			PlayerRelationship.CheckForObsoleteDeals(controller, gameData.players, gameData.turn);

			// If the player can now pick a new government, force them to do so.
			// When the popup is closed we call OnPlayerStartTurn again. This isn't
			// ideal, but we don't yet have a general purpose "show a popup and
			// wait for the player to acknowledge it" system.
			if (controller.government.transitionType && TurnHandling.GetTurnNumber() >= controller.inAnarchyUntilTurn) {
				new MsgSelectGovernment(this.controller.id).send();
			}

			// If the player can pick a new tech to research, prompt them to do so
			// once they have a city.
			PromptForResearchIfNeeded();

			// Autosave at start of player turn
			try {
				Autosave.PerformAutosave();
			} catch {
			}

			// Allow fast forwarding in observer mode.
			if (gameData.observerMode && turnsLeftToFastForward > 0) {
				--turnsLeftToFastForward;
				new MsgEndTurn().send();
				return;
			}

			CurrentState = GameState.PlayerTurn;
		});

		EmitSignal(SignalName.PlayerTurnStart);
	}

	private void OnPlayerEndTurn() {
		if (CurrentState != GameState.PlayerTurn) {
			return;
		}

		// Prompt the user if they would have a city riot when the turn ended.
		bool doEndTurn = true;
		EngineStorage.ReadGameData((GameData gameData) => {
			foreach (City city in controller.cities) {
				if (!controller.isHuman) {
					continue;
				}

				City.Mood cityMood = city.RecalculateCitizenMoods(gameData);
				if (cityMood == City.Mood.Unhappy) {
					new MsgCityRiotWarning(this.controller.id, city).send();
					doEndTurn = false;
					return;
				}
			}
		});
		if (doEndTurn) {
			DoActualEndTurn();
		}
	}

	private void DoActualEndTurn() {
		log.Information("Ending player turn");
		EmitSignal(SignalName.TurnEnded);
		log.Information("Starting computer turn");
		CurrentState = GameState.ComputerTurn;
		new MsgEndTurn().send(); // Triggers actual backend processing
		EmitSignal(SignalName.PlayerTurnEnd);
	}

	public void OnRetire() {
		// Quit to main menu, freeing previous scene data
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}

	public void OnSaveGame() {
		string saveDir = C7Engine.C7Settings.GetSaveFolder();
		try {
			if (!System.IO.Directory.Exists(saveDir)) {
				System.IO.Directory.CreateDirectory(saveDir);
			}
		} catch {
		}
		FileDialog.SetDirectoryForSavingAbsolute(saveDir);

		string fileName = null;
		EngineStorage.ReadGameData((GameData gameData) => {
			fileName = C7Engine.SaveNameGenerator.GenerateSaveFileName(gameData, controller);
		});
		if (!string.IsNullOrEmpty(fileName)) {
			FileDialog.SetSaveFileName(fileName);
		}

		// TODO: sound -- see MainMenu.PlayButtonPressedSound();
		FileDialog.Popup();
	}

	public void OnLoadGame() {
		try {
			FileDialog.SetupForLoadWithLatestSave();
		} catch {
			try {
				string saveDir = C7Engine.C7Settings.GetSaveFolder();
				if (!string.IsNullOrEmpty(saveDir)) {
					FileDialog.SetDirectoryForSavingAbsolute(saveDir);
				}
			} catch {
			}
		}

		// TODO: sound -- see MainMenu.PlayButtonPressedSound();
		FileDialog.Popup();
	}

	public void OnOpenPreferences() {
		popupOverlay.OnHidePopup();
		if (preferences.Visible) {
			preferences.HidePreferences();
		} else {
			preferences.ShowPreferences();
		}
	}

	public void OnResolved() {
		popupOverlay.OnHidePopup();
	}

	public void _on_Zoom_value_changed(float value) {
		mapView.setCameraZoomFromMiddle(value);
	}

	public override void _Input(InputEvent @event) {
		if ((HasVisibleModal() && !IsModalSwitchEvent(@event)) || CurrentState == GameState.ComputerTurn) {
			return;
		}

		if (@event is InputEventKey e && e.Pressed && !e.IsAction(C7Action.UnitGoto)) {
			this.SetGotoMode(false);
		}
	}

	public override void _UnhandledInput(InputEvent @event) {
		// Don't handle if there's an open modal, or if it's the AI's turn
		if ((HasVisibleModal() && !IsModalSwitchEvent(@event)) || CurrentState == GameState.ComputerTurn) {
			IsMovingCamera = false;
			return;
		}

		// If a text input has focus, don't send keys/mouse actions to the game
		if (@event is InputEventKey || @event is InputEventMouseButton) {
			Control focusOwner = GetViewport().GuiGetFocusOwner();
			if (focusOwner is LineEdit || focusOwner is TextEdit) {
				return;
			}
		}

		// Control node must not be in the way and/or have mouse pass enabled
		if (@event is InputEventMouseButton eventMouseButton) {
			HandleMouseButtonInput(eventMouseButton);
		} else if (@event is InputEventMouseMotion eventMouseMotion) {
			HandleMouseMotionInput(eventMouseMotion);
		} else if (@event is InputEventKey eventKeyDown && eventKeyDown.Pressed) {
			HandleKeyboardInput(eventKeyDown);
		} else if (@event is InputEventMagnifyGesture magnifyGesture) {
			HandleMagnifyGesture(magnifyGesture);
		}
	}

	private void HandleMouseButtonInput(InputEventMouseButton eventMouseButton) {
		if (CurrentState == GameState.ComputerTurn) return;
		if (eventMouseButton.ButtonIndex == MouseButton.Left) {
			HandleLeftMouseButton(eventMouseButton);
		} else if (eventMouseButton.ButtonIndex == MouseButton.Right && !eventMouseButton.IsPressed()) {
			HandleRightMouseButton(eventMouseButton);
		} else if (eventMouseButton.ButtonIndex == MouseButton.WheelUp) {
			AdjustZoom(0.1f);
		} else if (eventMouseButton.ButtonIndex == MouseButton.WheelDown) {
			AdjustZoom(-0.1f);
		}
	}

	private void AdjustZoom(float delta) {
		if (interactablePopUpController.Visible) return;

		float newScale = mapView.cameraZoom + delta;
		mapView.setCameraZoom(newScale, GetViewport().GetMousePosition());
		GetViewport().SetInputAsHandled();
	}

	private void HandleLeftMouseButton(InputEventMouseButton eventMouseButton) {
		GetViewport().SetInputAsHandled();
		Control uiHover = GetViewport().GuiGetHoveredControl();
		if (!eventMouseButton.IsPressed() && movePressTile != null) {
			EndMovePress(eventMouseButton);
			return;
		}
		// Can't drag the map when the mouse is over a ui element
		if (eventMouseButton.IsPressed() && uiHover is not TextureButton) {
			// With a unit selected a left press may pick where it goes; with
			// none, a left drag pans the map (user, 2026-10-29).
			if (CanClickToMove()) {
				movePressTile = PositionToTile(eventMouseButton.Position);
				movePressStartMs = Time.GetTicksMsec();
				return;
			}
			OldPosition = eventMouseButton.Position;
			IsMovingCamera = true;

			if (CanDoubleClick(eventMouseButton)) {
				doubleClickHandler.Accept(eventMouseButton);
			} else {
				OnSingleLeftMouseButtonClick(eventMouseButton);
			}
		} else {
			IsMovingCamera = false;
		}
	}

	private bool CanClickToMove() {
		return CurrentState == GameState.PlayerTurn && gotoInfo == null && airTargetInfo == null && bombardInfo == null
			&& IsMapUnitValid(CurrentlySelectedUnit) && CurrentlySelectedUnit.owner == controller
			&& CurrentlySelectedUnit.GetAvailableActions().Contains(UnitAction.Goto);
	}

	private void EndMovePress(InputEventMouseButton release) {
		// The route showed: the button was held, so the unit goes.
		GotoInfo move = gotoInfo;
		SetGotoMode(false);
		if (move != null) {
			ResolveMovement(move);
			return;
		}
		// A quick click selects our units there; on our own city it waits
		// for a second click, which opens the city.
		if (PositionToTile(release.Position)?.cityAtTile?.owner == controller) {
			doubleClickHandler.Accept(release);
		} else {
			HandleUnitSelectionTileClick(release);
		}
	}

	// After the button has been held a moment, the move's route shows.
	private void UpdateMovePress() {
		if (movePressTile != null && gotoInfo == null && Time.GetTicksMsec() - movePressStartMs >= HoldToMoveMs) {
			gotoInfo = GetGotoInfo(GetViewport().GetMousePosition());
		}
	}

	// Full screen, the mouse stays inside the game, so the map scrolls at
	// every edge, also beside a second screen. While a move is held (not a
	// quick click) the mouse hides, leaving only the go-to cursor (Civ3).
	private void UpdateMouseMode() {
		bool confined = GetWindow().HasFocus()
			&& DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
		bool hidden = movePressTile != null && Time.GetTicksMsec() - movePressStartMs >= HoldToMoveMs;
		Input.MouseModeEnum mode = confined
			? (hidden ? Input.MouseModeEnum.ConfinedHidden : Input.MouseModeEnum.Confined)
			: (hidden ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible);
		if (Input.MouseMode != mode) {
			Input.MouseMode = mode;
		}
	}

	public override void _ExitTree() {
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	// Scrolls the map while the mouse rests at an edge of the window.
	private void EdgeScroll(double delta) {
		if (IsMovingCamera || HasVisibleModal() || interactablePopUpController.Visible || !GetWindow().HasFocus()) {
			return;
		}
		Rect2I window = new(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
		if (!window.HasPoint(DisplayServer.MouseGetPosition())) {
			return;
		}
		Vector2 mouse = GetViewport().GetMousePosition();
		Vector2 direction = EdgeScrollDirection(mouse, GetViewport().GetVisibleRect().Size);
		if (direction == Vector2.Zero) {
			return;
		}
		mapView.cameraLocation += direction * EdgeScrollSpeed * (float)delta;
		// A move being picked follows the tile now under the mouse.
		if (movePressTile != null && gotoInfo != null) {
			gotoInfo = GetGotoInfo(mouse);
		}
	}

	private static Vector2 EdgeScrollDirection(Vector2 mouse, Vector2 size) {
		return new(
			mouse.X <= EdgeScrollMargin ? -1 : mouse.X >= size.X - 1 - EdgeScrollMargin ? 1 : 0,
			mouse.Y <= EdgeScrollMargin ? -1 : mouse.Y >= size.Y - 1 - EdgeScrollMargin ? 1 : 0);
	}

	private Tile PositionToTile(Vector2 position) {
		Tile tile = null;
		EngineStorage.ReadGameData((GameData gameData) => {
			tile = mapView.tileOnScreenAt(gameData.map, position);
		});
		return tile;
	}

	private bool CanDoubleClick(InputEventMouseButton eventMouseButton) {
		Tile tile = PositionToTile(eventMouseButton.Position);

		return gotoInfo == null && airTargetInfo == null && tile?.cityAtTile?.owner == controller;
	}

	private void OnSingleLeftMouseButtonClick(InputEventMouseButton eventMouseButton) {
		if (gotoInfo != null) {
			this.ResolveMovement(gotoInfo);
			this.SetGotoMode(false);
		} else if (airTargetInfo != null) {
			SendAirOrder(airTargetInfo, PositionToTile(eventMouseButton.Position));
			SetAirTarget(null);
		} else if (bombardInfo != null) {
			Tile tile = PositionToTile(eventMouseButton.Position);
			if (bombardInfo.bombardingUnit.CanBombardTile(tile, out var bombardTarget)) {
				bombardInfo.bombardTarget = bombardTarget;
				HandleBombardClick(bombardInfo, tile);
			}
			setBombard(null);
		} else {
			// Select unit on tile at mouse location
			HandleUnitSelectionTileClick(eventMouseButton);
		}
	}

	private void OnDoubleLeftMouseButtonClick(InputEventMouseButton eventMouseButton) {
		Tile tile = PositionToTile(eventMouseButton.Position);
		if (tile?.cityAtTile?.owner == controller) {
			EngineStorage.ReadGameData((GameData gameData) => {
				ShowCityScreenForCity(gameData, tile.cityAtTile);
			});
		}
	}

	private void HandleUnitSelectionTileClick(InputEventMouseButton eventMouseButton) {
		Tile tile = PositionToTile(eventMouseButton.Position);
		if (tile == null) {
			return;
		}

		// TODO: This should really be the top unit. One of ours that can
		// still move, so a click on a stack selects someone to order.
		MapUnit unit = tile.unitsOnTile.FirstOrDefault(u => u.owner == controller && u.movementPoints.canMove) ?? tile.unitsOnTile.FirstOrDefault();
		if (unit == null || unit.owner != controller) {
			return;
		}

		SelectUnit(unit, eventMouseButton.Position);
	}

	private void SelectUnit(MapUnit unit, Vector2 screenPosition) {
		bool canMove = unitSelector.SetSelectedUnit(unit);

		if (unit.WorkerJob != null) {
			return;
		}

		Tile tile = PositionToTile(screenPosition);

		if (!canMove) {
			new MsgShowTemporaryPopup("This unit has already moved.", tile).send();
		}
	}

	public void SelectUnit(MapUnit unit) {
		var screenPos = mapView.screenLocationOfTile(unit.location);
		SelectUnit(unit, screenPos);
	}

	private void HandleRightMouseButton(InputEventMouseButton eventMouseButton) {
		this.SetGotoMode(false);

		Tile tile = PositionToTile(eventMouseButton.Position);
		if (tile != null) {
			HandleRightClickOnTile(tile, eventMouseButton);
		} else {
			log.Debug("Didn't click on any tile");
		}
	}

	private void HandleRightClickOnTile(Tile tile, InputEventMouseButton eventMouseButton) {
		bool shiftDown = Input.IsKeyPressed(Godot.Key.Shift);

		var activeTile = controller.tileKnowledge.isActiveTile(tile);

		if (bombardInfo != null)
			setBombard(null);
		else if (airTargetInfo != null)
			SetAirTarget(null);
		// Handle the shortcut of shift+right clicking a city to get the change production menu.
		else if (shiftDown && activeTile && tile.cityAtTile?.owner == controller)
			new RightClickChooseProductionMenu(this, tile.cityAtTile).Open(eventMouseButton.Position);
		else if (!shiftDown && activeTile && tile.unitsOnTile.Count > 0)
			// There are units on this title, so open that menu.
			new RightClickTileMenu(this, tile).Open(eventMouseButton.Position);
		else if (!shiftDown && activeTile && tile.cityAtTile != null)
			// There are no units, but there's a city (ours, or another civ's
			// capital to build an embassy in).
			new RightClickCityMenu(this, tile).Open(eventMouseButton.Position);
		else
			ShowTileInfo(tile);

		LogTileDetails(tile);
	}

	public void ShowTileInfo(Tile tile) {
		tileInfo = new TileInfo(tile);
		var zoom = mapView.cameraZoom;
		var tileCenter = mapView.screenLocationOfTile(tile, true);
		var tileInfoPopup = new TileInfoPopup(this, tile, tileCenter, zoom);
		popupOverlay.ShowPopup(tileInfoPopup, PopupOverlay.PopupCategory.TileInfo);
	}

	public void HideTileInfo() {
		tileInfo = null;
		popupOverlay.OnHidePopup();
	}

	private void LogTileDetails(Tile tile) {
		string yield = tile.YieldString(controller);
		log.Debug($"({tile.XCoordinate}, {tile.YCoordinate}): {tile.overlayTerrainType.DisplayName} {yield}");

		if (tile.cityAtTile != null) {
			LogCityDetails(tile.cityAtTile);
		}

		if (tile.unitsOnTile.Count > 0) {
			LogUnitDetails(tile.unitsOnTile);
		}
	}

	private void LogCityDetails(City city) {
		log.Debug($"  {city.name}, production {city.shieldsStored} of {city.owner.ShieldCost(city.itemBeingProduced)}");
		foreach (CityResident resident in city.residents) {
			log.Debug($"  Resident working at {resident.tileWorked}");
		}
	}

	private void LogUnitDetails(List<MapUnit> unitsOnTile) {
		foreach (MapUnit unit in unitsOnTile) {
			log.Debug("  Unit on tile: " + unit);
			if (unit.currentAI != null) {
				log.Debug("  Strategy: " + unit.currentAI.SummarizePlan());
			}
		}
	}

	private void HandleMouseMotionInput(InputEventMouseMotion eventMouseMotion) {
		if (IsMovingCamera) {
			GetViewport().SetInputAsHandled();
			mapView.cameraLocation += OldPosition - eventMouseMotion.Position;
			OldPosition = eventMouseMotion.Position;
		} else if (movePressTile != null && !Input.IsMouseButtonPressed(MouseButton.Left)) {
			// Released somewhere we didn't hear about (over the interface).
			movePressTile = null;
			SetGotoMode(false);
		} else if (gotoInfo != null) {
			gotoInfo = GetGotoInfo(eventMouseMotion.Position);
		} else if (bombardInfo != null) {
			bombardInfo.mouseTile = PositionToTile(eventMouseMotion.Position);
		} else if (airTargetInfo != null) {
			airTargetInfo.mouseTile = PositionToTile(eventMouseMotion.Position);
		}
	}

	private void HandleKeyboardInput(InputEventKey eventKeyDown) {
		if (interactablePopUpController.Visible)
			return;

		if (eventKeyDown.Keycode == Godot.Key.O && eventKeyDown.ShiftPressed && eventKeyDown.IsCommandOrControlPressed() && eventKeyDown.AltPressed) {
			ToggleObserverMode();
		}

		if (eventKeyDown.Keycode == Godot.Key.P && eventKeyDown.IsCommandOrControlPressed()) {
			if (preferences.Visible) {
				preferences.HidePreferences();
			} else {
				preferences.ShowPreferences();
			}
			return;
		}


		if (eventKeyDown.Keycode == Godot.Key.C && HasCurrentlySelectedUnit()) {
			mapView.centerCameraOnTile(CurrentlySelectedUnit.location);
		}
		if (eventKeyDown.Keycode == Godot.Key.H) {
			City capital = controller.cities.Find(c => c.IsCapital());
			if (capital != null) {
				mapView.centerCameraOnTile(capital.location);
			}
		}
		// For inputs that have the same keys mapped to multiple actions like G
		// we need to manually handle what is triggered by adding extra conditions.
		// Otherwise when pressing CTRL + G for example, both the go-to
		// and the toggle grid actions are triggered, because godot does not distinguish
		// single key presses from combos, it sends both signals.
		// Sometimes even worse, when pressing CTRL + G, it only sends the go-to signal.
		// We continue to map these to an action and not call them directly,
		// because we could add a button in the ui that does the same and this would call the action too.
		if (eventKeyDown.Keycode == Godot.Key.G) {
			// Toggle Coordinates
			if (eventKeyDown.IsCommandOrControlPressed()
				&& eventKeyDown.ShiftPressed
				&& eventKeyDown.AltPressed) {
				ProcessAction(C7Action.ToggleCoordinates);
			}
			// Toggle Grid
			else if (eventKeyDown.IsCommandOrControlPressed()) {
				ProcessAction(C7Action.ToggleGrid);
			}
			// Trigger Unit go-to
			else if (!eventKeyDown.IsCommandOrControlPressed()
					   && !eventKeyDown.ShiftPressed
					   && !eventKeyDown.AltPressed) {
				ProcessAction(C7Action.UnitGoto);
			}
		}
	}

	private void ToggleObserverMode() {
		EngineStorage.ReadGameData((GameData gameData) => {
			gameData.observerMode = !gameData.observerMode;
			if (gameData.observerMode) {
				SetObserverModeOn(gameData);
			} else {
				SetObserverModeOff(gameData);
			}
		});
	}

	private void SetObserverModeOn(GameData gameData) {
		foreach (Player player in gameData.players) {
			player.isHuman = false;
		}
		animationController.SetAnimationsEnabled(false);
		popupOverlay.ShowPopup(
			new TextDialog("How many turns to fast forward through?",
							"Turns: ", "100",
							BoxContainer.AlignmentMode.Begin,
							(string turns) => { turnsLeftToFastForward = int.Parse(turns); }),
				PopupOverlay.PopupCategory.Advisor);
	}

	private void SetObserverModeOff(GameData gameData) {
		foreach (Player player in gameData.players) {
			if (player.id == EngineStorage.uiControllerID) {
				player.isHuman = true;
			}
		}
	}

	private void ToggleGridCoordinates() {
		EngineStorage.ReadGameData((GameData gameData) => {
			gameData.showGridCoordinates = !gameData.showGridCoordinates;
		});
	}

	private void HandleMagnifyGesture(InputEventMagnifyGesture magnifyGesture) {
		double newScale = mapView.cameraZoom * magnifyGesture.Factor;

		mapView.setCameraZoom((float)newScale, magnifyGesture.Position);
	}

	private void ProcessActions() {
		Godot.Collections.Array<StringName> actions = InputMap.GetActions();

		List<string> pressed = [];
		foreach (StringName action in actions) {
			if (Input.IsActionJustPressed(action, exactMatch: true)) {
				pressed.Add(action.ToString());
			} else if (Input.IsActionJustReleased(action)) {
				ProcessOnReleaseAction(action.ToString());
			}
		}
		foreach (string action in ResolveSharedKeys(pressed)) {
			ProcessAction(action);
		}
	}

	// Civ3 gives some orders one key; where more than one of them could
	// apply to the selected unit, keeps the one Civ3 picks. (Orders a unit
	// can't do at all check that themselves.)
	private List<string> ResolveSharedKeys(List<string> pressed) {
		if (!IsMapUnitValid(CurrentlySelectedUnit)) {
			return pressed;
		}
		MapUnit unit = CurrentlySelectedUnit;
		bool CanTerraform(string action) => C7Action.ToTerraform(action) is Terraform tf && unit.CanPerformTerraformAction(tf);

		// Shift-C cleans up pollution before clearing a forest or wetland.
		if (pressed.Contains(C7Action.UnitClearDamage) && CanTerraform(C7Action.UnitClearDamage)) {
			pressed.Remove(C7Action.UnitClearForest);
			pressed.Remove(C7Action.UnitClearWetlands);
		}
		// Ctrl-F builds a fortress, or a barricade where one stands.
		if (pressed.Contains(C7Action.UnitBuildFortress) && pressed.Contains(C7Action.UnitBuildBarricade)) {
			bool fortress = C7Action.ToTerraform(C7Action.UnitBuildFortress)?.Improvement is TerrainImprovement fort
				&& unit.location.overlays.HasImprovement(fort);
			pressed.Remove(fortress ? C7Action.UnitBuildFortress : C7Action.UnitBuildBarricade);
		}
		// R flies recon, or re-bases a plane that has no recon mission.
		int recon = pressed.IndexOf(C7Action.UnitRecon);
		if (recon >= 0 && !SelectedUnitCan(C7Action.UnitRecon)) {
			pressed[recon] = C7Action.UnitRebase;
		}
		return pressed;
	}

	private void ProcessOnReleaseAction(string currentAction) {
		if (currentAction == C7Action.EnableTempAnimations) {
			animationController.SetAnimationsEnabled(true);
		}
	}

	private bool HasVisibleModal() {
		if (popupOverlay.Visible || cityScreen.Visible || diplomacy.Visible)
			return true;

		if (advisor.Visible || gameViews.Visible || preferences.Visible)
			return true;

		if (FileDialog != null && FileDialog.Visible)
			return true;

		if (interactablePopUpController != null && interactablePopUpController.Visible)
			return true;

		return false;
	}

	private bool IsModalSwitchEvent(InputEvent @event) {
		if (@event is InputEventKey eventKeyDown && eventKeyDown.Pressed) {
			return eventKeyDown.Keycode is >= Key.F1 and <= Key.F12;
		}

		return false;
	}

	private void ProcessAction(string currentAction) {
		if (currentAction == C7Action.Escape && interactablePopUpController.Visible) {
			interactablePopUpController.Visible = false;
			return;
		}

		if (interactablePopUpController.Visible) {
			return;
		}

		if (currentAction == C7Action.Escape && tileInfo != null) {
			HideTileInfo();
			return;
		}

		if (currentAction == C7Action.Escape && popupOverlay.ShowingPopup) {
			popupOverlay.OnHidePopup();
			return;
		}

		if (currentAction == C7Action.Escape && cityScreen.Visible) {
			cityScreen.Hide();
			return;
		}

		if (currentAction == C7Action.Escape && advisor.Visible) {
			advisor.OnHide();
			return;
		}

		if (currentAction == C7Action.OpenPreferences) {
			if (preferences.Visible) {
				preferences.HidePreferences();
			} else {
				preferences.ShowPreferences();
			}
			return;
		}

		if (currentAction == C7Action.ChangeMobilization) {
			EmitChangeMobilizationSignal();
			return;
		}

		if (currentAction == C7Action.Escape && preferences.Visible) {
			preferences.HidePreferences();
			return;
		}

		if (currentAction == C7Action.Escape && gameViews.Visible) {
			gameViews.Hide();
			return;
		}

		if (currentAction == C7Action.Escape && diplomacy.Visible) {
			diplomacy.Hide();
			return;
		}

		if (currentAction == C7Action.Escape && bombardInfo != null) {
			setBombard(null);
			return;
		}

		if (currentAction == C7Action.Escape && airTargetInfo != null) {
			SetAirTarget(null);
			return;
		}

		// The advisors (F1-F6) and the game views (F7-F11), also while one of
		// them or the city screen is open (not over a popup, nor during the
		// computer's turn). They're read from the input map, which a focused
		// control can't swallow. Leaving the science advisor settles its
		// pending research pick first (asking if that loses beakers).
		if (!popupOverlay.ShowingPopup && CurrentState != GameState.ComputerTurn) {
			string action = currentAction;
			switch (action) {
				case C7Action.ShowScienceAdvisor:
					EmitSignal(SignalName.ShowSpecificAdvisor, action);
					return;
				case C7Action.ShowDomesticAdvisor or C7Action.ShowTradeAdvisor or C7Action.ShowMilitaryAdvisor
					or C7Action.ShowForeignAdvisor or C7Action.ShowCulturalAdvisor:
					LeaveScienceAdvisorThen(() => EmitSignal(SignalName.ShowSpecificAdvisor, action));
					return;
				case C7Action.ShowWondersView or C7Action.ShowVictoryStatusView or C7Action.ShowPalaceView
					or C7Action.ShowSpaceRaceView or C7Action.ShowDemographicsView:
					LeaveScienceAdvisorThen(() => EmitSignal(SignalName.ShowGameView, action));
					return;
			}
		}

		// never poll for actions if UI elements are visible
		if (HasVisibleModal()) {
			return;
		}

		if (currentAction == C7Action.OpenGameMenu) {
			EmitGameMainMenuSignal();
			return;
		}

		if (currentAction == C7Action.SaveGame) {
			OnSaveGame();
			return;
		}

		if (currentAction == C7Action.LoadGame) {
			OnLoadGame();
			return;
		}

		if (currentAction == C7Action.Retire) {
			EmitRetireConfirmationSignal();
			return;
		}

		if (currentAction == C7Action.EndTurn && !this.HasCurrentlySelectedUnit()) {
			log.Verbose("end_turn key pressed");
			this.OnPlayerEndTurn();
		}

		if (this.HasCurrentlySelectedUnit()) {
			TileDirection? dir = C7Action.ToTileDirection(currentAction);

			if (dir.HasValue) {
				this.gotoInfo = GetGotoInfo(CurrentlySelectedUnit.location.GetTileAtNeighborIndex((int)(dir) + 1));
				this.ResolveMovement(this.gotoInfo);
				this.SetGotoMode(false);
			}
		}

		if (currentAction == C7Action.ToggleGrid) {
			bool newValue = !this.mapView.gridLayer.visible;
			this.mapView.gridLayer.visible = newValue;
			PreferencesSettings.SetShowGrid(newValue);
		}

		if (currentAction == C7Action.ToggleCoordinates) {
			ToggleGridCoordinates();
		}

		if (currentAction == C7Action.Escape && this.gotoInfo == null) {
			log.Debug("Got request for escape/quit");
			EmitQuitGameSignal();
		}

		if (currentAction == C7Action.ToggleZoom) {
			if (mapView.cameraZoom != 1) {
				mapView.setCameraZoomFromMiddle(1.0f);
			} else {
				mapView.setCameraZoomFromMiddle(0.5f);
			}
		}

		if (currentAction == C7Action.ToggleAnimations) {
			animationController.ToggleAnimationsEnabled();
		}

		if (currentAction == C7Action.EnableTempAnimations) {
			animationController.SetAnimationsEnabled(false);
		}

		// actions with unit buttons, which are only relevant during the player
		// turn.
		if (CurrentState != GameState.PlayerTurn) {
			return;
		}

		if (!IsMapUnitValid(CurrentlySelectedUnit)) return;

		if (currentAction == C7Action.UnitHold) {
			new ActionToEngineMsg(() => CurrentlySelectedUnit?.SkipTurn()).send();
		}

		if (currentAction == C7Action.UnitWait) {
			UnitInteractions.waitUnit(CurrentlySelectedUnit.id);
			unitSelector.SetNextUnit();
		}

		if (currentAction == C7Action.UnitFortify) {
			new MsgSetFortification(CurrentlySelectedUnit.id, true).send();
		}

		if (currentAction == C7Action.UnitDisband) {
			new MsgDisbandUnitConfirmation(CurrentlySelectedUnit).send();
		}

		// unit_goto's behavior is more complicated than other actions - it
		// toggles the go to state, but must be detoggled in _*Input methods if
		// it is not the input being pressed.
		if (currentAction == C7Action.UnitGoto) {
			this.SetGotoMode(true);
		}

		if (currentAction == C7Action.UnitExplore) {
			new ActionToEngineMsg(() => CurrentlySelectedUnit.Explore()).send();
		}

		if (currentAction == C7Action.UnitAutomate && SelectedUnitCan(currentAction)) {
			new ActionToEngineMsg(() => CurrentlySelectedUnit.Automate()).send();
		}

		if (currentAction == C7Action.UnitSentry) {
			// unimplemented
		}

		if (currentAction == C7Action.UnitSentryEnemyOnly) {
			// unimplemented
		}

		if (currentAction == C7Action.UnitBuildCity && CurrentlySelectedUnit.canBuildCity()) {
			EngineStorage.ReadGameData((GameData gameData) => {
				MapUnit currentUnit = gameData.GetUnit(CurrentlySelectedUnit.id);
				log.Debug(currentUnit.Describe());
				new MsgNameCity(controller.id, controller.GetNextCityName()).send();
			});
		}

		// Air orders share their keys with other orders (B bombs or builds a
		// city, A airdrops or automates), so each acts only for a unit that has it.
		if (currentAction == C7Action.UnitAirSuperiority && SelectedUnitCan(currentAction)) {
			new MsgFlyMission(CurrentlySelectedUnit.id, Air.Mission.AirSuperiority, null).send();
		}
		if ((currentAction is C7Action.UnitBombing or C7Action.UnitRecon or C7Action.UnitPrecisionStrike or C7Action.UnitRebase
			or C7Action.UnitAirdrop or C7Action.UnitAirlift) && SelectedUnitCan(currentAction)) {
			SetAirTarget(new AirTargetInfo(CurrentlySelectedUnit, currentAction));
		}

		if (currentAction == C7Action.UnitBombard) {
			if (CurrentlySelectedUnit.HasBombardAbility()) {
				EngineStorage.ReadGameData((GameData gameData) => {
					MapUnit currentUnit = gameData.GetUnit(CurrentlySelectedUnit.id);
					setBombard(currentUnit);
				});
			}
		}


		if (currentAction == C7Action.UnitPillage) {
			new MsgPillage(CurrentlySelectedUnit.id).send();
		}

		if (currentAction == C7Action.UnitJoinCity && SelectedUnitCan(currentAction)) {
			new MsgJoinCity(CurrentlySelectedUnit.id).send();
		}

		if (currentAction == C7Action.UnitHurryProduction && SelectedUnitCan(currentAction)) {
			new MsgUnitHurryProduction(CurrentlySelectedUnit.id).send();
		}

		if (currentAction == C7Action.UnitStartAgeOfScience && SelectedUnitCan(currentAction)) {
			new MsgStartAgeOfScience(CurrentlySelectedUnit.id).send();
		}

		// B also builds cities and bombards; only a leader can build an army.
		if (currentAction == C7Action.UnitBuildArmy && CurrentlySelectedUnit.CanBuildArmy()) {
			new MsgBuildArmy(CurrentlySelectedUnit.id).send();
		}

		if (currentAction == C7Action.UnitUpgrade) {
			EmitUpgradeSignal(CurrentlySelectedUnit);
		}

		if (currentAction == C7Action.UnitLoad) {
			EngineStorage.ReadGameData((GameData gameData) => {
				new MsgSelectTransport(CurrentlySelectedUnit, CurrentlySelectedUnit.GetTransportsToBoard(CurrentlySelectedUnit.location)).send();
			});
		}
		if (currentAction == C7Action.UnitUnload) {
			new MsgUnloadTransport(CurrentlySelectedUnit.id).send();
		}

		Terraform terraform = C7Action.ToTerraform(currentAction);

		// the `r` key is mapped to the 'Build Road' action
		// if there is a road we want to map this to 'Build Railroad' action
		if (CurrentlySelectedUnit.location.HasRoad()
			&& !CurrentlySelectedUnit.location.HasRailroad()
			&& currentAction == C7Action.UnitBuildRoad) {
			terraform = C7Action.ToTerraform(C7Action.UnitBuildRailroad);
		}

		if (terraform == null || !CurrentlySelectedUnit.CanPerformTerraformAction(terraform))
			return;

		TerrainImprovement replacementTarget = CurrentlySelectedUnit.location.overlays.GetReplacementTarget(terraform);
		if (replacementTarget != null) {
			new MsgReplaceTerrainImprovementConfirmation(replacementTarget, terraform).send();
			return;
		}
		new MsgStartWorkerJob(CurrentlySelectedUnit.id, terraform).send();
	}

	// Leaving go-to mode also drops a move being held (a key or a right
	// click cancels it).
	private void SetGotoMode(bool isOn) {
		if (isOn) {
			this.gotoInfo = new();
		} else {
			this.gotoInfo = null;
			this.lastTile = null;
			this.movePressTile = null;
		}
	}

	private void setBombard(MapUnit bombardingUnit) {
		bombardInfo = bombardingUnit == null ? null : new BombardInfo(bombardingUnit);
		if (bombardingUnit == null)
			Input.SetCustomMouseCursor(null);
	}

	private void ResolveMovement(GotoInfo info) {
		if (info == null) {
			return;
		}

		log.Debug($"Resolve movement intent: {info.intent}");

		if (info.intent == Intent.NoticeUnit) {
			new MsgShowTemporaryPopup($"Non-combat units may not attack.", info.destinationTile).send();
			return;
		}
		if (info.intent == Intent.NoticeCity) {
			new MsgShowTemporaryPopup($"Only combat units can capture cities and improvements.", info.destinationTile).send();
			return;
		}
		if (info.intent == Intent.NoticeAlliance) {
			new MsgShowTemporaryPopup($"No aggression against alliance.", info.destinationTile).send();
			return;
		}
		if (info.intent == Intent.NoticeAttacked) {
			new MsgShowTemporaryPopup($"This unit has already attacked this turn.", info.destinationTile).send();
			return;
		}

		if (info.moveCost == -1) {
			return;
		}

		EngineStorage.ReadGameData((GameData gameData) => {
			// If this move would require declaring war, display a popup that checks
			// if the player really wants to declare war. If they do, declare the
			// war for them, clear out the player, and call this method again.
			if (info.requiresWarDeclarationOnPlayer != null) {
				GotoInfo stashed = info;
				this.MaybeDeclareWar(stashed.requiresWarDeclarationOnPlayer, () => {
					stashed.requiresWarDeclarationOnPlayer = null;
					this.ResolveMovement(stashed);
					this.SetGotoMode(false);
				});
			} else {
				new MsgSetUnitPath(CurrentlySelectedUnit.id, info.path).send();
			}
		});
	}

	private void MaybeDeclareWar(Player player, Action callback) {
		new MsgWarDeclarationConfirmation(controller.id, player.id, callback).send();
	}

	private Tile lastTile = null;
	private GotoInfo GetGotoInfo(Vector2 mousePos) {
		GotoInfo result = new();

		// We're in "goto" mode and moved the mouse over a tile.
		//
		// Figure out which tile it was.
		EngineStorage.ReadGameData((GameData gameData) => {
			Tile tile = mapView.tileOnScreenAt(gameData.map, mousePos);
			result = GetGotoInfo(tile);
		});

		return result;
	}

	private GotoInfo GetGotoInfo(Tile tile) {
		GotoInfo result = new();

		EngineStorage.ReadGameData((GameData gameData) => {
			if (tile == this.lastTile && this.gotoInfo != null) {
				result = this.gotoInfo;
				return;
			}
			this.lastTile = result.destinationTile = tile;

			// Figure out what unit is in goto mode. If the tile we're hovering over is
			// different than the tile the unit is on, calculate the path to move there.
			MapUnit unit = tile == null ? null : gameData.GetUnit(CurrentlySelectedUnit.id);

			// Units like the Bomber don't have a go-to action
			if (unit != null && !unit.GetAvailableActions().Contains(UnitAction.Goto)) {
				result = null;
			} else if (unit != null && unit.location != tile) {
				result.path = PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, tile, unit);
				result.moveCost =
					result.path.PathCost(unit.owner, unit.location, unit.MovementPerTurn(), unit.movementPoints.remaining);
				result.pathCoords = result.path.GetPathCoords();

				// If we couldn't path onto the tile, but the tile is next to us and
				// we could enter the tile if combat is allowed (or if we could
				// declare war with the move) mark the path.
				var distanceToTile = unit.location.DistanceTo(tile);
				var canEnterForcefully = unit.CanEnterForcefully(tile, out Intent intent);
				result.intent = intent;

				if (distanceToTile == 1 && canEnterForcefully) {
					Queue<Tile> pathQueue = new();
					pathQueue.Enqueue(tile);

					result.path = new TilePath(tile, pathQueue);
					result.moveCost = result.path.PathCost(unit.owner, unit.location, unit.MovementPerTurn(),
						unit.movementPoints.remaining);
					result.pathCoords = result.path.GetPathCoords();

					// If we couldn't enter this tile without a war declaration,
					// record which civ we need to declare war on.
					if (intent == Intent.WarDeclaration) {
						result.attackingMove = true;
						if (tile.cityAtTile != null) {
							result.requiresWarDeclarationOnPlayer = tile.cityAtTile.owner;
						} else {
							result.requiresWarDeclarationOnPlayer = tile.unitsOnTile[0].owner;
						}
					}
				}
			} else {
				// Hide the goto cursor, we don't have a valid move.
				result.moveCost = -1;
			}
		});

		return result;
	}

	// Waits for the air order's target, showing the targeting cursor and
	// the tiles in reach like bombarding (null stops waiting).
	private void SetAirTarget(AirTargetInfo info) {
		airTargetInfo = info;
		if (info == null) {
			Input.SetCustomMouseCursor(null);
		} else {
			info.mouseTile = PositionToTile(GetViewport().GetMousePosition());
		}
	}

	// Sends the air order picked with its target, if it can be flown there.
	private void SendAirOrder(AirTargetInfo info, Tile tile) {
		EngineStorage.ReadGameData((GameData gameData) => {
			if (!info.CanTarget(gameData, tile)) {
				return;
			}
			if (info.action is C7Action.UnitAirdrop or C7Action.UnitAirlift) {
				new MsgAirMove(info.unit.id, info.action == C7Action.UnitAirlift, tile).send();
			} else {
				new MsgFlyMission(info.unit.id, info.Mission(), tile).send();
			}
		});
	}

	private bool SelectedUnitCan(string action) {
		return C7Action.ToUnitAction(action) is UnitAction unitAction && CurrentlySelectedUnit.GetAvailableActions().Contains(unitAction);
	}

	private void HandleBombardClick(BombardInfo info, Tile tile) {
		if (info == null || tile == null) {
			return;
		}

		Action bombard = () => {new MsgBombard(CurrentlySelectedUnit.id, tile).send();};

		EngineStorage.ReadGameData((GameData gameData) => {
			// A nuke hits everyone there, seen or not: war with each civ at
			// peace with us there first.
			if (Nuclear.IsNuke(info.bombardingUnit.unitType)) {
				if (Nuclear.AtPeace(controller, tile).FirstOrDefault() is Player peaceful) {
					MaybeDeclareWar(peaceful, () => HandleBombardClick(info, tile));
				} else {
					bombard.Invoke();
				}
			} else if (info.RequiresWarDeclaration(tile, out Player player)) {
				MaybeDeclareWar(player, () => {
					bombard.Invoke();
				});
			} else {
				bombard.Invoke();
			}
		});
	}

	/**
	 * User quit.  We *may* want to do some things here like make a back-up save, or call the server and let it know we're bailing (esp. in MP).
	 **/
	private void OnQuitTheGame() {
		log.Information("Goodbye!");
		GetTree().Quit();
	}

	private void OnBuildCity(string name) {
		if (CurrentlySelectedUnit != null)
			new MsgBuildCity(CurrentlySelectedUnit, name).send();
	}

	public void ShowCityScreenForCity(GameData gameData, City city) {
		city.RecalculateCitizenMoods(gameData);
		EmitSignal(SignalName.ShowCityScreen, new ParameterWrapper<City>(city));
	}

	public void OnDiplomacySelected(ParameterWrapper<ID> opponentPlayer) {
		diplomacy.ShowTalkScreenForPlayer(controller.id, opponentPlayer.Value);
	}

	public void ShowInteractivePopUp(ParameterWrapper<InteractablePopUp> interactablePopUp) {
		interactablePopUpController.OnShowInteractablePopUp(interactablePopUp);
	}
}
