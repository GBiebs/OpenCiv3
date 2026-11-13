using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Editor;

// Civ3's Wealth is an improvement with the "capitalization" flag, which a mod
// may call anything (the QC mods have one); the game makes it an inflow whose
// commerce comes from the Wealth rule. A scenario or a game saved on such
// rules must open again.
public class RenamedWealthTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public RenamedWealthTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	[Fact]
	public void ARenamedWealthSurvivesASave() {
		SaveGame rules = SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave();
		SaveInflow wealth = rules.Inflows.Single();
		wealth.name = "Treasury. The State's";

		SaveGame scenario = ScenarioFiles.ForEditing(rules);
		C7GameData.GameData gameData = ScenarioEditing.Open(scenario, fixture.behaviors);
		using TempDirectory dir = new("renamed-wealth");
		string path = dir.File("mod.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, scenario.Scenario), path);

		SaveGame opened = SaveGame.Load(path, s => s);
		Assert.Equal("Treasury. The State's", opened.Inflows.Single().name);
		C7GameData.GameData reopened = ScenarioFiles.ForEditing(opened).ToGameData(fixture.behaviors);
		Assert.NotNull(reopened.Inflows.Single().GetInflowYieldFunc(InflowYield.commerce));
	}
}
