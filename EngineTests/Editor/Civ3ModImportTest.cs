using System.IO;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.Editor;

// Civ3 mods the editor opens as starting points, with what their files get
// wrong or do differently from Civ3's own rules.
public class Civ3ModImportTest {
	// A mod's PediaIcons file may leave a unit out (QC 1.70's Minuteman). The
	// unit is still imported: its art is named after it, for the game to
	// draw with a stock unit's if there's none by that name, and its icons
	// are a Warrior's.
	[Fact]
	public void AUnitMissingFromPediaIconsGetsStandInArt() {
		using TempDirectory dir = new("pedia-icons");
		string path = dir.File("PediaIcons.txt");
		File.WriteAllLines(path, [
			"#ANIMNAME_PRTO_Warrior", "Warrior",
			"#ICON_PRTO_Warrior", "art/civilopedia/icons/units/warriorlarge.pcx", "art/civilopedia/icons/units/warriorsmall.pcx",
			"#UNITICON_PRTO_Warrior", "3",
		]);
		PediaIcons icons = new(path);

		Assert.Equal("Warrior", icons.GetUnitMainArt("PRTO_Warrior").defaultName);
		Assert.Equal("Minuteman", icons.GetUnitMainArt("PRTO_Minuteman").defaultName);
		PediaArt art = icons.GetUnitCivilopediaArt("PRTO_Minuteman");
		Assert.Equal("art/civilopedia/icons/units/warriorlarge.pcx", art.large);
		Assert.Equal("art/civilopedia/icons/units/warriorsmall.pcx", art.small);
	}

	// Civ3's worker jobs are fixed; a mod may only rename them, so a job is
	// known by its place in the list. Space Opera calls the fortress a
	// Station ("Build Station") and the road a Tube.
	[SkippableFact]
	public void RenamedWorkerJobsKeepTheirPlace() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		string spaceOpera = Path.Join(conquests, "Scenarios", "Space Opera.biq");
		Skip.If(!File.Exists(spaceOpera), "Space Opera isn't installed");
		SaveGame save = ScenarioFiles.Open(spaceOpera, PathUtils.defaultBicPath,
			_ => Path.GetFullPath(Path.Combine(conquests, "Text", "PediaIcons.txt")));

		SaveTerraform station = save.TerraForms.Single(t => t.Name == "Station");
		SaveTerraform fortress = SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave().TerraForms.Single(t => t.Name == "Fortress");
		Assert.Equal(fortress.UIAction, station.UIAction);
		Assert.Equal(C7Action.UnitBuildFortress, station.UIAction);
		Assert.Equal(13, save.TerraForms.Select(t => t.UIAction).Distinct().Count());
	}
}
