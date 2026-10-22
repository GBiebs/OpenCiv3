using Godot;
using Serilog;
using C7Engine;

/**
 * Handles managing the advisor screens.
 * Showing them, hiding them... maybe some other things eventually.
 * This is part of the effort to de-centralize from Game.cs and be more event driven.
 */
public partial class GameViews : CenterContainer {
	private ILogger log = LogManager.ForContext<GameViews>();

	[Export] public WondersView wondersView;
	[Export] public VictoryStatusView victoryStatusView;
	[Export] public PalaceView palaceView;
	[Export] public SpaceRaceView spaceRaceView;
	[Export] public DemographicsView demographicsView;

	// Built in code rather than in the scene.
	public StatisticsView statisticsView { get; private set; }

	public override void _Ready() {
		statisticsView = new StatisticsView();
		AddChild(statisticsView);
		statisticsView.Hide();
		Hide();
	}

	private void OnShowSpecificAdvisor(string advisor) {
		HideGameViews();
		Hide();
	}

	private void OnShowGameView(string gameView) {
		HideGameViews();

		switch (gameView) {
			case C7Action.ShowWondersView:
				wondersView.ShowView();
				break;
			case C7Action.ShowVictoryStatusView:
				victoryStatusView.ShowView();
				break;
			case C7Action.ShowPalaceView:
				palaceView.ShowView();
				break;
			case C7Action.ShowSpaceRaceView:
				spaceRaceView.ShowView();
				break;
			case C7Action.ShowDemographicsView:
				demographicsView.ShowView();
				break;
			case C7Action.ShowStatisticsView:
				statisticsView.ShowView();
				break;
			default:
				log.Warning("Unknown game view: " + gameView);
				break;
		}

		Show();
	}

	/// The end-of-game statistics: every civ, and closing the screen runs <paramref name="onClose"/>.
	public void ShowStatistics(System.Action onClose) {
		HideGameViews();
		statisticsView.ShowView(onClose);
		Show();
	}

	private void HideGameViews() {
		wondersView.Hide();
		victoryStatusView.Hide();
		palaceView.Hide();
		spaceRaceView.Hide();
		demographicsView.Hide();
		statisticsView.Hide();
	}
}
