using System.IO;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// The data behind Civ3's great leaders and armies: which unit types are
// leaders and armies, the leader's two orders, the rules, each civ's leader
// names, and the state that has to be saved.
public class LeaderArmyDataTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;

	public LeaderArmyDataTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	[Fact]
	public void TheCiv3RulesetHasTheLeaderAndTheArmy() {
		Assert.Equal(["Leader"], gameData.unitPrototypes.Where(u => u.isLeader).Select(u => u.name).Distinct());
		Assert.Equal(["Army"], gameData.unitPrototypes.Where(u => u.isArmy).Select(u => u.name).Distinct());
		Assert.Contains(UnitAction.BuildArmy, Type("Leader").actions);
		Assert.Contains(UnitAction.HurryProduction, Type("Leader").actions);
		Assert.Equal(3, Type("Army").capacity);

		Assert.Equal("Leader", gameData.rules.BattleCreatedUnitType);
		Assert.Equal("Army", gameData.rules.ArmyUnitType);
		Assert.Equal(4, gameData.rules.CitiesNeededToSupportAnArmy);
		Assert.Equal(16, gameData.rules.LeaderChanceOneIn);
		Assert.Equal(12, gameData.rules.IncreasedLeaderChanceOneIn);
	}

	[Fact]
	public void EachCivHasItsGreatLeadersNames() {
		Civilization rome = gameData.civilizations.Find(c => c.name == "Rome");
		Assert.Equal(["Trajan", "Hadrian", "Maximus", "Germanicus"], rome.greatLeaderNames.Take(4));
		Assert.Equal(8, rome.greatLeaderNames.Count);
		Assert.All(gameData.civilizations.Where(c => !c.isBarbarian), c => Assert.NotEmpty(c.greatLeaderNames));
	}

	[Fact]
	public void LeaderAndArmyStateIsSaved() {
		Player player = gameData.players.First(p => !p.isBarbarians);
		player.hasVictoriousArmy = true;
		player.leadersCreated = 2;
		MapUnit unit = player.units[0];
		unit.hasProducedLeader = true;

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);

		Player loaded = reloaded.players.Find(p => p.id == player.id);
		Assert.True(loaded.hasVictoriousArmy);
		Assert.Equal(2, loaded.leadersCreated);
		Assert.True(reloaded.mapUnits.Find(u => u.id == unit.id).hasProducedLeader);
		Assert.All(reloaded.mapUnits.Where(u => u.id != unit.id), u => Assert.False(u.hasProducedLeader));
		Assert.False(reloaded.players.Find(p => p.id != player.id).hasVictoriousArmy);
	}

	[SkippableFact]
	public void TheyAreImportedFromCiv3() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// The stock rules have no map to import, so use a scenario with them.
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "5 Mesoamerica.biq");

		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);

		SaveUnitPrototype leader = Assert.Single(save.UnitPrototypes, u => u.flags.Contains(SaveUnitPrototype.Flag.Leader));
		Assert.Equal("Leader", leader.name);
		Assert.Contains(UnitAction.BuildArmy, leader.actions);
		Assert.Contains(UnitAction.HurryProduction, leader.actions);
		Assert.Equal(["Army"], save.UnitPrototypes.Where(u => u.flags.Contains(SaveUnitPrototype.Flag.Army)).Select(u => u.name));
		Assert.Equal("Leader", save.Rules.BattleCreatedUnitType);
		Assert.Equal("Army", save.Rules.ArmyUnitType);
		Assert.Equal(4, save.Rules.CitiesNeededToSupportAnArmy);
		// The Moche are only in this scenario, so their names come from its data.
		Assert.Equal("El Bruho \"The Decapitator\"", save.Civilizations.Find(c => c.name == "Moche").greatLeaderNames[0]);
		Assert.Equal("Ahuitzotl", save.Civilizations.Find(c => c.name == "Aztecs").greatLeaderNames[0]);
		Assert.All(save.Civilizations.SelectMany(c => c.greatLeaderNames), name => Assert.Equal(name.Trim(), name));
	}
}
