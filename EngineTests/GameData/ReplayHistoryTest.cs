using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The replay's history (ReplayHistory): each turn's tile owners, the
// cities and the main events, saved with the game.
public class ReplayHistoryTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public ReplayHistoryTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		CityInteractions.BuildCity(us.units[0].location, us, "Seat");
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private int[] Owners() => gameData.map.tiles.Select(t => t.OwningPlayer() is Player p ? gameData.players.IndexOf(p) + 1 : 0).ToArray();

	[Fact]
	public void EachTurnsOwnersComeBack() {
		ReplayHistory replay = gameData.replay;
		replay.Record(gameData);
		Assert.Equal(gameData.turn, replay.startTurn);
		int[] first = Owners();
		Assert.Contains(first, o => o == gameData.players.IndexOf(us) + 1);
		// The cities already there are known, without a line.
		Assert.Contains(replay.events, e => e.kind == ReplayHistory.Kind.City && e.text == "");

		Tile far = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 8));
		CityInteractions.BuildCity(far, us, "Outpost");
		gameData.turn++;
		replay.Record(gameData);
		int[] second = Owners();
		Assert.NotEqual(first, second);
		Assert.Contains(replay.events, e => e.text == $"Outpost is founded by the {us.civilization.noun}.");

		var frames = replay.Frames(gameData.map.tiles.Count).ToList();
		Assert.Equal(2, frames.Count);
		Assert.Equal(first, frames[0].owners);
		Assert.Equal(second, frames[1].owners);
		// A turn already recorded isn't recorded twice.
		replay.Record(gameData);
		Assert.Equal(2, replay.frames.Count);
	}

	[Fact]
	public void ADestroyedCityAndAWonderAreNoted() {
		ReplayHistory replay = gameData.replay;
		replay.Record(gameData);
		City seat = gameData.cities.Single(c => c.name == "Seat");
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder());
		new MsgWonderCompleted(us, seat, wonder).send();
		Assert.Contains(replay.events, e => e.text == $"The {us.civilization.noun} complete {wonder.name} in Seat.");

		CityInteractions.DestroyCity(seat);
		gameData.turn++;
		replay.Record(gameData);
		Assert.Contains(replay.events, e => e.kind == ReplayHistory.Kind.CityGone && e.text == "Seat is destroyed.");
	}

	// Civ3's list on the replay screen: founded, captured, destroyed, created.
	[Fact]
	public void Civ3sLines() {
		ReplayHistory replay = gameData.replay;
		int t0 = gameData.turn;
		replay.Record(gameData);
		Tile far = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 8));
		City outpost = CityInteractions.BuildCity(far, us, "Outpost");
		gameData.turn++;
		replay.Record(gameData);
		CultureFlip.Flip(gameData, outpost, gameData.players.First(p => p != us && !p.isBarbarians));
		City seat = gameData.cities.Single(c => c.name == "Seat");
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder());
		new MsgWonderCompleted(us, seat, wonder).send();
		// A wonder in a save from before wonders had their own kind, and news
		// that isn't on Civ3's list.
		replay.events.Add(new ReplayHistory.Event() { turn = gameData.turn, kind = ReplayHistory.Kind.News, text = "The Romans complete The Hanging Gardens in Rome." });
		replay.events.Add(new ReplayHistory.Event() { turn = gameData.turn, kind = ReplayHistory.Kind.News, text = "The Romans declare war on the Maya." });
		gameData.turn++;
		replay.Record(gameData);
		CityInteractions.DestroyCity(seat);
		gameData.turn++;
		replay.Record(gameData);

		Assert.Equal([
			$"Outpost founded in {t0 + 1}",
			$"{wonder.name} created in {t0 + 1}",
			$"The Hanging Gardens created in {t0 + 1}",
			$"Outpost captured in {t0 + 2}",
			$"Seat destroyed in {t0 + 3}",
		], replay.Lines().Select(l => ReplayHistory.Text(l.line, l.name, l.e.turn.ToString())).ToList());
		Assert.Contains(replay.Lines(), l => l.line == ReplayHistory.Line.Created && l.e.x == seat.location.XCoordinate);
		Assert.Contains(replay.events, e => e.kind == ReplayHistory.Kind.Wonder && e.name == wonder.name);
	}

	[Fact]
	public void TheHistoryIsSaved() {
		gameData.replay.Record(gameData);
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(gameData.replay.frames, reloaded.replay.frames);
		Assert.Equal(gameData.replay.events.Count, reloaded.replay.events.Count);
		Assert.Equal(gameData.replay.lastCityOwners, reloaded.replay.lastCityOwners);
	}
}
