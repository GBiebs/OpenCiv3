using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

/// <summary>
/// Developer screenshots of the statistics screen:
/// <c>-- --load=&lt;save.json&gt; --stats-shots=&lt;dir&gt;</c> opens the save, shows the
/// screen with every civ in each layout, writes &lt;dir&gt;/stats_&lt;layout&gt;.png and quits.
/// </summary>
public static class StatisticsCapture {
	public static string LoadPath => Arg("--load=");
	public static string ShotsDir => Arg("--stats-shots=");

	// The main menu opens LoadPath only once, so returning to it doesn't load again.
	public static bool loadConsumed = false;

	private static string Arg(string prefix) =>
		OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix))?.Substring(prefix.Length);

	public static async void Run(Node game, GameViews gameViews) {
		SceneTree tree = game.GetTree();
		for (int i = 0; i < 10; i++) {
			await game.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}

		List<StatisticsView.CivStats> civs = new();
		EngineStorage.ReadGameData((GameData gameData) => {
			civs = gameData.players
				.Where(p => !p.isBarbarians && p.stats.TurnCount > 0)
				.Select(p => StatisticsView.ForPlayer(p, p.id == EngineStorage.uiControllerID))
				.ToList();
		});

		List<StatisticsView.CivStats> playerOnly = civs.Where(c => c.isPlayer).ToList();
		// Another civ in focus, and the hover box at 70% across the big chart.
		string otherCiv = civs.FirstOrDefault(c => !c.isPlayer)?.name;
		(string name, List<StatisticsView.CivStats> civs, StatisticsView.Layout layout, string stat, string focus, bool hover)[] shots = [
			("dashboard", civs, StatisticsView.Layout.Dashboard, null, null, false),
			("chart_start_position", civs, StatisticsView.Layout.ChartBrowser, "Start position", null, false),
			("chart_production_efficiency", civs, StatisticsView.Layout.ChartBrowser, "Production efficiency", null, false),
			("chart_luck", civs, StatisticsView.Layout.ChartBrowser, "Luck", null, false),
			("dashboard_focus", civs, StatisticsView.Layout.Dashboard, null, otherCiv, false),
			("chart_population_focus_hover", civs, StatisticsView.Layout.ChartBrowser, "Population", otherCiv, true),
			("chart_production_efficiency_hover", civs, StatisticsView.Layout.ChartBrowser, "Production efficiency", null, true),
			("dashboard_own", playerOnly, StatisticsView.Layout.Dashboard, null, null, false),
			("chart_commerce_own_hover", playerOnly, StatisticsView.Layout.ChartBrowser, "Commerce", null, true),
		];

		Directory.CreateDirectory(ShotsDir);
		gameViews.Show();
		foreach ((string name, List<StatisticsView.CivStats> shown, StatisticsView.Layout layout, string stat, string focus, bool hover) in shots) {
			// The all-civs views are the end of the game; the player-only ones are mid-game.
			gameViews.statisticsView.ShowView(shown, layout, stat, focus: focus, endOfGame: shown.Count > 1);
			if (hover) {
				LineChart chart = gameViews.statisticsView.bigChart;
				chart.SetHover(new Vector2(chart.Size.X * 0.7f, chart.Size.Y * 0.5f));
			}
			for (int i = 0; i < 5; i++) {
				await game.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			}
			string file = Path.Combine(ShotsDir, $"stats_{name}.png");
			game.GetViewport().GetTexture().GetImage().SavePng(file);
			GD.Print($"STATS-SHOT {file}");
		}

		// The real end-of-game entry point, which gathers the civs itself.
		EngineStorage.gameData.gameOver = true;
		gameViews.ShowStatistics(onClose: () => { });
		for (int i = 0; i < 5; i++) {
			await game.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}
		string endOfGame = Path.Combine(ShotsDir, "stats_end_of_game.png");
		game.GetViewport().GetTexture().GetImage().SavePng(endOfGame);
		GD.Print($"STATS-SHOT {endOfGame}");
		tree.Quit();
	}
}
