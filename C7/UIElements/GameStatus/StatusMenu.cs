using Godot;
using C7GameData;
using C7Engine;

[GlobalClass]
public partial class StatusMenu : Control {
	[Signal] public delegate void ShowGameViewEventHandler();

	[Export] ConsoleButton openDiplomacy;
	[Export] ConsoleButton openEspionage;
	[Export] ConsoleButton openPalaceScreen;
	// Civ3's V and S buttons, and a G button for the statistics screen,
	// which isn't Civ3's.
	[Export] ConsoleButton openVictoryStatus;
	[Export] ConsoleButton openSpaceship;
	[Export] ConsoleButton openStatistics;

	[Export] PopupOverlay popupOverlay;

	public override void _Ready() {
		openDiplomacy.Pressed += OpenDiplomacyPopup;
		openEspionage.Pressed += OpenEmbassyPopup;
		openPalaceScreen.Pressed += () => {
			EmitSignal(SignalName.ShowGameView, C7Action.ShowPalaceView);
		};
		openVictoryStatus.Pressed += () => EmitSignal(SignalName.ShowGameView, C7Action.ShowVictoryStatusView);
		openSpaceship.Pressed += () => EmitSignal(SignalName.ShowGameView, C7Action.ShowSpaceRaceView);
		openStatistics.Pressed += () => EmitSignal(SignalName.ShowGameView, GameViews.ShowStatisticsView);
	}

	// What the buttons were last set to, so they are only changed when that
	// changes, and the player shown, so they aren't looked up every frame.
	private GameData shownGameData;
	private ID shownControllerId;
	private Player shownPlayer;
	private bool buttonsSet = false;
	private bool diplomacyShown;
	private bool palaceShown;

	public override void _Process(double delta) {
		GameData gD = EngineStorage.gameData;
		if (gD == null || gD.observerMode) {
			return;
		}

		if (!ReferenceEquals(gD, shownGameData) || shownControllerId != EngineStorage.uiControllerID || shownPlayer == null) {
			shownGameData = gD;
			shownControllerId = EngineStorage.uiControllerID;
			shownPlayer = gD.GetUIControllerPlayer();
		}

		// Only show the diplomacy button if we have civs to talk to.
		//
		// After meeting a civ and revealed the button,
		// if that civ gets destroyed and we don't have any more relationships
		// with other civs (haven't met them yet) we should hide the button again.
		// It will be shown again when we meet another civ.
		bool showDiplomacy = shownPlayer.playerRelationships.Count > 0;
		// The palace screen is for a civ with a palace, or a city that may
		// start one.
		bool showPalace = shownPlayer.cities.Count > 0 && shownPlayer.CanStartPalace(gD);
		if (buttonsSet && showDiplomacy == diplomacyShown && showPalace == palaceShown) {
			return;
		}
		buttonsSet = true;
		diplomacyShown = showDiplomacy;
		palaceShown = showPalace;

		// The victory, spaceship and statistics screens are always there.
		openVictoryStatus.ShowButton();
		openSpaceship.ShowButton();
		openStatistics.ShowButton();

		// The embassies are only for civs we've met, too.
		if (showDiplomacy) {
			openDiplomacy.ShowButton();
			openEspionage.ShowButton();
		} else {
			openDiplomacy.HideButton();
			openEspionage.HideButton();
		}

		if (showPalace) {
			openPalaceScreen.ShowButton();
		} else {
			openPalaceScreen.HideButton();
		}
	}

	// Does nothing while the diplomacy button is hidden, so that its
	// shortcut doesn't open an empty list.
	public void OpenDiplomacyPopup() {
		if (!diplomacyShown) {
			return;
		}
		EngineStorage.ReadGameData((GameData gD) => {
			Player player = gD.GetUIControllerPlayer();

			popupOverlay.ShowPopup(new DiplomacySelection(player, gD.players), PopupOverlay.PopupCategory.Info);
		});
	}

	// Civ3's Espionage screen.
	private void OpenEmbassyPopup() {
		(Owner as Game)?.ShowEspionageMissions();
	}
}
