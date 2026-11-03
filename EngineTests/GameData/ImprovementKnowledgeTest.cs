using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// What a player sees of tile improvements (upstream PR #1074, with Civ3's
// fog of war): a tile in view shows what's on it now, a tile out of view
// what was there when the player last saw it.
public class ImprovementKnowledgeTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player human;
	private readonly Tile home;
	private readonly Tile near;
	private readonly Tile far;
	private readonly TerrainImprovement road;

	public ImprovementKnowledgeTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		human = gameData.players.First(p => p.isHuman);
		home = human.units[0].location;
		near = home.neighbors.Values.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && !t.overlays.GetImprovements().Any());
		far = gameData.map.tiles.First(t => t.IsLand() && t.DistanceTo(home) >= 8 && gameData.players.All(p => p.units.All(u => u.location.DistanceTo(t) >= 4)));
		road = gameData.terrainImprovements.First(i => i.key == "road");
		human.tileKnowledge.AddTilesToKnown(home);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	// All the human's units go to `tile` (they have no city to see from).
	private void MoveEveryoneTo(Tile tile) {
		foreach (MapUnit unit in human.units.ToList()) {
			gameData.TeleportUnit(unit, tile);
		}
		human.tileKnowledge.RecomputeActiveTiles();
	}

	[Fact]
	public void ATileInViewShowsWhatsOnItNow() {
		Assert.True(human.tileKnowledge.isActiveTile(near));
		near.overlays.Add(road);
		Assert.Contains("road", human.tileKnowledge.SeenImprovements(near));
	}

	// A road built while we watch is remembered once we leave, and its
	// removal is only seen when we come back.
	[Fact]
	public void ATileOutOfViewShowsWhatWasLastSeen() {
		near.overlays.Add(road);
		MoveEveryoneTo(far);
		Assert.False(human.tileKnowledge.isActiveTile(near));
		Assert.Contains("road", human.tileKnowledge.SeenImprovements(near));

		near.overlays.Remove(road);
		Assert.Contains("road", human.tileKnowledge.SeenImprovements(near));

		MoveEveryoneTo(home);
		Assert.DoesNotContain("road", human.tileKnowledge.SeenImprovements(near));
	}

	[Fact]
	public void WhatWasSeenIsSaved() {
		near.overlays.Add(road);
		MoveEveryoneTo(far);
		near.overlays.Remove(road);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Player again = reloaded.GetPlayer(human.id);
		Tile there = reloaded.map.tileAt(near.XCoordinate, near.YCoordinate);
		again.tileKnowledge.RecomputeActiveTiles();
		Assert.False(again.tileKnowledge.isActiveTile(there));
		Assert.Contains("road", again.tileKnowledge.SeenImprovements(there));
	}

	// The AI draws no map, so its view changing doesn't update its memory.
	[Fact]
	public void TheAIKeepsNoMemoryOfWhatItSees() {
		Player ai = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		Tile aiHome = ai.units[0].location;
		Tile watched = aiHome.neighbors.Values.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && !t.overlays.GetImprovements().Any());
		ai.tileKnowledge.AddTilesToKnown(aiHome);
		Assert.True(ai.tileKnowledge.isActiveTile(watched));

		watched.overlays.Add(road);
		ai.tileKnowledge.RecomputeActiveTiles();
		Assert.DoesNotContain("road", ai.tileKnowledge.knownTerrainImprovements[watched.Id]);
	}
}
