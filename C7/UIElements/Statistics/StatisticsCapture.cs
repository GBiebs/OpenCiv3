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
		(string name, List<StatisticsView.CivStats> civs, StatisticsView.Layout layout, string stat)[] shots = [
			("dashboard", civs, StatisticsView.Layout.Dashboard, null),
			("chart_production_efficiency", civs, StatisticsView.Layout.ChartBrowser, "Production efficiency"),
			("chart_luck", civs, StatisticsView.Layout.ChartBrowser, "Luck"),
			("dashboard_own", playerOnly, StatisticsView.Layout.Dashboard, null),
			("chart_commerce_own", playerOnly, StatisticsView.Layout.ChartBrowser, "Commerce"),
		];

		Directory.CreateDirectory(ShotsDir);
		gameViews.Show();
		foreach ((string name, List<StatisticsView.CivStats> shown, StatisticsView.Layout layout, string stat) in shots) {
			gameViews.statisticsView.ShowView(shown, layout, stat);
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
