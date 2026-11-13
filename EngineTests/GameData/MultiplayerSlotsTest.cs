using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The Multiplayer Setup screen's slots (Open, Closed, Computer) become the
// players of a hotseat or networked game.
public class MultiplayerSlotsTest {
	private static readonly Lazy<SaveGame> basicRuleset = new(() => SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave());

	private static List<Civilization> Civilizations() => basicRuleset.Value.Civilizations;

	private static string Civ(int index) => Civilizations().Where(c => !c.isBarbarian).ElementAt(index).name;

	// With room for fewer slots, Closed ones go first, then the computer's,
	// then Open ones nobody has taken, and last the players who joined last.
	[Fact]
	public void FewerSlotsTakeTheComputersBeforeThePlayers() {
		List<PlayerSlot> slots = [
			new(SlotKind.Open),
			new(SlotKind.Computer),
			new(SlotKind.Open),
			new(SlotKind.Closed),
			new(SlotKind.Open),
			new(SlotKind.Computer),
		];
		// Slot 0 joined second, slot 4 first; slot 2 is empty.
		long[] joined = [2, 0, 0, 0, 1, 0];
		Assert.Equal([0, 1, 2, 4], MultiplayerSlots.SlotsToKeep(slots, 4, i => joined[i]));
		Assert.Equal([0, 2, 4], MultiplayerSlots.SlotsToKeep(slots, 3, i => joined[i]));
		Assert.Equal([0, 4], MultiplayerSlots.SlotsToKeep(slots, 2, i => joined[i]));
		Assert.Equal([4], MultiplayerSlots.SlotsToKeep(slots, 1, i => joined[i]));
		Assert.Equal([0, 1, 2, 3, 4, 5], MultiplayerSlots.SlotsToKeep(slots, 8, i => joined[i]));
	}

	[Fact]
	public void OpenSlotsAreSeatsForPlayersJoiningOverTheNetwork() {
		List<PlayerSlot> slots = [
			new(SlotKind.Open, Civ(1)),
			new(SlotKind.Computer, Civ(2)),
			new(SlotKind.Closed, Civ(3)),
			new(SlotKind.Computer),
			new(SlotKind.Open),
		];
		MultiplayerPlan plan = MultiplayerSlots.Plan(MultiplayerGameType.Lan, Civ(0), slots, Civilizations(), new Random(1));

		Assert.Null(plan.problem);
		Assert.Equal(Civ(0), plan.playerCivilization.name);
		Assert.Equal(2, plan.guestSeats);
		Assert.Empty(plan.hotseatPlayers);
		// The Closed slot has no civ, so the game has one fewer.
		Assert.Equal(2, plan.opponents.Count);
		Assert.Equal(Civ(2), plan.opponents[0].Name);
		Assert.False(plan.opponents[0].isRandom);
		Assert.True(plan.opponents[1].isRandom);
	}

	[Fact]
	public void InHotseatOpenSlotsArePlayersAtThisComputer() {
		List<PlayerSlot> slots = [
			new(SlotKind.Open, Civ(1), "Bob"),
			new(SlotKind.Computer),
			new(SlotKind.Open),
		];
		MultiplayerPlan plan = MultiplayerSlots.Plan(MultiplayerGameType.Hotseat, Civ(0), slots, Civilizations(), new Random(1));

		Assert.Null(plan.problem);
		Assert.Equal(0, plan.guestSeats);
		Assert.Single(plan.opponents);
		Assert.Equal(2, plan.hotseatPlayers.Count);
		Assert.Equal(Civ(1), plan.hotseatPlayers[0].civilization.name);
		Assert.Equal("Bob", plan.hotseatPlayers[0].name);
		// Random, under the default name by turn order.
		Assert.Null(plan.hotseatPlayers[1].civilization);
		Assert.Equal("Player 3", plan.hotseatPlayers[1].name);
	}

	// Playing by email, as in hotseat, the host names the other players and
	// chooses their civilizations; nobody joins over the network.
	[Fact]
	public void PlayingByEmailOpenSlotsAreOtherPlayersSetUpHere() {
		List<PlayerSlot> slots = [
			new(SlotKind.Open, Civ(1), "Bob"),
			new(SlotKind.Computer),
		];
		MultiplayerPlan plan = MultiplayerSlots.Plan(MultiplayerGameType.PlayByEmail, Civ(0), slots, Civilizations(), new Random(1));

		Assert.Null(plan.problem);
		Assert.Equal(0, plan.guestSeats);
		HotseatPlayer bob = Assert.Single(plan.hotseatPlayers);
		Assert.Equal(Civ(1), bob.civilization.name);
		Assert.Equal("Bob", bob.name);

		NewGameChoices choices = new();
		MultiplayerSlots.RememberSlots(choices, MultiplayerGameType.PlayByEmail, slots);
		Assert.Equal(nameof(MultiplayerGameType.PlayByEmail), choices.MultiplayerGameType);
		Assert.Equal(SlotKind.Open, MultiplayerSlots.SlotsAsLastTime(choices, MultiplayerGameType.PlayByEmail, 2)[0].kind);

		List<PlayerSlot> computers = [new(SlotKind.Computer)];
		Assert.NotNull(MultiplayerSlots.Plan(MultiplayerGameType.PlayByEmail, Civ(0), computers, Civilizations(), new Random(1)).problem);
	}

	[Fact]
	public void ARandomHostCivilizationIsNoSlotsCivilization() {
		List<string> playable = Civilizations().Where(c => !c.isBarbarian).Select(c => c.name).ToList();
		// Every civ but the last two is taken by a slot.
		List<PlayerSlot> slots = playable.Take(playable.Count - 2).Select(n => new PlayerSlot(SlotKind.Computer, n)).ToList();
		slots.Add(new PlayerSlot(SlotKind.Open));
		for (int seed = 0; seed < 20; ++seed) {
			MultiplayerPlan plan = MultiplayerSlots.Plan(MultiplayerGameType.Lan, NewGameChoices.Random, slots, Civilizations(), new Random(seed));
			Assert.Contains(plan.playerCivilization.name, playable.TakeLast(2));
		}
	}

	[Fact]
	public void ACivilizationChosenTwiceIsRandomTheSecondTime() {
		List<PlayerSlot> slots = [new(SlotKind.Computer, Civ(0)), new(SlotKind.Computer, Civ(1)), new(SlotKind.Computer, Civ(1)), new(SlotKind.Open)];
		MultiplayerPlan plan = MultiplayerSlots.Plan(MultiplayerGameType.Lan, Civ(0), slots, Civilizations(), new Random(1));

		Assert.True(plan.opponents[0].isRandom);
		Assert.Equal(Civ(1), plan.opponents[1].Name);
		Assert.True(plan.opponents[2].isRandom);
	}

	[Fact]
	public void AGameNeedsAnotherPlayer() {
		List<PlayerSlot> closed = [new(SlotKind.Closed), new(SlotKind.Closed)];
		Assert.NotNull(MultiplayerSlots.Plan(MultiplayerGameType.Lan, Civ(0), closed, Civilizations(), new Random(1)).problem);

		List<PlayerSlot> computers = [new(SlotKind.Computer), new(SlotKind.Closed)];
		Assert.NotNull(MultiplayerSlots.Plan(MultiplayerGameType.Lan, Civ(0), computers, Civilizations(), new Random(1)).problem);
		Assert.NotNull(MultiplayerSlots.Plan(MultiplayerGameType.Hotseat, Civ(0), computers, Civilizations(), new Random(1)).problem);
	}

	[Fact]
	public void GameModesSetTheSpecialRules() {
		VictoryConditions conditions = VictoryConditions.NewGameDefaults();
		Assert.Equal(MultiplayerGameMode.Standard, MultiplayerSlots.GameModeOf(conditions));

		foreach (MultiplayerGameMode mode in Enum.GetValues<MultiplayerGameMode>().Where(m => m != MultiplayerGameMode.Custom)) {
			MultiplayerSlots.ApplyGameMode(conditions, mode);
			Assert.Equal(mode, MultiplayerSlots.GameModeOf(conditions));
		}
		Assert.True(conditions.VictoryLocations);
		Assert.False(conditions.Regicide);

		// A mix of them is Custom, which changes nothing.
		conditions.Regicide = true;
		Assert.Equal(MultiplayerGameMode.Custom, MultiplayerSlots.GameModeOf(conditions));
		MultiplayerSlots.ApplyGameMode(conditions, MultiplayerGameMode.Custom);
		Assert.True(conditions.Regicide);
		Assert.True(conditions.VictoryLocations);
	}

	[Fact]
	public void SlotsAreRememberedForNextTime() {
		List<PlayerSlot> slots = [
			new(SlotKind.Open, Civ(1), "Bob"),
			new(SlotKind.Closed),
			new(SlotKind.Computer, Civ(2)),
		];
		NewGameChoices choices = new();
		MultiplayerSlots.RememberSlots(choices, MultiplayerGameType.Hotseat, slots);
		Dictionary<string, string> saved = [];
		choices.Write((key, value) => {
			if (value != null) {
				saved[key] = value;
			}
		});
		NewGameChoices read = NewGameChoices.Read(key => saved.GetValueOrDefault(key));

		Assert.Equal(nameof(MultiplayerGameType.Hotseat), read.MultiplayerGameType);
		List<PlayerSlot> back = MultiplayerSlots.SlotsAsLastTime(read, MultiplayerGameType.Hotseat, 4);
		Assert.Equal(new PlayerSlot(SlotKind.Open, Civ(1), "Bob"), back[0]);
		Assert.Equal(SlotKind.Closed, back[1].kind);
		Assert.Equal(new PlayerSlot(SlotKind.Computer, Civ(2)), back[2]);
		// A slot never set up is the computer's, with a random civ.
		Assert.Equal(new PlayerSlot(SlotKind.Computer, NewGameChoices.Random), back[3]);

		// Hosting on the LAN, the Open slots are its seats, and the game has
		// no hotseat players.
		MultiplayerSlots.RememberSlots(choices, MultiplayerGameType.Lan, slots);
		Assert.Equal([0], choices.LanSlots);
		Assert.Empty(choices.HotseatSlots);
		Assert.Equal(SlotKind.Open, MultiplayerSlots.SlotsAsLastTime(choices, MultiplayerGameType.Lan, 3)[0].kind);
	}

	[Fact]
	public void QuickStartLeavesClosedSlotsOut() {
		SaveGame save = basicRuleset.Value.Clone();
		NewGameChoices choices = new() {
			WorldSize = save.WorldSizes[1].name,
			Opponents = [Civ(1), Civ(2)],
			ClosedSlots = [0],
			TurnTimerMinutes = 5,
		};
		GameSetup setup = choices.QuickStartGame(save, 1, new Random(1));

		Assert.Equal(save.WorldSizes[1].numberOfCivs - 2, setup.opponents.Count);
		Assert.Equal(Civ(2), setup.opponents[0].Name);
	}
}
