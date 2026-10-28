using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Troops in another civ's land: asked to leave, then ordered home, then
// war; an AI's troops go home when asked.
public class BorderWarningTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player owner;
	private readonly Player intruder;
	private readonly City ownerCity;
	private readonly City intruderCity;
	private readonly MapUnit warrior;

	public BorderWarningTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(owner, intruder) = (ais[0], ais[1]);
		ownerCity = CityInteractions.BuildCity(owner.units[0].location, owner, "Home");
		intruderCity = CityInteractions.BuildCity(intruder.units[0].location, intruder, "Abroad");
		owner.EnsureRelationshipExists(intruder);
		foreach (MapUnit unit in owner.units.Concat(intruder.units).ToList()) {
			gameData.RemoveUnit(unit);
		}
		Tile field = ownerCity.location.neighbors.Values.First(t => t.IsLand() && t.OwningPlayer() == owner && !t.HasCity());
		warrior = gameData.SpawnUnit(intruder, gameData.unitPrototypes.Find(u => u.name == "Warrior"), field);
		gameData.turn = 30;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private PlayerRelationship OwnerToIntruder => owner.playerRelationships[intruder.id];

	[Fact]
	public void IntrudersAreTroopsOfACivAtPeaceWithoutRightOfPassage() {
		Assert.Equal([warrior], BorderWarnings.Intruders(gameData, owner, intruder));
		gameData.SpawnUnit(intruder, gameData.unitPrototypes.Find(u => u.name == "Worker"), warrior.location);
		Assert.Equal([warrior], BorderWarnings.Intruders(gameData, owner, intruder));

		PlayerRelationship.RegisterMultiTurnDeal(owner, intruder, new MultiTurnDeal(DealType.DiplomaticAgreement,
			DealSubType.RightOfPassage, DealDetails.Exchange, 0, null, 20, gameData.turn));
		Assert.Empty(BorderWarnings.Intruders(gameData, owner, intruder));
	}

	[Fact]
	public void NoneAtWar() {
		owner.DeclareWarOn(intruder, gameData.turn);
		Assert.Empty(BorderWarnings.Intruders(gameData, owner, intruder));
	}

	[Fact]
	public void AHumanIsAskedThenOrderedThenWarComes() {
		intruder.isHuman = true;
		int baseline = Attitude.Score(gameData, owner, intruder);

		BorderWarnings.Check(gameData, owner);
		MsgBorderWarning asked = Assert.IsType<MsgBorderWarning>(EngineStorage.messagesToUI.Single(m => m is MsgBorderWarning));
		Assert.False(asked.order);
		Assert.Equal(ownerCity, asked.city);
		Assert.Equal(gameData.rules.attitude.askedToLeave, Attitude.Score(gameData, owner, intruder) - baseline);

		EngineStorage.messagesToUI.Clear();
		BorderWarnings.Check(gameData, owner);
		Assert.True(Assert.IsType<MsgBorderWarning>(EngineStorage.messagesToUI.Single(m => m is MsgBorderWarning)).order);
		Assert.Equal(gameData.rules.attitude.orderedToLeave, Attitude.Score(gameData, owner, intruder) - baseline);

		gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == "Warrior"), ownerCity.location);
		BorderWarnings.Check(gameData, owner);
		Assert.True(PlayerRelationship.AtWar(owner, intruder));
	}

	[Fact]
	public void AWeakCivKeepsOrdering() {
		intruder.isHuman = true;
		for (int i = 0; i < 3; i++) {
			BorderWarnings.Check(gameData, owner);
		}
		Assert.False(PlayerRelationship.AtWar(owner, intruder));
		Assert.Equal(2, OwnerToIntruder.borderWarning);
	}

	[Fact]
	public void LeavingSetsThingsRight() {
		intruder.isHuman = true;
		int baseline = Attitude.Score(gameData, owner, intruder);
		BorderWarnings.Check(gameData, owner);
		BorderWarnings.Check(gameData, owner);
		new MsgSendTroopsHome(intruder.id, owner.id).process();
		Assert.Equal(intruderCity.location, warrior.location);

		BorderWarnings.Check(gameData, owner);
		Assert.Equal(0, OwnerToIntruder.borderWarning);
		Assert.Equal(baseline, Attitude.Score(gameData, owner, intruder));
	}

	[Fact]
	public void AnAIsTroopsGoHomeWhenAsked() {
		// Home is their nearest city.
		Tile far = gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3)).MaxBy(t => t.DistanceTo(warrior.location));
		CityInteractions.BuildCity(far, intruder, "Faraway");
		Assert.True(far.DistanceTo(warrior.location) > intruderCity.location.DistanceTo(warrior.location));
		BorderWarnings.Check(gameData, owner);
		Assert.Equal(intruderCity.location, warrior.location);
		Assert.Contains(warrior, intruderCity.location.unitsOnTile);
		Assert.DoesNotContain(EngineStorage.messagesToUI, m => m is MsgBorderWarning);
	}

	[Fact]
	public void AnAIsTroopsWanderingBackInAreAskedAgainNotFought() {
		Tile field = warrior.location;
		gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == "Warrior"), ownerCity.location);
		for (int i = 0; i < 4; i++) {
			BorderWarnings.Check(gameData, owner);
			gameData.TeleportUnit(warrior, field);
		}
		Assert.False(PlayerRelationship.AtWar(owner, intruder));
		Assert.Equal(0, OwnerToIntruder.borderWarning);
		Assert.Equal(gameData.rules.attitude.askedToLeave, OwnerToIntruder.borderAnger);
	}

	[Fact]
	public void WarningsAreSaved() {
		OwnerToIntruder.borderWarning = 2;
		OwnerToIntruder.borderAnger = 4;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		PlayerRelationship again = reloaded.GetPlayer(owner.id).playerRelationships[intruder.id];
		Assert.Equal(2, again.borderWarning);
		Assert.Equal(4, again.borderAnger);
	}
}
