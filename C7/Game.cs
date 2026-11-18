using Godot;
using System;
using System.Diagnostics;
using C7Engine;
using C7GameData;
using Serilog;
using C7Engine.Pathing;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using static C7GameData.MapUnit;
using C7Engine.Network;

public class GotoInfo {
	public Tile destinationTile = null;
	public int moveCost = -1;
	public TilePath path = null;
	public HashSet<System.Numerics.Vector2> pathCoords;
	public bool attackingMove = false;
	public Player requiresWarDeclarationOnPlayer = null;
	public Intent intent = Intent.Disabled;
	// Whether every unit on the selected unit's tile goes, not just the
	// selected one (the J key).
	public bool wholeStack = false;
	// Road or rail to (Ctrl-R, Ctrl-Shift-R): the tile picked is the
	// worker's destination, rather than where it goes now.
	public WorkerAutomation.Kind? workerAutomation = null;
	// The unit that moves, once a war declaration has to be confirmed first,
	// so the move doesn't go to whichever unit is selected by then.
	public ID unitID = null;
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
	// The air mission (or airdrop) whose target is being picked, or null for
	// a bombardment.
	public UnitAction? airMission;
	// For automated bombarding, its kind: the target picked is bombarded
	// every turn (see MapUnit_AutoBombard.cs).
	public MapUnit.AutoBombardMode autoMode;
	// For a re-base, whether the other planes on the tile go too (J).
	public bool wholeStack;

	public BombardInfo(MapUnit bombardingUnit, UnitAction? airMission = null, MapUnit.AutoBombardMode autoMode = MapUnit.AutoBombardMode.None) {
		this.bombardingUnit = bombardingUnit;
		this.airMission = airMission;
		this.autoMode = autoMode;
	}

	// Whether the unit can bombard, or fly its mission at, the tile.
	public bool CanTarget(Tile tile, out MapUnit.BombardTarget target) {
		target = MapUnit.BombardTarget.None;
		if (!Tile.IsTileValid(tile)) {
			return false;
		}
		if (autoMode != MapUnit.AutoBombardMode.None) {
			// Only civs we're at war with are bombarded automatically.
			bool can = autoMode == MapUnit.AutoBombardMode.Bombard
				? bombardingUnit.CanBombardTile(tile, out target)
				: bombardingUnit.CanFlyMissionAt(autoMode == MapUnit.AutoBombardMode.Bombing ? UnitAction.Bombing : UnitAction.PrecisionBombing, tile, out target);
			return can && bombardingUnit.CanAutoBombardTile(autoMode, tile);
		}
		return airMission is UnitAction mission
			? bombardingUnit.CanFlyMissionAt(mission, tile, out target)
			: bombardingUnit.CanBombardTile(tile, out target);
	}

	public bool CanTarget(Tile tile) => CanTarget(tile, out _);

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

public partial class Game : Node {
	private ILogger log = LogManager.ForContext<Game>();

	[Signal] public delegate void TurnEndedEventHandler();
	[Signal] public delegate void ShowSpecificAdvisorEventHandler();
	[Signal] public delegate void ShowGameViewEventHandler();
	[Signal] public delegate void ShowCityScreenEventHandler();

	[Signal] public delegate void PlayerTurnStartEventHandler();
	[Signal] public delegate void PlayerTurnEndEventHandler();
	[Signal] public delegate void GameInitializedEventHandler();

	[Signal] public delegate void UnitMovedEventHandler();

	[Export]
	Control Toolbar;
	private bool IsMovingCamera;
	private Vector2 OldPosition;

	[Export]
	private PopupOverlay popupOverlay;
	[Export]
	private CityScreen cityScreen;
	[Export]
	private Advisors advisor;
	private Civilopedia civilopedia;
	[Export]
	private GameViews gameViews;
	[Export]
	private Diplomacy diplomacy;
	[Export]
	private StatusMenu statusMenu;

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

	// In a hotseat game, the curtain hiding the map while the screen is handed
	// to the next human player, and where each player last left the camera.
	private HotseatHandoff hotseatHandoff = null;
	private readonly Dictionary<ID, Vector2> hotseatCameraLocations = new();
	private readonly Dictionary<ID, Queue<MessageToUI>> heldMessages = new();

	// In a LAN game, the banner shown while another machine's player takes
	// their turn, and whether we've told the player the host is gone.
	private CanvasLayer lanWaitingBanner = null;
	private string lanWaitingText = null;
	private bool lanDisconnectShown = false;

	// Whether the banner is for other players taking their turns, rather
	// than for something else we wait on.
	private bool lanWaitingOnOthers = false;

	// While a LAN client has lost the host, the curtain saying it's trying
	// to reconnect. Once it's back, the turns pick up where the host's game
	// is, when that has been shown.
	private CanvasLayer lanReconnectCurtain = null;
	private Label lanReconnectLabel = null;
	private bool lanResyncPending = false;

	// With simultaneous turns: the round we began playing the player at the
	// screen, and this machine's players who have ended their turn this
	// round, which the host's game may not show yet.
	private bool lanGameStarted = false;
	private int simultaneousPlayingRound = -1;
	private int simultaneousRound = -1;
	private readonly HashSet<ID> simultaneousTurnsEnded = new();

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
	// While set, the next left click on the map picks a tile for this (the
	// menus' "Set Rally Point"), and Escape cancels.
	private Action<Tile> tilePicker = null;

	public TileInfo tileInfo = null;

	// When in observer mode, the number of turns to play before prompting the
	// user to advance the turn manually. This allows for more rapid debugging
	// without pressing the spacebar repeatedly.
	public int turnsLeftToFastForward = 0;

	// When in observer mode, how many turns to play each time the user ends
	// the turn, so the game pauses for review every that many turns. 0 to
	// play one turn at a time.
	private int fastForwardInterval = 0;

	bool errorOnLoad = false;

	public override void _EnterTree() {
		EngineStorage.frameBudget = engineWorkBudget;
		loadTimer.Start();
	}

	// Called when the node enters the scene tree for the first time.
	// The catch should always catch any error, as it's the general catch
	// that gives an error if we fail to load for some reason.
	public override async void _Ready() {
		Global = GetNode<GlobalSingleton>("/root/GlobalSingleton");

		// The Civilopedia is drawn over the map and the other screens, but
		// under popups.
		civilopedia = new Civilopedia();
		popupOverlay.GetParent().AddChild(civilopedia);
		popupOverlay.GetParent().MoveChild(civilopedia, popupOverlay.GetIndex());

		FileDialog = GetNode<Civ3FileDialog>("%LoadDialog");
		FileDialog.Canceled += OnResolved;
		FileDialog.FileSelected += path => OnResolved();

		// A city the player zoomed to from its production popup has had its
		// say; on to the next.
		cityScreen.Hidden += ShowNextProductionPopup;

		// Without Always Wait at End of Turn, the turn ends once no unit
		// needs orders (see EndTurnIfNothingToDo).
		unitSelector.NoMoreAutoselectableUnits += () => endTurnWhenIdle = true;
		unitSelector.NewAutoselectedUnit += () => endTurnWhenIdle = false;
		PlayerTurnEnd += () => endTurnWhenIdle = false;

		try {
			await InitializeGame();
			await StartGame();
		} catch (Exception ex) {
			// The game is only partly set up, so nothing may be played; the
			// error offers the way back to the menu.
			errorOnLoad = true;
			CurrentState = GameState.ComputerTurn;
			string message = ex.Message;
			string[] stack = ex.StackTrace.Split("\r\n");   //for some reason it is returned with \r\n in the string as one line.  let's make it readable!
			foreach (string line in stack) {
				message = message + "\r\n" + line;
			}

			popupOverlay.ShowPopup(new ErrorMessage(message), PopupOverlay.PopupCategory.Advisor);
			log.Error(ex, "Unexpected error in Game.cs _Ready");
		}
	}

	private async Task InitializeGame() {
		// The scenario editor opens in the game scene (see Game_Editor.cs).
		if (ScenarioEditorSession.Opening) {
			InitializeEditor();
			return;
		}
		// The mod path is left over from whatever was set up last. A game
		// made from a save object (a new game, or a scenario) takes the
		// save's; a saved game being loaded sets its own while it loads,
		// if it has one.
		Util.setModPath(Global.SaveGame?.ScenarioSearchPath);

		// Ensure we clear out our image caches, as scenarios and games will
		// use the same filenames but have different content for them.
		Util.ClearCaches();

		GameParams options = CreateGameParams();

		await CreateGameAndAssignPlayerController(options);
		// Only a LAN game plays turns at the same time; a LAN host decides
		// afresh whether this one does.
		if (!LanSession.IsActive) {
			EngineStorage.gameData.simultaneousTurns = false;
		}
		StartLanGame();

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

	// ---- the play log's game folder (see C7Engine.PlayLog.PlayLogFolders) ----

	private string loadedSavePath;

	// With "Record play log" on, the engine keeps the game's rows until it's
	// told where the game's log is. A game that was recorded before carries
	// on its log, in its folder, after a warning if this save is older than
	// the log; any other is named first, and gets its folder in the saves
	// folder. Only an engine that plays the game records it, so a LAN guest
	// isn't asked.
	private void StartPlayLog() {
		if (!C7Engine.PlayLog.PlayRecorder.WaitingForLogFile) {
			return;
		}
		GameData gameData = EngineStorage.gameData;
		if (LanSession.IsClient || gameData.observerMode || !gameData.players.Any(p => p.isHuman)) {
			C7Engine.PlayLog.PlayRecorder.Close();
			return;
		}
		if (gameData.playLog != null) {
			C7Engine.PlayLog.PlayLogFolders.Locate(gameData, loadedSavePath);
			if (C7Engine.PlayLog.PlayLogFolders.LogIsAhead(gameData)) {
				int last = C7Engine.PlayLog.PlayLogFolders.LastLoggedTurn(C7Engine.PlayLog.PlayLogFolders.LogPath(gameData.playLog)) ?? 0;
				popupOverlay.ShowPopup(new ChoicePopup("Play Log",
					$"The play log of \"{gameData.playLog.name}\" already goes on to turn {last},\n"
						+ $"and this save is from turn {gameData.turn}. Logging this game\n"
						+ "will break the log's sequence: the turns from here repeat in it.",
					[
						new ChoicePopup.Choice("Continue (keep logging)", RecordPlayLogInFolder),
						new ChoicePopup.Choice("Don't log this session", () => C7Engine.PlayLog.PlayRecorder.Close()),
					], cancellable: false), PopupOverlay.PopupCategory.Advisor);
			} else {
				RecordPlayLogInFolder();
			}
			return;
		}
		// A saved game's file name is the name it has already; a new game's
		// is its civilization and the date.
		string defaultName = loadedSavePath != null && loadedSavePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
			? C7Engine.PlayLog.PlayLogFolders.Sanitize(System.IO.Path.GetFileNameWithoutExtension(loadedSavePath),
				C7Engine.PlayLog.PlayLogFolders.DefaultName(gameData, DateTime.Now))
			: C7Engine.PlayLog.PlayLogFolders.DefaultName(gameData, DateTime.Now);
		popupOverlay.ShowPopup(new TextDialog("Name This Game", "Name: ", defaultName, BoxContainer.AlignmentMode.Begin,
			name => NamePlayLogGame(string.IsNullOrWhiteSpace(name) ? defaultName : name),
			() => NamePlayLogGame(defaultName)), PopupOverlay.PopupCategory.Advisor);
	}

	private void NamePlayLogGame(string name) {
		GameData gameData = EngineStorage.gameData;
		try {
			PlayLogInfo info = C7Engine.PlayLog.PlayLogFolders.Start(gameData, Civ3FileDialog.SavesDirectory, name);
			log.Information("The game is {Name}, in {Folder}", info.name, info.folder);
		} catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException) {
			// The log goes beside C7.ini, as before games had folders.
			log.Warning(e, "Couldn't make a folder for the game {Name}", name);
			gameData.playLog = null;
			C7Engine.PlayLog.PlayRecorder.UseLogFile(System.IO.Path.Combine(DeveloperSettings.PlayLogDirectory,
				C7Engine.PlayLog.PlayLogFolders.Sanitize(name) + ".jsonl"));
			return;
		}
		RecordPlayLogInFolder();
	}

	private void RecordPlayLogInFolder() {
		C7Engine.PlayLog.PlayRecorder.UseLogFile(C7Engine.PlayLog.PlayLogFolders.LogPath(EngineStorage.gameData.playLog),
			loadedSavePath == null ? null : System.IO.Path.GetFileName(loadedSavePath));
	}

	// The game's folder, when it has one and the play log is on, for its
	// saves and autosaves; null otherwise.
	private static string GameFolder(GameData gameData) {
		return DeveloperSettings.RecordPlayLog && !string.IsNullOrEmpty(gameData?.playLog?.folder) ? gameData.playLog.folder : null;
	}

	private void InitializeAudio() {
		audioManager = GetNodeOrNull<AudioManager>("/root/GlobalAudioManager");
		if (audioManager == null) {
			return;
		}
		audioManager.StopMusic();
		// Civ3 starts a new game's music with the Ancient pieces, and a loaded
		// game's without them (FUN_00540670's last argument from the new game
		// and load game code).
		musicWithPieces = TurnHandling.GetTurnNumber() == 0;
		UpdateMusic();
	}

	private AudioManager audioManager;
	private ID musicPlayerId;
	private int musicEra = -1;
	private bool musicWithPieces;

	// Plays the in-game music for the UI controller's era and culture group,
	// changing it when they enter a new era (Civ3 FUN_0056a220) or, in a
	// hotseat game, when the next player's era or culture group differs.
	private void UpdateMusic() {
		if (audioManager == null || controller == null) {
			return;
		}
		int era = Math.Max(controller.EraIndex(), 0);
		if (musicPlayerId == controller.id && era == musicEra) {
			return;
		}
		// Entering a new era asks for the pieces as a new game does.
		bool withPieces = musicWithPieces || (musicPlayerId == controller.id && era > musicEra);
		musicWithPieces = false;
		musicPlayerId = controller.id;
		musicEra = era;
		int cultureGroup = controller.civilization?.cultureGroup?.index ?? 0;
		audioManager.InGameMusic.PlayFor(era, cultureGroup, withPieces);
	}

	private async Task StartGame() {
		if (editing) {
			StartEditor();
			return;
		}
		log.Information("Now in game!");

		// Show the moves and battles the player's preferences ask for.
		animationController.ApplyPreferences();

		bool watching = Global.WatchNextGame && !LanSession.IsActive;
		Global.WatchNextGame = false;
		if (watching) {
			ToggleObserverMode();
		}
		StartDebugMode();

		TurnHandling.OnBeginTurn();

		loadTimer.Stop();
		TimeSpan stopwatchElapsed = loadTimer.Elapsed;
		log.Information("Game scene load time: " + Convert.ToInt32(stopwatchElapsed.TotalMilliseconds) + " ms");

		EmitSignal(SignalName.GameInitialized);

		// With "Record play log" on, the game's name and folder, first of
		// all the popups.
		StartPlayLog();

		if (ShouldShowScoreboard(EngineStorage.gameData)) {
			ShowScoreboard();
		} else if (LanSession.IsSpectator) {
			// Without it, a spectator still chooses how to watch there.
			ShowScoreboard(viewChoiceOnly: true);
		}
		if (EngineStorage.gameData?.rules?.RisingTide == true) {
			ShowTidePanel();
		}
		if (CityStateTrade.IsOn(EngineStorage.gameData)) {
			ShowCityStatesPanel();
		}
		// Staging: says so while others place their units (see StagingBanner).
		if (EngineStorage.gameData?.rules?.StagingUnits == true) {
			GetNode<Control>("CanvasLayer/Control").AddChild(new StagingBanner());
		}
		// A custom scenario is introduced as its first turn starts.
		if (EngineStorage.gameData?.turn == 0 && ScenarioIntroPopup.For(EngineStorage.gameData) is ScenarioIntroPopup intro) {
			popupOverlay.ShowPopup(intro, PopupOverlay.PopupCategory.Advisor);
		}

		if (LanSession.IsSpectator) {
			// Spectators never play; see _UnhandledInput for what they can do.
			CurrentState = GameState.ComputerTurn;
		} else if (SimultaneousLanTurns) {
			ContinueSimultaneousTurns();
		} else if (LanSession.IsActive) {
			// Whoever plays first may be at another machine.
			Player active = EngineStorage.gameData.GetPlayer(EngineStorage.activePlayerID);
			if (active != null && !LanSession.IsLocalPlayer(active)) {
				ShowLanWaiting(active);
			} else if (active != null && LanSession.HasSeveralLocalPlayers) {
				// The others at this machine also need to look away.
				ShowHotseatHandoff(active, BeginLanTurn);
			} else {
				MaybeAutoplayLanTurn();
			}
		} else if (PlayByEmail.IsOn(EngineStorage.gameData)) {
			// Whoever opened the save gives the password of the player whose
			// turn it is before anything of theirs shows.
			BeginTurnByEmail(controller);
		} else if (TurnHandling.IsHotseat(EngineStorage.gameData)) {
			// The first hotseat player also needs the others to look away.
			ShowHotseatHandoff(controller, OnPlayerStartTurn);
		}

		lanGameStarted = true;
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
				log.Debug("RelativeModPath ", scenarioSearchPath);
				return Util.PediaIconsPathFor(scenarioSearchPath);
			},
			GameModeLoader = (config) => {
				Global.ActivateGameMode(config);
				return Global.GameMode.behaviors;
			},
		};
	}

	private async Task CreateGameAndAssignPlayerController(GameParams options) {
		// The AI's fog of war is this machine's setting; only a host's
		// engine (or a single machine's) plays the AI.
		// The play log, likewise: only an engine that plays the game records it.
		DeveloperSettings.Apply();
		AdvancedAiSettings.Apply();

		// The save being loaded, for its play log (see StartPlayLog).
		loadedSavePath = Global.SaveGame == null ? Global.LoadGamePath : null;

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

			if (mapView != null && mapView.CanShow(map)) {
				// A LAN snapshot of the same game: keep the map view, and
				// with it the camera, and point it at the new game data.
				mapView.RebindToGameData(gameData);
			} else {
				Vector2? cameraLocation = null;
				float cameraZoom = 1.0f;
				// The city whose tiles the city screen shows, if it's open.
				City shownCity = null;
				if (mapView != null) {
					cameraLocation = mapView.cameraLocation;
					cameraZoom = mapView.cameraZoom;
					shownCity = mapView.tileAssignmentLayer.city;
					RemoveChild(mapView);
					mapView.QueueFree();
				}

				mapView = new MapView(this, map.numTilesWide, map.numTilesTall, map.wrapHorizontally, map.wrapVertically);
				AddChild(mapView);

				mapView.cameraZoom = cameraZoom;
				mapView.gridLayer.visible = false;

				if (!cameraLocation.HasValue) {
					CenterCameraOnController();
				} else {
					mapView.cameraLocation = cameraLocation.Value;
				}

				// Carry the open city screen's city over to the new map view, as the same city in the new game data if it's still there.
				// Otherwise the old city is kept for the city screen to notice that it's gone when it's refreshed for the new game data
				// (see CityScreen.RefreshAfterGameReplaced, which the LAN snapshot handler calls next).
				if (shownCity != null) {
					City sameCity = gameData.cities.Find(c => c.id == shownCity.id);
					mapView.tileAssignmentLayer.city = sameCity ?? shownCity;
				}
			}

			// Allow the city screen to control whether tile assignments
			// are visible and map UI locations back to map locations.
			cityScreen.tileAssignmentLayer = mapView.tileAssignmentLayer;
			cityScreen.mapView = mapView;
			cityScreen.citizenTypes = gameData.citizenTypes;

			// Allow the domestic advisor to trigger popups.
			advisor.domesticAdvisor.SetPopupOverlay(popupOverlay);
		});
	}

	// If the UI controller has any cities, focus on their capital. Otherwise,
	// focus on their starting settler.
	private void CenterCameraOnController() {
		// Nobody plays in the scenario editor.
		if (controller == null) {
			return;
		}
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
	}

	// Hooks this game up to the LAN game being hosted or joined, if any.
	private void StartLanGame() {
		if (LanSession.Host != null) {
			LanSession.Host.StartGame();
			controller = EngineStorage.gameData.GetUIControllerPlayer();
		} else if (LanSession.Client != null) {
			LanClient client = LanSession.Client;
			EngineStorage.uiFollowsActivePlayer = false;
			if (client.IsSpectator) {
				// As the whole game, or as one or all of the civilizations.
				LanSession.ShowSpectatorView(EngineStorage.gameData);
			} else {
				// Of this machine's players, whoever plays first.
				ID active = EngineStorage.gameData.simultaneousTurns
					? TurnHandling.PlayersToMove(EngineStorage.gameData).FirstOrDefault(p => client.PlayerIDs.Contains(p.id))?.id
					: EngineStorage.activePlayerID;
				EngineStorage.uiControllerID = client.PlayerIDs.Contains(active) ? active : client.PlayerIDs.FirstOrDefault();
			}
			controller = EngineStorage.gameData.GetUIControllerPlayer();
			client.SnapshotReceived = OnLanSnapshot;
			client.UiMessageReceived = json => HandleEngineMessage(NetSerialization.DeserializeMessageToUI(json));
			client.PlayersChanged = OnLanPlayersChanged;
			// A spectator's new view shows straight away, and its snapshots
			// follow.
			client.SpectatorViewChanged = () => {
				LanSession.ShowSpectatorView(EngineStorage.gameData);
				controller = EngineStorage.gameData.GetUIControllerPlayer();
				mapView?.InvalidateMap();
			};
			// Our own moves show before the host's snapshot does.
			client.OrderPredicted = () => mapView?.InvalidateMap();
			// Losing the host from here on, we try to get back to it; and
			// after closing the game, the player can rejoin it.
			client.ReconnectAutomatically = true;
			LanSession.RememberJoinedGame(client);
		}
	}

	// Rejoining with several seats can take them one at a time, so the
	// player we were waiting on may turn out to be one of ours.
	private void OnLanPlayersChanged() {
		if (lanResyncPending) {
			// Back after losing the host: the turns pick up once its game
			// is shown.
			return;
		}
		if (SimultaneousLanTurns) {
			ContinueSimultaneousTurns();
			return;
		}
		Player active = EngineStorage.gameData.GetPlayer(EngineStorage.activePlayerID);
		if (IsWaitingForRemotePlayer && active != null && LanSession.IsLocalPlayer(active)) {
			OnControllerTurnStart(active);
		}
	}

	private void PollLanSession() {
		LanSession.Host?.Poll();
		LanSession.Client?.Poll();

		LanClient client = LanSession.Client;
		if (client != null && !PollLanConnection(client)) {
			return;
		}

		// With simultaneous turns, the host's game says when each of our
		// players' turns is over, even when their time runs out.
		if (lanGameStarted && SimultaneousLanTurns && !lanDisconnectShown) {
			ContinueSimultaneousTurns();
		} else if (lanWaitingOnOthers && !SimultaneousLanTurns) {
			// Whoever we wait on may have lost their connection, or come
			// back.
			Player active = EngineStorage.gameData.GetPlayer(EngineStorage.activePlayerID);
			if (active != null && !LanSession.IsLocalPlayer(active)) {
				ShowLanWaiting(active);
			}
		}
	}

	// Keeps a client's game screen in step with its connection to the host:
	// while it reconnects, or waits for the host to resume the game, nothing
	// can be done. Returns whether the game goes on as usual.
	private bool PollLanConnection(LanClient client) {
		if (client.Reconnecting) {
			// Once back, the turns pick up when the host's game is shown,
			// which may be as soon as the host answers.
			lanResyncPending = true;
			// Through a relay, the relay says why, like the host being away.
			ShowLanReconnecting(client.ReconnectAttempt, client.Endpoint is RelayEndpoint ? client.LastReconnectError : null);
			return false;
		}
		if (lanReconnectCurtain != null) {
			HideLanReconnecting();
		}
		if (!client.IsConnected && (client.RejectedReason != null || !client.ReconnectAutomatically)) {
			if (!lanDisconnectShown) {
				lanDisconnectShown = true;
				HideLanWaiting();
				string why = client.RejectedReason == null ? "We have lost contact with the host." : $"The host turned us away: {client.RejectedReason}";
				popupOverlay.ShowPopup(
					new ConfirmationPopup(
						$"{why}\nThe game cannot continue.\n\n",
						"Return to the main menu.",
						"Let me look around first.",
						OnRetire),
					PopupOverlay.PopupCategory.Advisor);
			}
			return false;
		}
		if (client.HostIsResuming) {
			// The host is hosting our game again, from where it last saved
			// it, and shows it to us once it starts.
			lanResyncPending = true;
			ShowLanBanner("Waiting for the host to resume the game...");
			return false;
		}
		return !lanDisconnectShown;
	}

	// The curtain over the game while we try to get back to the host.
	private void ShowLanReconnecting(int attempt, string why = null) {
		CurrentState = GameState.ComputerTurn;
		string text = attempt <= 1
			? "Lost connection to the host. Reconnecting..."
			: $"Lost connection to the host. Reconnecting... (attempt {attempt})";
		if (why != null) {
			text += $"\n{why}";
		}
		if (lanReconnectCurtain != null) {
			lanReconnectLabel.Text = text;
			return;
		}
		log.Information("Lost the connection to the host, reconnecting");
		HideLanWaiting();

		// Dims the game and takes every click.
		ColorRect dimmer = new() { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Stop };
		dimmer.SetAnchorsPreset(Control.LayoutPreset.FullRect);

		VBoxContainer box = new() { Alignment = BoxContainer.AlignmentMode.Center };
		box.AddThemeConstantOverride("separation", 16);
		box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		dimmer.AddChild(box);

		lanReconnectLabel = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
		lanReconnectLabel.AddThemeFontSizeOverride("font_size", 24);
		lanReconnectLabel.AddThemeColorOverride("font_color", Colors.White);
		box.AddChild(lanReconnectLabel);

		Button menu = new() { Text = "Return to main menu", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
		menu.AddThemeFontSizeOverride("font_size", 18);
		menu.Pressed += () => {
			LanSession.Client?.StopReconnecting();
			OnRetire();
		};
		box.AddChild(menu);

		// Above the hotseat curtain, too.
		lanReconnectCurtain = new CanvasLayer { Layer = 110 };
		lanReconnectCurtain.AddChild(dimmer);
		AddChild(lanReconnectCurtain);
	}

	private void HideLanReconnecting() {
		log.Information("Reconnected to the host");
		lanReconnectCurtain?.QueueFree();
		lanReconnectCurtain = null;
		lanReconnectLabel = null;
	}

	// Back with the host after losing it, or after it resumed the game:
	// each of our players picks up their turn where the host's game is now.
	private void ResumeLanTurns() {
		HideLanWaiting();
		CurrentState = GameState.ComputerTurn;
		if (LanSession.IsSpectator) {
			return;
		}
		GameData gameData = EngineStorage.gameData;
		if (SimultaneousLanTurns) {
			// Begin again with whichever of ours is still to move.
			simultaneousPlayingRound = -1;
			ContinueSimultaneousTurns();
			return;
		}
		// The host told us whose turn it is before showing us the game.
		if (LanSession.TurnClock?.activePlayerID is ID activeID) {
			EngineStorage.activePlayerID = activeID;
		}
		Player active = gameData.GetPlayer(EngineStorage.activePlayerID);
		if (active != null && active.isHuman && !active.hasPlayedThisTurn) {
			OnControllerTurnStart(active);
		}
	}

	// A LAN client shows the host's game: replace ours with the snapshot,
	// and point the UI at the new units, cities and players.
	private void OnLanSnapshot(C7GameData.Save.SaveGame save) {
		Stopwatch applyTime = Stopwatch.StartNew();
		GameData gameData = CreateGame.ReplaceWithSnapshot(save, Global.GameMode.behaviors);
		// Textures, art names and colors looked up by game object would
		// otherwise keep the old game's objects alive; the map looks up
		// terrain textures by value.
		TextureLoader.ForgetGameObjects();
		AnimationManager.ForgetGameObjects();
		C7.Textures.PlayerTextureUtil.ForgetGameObjects();
		// Animations of units that aren't in the snapshot would never end.
		animationController.animTracker.forgetRemovedUnits(gameData);
		// A spectator sees the map as the host sends it.
		LanSession.ShowSpectatorView(gameData);

		controller = gameData.GetUIControllerPlayer();
		InitializeMapView();
		unitSelector.RefreshAfterGameReplaced();
		cityScreen.RefreshAfterGameReplaced();
		if (bombardInfo != null) {
			MapUnit bombarder = gameData.GetUnit(bombardInfo.bombardingUnit.id);
			setBombard(bombarder, bombardInfo.airMission, bombardInfo.autoMode);
		}

		// The tile info box and the goto path refer to tiles, which are
		// new objects too: point them at the same places on the new map.
		if (tileInfo != null) {
			Tile target = gameData.map.tileAt(tileInfo.targetTile.XCoordinate, tileInfo.targetTile.YCoordinate);
			tileInfo = target != Tile.NONE ? new TileInfo(target) : null;
		}
		// So do the messages held for our other players.
		foreach (Queue<MessageToUI> held in heldMessages.Values) {
			foreach (MessageToUI msg in held) {
				RebindToGame(msg, gameData);
			}
		}

		Tile gotoDestination = gotoInfo?.destinationTile;
		lastTile = null;
		if (gotoDestination != null) {
			gotoInfo = GetGotoInfo(gameData.map.tileAt(gotoDestination.XCoordinate, gotoDestination.YCoordinate));
		}

		// An open advisor shows the old game's cities and techs.
		advisor.RefreshAfterGameReplaced();
		civilopedia.ForgetGameObjects();

		if (lanResyncPending && LanSession.Client?.HostIsResuming != true) {
			lanResyncPending = false;
			ResumeLanTurns();
		}

		if (applyTime.ElapsedMilliseconds > 100) {
			log.Information("Showing the host's snapshot took {Milliseconds} ms", applyTime.ElapsedMilliseconds);
		}
	}

	// Points the players, cities, units and tiles a message refers to at
	// the same ones in a LAN snapshot's game. Any that aren't in the snapshot
	// (like a city since destroyed) are left as they were, which is enough
	// for the message's text.
	private static void RebindToGame(object msg, GameData gameData) {
		for (Type type = msg.GetType(); type != null && type != typeof(object); type = type.BaseType) {
			foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
				object rebound = field.GetValue(msg) switch {
					Player p => gameData.GetPlayer(p.id),
					City c => gameData.cities.Find(x => x.id == c.id),
					MapUnit u when u != MapUnit.NONE => gameData.GetUnit(u.id),
					Tile t when t != Tile.NONE => gameData.map.tileAt(t.XCoordinate, t.YCoordinate),
					_ => null,
				};
				if (rebound != null) {
					field.SetValue(msg, rebound);
				}
			}
		}
	}

	// With the --lan-autoplay developer option, ends our turn soon after it
	// starts, for watching turns pass between machines.
	private void MaybeAutoplayLanTurn() {
		if (!LanSession.DevAutoplay) {
			return;
		}
		GetTree().CreateTimer(1.5).Timeout += () => {
			// The game may have been left in the meantime.
			if (!IsInstanceValid(this) || !IsInsideTree()) {
				return;
			}
			popupOverlay.OnHidePopup();
			if (CurrentState == GameState.PlayerTurn) {
				DoActualEndTurn();
			}
		};
	}

	// Games with more than one human show the scoreboard unless it was
	// turned off when the game was set up.
	private static bool ShouldShowScoreboard(GameData gameData) {
		return gameData.rules?.ShowScoreboard != false
			&& (LanSession.IsActive || TurnHandling.IsHotseat(gameData));
	}

	// The players' scores and the turn clock, in the top right corner under
	// the toolbar. It is part of the HUD, so advisors and popups cover it.
	// With viewChoiceOnly, it has only a spectator's choice of how to watch.
	private void ShowScoreboard(bool viewChoiceOnly = false) {
		Scoreboard scoreboard = new(viewChoiceOnly);
		GetNode<Control>("CanvasLayer/Control").AddChild(scoreboard);
		scoreboard.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		scoreboard.GrowHorizontal = Control.GrowDirection.Begin;
		scoreboard.OffsetLeft = scoreboard.OffsetRight = 0;
		scoreboard.OffsetTop = scoreboard.OffsetBottom = 48;
	}

	// When the next Tidal Wave comes, left of the scoreboard (see
	// TidePanel).
	private void ShowTidePanel() {
		TidePanel panel = new();
		GetNode<Control>("CanvasLayer/Control").AddChild(panel);
		panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		panel.GrowHorizontal = Control.GrowDirection.Begin;
		panel.OffsetLeft = panel.OffsetRight = 0;
		panel.OffsetTop = panel.OffsetBottom = 48;
	}

	// Kings & Nomads: our strength against the city-states and our trade
	// with them, left of the scoreboard (see CityStatesPanel).
	private void ShowCityStatesPanel() {
		CityStatesPanel panel = new();
		GetNode<Control>("CanvasLayer/Control").AddChild(panel);
		panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		panel.GrowHorizontal = Control.GrowDirection.Begin;
		panel.OffsetLeft = panel.OffsetRight = 0;
		panel.OffsetTop = panel.OffsetBottom = 48;
	}

	// Shows whose turn it is while another machine's player moves.
	private void ShowLanWaiting(Player active) {
		ShowLanWaiting([active]);
	}

	// Shows who we wait on while other machines' players move. The host can
	// go on without those who have lost their connection.
	private void ShowLanWaiting(List<Player> others) {
		List<Player> absent = others.Where(p => !IsLanPlayerHere(p)).ToList();
		string text;
		if (absent.Count > 0) {
			string names = string.Join(", ", absent.Select(p => $"{p.name ?? p.civilization.leader} of the {p.civilization.noun}"));
			text = $"Waiting for {names} to reconnect...";
		} else if (others.Count == 1) {
			Player active = others[0];
			string playerName = active.name ?? active.civilization.leader;
			text = $"Waiting for {playerName} of the {active.civilization.noun} to play their turn...";
		} else {
			text = $"Waiting for {others.Count} other players to finish their turns...";
		}
		if (lanWaitingText == null || !lanWaitingText.StartsWith(text)) {
			log.Information("{Waiting}", text);
		}
		if (absent.Count > 0 && LanSession.Host != null) {
			ShowLanBanner(text, "Continue without them", LanSession.Host.ContinueWithoutAbsentPlayers);
		} else {
			ShowLanBanner(text);
		}
		lanWaitingOnOthers = true;
	}

	// Whether the player is at their machine, or the host has gone on
	// without them so their turn ends by itself. Before the host says who is
	// connected, everyone is taken to be.
	private static bool IsLanPlayerHere(Player player) {
		TurnClockInfo clock = LanSession.TurnClock;
		if (clock?.connectedPlayers == null || clock.connectedPlayers.Count == 0) {
			return true;
		}
		return clock.connectedPlayers.Contains(player.id) || clock.awayPlayers?.Contains(player.id) == true;
	}

	private void ShowLanBanner(string text, string buttonText = null, Action onPressed = null) {
		CurrentState = GameState.ComputerTurn;
		if (lanWaitingBanner != null && lanWaitingText == text + buttonText) {
			return;
		}
		HideLanWaiting();
		lanWaitingText = text + buttonText;

		Label label = new() {
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		label.AddThemeFontSizeOverride("font_size", 20);
		label.AddThemeColorOverride("font_color", Colors.White);

		HBoxContainer row = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(label);
		if (buttonText != null) {
			Button button = new() { Text = buttonText };
			button.AddThemeFontSizeOverride("font_size", 16);
			button.Pressed += onPressed;
			row.AddChild(button);
		}

		PanelContainer panel = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.65f) });
		panel.AddChild(row);

		lanWaitingBanner = new CanvasLayer { Layer = 90 };
		lanWaitingBanner.AddChild(panel);
		AddChild(lanWaitingBanner);

		// A strip across the top of the screen, below the toolbar, leaving
		// room for the scoreboard on the right.
		panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		panel.OffsetTop = 70;
		panel.OffsetBottom = 110;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		LayOutLanBanner(panel);
		Viewport viewport = GetViewport();
		Action relayout = () => {
			if (IsInstanceValid(panel)) {
				LayOutLanBanner(panel);
			}
		};
		viewport.SizeChanged += relayout;
		panel.TreeExiting += () => viewport.SizeChanged -= relayout;
	}

	// Keeps the banner clear of the toolbar on the left and the scoreboard on
	// the right while there's room, giving up those margins in proportion on
	// a narrow window so the strip never turns inside out.
	private void LayOutLanBanner(Control panel) {
		const float left = 160, right = 440, minWidth = 300;
		float width = GetViewport().GetVisibleRect().Size.X;
		float scale = Math.Clamp((width - minWidth) / (left + right), 0, 1);
		panel.OffsetLeft = left * scale;
		panel.OffsetRight = -right * scale;
	}

	// A spectator hears about the world's events without having to answer
	// a popup for each: they show for a while in a list down the left.
	private VBoxContainer spectatorNews;

	private void ShowSpectatorNews(string text) {
		if (spectatorNews == null) {
			spectatorNews = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
			spectatorNews.AddThemeConstantOverride("separation", 4);
			spectatorNews.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
			spectatorNews.Position = new Vector2(16, 80);
			CanvasLayer layer = new() { Layer = 90 };
			layer.AddChild(spectatorNews);
			AddChild(layer);
		}

		Label label = new() { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeStyleboxOverride("normal", TemporaryPopup.PopupStyleBox());
		label.AddThemeColorOverride("font_color", Colors.White);
		label.AddThemeFontSizeOverride("font_size", 16);
		spectatorNews.AddChild(label);
		while (spectatorNews.GetChildCount() > 8) {
			Node oldest = spectatorNews.GetChild(0);
			spectatorNews.RemoveChild(oldest);
			oldest.QueueFree();
		}
		GetTree().CreateTimer(10).Timeout += () => {
			if (IsInstanceValid(label)) {
				label.QueueFree();
			}
		};
	}

	private void HideLanWaiting() {
		lanWaitingBanner?.QueueFree();
		lanWaitingBanner = null;
		lanWaitingText = null;
		lanWaitingOnOthers = false;
	}

	// Whether this machine plays in a LAN game whose humans play their turns
	// at the same time.
	private static bool SimultaneousLanTurns =>
		LanSession.IsActive && !LanSession.IsSpectator && EngineStorage.gameData?.simultaneousTurns == true;

	// This machine's players yet to finish this round, in turn order.
	private List<Player> LocalPlayersToMove() {
		GameData gameData = EngineStorage.gameData;
		NoteSimultaneousRound(gameData);
		return TurnHandling.PlayersToMove(gameData)
			.Where(p => LanSession.IsLocalPlayer(p) && !simultaneousTurnsEnded.Contains(p.id))
			.ToList();
	}

	// Forgets whose turns ended once a new round has begun.
	private void NoteSimultaneousRound(GameData gameData) {
		if (gameData.turn != simultaneousRound) {
			simultaneousRound = gameData.turn;
			simultaneousTurnsEnded.Clear();
		}
	}

	// With simultaneous turns, everyone plays at once, but a machine's own
	// players still take turns at it. Once the player at the screen is done,
	// this hands it to the next of them still to move, or says who we're
	// waiting on once they all are. It's called whenever that may change.
	private void ContinueSimultaneousTurns() {
		GameData gameData = EngineStorage.gameData;
		if (gameData.gameOver) {
			return;
		}
		List<Player> ours = LocalPlayersToMove();
		bool playing = (CurrentState == GameState.PlayerTurn || hotseatHandoff != null)
			&& simultaneousPlayingRound == gameData.turn && ours.Contains(controller);
		if (playing) {
			return;
		}

		if (ours.Count == 0) {
			// The curtain may be up for a player whose time ran out, and
			// others may be waiting behind it.
			hotseatHandoff?.QueueFree();
			hotseatHandoff = null;
			queuedHandoffs.Clear();
			List<Player> others = TurnHandling.PlayersToMove(gameData).Where(p => !LanSession.IsLocalPlayer(p)).ToList();
			if (others.Count > 0) {
				ShowLanWaiting(others);
			} else {
				// The computer players are moving.
				CurrentState = GameState.ComputerTurn;
				HideLanWaiting();
			}
			return;
		}

		Player next = ours[0];
		simultaneousPlayingRound = gameData.turn;
		HideLanWaiting();
		if (LanSession.HasSeveralLocalPlayers) {
			ShowHotseatHandoff(next, BeginLanTurn);
			return;
		}
		controller = next;
		EngineStorage.uiControllerID = next.id;
		BeginLanTurn();
	}

	public override void _ExitTree() {
		audioManager?.InGameMusic.Stop();
		// Leaving the game leaves the LAN game, too.
		LanSession.End();
		// A save still being written is finished before quitting.
		SaveManager.WaitForPendingSaves();
	}

	// Called when the engine hands the UI to a player at the start of their
	// turn. In a hotseat game this may be a different player than before, so
	// hide the map until the new player is at the screen.
	private void OnControllerTurnStart(Player next) {
		if (LanSession.IsSpectator) {
			// Turns pass without the spectator.
			return;
		}

		if (LanSession.IsActive) {
			// Each machine plays its own players; the others wait for them.
			EngineStorage.activePlayerID = next.id;
			if (!LanSession.IsLocalPlayer(next)) {
				ShowLanWaiting(next);
				return;
			}
			HideLanWaiting();
			if (LanSession.HasSeveralLocalPlayers) {
				// This machine's players take turns at it, as in a hotseat
				// game.
				ShowHotseatHandoff(next, BeginLanTurn);
				return;
			}
			BeginLanTurn();
			return;
		}

		if (PlayByEmail.IsOn(EngineStorage.gameData)) {
			if (controller != null && next.id != controller.id) {
				// The next human plays on their own computer.
				SendTurnByEmail(next);
			} else {
				// The only human left plays on.
				controller = next;
				HearFromAIs();
			}
			return;
		}

		if (!TurnHandling.IsHotseat(EngineStorage.gameData)) {
			controller = next;
			OnPlayerStartTurn();
			return;
		}

		ShowHotseatHandoff(next, OnPlayerStartTurn);
	}

	// ---- Play by email (see C7Engine.PlayByEmail) ----

	// Whether the computer players are having their say with the human at
	// the start of their turn, which begins once they've had it.
	private bool hearingFromAIs = false;

	// The end-of-turn curtain, while it's up.
	private PlayByEmailSave playByEmailSave = null;

	// Starts the turn of the human whose save this is: once they've given
	// their password (or chosen one), the news from while they were away,
	// then what the computer players have to say to them, then their turn.
	private void BeginTurnByEmail(Player player) {
		ShowHotseatHandoff(player, "It is now your turn.", "Begin Turn", () => {
			foreach (MessageToUI news in PlayByEmail.TakeNews(EngineStorage.gameData, controller)) {
				HoldMessage(controller.id, news);
			}
			HearFromAIs();
		}, turnStart: true, offerMainMenu: true);
	}

	private void HearFromAIs() {
		CurrentState = GameState.ComputerTurn;
		hearingFromAIs = true;
		new MsgHearFromAIs().send();
	}

	// The turn is over, and the next human's begins on their computer: the
	// player saves the game for them, under the name they give it, and
	// goes back to the main menu. What they'll be told when they open it
	// (the news held for them, and for the others) goes with it.
	private void SendTurnByEmail(Player next) {
		CloseScreensForHandoff();
		CurrentState = GameState.ComputerTurn;
		GameData gameData = EngineStorage.gameData;
		string folder = Civ3FileDialog.SavesDirectory;

		string Save(string fileName) {
			Dictionary<ID, int> kept = gameData.players.ToDictionary(p => p.id, p => p.newsWhileAway.Count);
			try {
				foreach (Player player in gameData.players.Where(p => p.isHuman)) {
					List<MessageToUI> news = new();
					if (pendingProductionPopups.TryGetValue(player.id, out LinkedList<MsgCityProductionCompleted> production)) {
						news.AddRange(production);
					}
					if (heldMessages.TryGetValue(player.id, out Queue<MessageToUI> held)) {
						news.AddRange(held);
					}
					PlayByEmail.KeepNews(gameData, player.id, news);
				}
				System.IO.Directory.CreateDirectory(folder);
				SaveManager.Save(System.IO.Path.Combine(folder, fileName));
				return null;
			} catch (Exception e) {
				log.Error(e, "Couldn't save the turn to send on");
				return e.Message;
			} finally {
				// The news is in the save; here it's still held.
				foreach (Player player in gameData.players) {
					int count = kept.GetValueOrDefault(player.id);
					if (player.newsWhileAway.Count > count) {
						player.newsWhileAway.RemoveRange(count, player.newsWhileAway.Count - count);
					}
				}
			}
		}

		playByEmailSave = new PlayByEmailSave(PlayByEmail.PlayerName(next), PlayByEmail.SaveFileName(gameData, next), folder, Save, OnRetire);
		CanvasLayer curtainLayer = new() { Layer = 100 };
		curtainLayer.AddChild(playByEmailSave);
		playByEmailSave.TreeExited += curtainLayer.QueueFree;
		AddChild(curtainLayer);
	}

	private void BeginLanTurn() {
		OnPlayerStartTurn();
		MaybeAutoplayLanTurn();
	}

	private void ShowHotseatHandoff(Player next, Action onBeginTurn) {
		ShowHotseatHandoff(next,
			"It is your turn. Make sure the other players aren't looking.",
			"Begin Turn",
			onBeginTurn,
			turnStart: true);
	}

	// Handoffs asked for while the curtain is already up, shown in turn once
	// it's dismissed, so that what each of them goes on to do isn't lost.
	private readonly Queue<(ID next, string message, string buttonText, Action onContinue, bool turnStart)> queuedHandoffs = new();
	// Who the curtain is up for, and what it says.
	private (ID, string) hotseatHandoffShown;

	private void ShowNextQueuedHandoff() {
		while (hotseatHandoff == null && queuedHandoffs.Count > 0) {
			var (nextID, message, buttonText, onContinue, turnStart) = queuedHandoffs.Dequeue();
			// Found by id, as a LAN client's snapshots replace the players.
			Player next = EngineStorage.gameData?.GetPlayer(nextID);
			if (next == null) {
				continue;
			}
			// The player is already at the screen.
			if (next.id == controller?.id) {
				onContinue();
				continue;
			}
			ShowHotseatHandoff(next, message, buttonText, onContinue, turnStart);
		}
	}

	// A handoff at the start of the player's turn lets them choose a
	// password, the first time; one opening a save played by email lets
	// whoever opened it go back to the main menu.
	private void ShowHotseatHandoff(Player next, string message, string buttonText, Action onContinue,
		bool turnStart = false, bool offerMainMenu = false) {
		if (hotseatHandoff != null) {
			// The same news twice, like a turn starting again for whoever the
			// curtain is already up for, is only shown once.
			bool duplicate = hotseatHandoffShown == (next.id, message)
				|| queuedHandoffs.Any(h => h.next == next.id && h.message == message);
			if (!duplicate) {
				queuedHandoffs.Enqueue((next.id, message, buttonText, onContinue, turnStart));
			}
			return;
		}
		hotseatHandoffShown = (next.id, message);

		CurrentState = GameState.ComputerTurn;

		// Close anything the previous player left open, and remember where
		// they were looking so we can restore it on their next turn.
		CloseScreensForHandoff();
		if (controller != null && controller != next) {
			hotseatCameraLocations[controller.id] = mapView.cameraLocation;
		}

		controller = next;
		// A hotseat game's engine has already moved the UI to them; a LAN
		// client's players move it themselves.
		EngineStorage.uiControllerID = next.id;
		if (hotseatCameraLocations.TryGetValue(controller.id, out Vector2 cameraLocation)) {
			mapView.cameraLocation = cameraLocation;
		} else {
			CenterCameraOnController();
		}

		// Games without player names (e.g. older saves) fall back to the leader.
		string playerName = controller.name ?? controller.civilization.leader;
		// Passwords are for players sharing a computer or a save, not a
		// LAN game's.
		bool passwords = !LanSession.IsActive;
		hotseatHandoff = new HotseatHandoff(
			$"{playerName} - {controller.civilization.name}",
			message,
			buttonText,
			() => {
				hotseatHandoff = null;
				onContinue();
				ShowNextQueuedHandoff();
			},
			player: passwords ? controller : null,
			mayChoosePassword: passwords && turnStart,
			onMainMenu: offerMainMenu ? OnRetire : null);
		CanvasLayer curtainLayer = new() { Layer = 100 };
		curtainLayer.AddChild(hotseatHandoff);
		hotseatHandoff.TreeExited += curtainLayer.QueueFree;
		AddChild(curtainLayer);
	}

	// Closes anything the player at the screen left open, as the screen
	// changes hands.
	private void CloseScreensForHandoff() {
		if (tileInfo != null) {
			HideTileInfo();
		}
		SetGotoMode(false);
		setBombard(null);
		cityScreen.Hide();
		advisor.Hide();
		gameViews.Hide();
		diplomacy.Hide();
		civilopedia.Hide();
		// Production news waits for its owner's turn.
		HoldShownProductionPopup();
	}

	public void HandleEngineMessage(MessageToUI msg) {
		// Whatever the engine tells us about may have changed the map.
		mapView?.InvalidateMap();

		// The map shows everyone's cities, so it's updated right away, even
		// for a message that's held for another player.
		UpdateMapForMessage(msg);

		// Hold messages for a human player who isn't at the screen (for
		// example barbarians raiding them during the AI turns) until they are.
		// On a LAN that is only one of this machine's players. A deal another
		// human proposes can't wait, as they are waiting on it, and neither
		// can Staging, which every human answers before anyone moves.
		if (msg.recipient != null && msg.recipient.isHuman && msg.recipient.id != controller.id
			&& (!LanSession.IsActive || LanSession.IsLocalPlayer(msg.recipient))
			&& msg is not MsgShowDealProposal
			&& msg is not MsgShowMarket && msg is not MsgShowMarketResults) {
			HoldMessage(msg.recipient.id, msg);
			return;
		}

		// News of a loss at a place, like a city taken (see LossNews), that
		// comes while others play waits for the player's turn to start, to
		// be shown one popup at a time with the map centred on the place.
		if (msg is MsgShowMilitaryAdvisorPopup { location: not null } && controller != null
			&& (msg.recipient == null || msg.recipient.id == controller.id)
			&& (CurrentState != GameState.PlayerTurn || hotseatHandoff != null)
			&& !LanSession.IsSpectator && !WatchingAiGame) {
			HoldMessage(controller.id, msg);
			return;
		}

		ShowEngineMessage(msg);
	}

	// Keeps the message for the player until their turn is underway (see
	// ReplayHeldMessage).
	private void HoldMessage(ID playerID, MessageToUI msg) {
		if (!heldMessages.TryGetValue(playerID, out Queue<MessageToUI> held)) {
			held = new();
			heldMessages[playerID] = held;
		}
		// Such as a city razed after it was taken: the news of its capture
		// is no longer worth telling.
		if (msg is MsgShowMilitaryAdvisorPopup { supersedesNewsHere: true, location: Tile place }) {
			List<MessageToUI> kept = held.Where(m => !(m is MsgShowMilitaryAdvisorPopup { location: Tile there }
				&& there.XCoordinate == place.XCoordinate && there.YCoordinate == place.YCoordinate)).ToList();
			held.Clear();
			foreach (MessageToUI m in kept) {
				held.Enqueue(m);
			}
		}
		held.Enqueue(msg);
	}

	// What a message changes on the map, done when the message arrives.
	private void UpdateMapForMessage(MessageToUI msg) {
		switch (msg) {
			case MsgCityDestroyed mCD:
				// A LAN client has already redrawn the map without the city.
				if (mCD.city != null) {
					mapView?.cityLayer.UpdateAfterCityDestruction(mCD.city);
				}
				break;
			case MsgCityCaptured mCCap:
				mapView?.cityLayer.UpdateAfterCityCapture(mCCap.city);
				break;
		}
	}

	// Civ3's era splash for a human entering a new era (see EraSplash). Civ3
	// leaves it out of a network game (FUN_0056a220 skips it when
	// FUN_0049f9f0 says the game is networked), so a LAN game does too.
	private void ShowEraSplash(MsgEnteredNewEra msg) {
		if (LanSession.IsActive) {
			return;
		}
		GameData gameData = EngineStorage.gameData;
		Player player = msg.recipient ?? controller;
		if (gameData == null || player == null) {
			return;
		}
		Random random = new();
		string text = NewEra.Announcement(player, gameData.rules, random);
		EraSplash splash = new(msg.previousEraCivilopediaName, msg.eraCivilopediaName, text, () => {
			// "What's the big picture?" sets the player researching one of the
			// new era's techs and opens the Science Advisor.
			GameData current = EngineStorage.gameData;
			Player us = current?.GetPlayer(player.id);
			Tech tech = us == null ? null : NewEra.BigPictureResearch(us, current, random);
			// Nothing comes up over the advisor until it's closed.
			ViewTheBigPicture(chooseAfterwards: false);
			if (tech != null) {
				new MsgChooseResearch(tech, MsgChooseResearch.AdvisorState.Show).send();
			} else {
				EmitSignal(SignalName.ShowSpecificAdvisor, C7Action.ShowScienceAdvisor);
			}
		});
		popupOverlay.ShowPopup(splash, PopupOverlay.PopupCategory.EraChange);
		InterestingEvent();
	}

	// Shows a message to the player at the screen: the popups, screens and
	// other reactions to the message. Held messages are shown when they're
	// replayed.
	private void ShowEngineMessage(MessageToUI msg) {
		GameData gameData = EngineStorage.gameData;

		// A spectator hears the news the way it watches the game (see
		// LanHost.SpectatorHears) without having to answer a popup for each;
		// watching as a civ, its own news reads as its player is told it.
		if (LanSession.IsSpectator && msg.SpectatorHeadline(LanSession.SpectatorWatchedAs) is string headline) {
			ShowSpectatorNews(headline);
			return;
		}

		// Watching the AIs play here, news doesn't stop the game or wait to be
		// read: the wars are listed at each pause (see WarList), and the
		// rest, such as wonders built, isn't announced.
		if (WatchingAiGame && msg.SpectatorHeadline() != null) {
			if (msg is MsgWarDeclaration war) {
				WarListForWatching().RecordDeclaration(war.aggressor, war.opponent);
			}
			return;
		}

		switch (msg) {
			case MsgStartTurn when SimultaneousLanTurns:
				// Every human is told at once; we play ours one by one.
				ContinueSimultaneousTurns();
				break;
			case MsgStartTurn mST:
				// Hotseat follows the UI controller, which the engine moved to
				// the player whose turn it is; a LAN game is told who they are.
				OnControllerTurnStart(LanSession.IsActive ? mST.player : EngineStorage.gameData.GetUIControllerPlayer());
				break;
			case MsgTurnReady mTR:
				// Playing by email, the computer players have had their say.
				if (hearingFromAIs && mTR.recipient?.id == controller?.id) {
					hearingFromAIs = false;
					OnPlayerStartTurn();
				}
				break;
			case MsgShowCityScreen mSCS:
				ShowCityScreenForCity(gameData, mSCS.city);
				break;
			case MsgCityCreated mCC:
				ShowCityScreenForCity(gameData, mCC.city);
				break;
			case MsgCivilizationDestroyed mCivD:
				popupOverlay.ShowPopup(new CivilizationDestroyed(mCivD.civilization), PopupOverlay.PopupCategory.Advisor);
				InterestingEvent();
				break;
			case MsgShowMilitaryAdvisorPopup mSMAP: {
					// News like a golden age or a city lost to disorder waits its
					// turn behind any popup already showing, rather than being lost.
					var mood = mSMAP.happy ? AdvisorHead.Mood.Happy : AdvisorHead.Mood.Angry;
					var pop = new InformationalPopup(mSMAP.message, AdvisorHead.Advisor.Military, mood);
					// News of a place, like a city lost, centres the map on it
					// as it comes up (Civ3's "#map_center").
					if (mSMAP.location is Tile place && place != Tile.NONE) {
						pop.OnShown = () => mapView?.centerCameraOnTile(place);
						InterestingEvent();
					}
					popupOverlay.ShowPopup(pop, PopupOverlay.PopupCategory.Advisor);
					break;
				}
			case MsgShowTradeAdvisorPopup mSTAP: {
					// Like the military advisor's news, this waits its turn.
					var mood = mSTAP.happy ? AdvisorHead.Mood.Happy : AdvisorHead.Mood.Surprised;
					var pop = new InformationalPopup(mSTAP.message, AdvisorHead.Advisor.Trade, mood);
					popupOverlay.ShowPopup(pop, PopupOverlay.PopupCategory.Advisor);
					break;
				}
			case MsgShowForeignAdvisorPopup mSFAP: {
					// Like the military advisor's news, this waits its turn.
					var mood = mSFAP.happy ? AdvisorHead.Mood.Happy : AdvisorHead.Mood.Surprised;
					var pop = new InformationalPopup(mSFAP.message, AdvisorHead.Advisor.Foreign, mood);
					popupOverlay.ShowPopup(pop, PopupOverlay.PopupCategory.Advisor);
					break;
				}
			case MsgShowScienceAdvisorPopup mSSAP: {
					// The space race news (such as the ship being complete) is too
					// important to drop, so it waits its turn behind any popup
					// already showing.
					AdvisorHead.Mood scienceMood = mSSAP.mood switch {
						MsgShowScienceAdvisorPopup.Mood.Happy => AdvisorHead.Mood.Happy,
						MsgShowScienceAdvisorPopup.Mood.Angry => AdvisorHead.Mood.Angry,
						MsgShowScienceAdvisorPopup.Mood.Sad => AdvisorHead.Mood.Sad,
						_ => AdvisorHead.Mood.Surprised,
					};
					var pop = new InformationalPopup(mSSAP.message, AdvisorHead.Advisor.Science, scienceMood);
					popupOverlay.ShowPopup(pop, PopupOverlay.PopupCategory.Advisor);
					break;
				}
			case MsgEnteredNewEra mENE:
				ShowEraSplash(mENE);
				break;
			case MsgWonderCompleted mWC: {
					// Like the space race news, this is too important to drop, so
					// it waits its turn behind any popup already showing.
					var pop = new InformationalPopup(mWC.Announcement(), AdvisorHead.Advisor.Domestic, AdvisorHead.Mood.Surprised);
					popupOverlay.ShowPopup(pop, PopupOverlay.PopupCategory.Advisor);
					break;
				}
			case MsgGreatHistoryBook mGHB:
				ShowGreatHistoryBook(mGHB);
				break;
			case MsgShowDomesticAdvisorPopup mSDAP: {
					// Like the military advisor's news, this waits its turn.
					var pop = new InformationalPopup(mSDAP.message, AdvisorHead.Advisor.Domestic, AdvisorHead.Mood.Angry);
					popupOverlay.ShowPopup(pop, PopupOverlay.PopupCategory.Advisor);
					break;
				}
			case MsgShowScienceAdvisor mSSA:
				EmitSignal(SignalName.ShowSpecificAdvisor, C7Action.ShowScienceAdvisor);
				break;
			case MsgShowScienceSelection mSSS:
				ShowScienceSelection(mSSS);
				// Research something even if the player dismisses the popup.
				new MsgPickDefaultResearch().send();
				break;
			case MsgCityProductionCompleted mCPC when mCPC.city != null:
				// Nor are the watched civ's builds (wonders among them)
				// announced while watching the AIs play.
				if (!WatchingAiGame) {
					EnqueueProductionPopup(mCPC);
				}
				break;
			case MsgUpdateUiAfterDomesticChange mUUASC:
				// Ensure the citizen moods are correct before displaying them.
				foreach (City c in controller.cities) {
					c.RecalculateCitizenMoods(gameData);
				}
				EmitSignal(SignalName.ShowSpecificAdvisor, C7Action.ShowDomesticAdvisor);
				break;
			case MsgShowTradeOffer mSTO:
				Action showOffer = () => diplomacy.ShowDealScreenForPlayer(
					mSTO.humanPlayer.id, mSTO.aiPlayer.id,
					humanGives: mSTO.aiWant,
					humanWants: mSTO.aiGive,
					greeting: mSTO.greeting);
				// In a hotseat game the offer may be for a player who isn't at
				// the screen, so hand it to them first.
				if (mSTO.humanPlayer.id != controller.id) {
					ShowHotseatHandoff(mSTO.humanPlayer,
						$"The {mSTO.aiPlayer.civilization.noun} have a proposal for you.",
						"Hear Them Out",
						showOffer);
				} else {
					showOffer();
				}
				break;
			case MsgShowTerritoryDemand mSTD:
				Action showDemand = () => diplomacy.ShowTerritoryDemand(
					mSTD.humanPlayer.id, mSTD.aiPlayer.id, mSTD.unitCount, mSTD.ultimatum, mSTD.cityName);
				if (mSTD.humanPlayer.id != controller.id) {
					ShowHotseatHandoff(mSTD.humanPlayer,
						$"The {mSTD.aiPlayer.civilization.noun} demand to speak with you.",
						"Hear Them Out",
						showDemand);
				} else {
					showDemand();
				}
				break;
			case MsgShowTributeDemand mSTrD:
				Action showTributeDemand = () => diplomacy.ShowTributeDemand(
					mSTrD.humanPlayer.id, mSTrD.aiPlayer.id, mSTrD.gold, mSTrD.tech?.Name);
				if (mSTrD.humanPlayer.id != controller.id) {
					ShowHotseatHandoff(mSTrD.humanPlayer,
						$"The {mSTrD.aiPlayer.civilization.noun} demand to speak with you.",
						"Hear Them Out",
						showTributeDemand);
				} else {
					showTributeDemand();
				}
				break;
			case MsgDisplayHurryProductionPopup mDHPP:
				if (mDHPP.details.errorMessage != null) {
					popupOverlay.ShowPopup(
						new InformationalPopup(mDHPP.details.errorMessage),
						PopupOverlay.PopupCategory.Advisor);
				} else {
					popupOverlay.ShowPopup(
						new ConfirmationPopup(message: mDHPP.details.costMessage,
												yesText: "Yes I'm sure!",
												noText: "Maybe you're right. Nevermind.",
												yesAction: () => {
													new MsgDoHurryProduction(mDHPP.city).send();
												}),
						PopupOverlay.PopupCategory.Advisor);
				}
				break;
			case MsgDisplaySellBuildingPopup mDSBP:
				if (mDSBP.errorMessage != null) {
					popupOverlay.ShowPopup(
						new InformationalPopup(mDSBP.errorMessage),
						PopupOverlay.PopupCategory.Advisor);
				} else {
					// Civ3's Script.txt, IMPROVEMENT_SCRAP.
					popupOverlay.ShowPopup(
						new ConfirmationPopup(message: $"Are you sure you want to sell this improvement for {mDSBP.gold} gold?",
												yesText: "Sure I'm sure!",
												noText: "No I've had second thoughts!",
												yesAction: () => {
													new MsgSellBuilding(mDSBP.city, mDSBP.building).send();
												}),
						PopupOverlay.PopupCategory.Advisor);
				}
				break;
			case MsgDisplayDraftPopup mDDP:
				if (mDDP.errorMessage != null) {
					popupOverlay.ShowPopup(
						new InformationalPopup(mDDP.errorMessage),
						PopupOverlay.PopupCategory.Advisor);
				} else {
					popupOverlay.ShowPopup(
						new ConfirmationPopup(message: $"Draft a citizen of {mDDP.city.name} into a conscript {mDDP.unitName}? The draft will make the city unhappy.",
												yesText: "Yes, draft them.",
												noText: "No, leave them be.",
												yesAction: () => {
													new MsgDoDraft(mDDP.city).send();
												}),
						PopupOverlay.PopupCategory.Advisor);
				}
				break;
			case MsgDisplayStopWorkerActionPopup mDSWA:
				popupOverlay.ShowPopup(
					new ConfirmationPopup(
						$"This worker has been ordered to {C7Action.ToTooltip(mDSWA.workerJob.UIAction)} and will be done in {mDSWA.turnsLeft} turns." +
						$"\nDo you want them to stop?",
						"Yes, there is more important work to do!",
						"No, carry on.",
						() => {
							new MsgDoStopWorkerAction(mDSWA.worker).send();
						}),
					PopupOverlay.PopupCategory.Advisor);
				break;
			case MsgWarDeclaration mWD:
				popupOverlay.ShowPopup(
					new InformationalPopup($"The {mWD.aggressor.civilization.noun} declared war on the {mWD.opponent.civilization.noun}"),
					PopupOverlay.PopupCategory.Advisor);
				InterestingEvent();
				break;
			case MsgShowTemporaryPopup mSTP:
				Vector2 pos = mapView.screenLocationOfTile(mSTP.location, true);
				TemporaryPopup.Show(this, mSTP.message, pos);
				break;
			case MsgUnitMoved mUUAAB:
				EmitSignal(SignalName.UnitMoved, new ParameterWrapper<MapUnit>(mUUAAB.Unit));
				if (mUUAAB.Unit == unitSelector.CurrentlySelectedUnit) {
					ShowTutorialForSelectedUnit();
				}
				break;
			case MsgTransportUnloaded mTU:
				// UnitMoved is enough to refresh UI
				EmitSignal(SignalName.UnitMoved, new ParameterWrapper<MapUnit>(mTU.Unit));
				break;
			case MsgDisplayAbandonCityPopup mDACP:
				// The menu greys the order out for a city that can't be
				// abandoned (e.g. our only one), but explain if it gets here.
				string cannotAbandon = null;
				EngineStorage.ReadGameData((GameData gameData) => {
					cannotAbandon = CityInteractions.WhyCannotAbandon(mDACP.city.owner, mDACP.city);
				});
				if (cannotAbandon != null) {
					popupOverlay.ShowPopup(new InformationalPopup(cannotAbandon, AdvisorHead.Advisor.Domestic, AdvisorHead.Mood.Sad),
						PopupOverlay.PopupCategory.Advisor);
					break;
				}
				popupOverlay.ShowPopup(
					new ConfirmationPopup(
						$"Are you sure you want to abandon {mDACP.city.name}?",
						"Yes, we don't want it anymore.",
						"No, sorry.",
						() => {
							new MsgAbandonCity(mDACP.city).send();
						}),
					PopupOverlay.PopupCategory.Advisor);
				break;
			case MsgDisplayRazeCityPopup mDRCP:
				popupOverlay.ShowPopup(
					new ConfirmationPopup(
						$"We have taken {mDRCP.city.name}. What shall we do with it?",
						"Keep the city.",
						"Raze it to the ground!",
						() => { },
						() => {
							new MsgAbandonCity(mDRCP.city, raze: true).send();
						}),
					PopupOverlay.PopupCategory.Advisor);
				break;
			case MsgNoHumansRemain:
				RecordInHallOfFame(HallOfFame.ForDefeat);
				InterestingEvent();
				// After the end of the game's pages, this is their last
				// question, and looking around lets the game go on (see
				// EndOfGame.Claim).
				void ShowNoHumansRemain(bool withReplay) {
					popupOverlay.ShowPopup(
						new ConfirmationPopup(
							"Every human player has been defeated.\nThis game is over.\n\n",
							"Return to the main menu.",
							"Let me look around first.",
							() => {
								// Civ3 shows the replay as the game ends (see ReplayView),
								// unless its defeat screen has already shown it.
								if (withReplay) {
									gameViews.ShowReplay(OnRetire);
								} else {
									OnRetire();
								}
							},
							EndOfGame.PlayOn),
						PopupOverlay.PopupCategory.Advisor, withReplay ? null : EndOfGame.Claim);
				}
				// Civ3's announcement of the loss and its defeat screen
				// first, when the Civ3 install has them (see EndOfGame).
				EndOfGame.ShowReplay = (host, done) => gameViews.ShowReplay(done);
				if (!EndOfGame.Show(this, popupOverlay, null, null, recordedInHallOfFame, () => ShowNoHumansRemain(false))) {
					ShowNoHumansRemain(true);
				}
				break;
			case MsgShowDealProposal mSDP:
				Action showProposal = () => {
					popupOverlay.ShowPopup(
						new ConfirmationPopup(
							DealScreen.DescribeDeal(mSDP.proposer, mSDP.proposerGives, mSDP.proposerWants),
							"We accept.",
							"We refuse.",
							() => { new MsgRespondToDeal(true).send(); },
							() => { new MsgRespondToDeal(false).send(); }) { escapeMeansNo = true },
						PopupOverlay.PopupCategory.Advisor);
					InterestingEvent();
				};
				// A LAN client's players take turns at its screen, so the deal
				// may be for one who isn't at it.
				if (mSDP.opponent.id != controller.id) {
					ShowHotseatHandoff(mSDP.opponent,
						$"The {mSDP.proposer.civilization.noun} propose a deal.",
						"Hear Them Out",
						showProposal);
				} else {
					showProposal();
				}
				break;
			case MsgPeaceTerms mPT:
				if (diplomacy.Visible) {
					diplomacy.OnPeaceTerms(mPT.recipient.id, mPT.opponent.id, mPT.humanGives, mPT.humanWants);
				}
				break;
			case MsgDealResult mDR:
				if (diplomacy.Visible) {
					diplomacy.OnDealResult(mDR.opponent.id, mDR.accepted);
				} else if (mDR.opponent.isHuman) {
					string answer = mDR.accepted ? "accepted" : "refused";
					popupOverlay.ShowPopup(
						new InformationalPopup($"{mDR.opponent.civilization.leader} {answer} our offer."),
						PopupOverlay.PopupCategory.Advisor);
				}
				break;
			case MsgWithdrawalDemandResult mWDR:
				if (diplomacy.Visible) {
					diplomacy.OnWithdrawalDemandResult(mWDR.opponent.id, mWDR.withdrew);
				} else if (mWDR.withdrew) {
					popupOverlay.ShowPopup(
						new InformationalPopup($"The {mWDR.opponent.civilization.noun} withdrew their units from our territory."),
						PopupOverlay.PopupCategory.Advisor);
				}
				break;
			case MsgCityChanged mCC:
				cityScreen.RefreshCity(mCC.city);
				break;
			case MsgShowUnitedNationsVote mSUNV: {
					List<ChoicePopup.Choice> choices = new();
					foreach (Player candidate in mSUNV.candidates) {
						choices.Add(new ChoicePopup.Choice($"Vote for {candidate.civilization.leader} of the {candidate.civilization.noun}",
							() => new MsgCastUnitedNationsVote(candidate).send()));
					}
					choices.Add(new ChoicePopup.Choice("Abstain", () => new MsgCastUnitedNationsVote(null).send()));
					popupOverlay.ShowPopup(
						new ChoicePopup("United Nations",
							"The United Nations is electing a Secretary General.\nHow do we vote?",
							choices, cancellable: false),
						PopupOverlay.PopupCategory.Advisor);
					InterestingEvent();
					break;
				}
			case MsgShowUnitedNationsElectionOffer mSUNEO: {
					string candidates = string.Join(", ", mSUNEO.candidates.Select(c => $"{c.civilization.leader} of the {c.civilization.noun}"));
					popupOverlay.ShowPopup(
						new ChoicePopup("United Nations",
							"Hold a United Nations election?\n"
							+ $"The candidates for Secretary General would be {candidates}.\n"
							+ $"If we don't, we may next choose in {UnitedNations.ElectionInterval} turns.",
							[
								new ChoicePopup.Choice("Yes, hold the election.", () => new MsgHoldUnitedNationsElection(true).send()),
								new ChoicePopup.Choice("No, not now.", () => new MsgHoldUnitedNationsElection(false).send()),
							], cancellable: false),
						PopupOverlay.PopupCategory.Advisor);
					InterestingEvent();
					break;
				}
			case MsgShowPalaceUpgrade: {
					// Civ3's popup (script.txt, PALACE_VIEW_UPGRADE), after
					// which the palace view opens to place the upgrade (Civ3
					// FUN_0056c5a0). Without Civ3's text, as in standalone
					// mode, there is no palace art to build with either, so
					// no popup.
					ScriptEntry upgrade = Util.HasCiv3Media("Text/script.txt")
						? ScriptText.Load(Util.Civ3MediaPath("Text/script.txt")).Get("PALACE_VIEW_UPGRADE")
						: null;
					if (upgrade == null || C7Settings.UseStandaloneMode()) {
						break;
					}
					popupOverlay.ShowPopup(
						new ChoicePopup(upgrade.Caption ?? "", string.Join("\n", upgrade.Lines),
							[new ChoicePopup.Choice("OK", () => {
								// The palace has the screen until it's built on.
								ClaimWhileOpen("palace", () => gameViews.Visible, admitsNewPopups: true);
								EmitSignal(SignalName.ShowGameView, C7Action.ShowPalaceView);
							})],
							cancellable: false),
						PopupOverlay.PopupCategory.Advisor);
					InterestingEvent();
					break;
				}
			case MsgShowMarket mSM: {
					// Bid for the player it's for, who in a hotseat game may
					// not be the one at the screen.
					Player player = mSM.recipient ?? controller;
					popupOverlay.ShowPopup(
						new MarketPopup(player, mSM.offers, mSM.gold, mSM.seconds, mSM.cities,
							(bids, cities) => new MsgPlaceMarketBids(bids, cities) { playerID = player.id }.send()),
						PopupOverlay.PopupCategory.Advisor);
					InterestingEvent();
					break;
				}
			case MsgShowMarketResults mSMR: {
					popupOverlay.ShowPopup(new MarketResultsPopup(mSMR.recipient ?? controller, mSMR.results), null);
					break;
				}
			case MsgShowCultureFlipOffer mSCFO: {
					// Civ3's wording (script.txt, WE_CONVERT_CITY and
					// WE_CONVERT_CITY_WITH_WONDER).
					City city = mSCFO.city;
					bool wonder = mSCFO.wonders.Count > 0;
					string message = wonder
						? $"The citizens of {city.name} have overthrown their oppressors and pledged allegiance to us!\n"
							+ $"And I am honored to report that the city contains {string.Join(", ", mSCFO.wonders)}!"
						: $"The loyal citizens of {city.name} have overthrown their oppressors and have pledged allegiance to us!\n"
							+ "They yearn to be with us.";
					popupOverlay.ShowPopup(
						new ChoicePopup(city.name, Embassies.Wrap(message),
							[
								new ChoicePopup.Choice(wonder ? "Wow! Install a new governor." : "Great! Install a new governor.",
									() => new MsgAnswerCultureFlip(city, true).send()),
								new ChoicePopup.Choice(wonder ? $"Don't want {city.name}. Rebuff the rebels." : $"We don't want {city.name}. Rebuff the rebels.",
									() => new MsgAnswerCultureFlip(city, false).send()),
							], cancellable: false),
						PopupOverlay.PopupCategory.Advisor);
					InterestingEvent();
					break;
				}
			case MsgUnitedNationsElectionResult mUNER: {
					string outcome = mUNER.winner == null
					? "No candidate won a majority."
					: $"{mUNER.winner.civilization.leader} of the {mUNER.winner.civilization.noun} has been elected Secretary General!";
					popupOverlay.ShowPopup(
						new ChoicePopup("United Nations",
							string.Concat(mUNER.candidates.Select((c, i) => $"{AdvisorUtils.CivTitle(c)}: {mUNER.votes[i]} votes\n"))
							+ $"Abstaining: {mUNER.abstentions} votes\n{outcome}",
							[new ChoicePopup.Choice("Very well.", () => { })], cancellable: false),
						PopupOverlay.PopupCategory.Advisor);
					InterestingEvent();
					break;
				}
			case MsgEspionageResult mER: {
					// A new embassy shows us their capital once, then the land
					// around it stays on the map as it was. An investigated
					// city is shown on the city screen too, as in Civ3.
					bool showCapital = mER.success && mER.mission is EspionageMission.EstablishEmbassy or EspionageMission.InvestigateCity
						&& mER.city != null;
					// A stolen map, stolen plans or a city won by propaganda change
					// what the map shows.
					bool mapChanged = mER.success && mER.mission is EspionageMission.StealWorldMap or EspionageMission.StealPlans
						or EspionageMission.Propaganda;
					if (showCapital || mapChanged) {
						mapView.InvalidateMap();
					}
					City capital = mER.city;
					// The Espionage screen shows the new embassy, spy and treasury.
					advisor.RefreshEspionage();
					popupOverlay.ShowPopup(
						new ChoicePopup(Espionage.Describe(mER.mission), Embassies.Wrap(mER.message ?? ""),
							[new ChoicePopup.Choice("Very well.", () => {
							if (showCapital) {
								// The city is shown on the map, under the
								// Espionage screen.
								advisor.Hide();
								ClaimWhileOpen("foreign city", () => cityScreen.Visible, admitsNewPopups: true);
								ShowForeignCityScreen(capital);
							}
						})], cancellable: false),
						PopupOverlay.PopupCategory.Advisor);
					break;
				}
			case MsgVictory mV:
				RecordInHallOfFame((gameData, player, date, now) =>
					HallOfFame.ForVictory(gameData, player, mV.winner, mV.victory, date, now));
				bool campaignWon = RecordCampaignVictory(mV);
				// A won campaign shows its record once the player is done (Civ3
				// FUN_004f9fb0).
				Action retire = campaignWon ? () => ShowCampaignRecord(OnRetire) : OnRetire;
				InterestingEvent();
				// Civ3's announcement, victory screen, replay and Hall of
				// Fame, then its "Game Over, Man!" (see EndOfGame).
				EndOfGame.ShowReplay = (host, done) => gameViews.ShowReplay(done);
				if (EndOfGame.Show(this, popupOverlay, VictoryScreenLayout.ReasonFor(mV.victory), mV.winner, recordedInHallOfFame,
						() => EndOfGame.ShowGameOver(popupOverlay, retire))) {
					break;
				}
				var endMsg =
					$"The {mV.winner.civilization.noun} have won a {mV.victory.Header()} victory!\n"
					+ $"Final score: {mV.finalScore}"
					+ (mV.earlyWinBonus > 0 ? $" ({mV.earlyWinBonus} for winning early)" : "") + "\n"
					+ "This game is over: No further score will be entered.\n\n";

				popupOverlay.ShowPopup(
					new ConfirmationPopup(
						endMsg,
						"Good! I’m Done!",
						"Wait, lemme just play a couple of more turns...",
						() => {
							// Civ3 shows the replay as the game ends (see ReplayView).
							gameViews.ShowReplay(retire);
						}),
					PopupOverlay.PopupCategory.Advisor);
				break;
		}
	}

	// One of Civ3's Great Books (see GreatBooks), worded by the player's Civ3
	// Text/Great Books.txt, as Civ3 FUN_00548d80 shows it: the popup's
	// #ADVISOR and #CAPTION, and its text with the historian, the title and
	// the ranks filled in. Without the file (standalone mode) there is no
	// book to show.
	private void ShowGreatHistoryBook(MsgGreatHistoryBook book) {
		const string path = "Text/Great Books.txt";
		GreatBooksText text = Util.HasCiv3Media(path) ? GreatBooksText.Load(Util.Civ3MediaPath(path)) : null;
		string message = text?.Format(new GreatBooks.Book(book.historian, book.title, book.ranking ?? []), AdvisorUtils.CivName);
		if (message == null) {
			log.Information("No Great Books text to show a book with");
			return;
		}
		(AdvisorHead.Advisor advisor, AdvisorHead.Mood mood) = GreatBookAdvisor(text.Advisor);
		InformationalPopup popup = new(message, advisor, mood) { header = text.Caption };
		popupOverlay.ShowPopup(popup, PopupOverlay.PopupCategory.Advisor);
	}

	// The advisor of a text file's "#ADVISOR Cultural Happy" line.
	private static (AdvisorHead.Advisor, AdvisorHead.Mood) GreatBookAdvisor(string line) {
		AdvisorHead.Advisor advisor = AdvisorHead.Advisor.Culture;
		AdvisorHead.Mood mood = AdvisorHead.Mood.Happy;
		string[] words = (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (words.Length > 0 && !words[0].StartsWith("Cultur", StringComparison.OrdinalIgnoreCase)
			&& Enum.TryParse(words[0], true, out AdvisorHead.Advisor named)) {
			advisor = named;
		}
		if (words.Length > 1 && Enum.TryParse(words[1], true, out AdvisorHead.Mood felt)) {
			mood = felt;
		}
		return (advisor, mood);
	}

	// The cities waiting to tell their owner they've finished building
	// something, by owner. Cities produce at the end of the round, so in a
	// hotseat game these wait for each player's turn to start. They're shown
	// one at a time, and not while the city screen is open, so a city the
	// player zoomed to gets its production chosen before the next city asks.
	private readonly Dictionary<ID, LinkedList<MsgCityProductionCompleted>> pendingProductionPopups = new();
	private CityProductionPopup shownProductionPopup = null;
	private MsgCityProductionCompleted shownProductionMessage = null;
	// The wonder splash shown before the production popup, if any.
	private Popup shownWonderSplash = null;

	// Whether the player is choosing what a city builds next: its production
	// popup is up, another is waiting its turn, or the city screen is open.
	// The unit selector holds off until they're done, so the camera stays
	// on the city rather than moving to the next unit.
	public bool IsChoosingProduction => shownProductionPopup != null
		|| cityScreen.Visible
		|| (controller != null && pendingProductionPopups.TryGetValue(controller.id, out LinkedList<MsgCityProductionCompleted> pending) && pending.Count > 0);

	private LinkedList<MsgCityProductionCompleted> PendingProductionPopupsFor(ID playerID) {
		if (!pendingProductionPopups.TryGetValue(playerID, out LinkedList<MsgCityProductionCompleted> pending)) {
			pending = new();
			pendingProductionPopups[playerID] = pending;
		}
		return pending;
	}

	private static ID ProductionPopupOwner(MsgCityProductionCompleted msg) {
		return msg.recipient?.id ?? msg.city.owner?.id;
	}

	private void EnqueueProductionPopup(MsgCityProductionCompleted msg) {
		ID ownerID = ProductionPopupOwner(msg);
		if (ownerID == null) {
			return;
		}
		PendingProductionPopupsFor(ownerID).AddLast(msg);
		ShowNextProductionPopup();
	}

	private void ShowNextProductionPopup() {
		// Only once the player's turn is underway, so the news isn't shown
		// to whoever had the screen before them.
		if (CurrentState != GameState.PlayerTurn || hotseatHandoff != null || controller == null) {
			return;
		}
		LinkedList<MsgCityProductionCompleted> pending = PendingProductionPopupsFor(controller.id);
		if (pending.Count == 0 || !MayShowTurnStartPopup()) {
			// Tried again each frame (see _Process) until it may.
			return;
		}
		while (shownProductionPopup == null && !cityScreen.Visible && pending.Count > 0) {
			MsgCityProductionCompleted msg = pending.First.Value;
			pending.RemoveFirst();
			// Found by id, as a LAN client's snapshots replace the cities. It
			// may have been lost or destroyed since.
			City city = EngineStorage.gameData.cities.Find(c => c.id == msg.city.id);
			if (city == null || city.owner?.id != controller.id) {
				continue;
			}
			// The player may prefer not to be asked after a unit: the city
			// goes on with what it chose next.
			if (!Preferences.AskForBuildOrdersAfterUnitConstruction && msg.next != null
					&& EngineStorage.gameData.unitPrototypes.Exists(p => p.name == msg.completed)) {
				continue;
			}
			// Nothing else is up (see MayShowTurnStartPopup), so the map
			// can go to the city now.
			mapView?.centerCameraOnTile(city.location);
			// A wonder is first celebrated with Civ3's splash; the city asks
			// what to build next once it's gone.
			Building wonder = EngineStorage.gameData.Buildings.Find(b => b.name == msg.completed);
			shownWonderSplash = WonderSplashScreen.For(wonder, city, controller, LanSession.IsActive);
			if (shownWonderSplash != null) {
				// The splash plays its own sound.
				popupOverlay.ShowPopup(shownWonderSplash,
					shownWonderSplash is WonderSplashScreen ? null : PopupOverlay.PopupCategory.Advisor);
			}
			CityProductionPopup popup = new(city, msg.completed,
				// The next popup waits for the city screen to close.
				onZoom: () => {
					ClaimWhileOpen("city screen", () => cityScreen.Visible, admitsNewPopups: true);
					ShowCityScreenForCity(EngineStorage.gameData, city);
				},
				onDone: ShowNextProductionPopup);
			shownProductionPopup = popup;
			shownProductionMessage = msg;
			// However the popup goes away, the next one may follow.
			popup.TreeExiting += () => {
				if (shownProductionPopup == popup) {
					shownProductionPopup = null;
					shownProductionMessage = null;
				}
				Callable.From(ShowNextProductionPopup).CallDeferred();
			};
			popupOverlay.ShowPopup(popup, PopupOverlay.PopupCategory.Advisor);
		}
	}

	// Whether a city may ask what to build next now. The production popups
	// move the map to their city, and the player answers them one after
	// another, so they wait until nothing else is up or on its way: not
	// behind another popup, which they used to join the queue of, moving
	// the map from under it; and not while the engine has yet to answer
	// something the player just did. Choosing a free tech (such as
	// Philosophy's) is answered by asking what to research next, which
	// otherwise came only after the next city's popup had taken its place.
	// News held for the player (see ReplayHeldMessage) is shown first.
	private bool MayShowTurnStartPopup() {
		return !popupOverlay.Visible
			&& !popupOverlay.Claims.Any
			&& !cityScreen.Visible
			&& !EngineStorage.HasPendingMessagesToEngine()
			&& researchQuestionDueSeconds <= 0
			&& !(heldMessages.TryGetValue(controller.id, out Queue<MessageToUI> held) && held.Count > 0);
	}

	// A LAN client's choice of research goes to the host, whose next question
	// takes a moment to come back, so after a free tech is chosen the
	// production popups wait for it this long at most (see
	// MayShowTurnStartPopup); a single machine's engine answers in the next
	// frame, while the choice is still a pending message.
	private const double ResearchQuestionWaitSeconds = 3.0;
	private double researchQuestionDueSeconds = 0;

	// Shows the science advisor's question of what to research (or, with a
	// free tech to come, which tech to take).
	private void ShowScienceSelection(MsgShowScienceSelection msg) {
		// The question the production popups were waiting for has come.
		researchQuestionDueSeconds = 0;
		bool freeTech = controller.freeTechsRemaining > 0;
		ScienceSelection selection = new(controller, msg.discovered, () => ViewTheBigPicture(chooseAfterwards: true));
		if (freeTech && LanSession.IsClient) {
			// Once the free tech is chosen the host asks what's next.
			selection.TreeExiting += () => researchQuestionDueSeconds = ResearchQuestionWaitSeconds;
		}
		popupOverlay.ShowPopup(selection, PopupOverlay.PopupCategory.Info);
	}

	// "What's the big picture?": opens the Science Advisor, which has the
	// screen until the player has closed it (see ClaimWhileOpen), so nothing
	// comes up over it, and no city asks what to build, while they choose
	// what to research (owner's requests, 2026-11-08 and 2026-11-12). If they
	// close it without choosing, they're asked again before anything else.
	// The advisor may open a moment later (when the engine answers a choice
	// of research).
	private void ViewTheBigPicture(bool chooseAfterwards) {
		Player player = controller;
		ClaimWhileOpen("big picture", () => advisor.Visible, afterwards: () => {
			GameData gameData = EngineStorage.gameData;
			Player us = gameData?.GetPlayer(player.id);
			// The engine may have asked already (after a free tech).
			if (!chooseAfterwards || us == null || us != controller || popupOverlay.HasPopup<ScienceSelection>()) {
				return;
			}
			bool undecided = us.freeTechsRemaining > 0 || us.currentlyResearchedTech == null;
			if (undecided && us.GetAvailableTechsToResearch(gameData.techs).Any()) {
				popupOverlay.ShowPopup(new ScienceSelection(us, null, () => ViewTheBigPicture(chooseAfterwards: true)),
					PopupOverlay.PopupCategory.Info);
			}
		});
		if (chooseAfterwards) {
			EmitSignal(SignalName.ShowSpecificAdvisor, C7Action.ShowScienceAdvisor);
		}
	}

	// Gives the screen to one the player is sent to from a popup (the Science
	// Advisor, a city screen, the palace) until they close it: the popups
	// waiting stay back rather than come up over it, and the cities' and the
	// held news's prompts wait (see ScreenClaim). With admitsNewPopups, what
	// comes up while it's open (such as the city screen's own questions)
	// still does. It's given up if the screen hasn't opened within a few
	// seconds. Call it before opening the screen.
	private ScreenClaim ClaimWhileOpen(string name, Func<bool> isOpen, bool admitsNewPopups = false, Action afterwards = null) {
		return popupOverlay.Claims.ClaimWhileOpen(name, isOpen, () => Time.GetTicksMsec() / 1000.0,
			admitsNewPopups: admitsNewPopups, afterwards: afterwards);
	}

	// Takes down the production popup on screen (or waiting behind another
	// popup) and keeps its news for its owner's next turn, e.g. when the
	// screen is handed to another hotseat player.
	private void HoldShownProductionPopup() {
		if (shownProductionPopup == null) {
			return;
		}
		CityProductionPopup popup = shownProductionPopup;
		MsgCityProductionCompleted msg = shownProductionMessage;
		shownProductionPopup = null;
		shownProductionMessage = null;
		ID ownerID = ProductionPopupOwner(msg);
		if (ownerID != null) {
			PendingProductionPopupsFor(ownerID).AddFirst(msg);
		}
		if (IsInstanceValid(popup)) {
			popupOverlay.Dismiss(popup);
		}
		// The splash comes back with the news.
		if (shownWonderSplash != null && IsInstanceValid(shownWonderSplash)) {
			popupOverlay.Dismiss(shownWonderSplash);
		}
		shownWonderSplash = null;
	}

	private void InterestingEvent() {
		// Break out of fast forward mode after interesting events.
		turnsLeftToFastForward = 0;
	}

	// How long a frame may spend handling the engine's messages to the UI.
	private static readonly long uiMessageBudgetTicks = Stopwatch.Frequency * 4 / 1000;

	// How long a frame may spend handling a run of messages to the engine.
	private static readonly TimeSpan engineMessageBudget = TimeSpan.FromMilliseconds(4);

	// How long a frame may spend on the AIs' turns before drawing, so the
	// window doesn't freeze while they play.
	private static readonly TimeSpan engineWorkBudget = TimeSpan.FromMilliseconds(8);

	public override void _Process(double delta) {
		if (errorOnLoad || editing) {
			return;
		}
		PollLanSession();
		ProcessActions();
		EdgeScroll(delta);
		UpdateGotoPath();

		// The engine waits for animations to finish before going on, and for
		// a flow that holds the game, like the end of the game, to end (see
		// ScreenClaim). On a LAN the other machines' players play on.
		if (!EngineStorage.HasPendingAnimations() && (!GameHeld || LanSession.IsActive))
			EngineStorage.ProcessMessagesToEngine(engineMessageBudget);

		// Handle the waiting messages to the UI, as many as fit in the frame's
		// budget. None of them start animations (those have their own queue),
		// so there's no need to pace them. While a flow holds the game, they
		// stay in the queue, in order, until it ends.
		long start = Stopwatch.GetTimestamp();
		bool handledMessage = false;
		while (!GameHeld && EngineStorage.TryDequeueNextMessageToUI(out MessageToUI msg)) {
			HandleEngineMessage(msg);
			handledMessage = true;
			if (Stopwatch.GetTimestamp() - start >= uiMessageBudgetTicks) {
				break;
			}
		}
		if (!handledMessage)
			ReplayHeldMessage();

		if (researchQuestionDueSeconds > 0) {
			researchQuestionDueSeconds -= delta;
		}
		// Cities with news wait until nothing else is up.
		ShowNextProductionPopup();

		EndTurnIfNothingToDo(delta);
		UpdateMusic();
	}

	// With Edge scrolling on in the settings, pans the map while the mouse
	// rests at an edge of the window: only while the game's window has focus
	// and the mouse is in it, the map is in front with nothing over it, and
	// the map or a unit isn't being dragged. The mouse position, margin and
	// camera are all in the UI's logical pixels, so the margin and speed grow
	// with the interface scale; the speed is per second, whatever the frame
	// rate. Edge scrolling adapted from GBiebs/OpenCiv3.
	private void EdgeScroll(double delta) {
		if (!EdgeScrolling.Enabled || IsMovingCamera || draggingUnit || HasVisibleModal() || hotseatHandoff != null
			|| (FileDialog?.Visible ?? false) || (CurrentState == GameState.ComputerTurn && !LanSession.IsSpectator)) {
			return;
		}
		Window window = GetWindow();
		if (!window.HasFocus()) {
			return;
		}
		// The viewport keeps the last position the mouse had in the window
		// after it leaves, so ask where it really is.
		Rect2I windowRect = new(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
		if (!windowRect.HasPoint(DisplayServer.MouseGetPosition())) {
			return;
		}
		Vector2 mouse = GetViewport().GetMousePosition();
		Vector2 size = GetViewport().GetVisibleRect().Size;
		float margin = EdgeScrolling.Margin;
		Vector2 direction = new(
			mouse.X < margin ? -1 : mouse.X >= size.X - margin ? 1 : 0,
			mouse.Y < margin ? -1 : mouse.Y >= size.Y - margin ? 1 : 0);
		if (direction != Vector2.Zero) {
			mapView.moveCamera(direction * EdgeScrolling.Speed * (float)delta);
		}
	}

	// Set once no unit needs orders this turn, until one does again.
	private bool endTurnWhenIdle = false;
	private double idleSeconds = 0;

	// How long nothing needs doing before the turn ends by itself, so the
	// player sees the last move and anything that comes up stops it.
	private const double IdleSecondsBeforeEndingTurn = 1.0;

	// The preferences' Always Wait at End of Turn: when it's off, the turn
	// ends by itself once none of the player's units needs orders and
	// nothing is on screen for them to answer or look at.
	private void EndTurnIfNothingToDo(double delta) {
		bool idle = endTurnWhenIdle
			&& !Preferences.AlwaysWaitAtEndOfTurn
			&& CurrentState == GameState.PlayerTurn
			&& hotseatHandoff == null
			&& !LanSession.IsSpectator
			&& !WatchingAiGame
			&& CurrentlySelectedUnit == MapUnit.NONE
			&& !HasVisibleModal()
			&& !IsChoosingProduction
			&& !(FileDialog?.Visible ?? false)
			&& !EngineStorage.HasPendingAnimations()
			&& !EngineStorage.HasPendingMessagesToEngine();
		if (!idle) {
			idleSeconds = 0;
			return;
		}
		idleSeconds += delta;
		if (idleSeconds >= IdleSecondsBeforeEndingTurn) {
			idleSeconds = 0;
			endTurnWhenIdle = false;
			log.Information("Nothing left to do this turn; ending it");
			OnPlayerEndTurn();
		}
	}

	// Whether the hotseat curtain hides the whole map.
	public bool IsMapHidden => CurtainIsUp;

	// Whether a curtain hides the map: the hotseat handoff, or the end of a
	// turn played by email.
	private bool CurtainIsUp => hotseatHandoff != null || playByEmailSave != null;

	// Whether the game may change without the UI being told, i.e. while the
	// AI plays, animations run, or the engine has messages to process. The
	// map is redrawn regularly while this is true. A LAN client's game only
	// changes with the host's snapshots and messages, which it is told about.
	// Neither does a LAN host's while it waits for another machine's player:
	// their moves are messages its engine processes, which redraw the map.
	public bool MapMayChangeWithoutNotice =>
		(CurrentState == GameState.ComputerTurn && !LanSession.IsClient && !IsWaitingForRemotePlayer)
		|| EngineStorage.HasPendingAnimations()
		|| EngineStorage.HasPendingMessagesToEngine();

	// Whether another machine's player is taking their turn in a LAN game,
	// while the banner saying so is shown.
	public bool IsWaitingForRemotePlayer => lanWaitingBanner != null;

	// Whether a flow on screen holds the game (see ScreenClaim), as the end
	// of the game does until the player chooses to play on.
	private bool GameHeld => popupOverlay.Claims.HoldsGame;

	// Shows messages that were held for the UI controller while another
	// player had the screen, one popup at a time, once their turn is underway.
	private void ReplayHeldMessage() {
		if (CurrentState != GameState.PlayerTurn || hotseatHandoff != null || popupOverlay.Visible || popupOverlay.Claims.Any) {
			return;
		}
		if (heldMessages.TryGetValue(controller.id, out Queue<MessageToUI> held) && held.TryDequeue(out MessageToUI msg)) {
			// The map was updated for the message when it arrived.
			mapView?.InvalidateMap();
			ShowEngineMessage(msg);
		}
	}

	// If "location" is not already near the center of the screen, moves the camera to bring it into view.
	public void ensureLocationIsInView(Tile location) {
		if (controller.tileKnowledge.isTileKnown(location) && location != Tile.NONE) {
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

	private int governmentPromptTurn = -1;

	// If the player can now pick a new government, forces them to do so. On a
	// LAN the choice may not be back from the host yet, so only asks once a
	// turn.
	private void PromptForGovernmentIfDue(GameData gameData) {
		if (controller.government.transitionType && TurnHandling.GetTurnNumber() >= controller.inAnarchyUntilTurn
				&& (!LanSession.IsClient || governmentPromptTurn != gameData.turn)) {
			governmentPromptTurn = gameData.turn;
			popupOverlay.ShowPopup(
				new GovernmentSelection(controller, controller.GetAvailableGovernments(gameData), OnGovernmentSelectionClosed),
				PopupOverlay.PopupCategory.Info);
		}
	}

	// A government selection that went away without a choice (e.g. taken
	// down while the screen changed hands) asks again if it's still this
	// player's turn; otherwise their next turn will.
	private void OnGovernmentSelectionClosed(bool chosen) {
		if (chosen) {
			return;
		}
		governmentPromptTurn = -1;
		Callable.From(() => {
			if (!IsInstanceValid(this) || !IsInsideTree() || CurrentState != GameState.PlayerTurn || hotseatHandoff != null || controller == null) {
				return;
			}
			EngineStorage.ReadGameData(PromptForGovernmentIfDue);
		}).CallDeferred();
	}

	private void OnPlayerStartTurn() {
		EngineStorage.ReadGameData((GameData gameData) => {
			log.Information("Starting player turn");

			PromptForGovernmentIfDue(gameData);

			// If the player can pick a new tech to research, the engine
			// prompts them to do so, naming the tech they just discovered.
			new MsgAskWhatToResearch().send();

			if (WatchingAiGame) {
				WarListForWatching().Track(gameData);
			}

			// Allow fast forwarding in observer mode.
			if (gameData.observerMode && turnsLeftToFastForward > 0) {
				--turnsLeftToFastForward;
				new MsgEndTurn().send();
				return;
			}

			// Paused while watching: show who's at war.
			if (WatchingAiGame) {
				WarListForWatching().ShowAtPause(gameData.turn);
			}

			AutosaveIfDue(gameData);

			CurrentState = GameState.PlayerTurn;
		});

		// Cities that finished building something at the end of the round
		// can tell the player now.
		ShowNextProductionPopup();
		TutorialGuide.OnTurnStart(popupOverlay, controller);
		EmitSignal(SignalName.PlayerTurnStart);
	}

	// Civ3's tutorial tip for the unit the player is to move, if Tutorial
	// Mode is on (see TutorialGuide). Civ3 looks for one each time its turn
	// loop has the unit for the player to move (FUN_004e8540), so this is
	// asked as a unit is selected and as the selected unit moves.
	public void ShowTutorialForSelectedUnit() {
		if (CurrentState == GameState.PlayerTurn) {
			TutorialGuide.OnUnitActive(popupOverlay, unitSelector.CurrentlySelectedUnit);
		}
	}

	// The autosaves kept, the latest first; older ones are deleted.
	private const int AutosavesKept = 10;
	private const string AutosavePrefix = "Autosave Turn ";
	private int autosavedTurn = -1;

	// The preferences' Auto-Save: saves the game as each of the player's
	// turns begins (once a round in a hotseat game), to Saves/Auto, or a
	// recorded game's folder. Not in a
	// LAN game, whose host keeps its own autosave, nor while watching the AIs.
	private void AutosaveIfDue(GameData gameData) {
		if (!Preferences.AutoSave || LanSession.IsActive || gameData.observerMode || gameData.turn == autosavedTurn) {
			return;
		}
		autosavedTurn = gameData.turn;
		// A recorded game's autosaves go in its folder (see StartPlayLog).
		string directory = GameFolder(gameData) ?? System.IO.Path.Combine(Civ3FileDialog.SavesDirectory, "Auto");
		string path = System.IO.Path.Combine(directory, $"{AutosavePrefix}{gameData.turn}.json");
		try {
			System.IO.Directory.CreateDirectory(directory);
			DeleteOldAutosaves(directory);
			log.Information("Autosaving the game to {Path}", path);
			SaveManager.SaveInBackground(gameData, path).ContinueWith(
				saving => log.Warning(saving.Exception, "Couldn't autosave the game to {Path}", path),
				TaskContinuationOptions.OnlyOnFaulted);
		} catch (Exception e) {
			// An autosave that fails mustn't stop the game.
			log.Warning(e, "Couldn't autosave the game to {Path}", path);
		}
	}

	// Leaves room for the autosave about to be written.
	private static void DeleteOldAutosaves(string directory) {
		var old = new System.IO.DirectoryInfo(directory).GetFiles(AutosavePrefix + "*.json")
			.OrderByDescending(file => file.LastWriteTimeUtc)
			.Skip(AutosavesKept - 1);
		foreach (System.IO.FileInfo file in old) {
			file.Delete();
		}
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
					popupOverlay.ShowPopup(
						new ConfirmationPopup(
							$"{city.name} will riot! Are you sure?",
							"Yes, let them riot!",
							"No. Maybe you are right, advisor.",
							() => {
								DoActualEndTurn();
							}),
						PopupOverlay.PopupCategory.Advisor);
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
		warList?.HideAndContinue();
		// Ending the turn by hand in observer mode plays on to the next pause.
		if (EngineStorage.gameData.observerMode && fastForwardInterval > 0) {
			turnsLeftToFastForward = fastForwardInterval - 1;
		}
		new MsgEndTurn { turn = EngineStorage.gameData.turn }.send(); // Triggers actual backend processing
																	  // Production news the player hasn't seen is out of date. Other
																	  // hotseat players keep theirs for their own turns.
		if (controller != null) {
			pendingProductionPopups.Remove(controller.id);
		}
		EmitSignal(SignalName.PlayerTurnEnd);

		// With simultaneous turns, the next of our players can go on while
		// others are still moving.
		if (SimultaneousLanTurns && controller != null) {
			NoteSimultaneousRound(EngineStorage.gameData);
			simultaneousTurnsEnded.Add(controller.id);
			ContinueSimultaneousTurns();
		}
	}

	// Whether the whole map can be revealed: not in LAN games, where it would show other players what they haven't explored.
	public bool CanRevealWholeMap => !LanSession.IsActive && mapView != null;

	public bool IsWholeMapRevealed => mapView?.revealWholeMap ?? false;

	// Whether every tile is drawn as known: in observer mode, or while the whole map is revealed.
	public bool ShowsWholeMap(GameData gameData) {
		return mapView?.ShowsWholeMap(gameData) ?? gameData.observerMode;
	}

	// Reveals the whole map, or puts the fog of war back. Only this client's drawing changes; the engine's map knowledge is untouched.
	public void OnToggleRevealWholeMap() {
		if (!CanRevealWholeMap) {
			return;
		}
		mapView.revealWholeMap = !mapView.revealWholeMap;
	}

	public void OnRetire() {
		// Quit to main menu, freeing previous scene data
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}

	// Retire from the game menu: as in Civ3, the game goes into the Hall of
	// Fame as "Retired", unless it was already over.
	public void OnRetireFromGameMenu() {
		bool over = false;
		EngineStorage.ReadGameData((GameData gameData) => over = gameData.gameOver);
		if (!over) {
			RecordInHallOfFame(HallOfFame.ForRetirement);
			// Civ3 sees a retiring player off with its loss announcement and
			// defeat screen (Civ3 FUN_004f9fb0's ending 6), then the replay
			// and the Hall of Fame, before the main menu. Without Civ3
			// (standalone mode) it's the same, in our own words and art.
			popupOverlay.OnHidePopup();
			EndOfGame.ShowReplay = (host, done) => gameViews.ShowReplay(done);
			if (EndOfGame.Show(this, popupOverlay, GameEndReason.Retirement, null, recordedInHallOfFame, OnRetire)) {
				return;
			}
		}
		OnRetire();
	}

	// Whether this game is in the Hall of Fame yet, so it goes in only once
	// however it ends.
	private bool recordedInHallOfFame = false;

	// Puts the game into the Hall of Fame for the player playing it here,
	// unless they're only watching the AIs.
	private void RecordInHallOfFame(Func<GameData, Player, string, DateTime, HallOfFameEntry> makeEntry) {
		if (recordedInHallOfFame) {
			return;
		}
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			if (player == null || !player.isHuman) {
				return;
			}
			string date = "";
			try {
				date = gameData.timeOptions?.GetDisplayTime(gameData.turn) ?? "";
			} catch (Exception e) {
				log.Warning(e, "Couldn't work out the date for the Hall of Fame");
			}
			recordedInHallOfFame = true;
			HallOfFame.Add(HallOfFameScreen.GlobalPath, makeEntry(gameData, player, date, DateTime.UtcNow));
		});
	}

	// Whether this game's win is in the Campaign Record yet.
	private bool recordedCampaignVictory = false;

	// Puts a win at one of Conquests' campaigns into the Campaign Record, if
	// the player playing here won it. Returns whether it went in, for the
	// record to be shown (Civ3 FUN_004f9fb0 records and shows it when the
	// winner is the human player).
	private bool RecordCampaignVictory(MsgVictory victory) {
		if (recordedCampaignVictory) {
			return false;
		}
		bool recorded = false;
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			if (player == null || !player.isHuman || victory.winner != player) {
				return;
			}
			recordedCampaignVictory = true;
			recorded = CampaignRecordScreen.RecordVictory(gameData, player, victory.victory, victory.finalScore);
		});
		return recorded;
	}

	// The Campaign Record over the game, as Civ3 shows it after a campaign
	// is won: without its Next and Hall of Fame buttons.
	private void ShowCampaignRecord(Action closed) {
		CanvasLayer layer = new() { Layer = 20 };
		CampaignRecordScreen screen = new(fromMainMenu: false);
		screen.Closed += () => {
			layer.QueueFree();
			closed();
		};
		layer.AddChild(screen);
		AddChild(layer);
	}

	public void OnSaveGame() {
		popupOverlay.OnHidePopup(); // hide game menu

		// FileDialog is a Window, not a Control, so we have the popup overlay present a blank control
		popupOverlay.ShowBlank();

		// A recorded game saves in its folder, as "<name> Turn <turn>".
		GameData gameData = EngineStorage.gameData;
		FileDialog.SetDirectoryForSaving(GameFolder(gameData), GameFolder(gameData) == null ? null : C7Engine.PlayLog.PlayLogFolders.SaveName(gameData));

		// TODO: sound -- see MainMenu.PlayButtonPressedSound();
		FileDialog.Popup();
	}

	// New Game (Ctrl-Shift-Q, or from the game menu): leaves this game for
	// the new game screens, once the player agrees to abandon it.
	public void AskToStartNewGame() {
		AskToAbandonGame(() => GetTree().ChangeSceneToFile("res://UIElements/NewGame/world_setup.tscn"));
	}

	// Quick Start from the game menu: a new game with the last game's setup
	// on a new map.
	public void AskToQuickStart() {
		AskToAbandonGame(() => {
			QuickStartSetup.Init(Global);
			GetTree().ChangeSceneToFile("res://C7Game.tscn");
		});
	}

	private void AskToAbandonGame(Action startAnother) {
		if (LanSession.IsActive) {
			return;
		}
		popupOverlay.OnHidePopup();
		popupOverlay.ShowPopup(new ConfirmationPopup(
			"Abandon this game? Anything not saved will be lost.",
			"Yes, start a new game.", "No, keep playing.", startAnother), PopupOverlay.PopupCategory.Info);
	}

	// Civ3's City Governor for one of the player's cities.
	public void ShowGovernor(City city) {
		popupOverlay.ShowPopup(new GovernorPopup(city), PopupOverlay.PopupCategory.Info);
	}

	// Civ3's Preferences (Ctrl-P, or from the game menu).
	public void ShowPreferences() {
		popupOverlay.ShowPopup(new PreferencesPopup(), PopupOverlay.PopupCategory.Info);
	}

	public void OnLoadGame() {
		popupOverlay.OnHidePopup(); // hide game menu

		// FileDialog is a Window, not a Control, so we have the popup overlay present a blank control
		popupOverlay.ShowBlank();

		FileDialog.SetDirectoryForLoadingSaves();

		// TODO: sound -- see MainMenu.PlayButtonPressedSound();
		FileDialog.Popup();
	}

	public void OnResolved() {
		popupOverlay.OnHidePopup();
	}

	public void _on_Zoom_value_changed(float value) {
		mapView.setCameraZoomFromMiddle(value);
	}

	public override void _Input(InputEvent @event) {
		if (editing) {
			return;
		}
		if (@event is InputEventKey e && e.Pressed && !e.IsAction(C7Action.UnitGoto)) {
			this.SetGotoMode(false);
		}

		// A unit drag ends when the button is released, even if a control or a modal takes the release before the map sees it. The check
		// is deferred so that a release the map does see finishes the drag first.
		if (draggingUnit && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) {
			Callable.From(AbandonUnitDrag).CallDeferred();
		}
	}

	public override void _UnhandledInput(InputEvent @event) {
		if (errorOnLoad) {
			return;
		}
		if (editing) {
			EditorUnhandledInput(@event);
			return;
		}
		// Don't handle if there's an open modal, if it's the AI's turn, or if
		// the screen is being handed to the next hotseat player.
		// A spectator may always look around.
		if (MovesMapUnderPopup(@event)) {
			return;
		}
		bool waiting = CurrentState == GameState.ComputerTurn && !LanSession.IsSpectator;
		if ((HasVisibleModal() && !IsModalSwitchEvent(@event)) || waiting || CurtainIsUp) {
			IsMovingCamera = false;
			return;
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

	// While a popup that lets the map move (Staging) is in front, the mouse
	// drags and zooms the map, and does nothing else. Returns whether it
	// took the event.
	private bool MovesMapUnderPopup(InputEvent @event) {
		if (!popupOverlay.LetsMapMove || CurtainIsUp
			|| cityScreen.Visible || diplomacy.Visible || advisor.Visible || gameViews.Visible || civilopedia.Visible) {
			return false;
		}
		if (@event is InputEventMouseButton button) {
			if (button.ButtonIndex == MouseButton.Left) {
				IsMovingCamera = button.IsPressed() && GetViewport().GuiGetHoveredControl() == null;
				OldPosition = button.Position;
			} else if (button.ButtonIndex == MouseButton.WheelUp && button.IsPressed()) {
				AdjustZoom(0.1f);
			} else if (button.ButtonIndex == MouseButton.WheelDown && button.IsPressed()) {
				AdjustZoom(-0.1f);
			}
			GetViewport().SetInputAsHandled();
			return true;
		}
		if (@event is InputEventMouseMotion motion) {
			if (IsMovingCamera) {
				mapView.cameraLocation += OldPosition - motion.Position;
				OldPosition = motion.Position;
			}
			GetViewport().SetInputAsHandled();
			return true;
		}
		return false;
	}

	private void HandleMouseButtonInput(InputEventMouseButton eventMouseButton) {
		if (CurrentState == GameState.ComputerTurn && !LanSession.IsSpectator) return;
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
		float newScale = mapView.cameraZoom + delta;
		mapView.setCameraZoom(newScale, GetViewport().GetMousePosition());
		GetViewport().SetInputAsHandled();
	}

	private void HandleLeftMouseButton(InputEventMouseButton eventMouseButton) {
		GetViewport().SetInputAsHandled();
		Control uiHover = GetViewport().GuiGetHoveredControl();
		// Can't drag the map when the mouse is over a ui element
		if (eventMouseButton.IsPressed() && uiHover is not TextureButton) {
			// As in Civ3, pressing on the selected unit and dragging picks
			// where it goes; it moves when the button is released.
			if (TryStartUnitDrag(eventMouseButton)) {
				return;
			}

			OldPosition = eventMouseButton.Position;
			IsMovingCamera = true;

			if (CanDoubleClick(eventMouseButton)) {
				AcceptPossibleDoubleClick(eventMouseButton);
			} else {
				HandleSingleClick(PositionToTile(eventMouseButton.Position));
			}
		} else {
			IsMovingCamera = false;
			if (draggingUnit) {
				FinishUnitDrag(eventMouseButton);
			}
		}
	}

	private bool TryStartUnitDrag(InputEventMouseButton eventMouseButton) {
		draggingUnit = false;
		if (bombardInfo != null || editorUnit != null || CurrentState != GameState.PlayerTurn || !IsMapUnitValid(CurrentlySelectedUnit)) {
			return false;
		}
		MapUnit unit = CurrentlySelectedUnit;
		if (unit.owner != controller || unit.location != PositionToTile(eventMouseButton.Position)
			|| !unit.GetAvailableActions().Contains(UnitAction.Goto)) {
			return false;
		}

		// Keep moving the whole stack if J was pressed first.
		SetGotoMode(true, gotoInfo?.wholeStack ?? false);
		draggingUnit = true;
		return true;
	}

	private void FinishUnitDrag(InputEventMouseButton eventMouseButton) {
		draggingUnit = false;
		Tile tile = PositionToTile(eventMouseButton.Position);
		bool released = gotoInfo != null && IsMapUnitValid(CurrentlySelectedUnit);
		if (released && Tile.IsTileValid(tile) && tile != CurrentlySelectedUnit.location) {
			gotoInfo = GetGotoInfo(tile);
			ResolveMovement(gotoInfo);
			SetGotoMode(false);
			return;
		}

		// Released where it started: that's an ordinary click on the tile.
		SetGotoMode(false);
		if (!Tile.IsTileValid(tile)) {
			return;
		}
		if (CanDoubleClick(eventMouseButton)) {
			AcceptPossibleDoubleClick(eventMouseButton);
		} else {
			HandleUnitSelectionTileClick(tile);
		}
	}

	// The tile pressed on and where, for a click that may turn out to be
	// the first of a double click. The click is only handled once it's
	// clear that it isn't, by which time the map may have been dragged, so
	// the tile is the one under the mouse when it was pressed.
	private Tile pendingClickTile;
	private Vector2 pendingClickPosition;

	// How far the mouse may move before a press is a drag of the map rather
	// than a click.
	private const float ClickSlop = 8;

	private void AcceptPossibleDoubleClick(InputEventMouseButton eventMouseButton) {
		pendingClickTile = PositionToTile(eventMouseButton.Position);
		pendingClickPosition = eventMouseButton.Position;
		doubleClickHandler.Accept(eventMouseButton);
	}

	// Ends a drag whose release the map didn't see, without moving the unit.
	private void AbandonUnitDrag() {
		if (draggingUnit) {
			SetGotoMode(false);
		}
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

		return gotoInfo == null && tile?.cityAtTile != null && (tile.cityAtTile.owner == controller || LanSession.IsSpectator);
	}

	// Called by the double click handler once a click turned out to be single.
	private void OnSingleLeftMouseButtonClick(InputEventMouseButton eventMouseButton) {
		Tile tile = pendingClickTile ?? PositionToTile(eventMouseButton.Position);
		pendingClickTile = null;
		HandleSingleClick(tile);
	}

	// Has the next click on the map pick a tile, which is given to the action.
	public void PickTile(Action<Tile> picked) {
		tilePicker = picked;
	}

	private void HandleSingleClick(Tile tile) {
		if (tilePicker != null) {
			Action<Tile> picker = tilePicker;
			tilePicker = null;
			if (Tile.IsTileValid(tile)) {
				picker(tile);
			}
		} else if (editorUnit != null) {
			PlaceEditorUnit(tile);
		} else if (gotoInfo != null) {
			this.ResolveMovement(gotoInfo);
			this.SetGotoMode(false);
		} else if (bombardInfo != null) {
			if (bombardInfo.CanTarget(tile, out var bombardTarget)) {
				bombardInfo.bombardTarget = bombardTarget;
				HandleBombardClick(bombardInfo, tile);
			}
			setBombard(null);
		} else {
			// Select unit on tile at mouse location
			HandleUnitSelectionTileClick(tile);
		}
	}

	private void OnDoubleLeftMouseButtonClick(InputEventMouseButton eventMouseButton) {
		Tile tile = pendingClickTile ?? PositionToTile(eventMouseButton.Position);
		pendingClickTile = null;
		if (tile?.cityAtTile != null && LanSession.IsSpectator && LanSession.SpectatorMayLookAs(tile.cityAtTile.owner)) {
			// A spectator looks at the game as the city's owner, so the city
			// screen and the advisors show that civilization; the map stays
			// as the spectator watches it.
			controller = tile.cityAtTile.owner;
			EngineStorage.uiControllerID = controller.id;
			LanSession.ShowSpectatorView(EngineStorage.gameData);
			mapView.InvalidateMap();
		}
		if (tile?.cityAtTile?.owner == controller) {
			EngineStorage.ReadGameData((GameData gameData) => {
				ShowCityScreenForCity(gameData, tile.cityAtTile);
			});
		}
	}

	private void HandleUnitSelectionTileClick(Tile tile) {
		if (!Tile.IsTileValid(tile)) {
			return;
		}

		// Pick the unit drawn on top of the stack, if it's ours. Failing that, a
		// top unit, never one carried by an army or a ship: the selected unit
		// if it's here, otherwise one that can still move.
		MapUnit drawn = mapView.DrawnUnitOn(EngineStorage.gameData, tile);
		if (drawn != null && drawn.owner == controller && !drawn.IsLoaded()) {
			SelectUnit(drawn, tile);
			return;
		}
		MapUnit unit = null;
		foreach (MapUnit u in tile.unitsOnTile) {
			if (u.owner != controller || u.IsLoaded()) {
				continue;
			}
			if (u == CurrentlySelectedUnit) {
				unit = u;
				break;
			}
			if (unit == null || (!unit.movementPoints.canMove && u.movementPoints.canMove)) {
				unit = u;
			}
		}
		if (unit == null) {
			return;
		}

		SelectUnit(unit, tile);
	}

	private void SelectUnit(MapUnit unit, Tile tile) {
		bool canMove = unitSelector.SetSelectedUnit(unit);

		if (unit.WorkerJob != null) {
			return;
		}

		if (!canMove && Tile.IsTileValid(tile)) {
			new MsgShowTemporaryPopup("This unit has already moved.", tile).send();
		}
	}

	public void SelectUnit(MapUnit unit) {
		SelectUnit(unit, unit.location);
	}

	private void HandleRightMouseButton(InputEventMouseButton eventMouseButton) {
		this.SetGotoMode(false);
		if (editorUnit != null) {
			editorUnit = null;
			return;
		}

		Tile tile = PositionToTile(eventMouseButton.Position);
		if (Tile.IsTileValid(tile)) {
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
		// Handle the shortcut of shift+right clicking a city to get the change production menu.
		else if (shiftDown && activeTile && tile.cityAtTile?.owner == controller)
			new RightClickChooseProductionMenu(this, tile.cityAtTile).Open(eventMouseButton.Position);
		else if (!shiftDown && activeTile && tile.UnitsSeenBy(controller).Count > 0)
			// There are units on this title, so open that menu.
			new RightClickTileMenu(this, tile).Open(eventMouseButton.Position);
		else if (!shiftDown && activeTile && tile.cityAtTile?.owner == controller)
			// There are no units, but this is the player's city.
			new RightClickCityMenu(this, tile).Open(eventMouseButton.Position);
		else
			ShowTileInfo(tile);

		LogTileDetails(tile);
	}

	// Opens the Espionage screen, with a foreign civ and one of its cities
	// picked if given, for the player at the screen to send missions from.
	public void ShowEspionageMissions(Player target = null, City city = null) {
		advisor.ShowEspionage(target?.id, city);
	}

	public void ShowTileInfo(Tile tile) {
		tileInfo = new TileInfo(tile);
		// The fog and cities around the tile are drawn differently under the box.
		mapView.InvalidateMap();
		var zoom = mapView.cameraZoom;
		var tileCenter = mapView.screenLocationOfTile(tile, true);
		var tileInfoPopup = new TileInfoPopup(this, tile, tileCenter, zoom);
		popupOverlay.ShowPopup(tileInfoPopup, PopupOverlay.PopupCategory.TileInfo);
	}

	public void HideTileInfo() {
		tileInfo = null;
		mapView.InvalidateMap();
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
			// Dragging the map isn't clicking on it.
			if (pendingClickTile != null && eventMouseMotion.Position.DistanceTo(pendingClickPosition) > ClickSlop) {
				pendingClickTile = null;
				doubleClickHandler.Cancel();
			}
			mapView.cameraLocation += OldPosition - eventMouseMotion.Position;
			OldPosition = eventMouseMotion.Position;
		} else if (gotoInfo != null) {
			// The path to the tile under the mouse is found once a frame, for wherever the mouse ended up; see _Process.
			gotoMousePosition = eventMouseMotion.Position;
		} else if (bombardInfo != null) {
			Tile tile = PositionToTile(eventMouseMotion.Position);
			bombardInfo.mouseTile = Tile.IsTileValid(tile) ? tile : null;
		}
	}

	private void HandleKeyboardInput(InputEventKey eventKeyDown) {
		if (HandleDebugKey(eventKeyDown) || HandleCiv3Key(eventKeyDown)) {
			return;
		}

		if (eventKeyDown.Keycode == Godot.Key.O && eventKeyDown.ShiftPressed && eventKeyDown.IsCommandOrControlPressed() && eventKeyDown.AltPressed) {
			if (!LanSession.IsActive) {
				ToggleObserverMode();
			}
		}

		// Civ3's hidden Soren advisor, a developer screen Civ3 itself never
		// opens (see SorenReport), behind a developer key.
		if (eventKeyDown.Keycode == Godot.Key.S && eventKeyDown.ShiftPressed && eventKeyDown.IsCommandOrControlPressed()
			&& eventKeyDown.AltPressed && !eventKeyDown.Echo && SorenAdvisor.IsAvailable()) {
			EmitSignal(SignalName.ShowSpecificAdvisor, C7Action.ShowSorenAdvisor);
			return;
		}

		int screen = AdvisorScreenFor(eventKeyDown);
		if (screen >= 0) {
			(string action, _, bool isAdvisor) = C7Action.AdvisorScreens[screen];
			EmitSignal(isAdvisor ? SignalName.ShowSpecificAdvisor : SignalName.ShowGameView, action);
		}

		// The same as the diplomacy button.
		if (eventKeyDown.Keycode == Godot.Key.D
			&& eventKeyDown.IsCommandOrControlPressed()
			&& eventKeyDown.ShiftPressed
			&& !eventKeyDown.AltPressed
			&& !eventKeyDown.Echo) {
			statusMenu.OpenDiplomacyPopup();
		}

		// New Game, as in Civ3.
		if (eventKeyDown.Keycode == Godot.Key.Q
			&& eventKeyDown.IsCommandOrControlPressed()
			&& eventKeyDown.ShiftPressed
			&& !eventKeyDown.AltPressed
			&& !eventKeyDown.Echo) {
			AskToStartNewGame();
		}

		// Ctrl-C opens the Civilopedia, as in Civ3.
		if (eventKeyDown.Keycode == Godot.Key.C
			&& eventKeyDown.IsCommandOrControlPressed()
			&& !eventKeyDown.ShiftPressed
			&& !eventKeyDown.AltPressed
			&& !eventKeyDown.Echo) {
			Civilopedia.Open();
			return;
		}

		// Plain C only: Shift-C clears forest, wetlands or damage.
		if (eventKeyDown.Keycode == Godot.Key.C && !eventKeyDown.ShiftPressed && !eventKeyDown.IsCommandOrControlPressed()
			&& HasCurrentlySelectedUnit()) {
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
			// Go to City (Civ3's Ctrl-Shift-G)
			else if (eventKeyDown.IsCommandOrControlPressed()
					   && eventKeyDown.ShiftPressed) {
				if (!eventKeyDown.Echo) {
					ProcessAction(C7Action.UnitGotoCity);
				}
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

		// Go-to for every unit on the selected unit's tile.
		if (eventKeyDown.Keycode == Godot.Key.J
			&& !eventKeyDown.IsCommandOrControlPressed()
			&& !eventKeyDown.ShiftPressed
			&& !eventKeyDown.AltPressed
			&& CurrentState == GameState.PlayerTurn
			&& IsMapUnitValid(CurrentlySelectedUnit)) {
			SetGotoMode(true, wholeStack: true);
		}

		// Show only the terrain and resources, hiding cities, units, roads and the like.
		if (eventKeyDown.Keycode == Godot.Key.M
			&& eventKeyDown.IsCommandOrControlPressed()
			&& eventKeyDown.ShiftPressed
			&& !eventKeyDown.AltPressed
			&& !eventKeyDown.Echo) {
			mapView.bareMap = !mapView.bareMap;
		}
	}

	// Whether Civ3's debug mode is on in this game (see C7Engine.DebugMode):
	// never in a LAN game.
	public bool DebugModeOn => !LanSession.IsActive && DebugMode.IsOn(EngineStorage.gameData);

	// Debug mode is the scenario's to turn on (its GAME "Debug Mode" flag),
	// or the --debug developer option's, which OpenCiv3 adds since its own
	// rules have no editor to set the flag with. Civ3 then lets the player
	// see everything (Civ3 FUN_004eba60 sets the see-all flag along with the
	// debug one), which the revealed map and UnitLayer show.
	private void StartDebugMode() {
		if (LanSession.IsActive) {
			return;
		}
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--debug") >= 0) {
			EngineStorage.ReadGameData((GameData gameData) => gameData.debugMode = true);
		}
		if (DebugModeOn) {
			log.Information("Debug mode is on");
			mapView.revealWholeMap = true;
		}
	}

	// Debug mode's keys on the map: Shift+F1 opens EDITORUNIT, and "$" adds
	// 100 gold (Civ3's map key handler, cases at 0x4e93e1 and 0x4ea5af).
	// Returns whether the key was one of them.
	private bool HandleDebugKey(InputEventKey key) {
		if (!DebugModeOn || key.Echo || HasVisibleModal() || CurrentState != GameState.PlayerTurn) {
			return false;
		}
		if (key.Keycode == Godot.Key.F1 && key.ShiftPressed && !key.IsCommandOrControlPressed() && !key.AltPressed) {
			editorUnitOwner = controller;
			ShowEditorUnitPopup();
			return true;
		}
		if (key.Unicode == '$') {
			new MsgDebugAddGold().send();
			return true;
		}
		return false;
	}

	// The unit EDITORUNIT puts on each tile clicked, until Escape or a
	// right click; null when not placing one.
	private record EditorUnit(Player owner, string type, string experienceKey);
	private EditorUnit editorUnit;

	// What EDITORUNIT has chosen. The owner starts as the player each time
	// it opens; the unit type and experience are remembered (Civ3
	// FUN_004ebed0, DAT_00a4abb8 and DAT_0074621c).
	private Player editorUnitOwner;
	private string editorUnitType;
	private string editorExperienceKey;

	// Civ3's EDITORUNIT popup (script.txt): every unit type, with buttons to
	// choose the owner and the experience level. Its "All" button flips a
	// flag (DAT_00a4abb4) that nothing reads, so here it only shows the list
	// again.
	private void ShowEditorUnitPopup() {
		List<string> types = new();
		EngineStorage.ReadGameData((GameData gameData) => {
			types = gameData.unitPrototypes.ConvertAll(p => p.name);
			editorExperienceKey ??= DebugMode.DefaultExperience(gameData)?.key;
		});
		popupOverlay.ShowPopup(new ListPopup("Select type of unit to create", types, types.IndexOf(editorUnitType),
			index => {
				editorUnitType = types[index];
				editorUnit = new EditorUnit(editorUnitOwner ?? controller, editorUnitType, editorExperienceKey);
			},
			[
				new ListPopup.Button("All", ShowEditorUnitPopup),
				new ListPopup.Button("Player", ShowEditorOwnerPopup),
				new ListPopup.Button("Experience", ShowEditorExperiencePopup),
			]), PopupOverlay.PopupCategory.Info);
	}

	// EDITORUNIT's Player button (Civ3 FUN_004ebbc0, "EDITORWHO"): the
	// players still in the game. Civ3's script.txt has no EDITORWHO entry,
	// so the caption is that of its EDITVEHWHO.
	private void ShowEditorOwnerPopup() {
		List<Player> owners = new();
		EngineStorage.ReadGameData((GameData gameData) => owners = DebugMode.UnitOwners(gameData));
		popupOverlay.ShowPopup(new ListPopup("Select Unit Owner", owners.ConvertAll(p => p.civilization.name),
			owners.IndexOf(editorUnitOwner ?? controller),
			index => {
				editorUnitOwner = owners[index];
				ShowEditorUnitPopup();
			}), PopupOverlay.PopupCategory.Info);
	}

	// EDITORUNIT's Experience button: the same list, of the experience
	// levels.
	private void ShowEditorExperiencePopup() {
		List<ExperienceLevel> levels = new();
		EngineStorage.ReadGameData((GameData gameData) => levels = gameData.experienceLevels.ToList());
		popupOverlay.ShowPopup(new ListPopup("Select type of unit to create", levels.ConvertAll(l => l.displayName),
			levels.FindIndex(l => l.key == editorExperienceKey),
			index => {
				editorExperienceKey = levels[index].key;
				ShowEditorUnitPopup();
			}), PopupOverlay.PopupCategory.Info);
	}

	// A click while placing: Civ3 puts the unit there unless Shift or Ctrl
	// is held (FUN_004ef820).
	private void PlaceEditorUnit(Tile tile) {
		if (!DebugModeOn || !Tile.IsTileValid(tile)) {
			editorUnit = null;
			return;
		}
		if (Input.IsKeyPressed(Godot.Key.Shift) || Input.IsKeyPressed(Godot.Key.Ctrl)) {
			return;
		}
		new MsgDebugCreateUnit(editorUnit.owner, editorUnit.type, editorUnit.experienceKey, tile).send();
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
		// Observers see the whole map.
		mapView.InvalidateMap();
	}

	// Whether the user is watching the AIs play on this machine (observer
	// mode outside a LAN game, whose spectators have their own news).
	private static bool WatchingAiGame => EngineStorage.gameData?.observerMode == true && !LanSession.IsActive;

	// The wars shown at each pause while watching, made when first needed.
	private WarList warList;

	private WarList WarListForWatching() {
		if (warList == null) {
			warList = new WarList();
			AddChild(warList);
		}
		return warList;
	}

	// The most turns observer mode plays between pauses.
	private const int MaxTurnsBetweenPauses = 2500;

	// The human players to restore when observer mode is turned off.
	private HashSet<ID> observerModeHumans = new();

	private void SetObserverModeOn(GameData gameData) {
		observerModeHumans = gameData.players.Where(p => p.isHuman).Select(p => p.id).ToHashSet();
		foreach (Player player in gameData.players) {
			player.isHuman = false;
		}
		animationController.SetAnimationsEnabled(false);
		popupOverlay.ShowPopup(
			new TextDialog($"Pause every how many turns? (up to {MaxTurnsBetweenPauses})",
							"Turns: ", "100",
							BoxContainer.AlignmentMode.Begin,
							(string turns) => {
								// Ignore what isn't a number of turns rather than throwing.
								if (int.TryParse(turns, out int n) && n >= 0) {
									n = Math.Min(n, MaxTurnsBetweenPauses);
									fastForwardInterval = n;
									turnsLeftToFastForward = n;
								}
								// Play on to the first pause straight away.
								if (CurrentState == GameState.PlayerTurn) {
									DoActualEndTurn();
								}
							}),
				PopupOverlay.PopupCategory.Advisor);
	}

	private void SetObserverModeOff(GameData gameData) {
		fastForwardInterval = 0;
		turnsLeftToFastForward = 0;
		warList?.HideAndContinue();
		foreach (Player player in gameData.players) {
			if (player.id == EngineStorage.uiControllerID || observerModeHumans.Contains(player.id)) {
				player.isHuman = true;
			}
		}
		animationController.ApplyPreferences();
	}

	private void ToggleGridCoordinates() {
		EngineStorage.ReadGameData((GameData gameData) => {
			gameData.showGridCoordinates = !gameData.showGridCoordinates;
		});
		mapView.InvalidateMap();
	}

	private void HandleMagnifyGesture(InputEventMagnifyGesture magnifyGesture) {
		double newScale = mapView.cameraZoom * magnifyGesture.Factor;

		mapView.setCameraZoom((float)newScale, magnifyGesture.Position);
	}

	// The input actions and their names, fetched once rather than every frame.
	private StringName[] inputActions;
	private string[] inputActionNames;

	private void ProcessActions() {
		if (inputActions == null) {
			Godot.Collections.Array<StringName> actions = InputMap.GetActions();
			inputActions = new StringName[actions.Count];
			inputActionNames = new string[actions.Count];
			for (int i = 0; i < actions.Count; i++) {
				inputActions[i] = actions[i];
				inputActionNames[i] = actions[i].ToString();
			}
		}

		// Keys typed into a text field are text, not commands.
		Control focused = GetViewport().GuiGetFocusOwner();
		bool typing = focused is LineEdit or TextEdit && focused.IsVisibleInTree();

		for (int i = 0; i < inputActions.Length; i++) {
			// Match modifiers exactly, so that Shift+Enter or Ctrl+L don't also
			// trigger the actions bound to plain Enter or L.
			if (!typing && Input.IsActionJustPressed(inputActions[i], exactMatch: true)) {
				ProcessAction(inputActionNames[i]);
			} else if (Input.IsActionJustReleased(inputActions[i])) {
				ProcessOnReleaseAction(inputActionNames[i]);
			}
		}
	}

	private void ProcessOnReleaseAction(string currentAction) {
		if (currentAction == C7Action.EnableTempAnimations) {
			animationController.EndTemporaryToggle();
		}
	}

	private bool HasVisibleModal() {
		if (popupOverlay.Visible || cityScreen.Visible || diplomacy.Visible || RightClickMenu.IsAnyOpen)
			return true;

		if (advisor.Visible || gameViews.Visible || civilopedia.Visible)
			return true;

		// A flow that has the screen, like the end of the game's pages (see
		// EndOfGame), between its popups and screens.
		if (EndOfGame.InProgress || popupOverlay.Claims.Any)
			return true;

		return false;
	}

	private bool IsModalSwitchEvent(InputEvent @event) {
		if (@event is InputEventKey eventKeyDown && eventKeyDown.Pressed) {
			return AdvisorScreenFor(eventKeyDown) >= 0;
		}

		return false;
	}

	// The index in C7Action.AdvisorScreens of the advisor or report screen a
	// key opens, or -1: F1 to F11 (the keys of the screens' input actions),
	// and on a Mac also Cmd+1 to Cmd+-, which MacShortcuts adds to the
	// actions. Those were never looked for, only the F keys themselves, so
	// on a Mac the Cmd keys did nothing (owner's report, 2026-10-12).
	private static int AdvisorScreenFor(InputEventKey key) {
		for (int i = 0; i < C7Action.AdvisorScreens.Length; i++) {
			if (key.IsAction(C7Action.AdvisorScreens[i].action)) {
				return i;
			}
		}
		return -1;
	}

	private void ProcessAction(string currentAction) {
		// Nothing happens behind the hotseat curtain.
		if (CurtainIsUp) {
			return;
		}

		// An open right-click menu takes the keys; Escape closes it.
		if (RightClickMenu.IsAnyOpen) {
			return;
		}

		if (currentAction == C7Action.Escape && tileInfo != null) {
			HideTileInfo();
			return;
		}

		if (currentAction == C7Action.Escape && popupOverlay.ShowingPopup) {
			popupOverlay.OnEscape();
			return;
		}

		if (currentAction == C7Action.Escape && civilopedia.Visible) {
			civilopedia.Hide();
			return;
		}

		if (currentAction == C7Action.Escape && cityScreen.Visible) {
			cityScreen.Hide();
			return;
		}

		if (currentAction == C7Action.Escape && advisor.Visible) {
			advisor.Hide();
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

		if (currentAction == C7Action.Escape && tilePicker != null) {
			tilePicker = null;
			return;
		}

		if (currentAction == C7Action.Escape && bombardInfo != null) {
			setBombard(null);
			return;
		}

		if (currentAction == C7Action.Escape && editorUnit != null) {
			editorUnit = null;
			return;
		}

		// never poll for actions if UI elements are visible, or in the frame
		// a popup closed (see PopupOverlay.HidThisFrame)
		if (HasVisibleModal() || popupOverlay.HidThisFrame) {
			return;
		}

		if (currentAction == C7Action.EndTurn && !this.HasCurrentlySelectedUnit()) {
			log.Verbose("end_turn key pressed");
			this.OnPlayerEndTurn();
		}

		if (currentAction == C7Action.EndTurnNow) {
			log.Verbose("end_turn_now key pressed");
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
			this.mapView.gridLayer.visible = !this.mapView.gridLayer.visible;
		}

		if (currentAction == C7Action.ToggleCoordinates) {
			ToggleGridCoordinates();
		}

		if (currentAction == C7Action.Escape && this.gotoInfo == null) {
			log.Debug("Got request for escape/quit");
			popupOverlay.ShowPopup(new EscapeQuitPopup(), PopupOverlay.PopupCategory.Info);
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

		// Holding the key flips animations until it's released.
		if (currentAction == C7Action.EnableTempAnimations) {
			animationController.BeginTemporaryToggle();
		}

		if (currentAction == C7Action.ShowPreferences) {
			ShowPreferences();
		}

		// actions with unit buttons, which are only relevant during the player
		// turn.
		if (CurrentState != GameState.PlayerTurn) {
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

		if (!IsMapUnitValid(CurrentlySelectedUnit)) return;

		if (currentAction == C7Action.UnitHold) {
			new MsgUnitCommand(CurrentlySelectedUnit.id, MsgUnitCommand.Command.SkipTurn).send();
		}

		if (currentAction == C7Action.UnitWait) {
			UnitInteractions.waitUnit(CurrentlySelectedUnit.id);
			unitSelector.SetNextUnit();
		}

		if (currentAction == C7Action.UnitFortify) {
			new MsgSetFortification(CurrentlySelectedUnit.id, true).send();
		}

		if (currentAction == C7Action.UnitDisband) {
			// The unit asked about, not whichever is selected once confirmed.
			ID disbandedID = CurrentlySelectedUnit.id;
			popupOverlay.ShowPopup(
				new ConfirmationPopup(
					$"Disband {CurrentlySelectedUnit.name}? Pardon me but these are OUR people.\nDo you really want to disband them?",
					"Yes, we need to!",
					"No. Maybe you are right, advisor.",
					() => {
						new MsgUnitCommand(disbandedID, MsgUnitCommand.Command.Disband).send();
					}),
				PopupOverlay.PopupCategory.Advisor);
		}

		// unit_goto's behavior is more complicated than other actions - it
		// toggles the go to state, but must be detoggled in _*Input methods if
		// it is not the input being pressed.
		if (currentAction == C7Action.UnitGoto) {
			this.SetGotoMode(true);
		}

		if (currentAction == C7Action.UnitGotoCity) {
			this.ShowGotoCityPopup(CurrentlySelectedUnit);
		}

		if (currentAction == C7Action.UnitExplore) {
			new MsgUnitCommand(CurrentlySelectedUnit.id, MsgUnitCommand.Command.Explore).send();
		}

		if (currentAction == C7Action.UnitAutomate) {
			new MsgUnitCommand(CurrentlySelectedUnit.id, MsgUnitCommand.Command.Automate).send();
		}

		// Civ3's other kinds of worker automation; road and rail to pick
		// their destination as a go-to does.
		if (C7Action.ToWorkerAutomation(currentAction) is WorkerAutomation.Kind automation
			&& C7Action.CanAutomate(CurrentlySelectedUnit, automation)) {
			if (automation is WorkerAutomation.Kind.RoadTo or WorkerAutomation.Kind.RailTo) {
				SetGotoMode(true, workerAutomation: automation);
			} else {
				new MsgAutomateWorker(CurrentlySelectedUnit.id, automation).send();
			}
		}

		if (currentAction == C7Action.UnitSentry) {
			new MsgSentry(CurrentlySelectedUnit.id, enemyOnly: false).send();
		}

		if (currentAction == C7Action.UnitSentryEnemyOnly) {
			new MsgSentry(CurrentlySelectedUnit.id, enemyOnly: true).send();
		}

		if (currentAction == C7Action.UnitBuildCity && CurrentlySelectedUnit.canBuildCity()) {
			EngineStorage.ReadGameData((GameData gameData) => {
				MapUnit currentUnit = gameData.GetUnit(CurrentlySelectedUnit.id);
				log.Debug(currentUnit.Describe());
				if (currentUnit.canBuildCity()) {
					Action nameCity = () => popupOverlay.ShowPopup(new BuildCityDialog(controller.GetNextCityName()),
						PopupOverlay.PopupCategory.Advisor);
					// In the land of a civ at peace with us, founding a city
					// means war, so the Foreign Advisor asks first ("this
					// will cause war with the $CIVADJ0 people", script.txt
					// DECLARE_WAR; compendium 6.10). The engine declares the
					// war when the city is founded.
					if (currentUnit.location.PeacefulOwnerFor(controller) is Player landOwner) {
						popupOverlay.ShowPopup(new WarConfirmation(landOwner, nameCity), PopupOverlay.PopupCategory.Advisor);
					} else {
						nameCity();
					}
				}
			});
		}

		if (currentAction == C7Action.UnitBombard) {
			if (CurrentlySelectedUnit.HasBombardAbility()) {
				EngineStorage.ReadGameData((GameData gameData) => {
					MapUnit currentUnit = gameData.GetUnit(CurrentlySelectedUnit.id);
					setBombard(currentUnit);
				});
			} else if (CurrentlySelectedUnit.GetAvailableActions().Contains(UnitAction.Bombing)) {
				// B is Civ3's key for both bombarding and bombing missions.
				ProcessAction(C7Action.UnitBombing);
			}
		}

		// Air missions: air superiority is ordered at once; the others,
		// like a bombardment, wait for a target tile to be clicked (see
		// HandleSingleClick).
		if (C7Action.ToUnitAction(currentAction) is UnitAction airAction
			&& (airAction == UnitAction.Intercept || C7Action.IsTargetedAirMission(airAction))
			&& CurrentlySelectedUnit.GetAvailableActions().Contains(airAction)) {
			if (airAction == UnitAction.Intercept) {
				new MsgAirMission(CurrentlySelectedUnit.id, UnitAction.Intercept, null).send();
			} else {
				EngineStorage.ReadGameData((GameData gameData) => {
					MapUnit currentUnit = gameData.GetUnit(CurrentlySelectedUnit.id);
					setBombard(currentUnit, airAction);
				});
			}
		}


		// Automated bombarding: Shift-B (an aircraft's bombing), or its
		// buttons, then the target; Shift-P, the pillage key, for an
		// aircraft's precision bombing.
		MapUnit.AutoBombardMode? autoMode = currentAction switch {
			C7Action.UnitAutoBombard => CurrentlySelectedUnit.CanAutomateBombard(MapUnit.AutoBombardMode.Bombard)
				? MapUnit.AutoBombardMode.Bombard : MapUnit.AutoBombardMode.Bombing,
			C7Action.UnitAutoBombing => MapUnit.AutoBombardMode.Bombing,
			C7Action.UnitAutoPrecisionBombing or C7Action.UnitPillage => MapUnit.AutoBombardMode.PrecisionBombing,
			_ => null,
		};
		if (autoMode is MapUnit.AutoBombardMode mode
			&& CurrentlySelectedUnit.GetAvailableActions().Contains(MapUnit.ActionFor(mode))) {
			EngineStorage.ReadGameData((GameData gameData) => {
				MapUnit currentUnit = gameData.GetUnit(CurrentlySelectedUnit.id);
				setBombard(currentUnit, null, mode);
			});
		}

		if (currentAction == C7Action.UnitPillage && CurrentlySelectedUnit.CanPillage()) {
			new MsgPillage(CurrentlySelectedUnit.id).send();
		}

		// B builds a city, or with a worker a colony: "Build/Join city/colony
		// B" (Conquests Civilopedia, the keyboard shortcuts).
		if ((currentAction == C7Action.UnitBuildColony || (currentAction == C7Action.UnitBuildCity && !CurrentlySelectedUnit.canBuildCity()))
			&& CurrentlySelectedUnit.CanBuildColony()) {
			new MsgUnitCommand(CurrentlySelectedUnit.id, MsgUnitCommand.Command.BuildColony).send();
		}

		// In a city, B joins it instead: "Join City (B)" (labels.txt,
		// #UNIT_ACTIONS).
		if ((currentAction == C7Action.UnitJoinCity || (currentAction == C7Action.UnitBuildCity && !CurrentlySelectedUnit.canBuildCity()))
			&& CurrentlySelectedUnit.CanJoinCity()) {
			new MsgUnitCommand(CurrentlySelectedUnit.id, MsgUnitCommand.Command.JoinCity).send();
		}

		if (currentAction == C7Action.UnitBuildArmy && CurrentlySelectedUnit.CanFormArmy()) {
			new MsgUnitCommand(CurrentlySelectedUnit.id, MsgUnitCommand.Command.FormArmy).send();
		}

		if (currentAction == C7Action.UnitScienceAge && CurrentlySelectedUnit.CanStartScienceAge()) {
			new MsgUnitCommand(CurrentlySelectedUnit.id, MsgUnitCommand.Command.StartScienceAge).send();
		}

		if (currentAction == C7Action.UnitSacrifice && CurrentlySelectedUnit.CanSacrifice()) {
			// Civ3 asks first (script.txt, #SACRIFICE: the domestic advisor).
			// The unit asked about, not whichever is selected once confirmed.
			ID sacrificedID = CurrentlySelectedUnit.id;
			popupOverlay.ShowPopup(
				new ConfirmationPopup(
					$"Sacrifice {CurrentlySelectedUnit.name}?\nI know these people aren't native to our land, but do you really want to sacrifice them?\nSacrificing them will gain your city {CurrentlySelectedUnit.SacrificeValue()} points of culture.",
					"Yes, let the blood flow!",
					"No. Maybe you are right, advisor.",
					() => {
						new MsgUnitCommand(sacrificedID, MsgUnitCommand.Command.Sacrifice).send();
					}),
				PopupOverlay.PopupCategory.Advisor);
		}

		if (currentAction == C7Action.UnitHurryBuilding && CurrentlySelectedUnit.CanOfferHurryProduction()) {
			string blocker = CurrentlySelectedUnit.HurryProductionBlocker();
			if (blocker == null) {
				new MsgUnitCommand(CurrentlySelectedUnit.id, MsgUnitCommand.Command.HurryProduction).send();
			} else {
				popupOverlay.ShowPopup(new InformationalPopup(blocker, AdvisorHead.Advisor.Military, AdvisorHead.Mood.Angry),
					PopupOverlay.PopupCategory.Advisor);
			}
		}

		if (currentAction == C7Action.UnitLoad) {
			ChooseTransportToBoard(CurrentlySelectedUnit);
		}
		// Upgrading and unloading share a key; upgrading wins when possible.
		if (currentAction == C7Action.UnitUpgrade) {
			ConfirmUpgrade(CurrentlySelectedUnit);
		}
		if (currentAction == C7Action.UnitUnload && CurrentlySelectedUnit.GetAvailableUpgrade() == null) {
			ChooseUnitsToUnload(CurrentlySelectedUnit);
		}

		Terraform terraform = C7Action.ToTerraform(currentAction);

		// Shift-C is Civ3's key for all three clearing jobs (labels.txt,
		// #UNIT_ACTIONS), and does whichever the tile needs, damage first, as
		// the Civilopedia's "Clear Pollution/Forest/Jungle/Marsh  Shift-C"
		// lists it.
		if (currentAction == C7Action.UnitClearForest) {
			terraform = new[] { C7Action.UnitClearDamage, C7Action.UnitClearForest, C7Action.UnitClearWetlands }
				.Select(C7Action.ToTerraform)
				.FirstOrDefault(tf => tf != null && CurrentlySelectedUnit.CanPerformTerraformAction(tf));
		}

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
			ID workerID = CurrentlySelectedUnit.id;
			popupOverlay.ShowPopup(
				new ConfirmationPopup(
					$"A previous terrain enhancement ({replacementTarget.key.Capitalize()}) will be replaced \nby this operation. Do you wish to continue?",
					"Continue.",
					"Cancel action.",
					() => {
						new MsgStartWorkerJob(workerID, terraform).send();
					}),
				PopupOverlay.PopupCategory.Advisor);
			return;
		}
		new MsgStartWorkerJob(CurrentlySelectedUnit.id, terraform).send();
	}

	private void ConfirmUpgrade(MapUnit unit) {
		UnitPrototype upgrade = unit.GetAvailableUpgrade();
		if (upgrade == null) {
			return;
		}

		int cost = unit.UpgradeCost(upgrade);
		if (unit.owner.gold < cost) {
			popupOverlay.ShowPopup(
				new InformationalPopup(
					$"Upgrading our {unit.name} to {upgrade.name} would cost {cost} gold.\nWe only have {unit.owner.gold}.",
					AdvisorHead.Advisor.Domestic, AdvisorHead.Mood.Surprised),
				PopupOverlay.PopupCategory.Advisor);
			return;
		}

		popupOverlay.ShowPopup(
			new ConfirmationPopup(
				$"Upgrade our {unit.name} to {upgrade.name} for {cost} gold?",
				"Yes, upgrade it.",
				"No, not now.",
				() => { new MsgUpgradeUnit(unit.id).send(); }),
			PopupOverlay.PopupCategory.Advisor);
	}

	// Civ3's Go to City (Ctrl-Shift-G; script.txt's GOTO_CITY, "Goto
	// City"): a list of our cities the unit can reach, with the turns to
	// each as the go-to cursor shows them. A click selects a city, and a
	// second click on it (or Enter, or the confirm button) sends the unit
	// there the way a go-to would.
	private void ShowGotoCityPopup(MapUnit selected) {
		List<City> cities = [];
		List<string> items = [];
		EngineStorage.ReadGameData((GameData gameData) => {
			MapUnit unit = gameData.GetUnit(selected.id);
			if (unit == null || unit.owner != controller) {
				return;
			}
			foreach ((City city, TilePath _, int turns) in UnitInteractions.GotoCityChoices(unit)) {
				cities.Add(city);
				items.Add($"{city.name} ({turns} {(turns == 1 ? "turn" : "turns")})");
			}
		});
		if (cities.Count == 0) {
			return;
		}
		popupOverlay.ShowPopup(new ListPopup("Goto City", items, -1, index => {
			SetGotoMode(false);
			gotoInfo = GetGotoInfo(cities[index].location);
			ResolveMovement(gotoInfo);
			SetGotoMode(false);
		}) { clickTwice = true }, PopupOverlay.PopupCategory.Info);
	}

	// Whether the player is dragging the selected unit to pick its destination.
	private bool draggingUnit = false;

	private void SetGotoMode(bool isOn, bool wholeStack = false, WorkerAutomation.Kind? workerAutomation = null) {
		this.lastTile = null;
		if (isOn) {
			this.gotoInfo = new() { wholeStack = wholeStack, workerAutomation = workerAutomation };
		} else {
			this.gotoInfo = null;
			this.draggingUnit = false;
		}
	}

	// Loads the unit, asking which transport when more than one on its tile
	// could take it, as Civ3 does (script.txt's SELECT_TRANSPORT: "What
	// Transport?", "Select a transport to board:"), with how full each is.
	private void ChooseTransportToBoard(MapUnit unit) {
		List<MapUnit> transports = null;
		EngineStorage.ReadGameData((GameData gameData) => {
			transports = unit.TransportsToBoard(unit.location, explicitLoad: true);
		});
		if (transports == null || transports.Count <= 1) {
			new MsgLoadToTransport(unit.id).send();
			return;
		}
		ID unitID = unit.id;
		List<ChoicePopup.Choice> choices = transports.ConvertAll(t => {
			ID transportID = t.id;
			return new ChoicePopup.Choice($"{t.name} ({t.PassengerCount()}/{t.Capacity()})",
				() => new MsgLoadToTransport(unitID, transportID).send());
		});
		popupOverlay.ShowPopup(new ChoicePopup("What Transport?", "Select a transport to board:", choices),
			PopupOverlay.PopupCategory.Advisor);
	}

	// Unloads the transport, asking which of several units aboard, as Civ3
	// does (script.txt's DISEMBARK: "Disembark?", with "Never mind.",
	// "Unload all." and then the units).
	private void ChooseUnitsToUnload(MapUnit transport) {
		List<MapUnit> passengers = null;
		EngineStorage.ReadGameData((GameData gameData) => {
			passengers = transport.Passengers();
		});
		ID transportID = transport.id;
		if (passengers == null || passengers.Count <= 1) {
			new MsgUnloadTransport(transportID).send();
			return;
		}
		List<ChoicePopup.Choice> choices = [
			new("Never mind.", () => { }),
			new("Unload all.", () => new MsgUnloadTransport(transportID).send()),
		];
		foreach (MapUnit passenger in passengers) {
			ID passengerID = passenger.id;
			choices.Add(new(passenger.Describe(), () => new MsgUnloadTransport(transportID, passengerID).send()));
		}
		popupOverlay.ShowPopup(new ChoicePopup("Disembark?", null, choices), PopupOverlay.PopupCategory.Advisor);
	}

	private void setBombard(MapUnit bombardingUnit, UnitAction? airMission = null,
			MapUnit.AutoBombardMode autoMode = MapUnit.AutoBombardMode.None) {
		bombardInfo = bombardingUnit == null ? null : new BombardInfo(bombardingUnit, airMission, autoMode);
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

		if (info.moveCost == -1) {
			return;
		}

		if (info.workerAutomation is WorkerAutomation.Kind automation) {
			new MsgAutomateWorker(info.unitID ?? CurrentlySelectedUnit.id, automation, info.destinationTile).send();
			return;
		}

		EngineStorage.ReadGameData((GameData gameData) => {
			// If this move would require declaring war, display a popup that checks
			// if the player really wants to declare war. If they do, declare the
			// war for them, clear out the player, and call this method again.
			if (info.requiresWarDeclarationOnPlayer != null) {
				GotoInfo stashed = info;
				stashed.unitID ??= CurrentlySelectedUnit.id;
				this.MaybeDeclareWar(stashed.requiresWarDeclarationOnPlayer, gameData.turn, () => {
					stashed.requiresWarDeclarationOnPlayer = null;
					this.ResolveMovement(stashed);
					this.SetGotoMode(false);
				});
			} else if (info.wholeStack) {
				MoveStack(gameData, info);
			} else if (!MaybeChooseStealthTarget(gameData, info)) {
				new MsgSetUnitPath(info.unitID ?? CurrentlySelectedUnit.id, info.path).send();
			}
		});
	}

	// "As soon as a unit with stealth capability attacks a stack of enemy
	// units, a selection box appears listing the target units" (Civilopedia,
	// GCON_Stealth_Attack). Asks which unit to attack, and attacks it, if the
	// move is a stealth attack on the next tile; returns whether it was. The
	// wording is script.txt's SELECT_ASSASSIN.
	private bool MaybeChooseStealthTarget(GameData gameData, GotoInfo info) {
		MapUnit unit = gameData.GetUnit(info.unitID ?? CurrentlySelectedUnit?.id);
		Tile tile = info.destinationTile;
		if (unit == null || tile == null || unit.location == null || unit.location.DistanceTo(tile) != 1) {
			return false;
		}
		List<MapUnit> targets = unit.StealthAttackTargets(tile);
		if (targets.Count == 0) {
			return false;
		}
		ID unitID = unit.id;
		TileDirection dir = unit.location.DirectionTo(tile);
		List<ChoicePopup.Choice> choices = targets.ConvertAll(target => {
			ID targetID = target.id;
			return new ChoicePopup.Choice(target.Describe(), () => new MsgMoveUnit(unitID, dir) { stealthTarget = targetID }.send());
		});
		popupOverlay.ShowPopup(new ChoicePopup("What should be targeted?", "Select a target for attack:", choices),
			PopupOverlay.PopupCategory.Advisor);
		return true;
	}

	// Sends the selected unit along its path and every other unit of ours on
	// its tile that can move to the same destination, each along its own
	// path (see UnitInteractions.StackMoveOrders).
	private void MoveStack(GameData gameData, GotoInfo info) {
		MapUnit leader = gameData.GetUnit(info.unitID ?? CurrentlySelectedUnit.id);
		if (leader == null) {
			return;
		}
		List<(ID, TilePath)> moves = UnitInteractions.StackMoveOrders(leader, info.path, info.destinationTile)
			.ConvertAll(move => (move.unit.id, move.path));
		foreach ((ID id, TilePath path) in moves) {
			new MsgSetUnitPath(id, path).send();
		}
	}

	private void MaybeDeclareWar(Player player, int currentTurn, Action callback) {
		popupOverlay.ShowPopup(new WarConfirmation(player,
			() => {
				new MsgDeclareWar(player).send();
				callback();
			}), PopupOverlay.PopupCategory.Advisor);
	}

	// Asks to declare war on each of the players from the index on, one after
	// another, and calls back once all of them are confirmed. Declining any
	// of them cancels the rest.
	private void ConfirmWarDeclarations(List<Player> players, int index, int currentTurn, Action callback) {
		if (index >= players.Count) {
			callback();
			return;
		}
		MaybeDeclareWar(players[index], currentTurn, () => {
			ConfirmWarDeclarations(players, index + 1, currentTurn, callback);
		});
	}

	// Where the mouse moved to in goto mode since the path to it was last found. Finding a path can be costly, and the mouse can cross several
	// tiles in a frame.
	private Vector2? gotoMousePosition = null;

	private void UpdateGotoPath() {
		if (gotoMousePosition is Vector2 position) {
			gotoMousePosition = null;
			if (gotoInfo != null) {
				gotoInfo = GetGotoInfo(position);
			}
		}
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
		GotoInfo result = new() { wholeStack = this.gotoInfo?.wholeStack ?? false, workerAutomation = this.gotoInfo?.workerAutomation };

		EngineStorage.ReadGameData((GameData gameData) => {
			if (tile == this.lastTile && this.gotoInfo != null) {
				result = this.gotoInfo;
				return;
			}
			this.lastTile = result.destinationTile = tile;

			// Figure out what unit is in goto mode. If the tile we're hovering over is
			// different than the tile the unit is on, calculate the path to move there.
			MapUnit unit = !Tile.IsTileValid(tile) || !IsMapUnitValid(CurrentlySelectedUnit) ? null : gameData.GetUnit(CurrentlySelectedUnit.id);

			// Units like the Bomber don't have a go-to action
			if (unit != null && !unit.GetAvailableActions().Contains(UnitAction.Goto)) {
				result = null;
			} else if (unit != null && unit.location != tile) {
				result.path = PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, tile, unit);
				result.moveCost =
					result.path.PathCost(unit.owner, unit.location, unit.MaxMovementPoints(), unit.movementPoints.remaining, unit.unitType);
				result.pathCoords = result.path.GetPathCoords();

				// If we couldn't path onto the tile, but the tile is next to us and
				// we could enter the tile if combat is allowed (or if we could
				// declare war with the move) mark the path.
				var distanceToTile = unit.location.DistanceTo(tile);
				var canEnterForcefully = unit.CanEnterForcefully(tile, out Intent intent);
				result.intent = intent;

				if (distanceToTile == 1 && canEnterForcefully && unit.unitType.CanMoveBetween(unit.location, tile)) {
					Queue<Tile> pathQueue = new();
					pathQueue.Enqueue(tile);

					result.path = new TilePath(tile, pathQueue);
					result.moveCost = result.path.PathCost(unit.owner, unit.location, unit.MaxMovementPoints(),
						unit.movementPoints.remaining, unit.unitType);
					result.pathCoords = result.path.GetPathCoords();

					// If we couldn't enter this tile without a war declaration,
					// record which civ we need to declare war on.
					if (intent == Intent.WarDeclaration) {
						result.attackingMove = true;
						if (tile.cityAtTile != null) {
							result.requiresWarDeclarationOnPlayer = tile.cityAtTile.owner;
						} else {
							// The owner of the first unit we see there, not of
							// an invisible one we don't.
							result.requiresWarDeclarationOnPlayer = tile.UnitsSeenBy(unit.owner).FirstOrDefault(u => u.owner != unit.owner)?.owner
								?? tile.unitsOnTile[0].owner;
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

	private void HandleBombardClick(BombardInfo info, Tile tile) {
		if (info == null || !Tile.IsTileValid(tile)) {
			return;
		}
		// The unit bombarding, not whichever is selected once war is declared.
		ID bombarderID = info.bombardingUnit.id;

		// Automated bombarding never needs a war declared (see CanTarget).
		if (info.autoMode != MapUnit.AutoBombardMode.None) {
			new MsgAutoBombard(bombarderID, info.autoMode, tile).send();
			return;
		}

		if (info.airMission is UnitAction mission) {
			EngineStorage.ReadGameData((GameData gameData) => {
				// Only bombing runs attack; the other missions are peaceful.
				bool attack = mission == UnitAction.Bombing || mission == UnitAction.PrecisionBombing;
				if (attack && info.RequiresWarDeclaration(tile, out var victim)) {
					MaybeDeclareWar(victim, gameData.turn, () => {
						new MsgAirMission(bombarderID, mission, tile).send();
					});
				} else if (mission == UnitAction.Rebase && info.wholeStack) {
					foreach (MapUnit plane in UnitInteractions.StackRebasers(gameData.GetUnit(bombarderID), tile)) {
						new MsgAirMission(plane.id, mission, tile).send();
					}
				} else {
					new MsgAirMission(bombarderID, mission, tile).send();
				}
			});
			return;
		}

		EngineStorage.ReadGameData((GameData gameData) => {
			// A nuke goes to war with every civ it hits, not only the target
			// tile's owner, so ask about each of them in turn.
			if (info.bombardingUnit.IsNuclearWeapon()) {
				List<Player> wars = info.bombardTarget == MapUnit.BombardTarget.None
					? []
					: info.bombardingUnit.NuclearStrikeWarDeclarations(tile);
				ConfirmWarDeclarations(wars, 0, gameData.turn, () => {
					new MsgBombard(bombarderID, tile).send();
				});
				return;
			}

			if (info.RequiresWarDeclaration(tile, out var player)) {
				MaybeDeclareWar(player, gameData.turn, () => {
					new MsgBombard(bombarderID, tile).send();
				});
			} else {
				new MsgBombard(bombarderID, tile).send();
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
		if (IsMapUnitValid(CurrentlySelectedUnit))
			new MsgBuildCity(CurrentlySelectedUnit, name) {
				capitalGovernorIsDefault = Preferences.CapitalGovernorIsDefaultForNewCities,
			}.send();
	}

	// Shows another civ's city on the city screen (the capital a new embassy
	// reports on, or an investigated city), with the tiles around it as they
	// are now, until the screen is closed.
	private void ShowForeignCityScreen(City capital) {
		Player viewer = controller;
		viewer.tileKnowledge.Peek(Espionage.CityRadius(capital));
		mapView.InvalidateMap();
		EngineStorage.ReadGameData((GameData gameData) => {
			cityScreen.ShowForeignCity(gameData, capital, () => {
				viewer.tileKnowledge.EndPeek();
				mapView.InvalidateMap();
			}, (GameData newGameData, City newCapital) => {
				// A LAN snapshot replaced the players and their knowledge,
				// so look again with the new ones.
				Player newViewer = newGameData.GetPlayer(viewer.id);
				if (newViewer == null) {
					return;
				}
				viewer = newViewer;
				viewer.tileKnowledge.Peek(Espionage.CityRadius(newCapital));
				mapView.InvalidateMap();
			});
		});
	}

	public void ShowCityScreenForCity(GameData gameData, City city) {
		city.RecalculateCitizenMoods(gameData);
		EmitSignal(SignalName.ShowCityScreen, new ParameterWrapper<City>(city));
	}

	public void OnDiplomacySelected(ParameterWrapper<ID> opponentPlayer) {
		diplomacy.ShowTalkScreenForPlayer(controller.id, opponentPlayer.Value);
	}
}
