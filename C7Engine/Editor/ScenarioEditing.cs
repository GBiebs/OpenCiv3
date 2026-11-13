using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// The engine's side of the scenario editor: the scenario being edited is
// the game's data, as for a game being played, but nobody plays it. It's
// seen whole (observer mode), and no turn ever starts.
public static class ScenarioEditing {
	// Makes the scenario the game's data, dropping whatever game was there.
	public static GameData Open(SaveGame scenario, BehaviorEngine behaviors) {
		EngineStorage.ResetForNewGame();
		GameData gameData = scenario.ToGameData(behaviors);
		EngineStorage.gameData = gameData;
		gameData.observerMode = true;

		// A map made in the editor (a blank one) has no art chosen for its
		// tiles yet; maps the game makes get theirs as they're made (see
		// MapGenerator), and Civ3's come with theirs.
		List<Tile> withoutArt = gameData.map.tiles.Where(t => t.ExtraInfo == null).ToList();
		if (withoutArt.Count > 0) {
			TerrainTextureFiles.ReassignTextureDetails(new Random(gameData.seed), gameData.terrainTypes, withoutArt);
		}
		return gameData;
	}
}
