using System.IO;
using System.Linq;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.Editor;

// Civ3 tells buildings apart by their place in its list, so a mod may give
// several the same name (Modern has three Barracks); C7 tells them apart by
// name, so the import numbers the later ones.
public class DuplicateBuildingNamesTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public DuplicateBuildingNamesTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	[Fact]
	public void TheFirstKeepsItsNameAndTheOthersAreNumbered() {
		Assert.Equal(["Barracks", "Airport", "Barracks (2)", "Barracks (3)", "Airport (2)"],
			ImportCiv3.UniqueNames(["Barracks", "Airport", "Barracks", "Barracks", "Airport"]));
	}

	[Fact]
	public void ANumberedNameNeverTakesAnotherBuildingsOwnName() {
		Assert.Equal(["Barracks", "Barracks (3)", "Barracks (2)"],
			ImportCiv3.UniqueNames(["Barracks", "Barracks", "Barracks (2)"]));
	}

	[Fact]
	public void NamesThatDifferAreUnchanged() {
		string[] names = ["Palace", "Barracks", "Granary"];
		Assert.Equal(names, ImportCiv3.UniqueNames(names));
	}

	// A Civ3 mod with duplicate names (the user's Modern 2.27, where it's
	// installed) opens in the editor: before, making its game data threw
	// "An item with the same key has already been added. Key: Barracks".
	[SkippableFact]
	public void AModWithDuplicateBuildingNamesOpens() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		string modern = Path.Join(conquests, "Scenarios", "Modern_2.27.biq");
		Skip.IfNot(File.Exists(modern), "Modern_2.27.biq isn't installed");
		SaveGame save = ScenarioFiles.Open(modern, PathUtils.defaultBicPath,
			_ => Path.GetFullPath(Path.Combine(conquests, "Text", "PediaIcons.txt")));
		Assert.Equal(save.Buildings.Count, save.Buildings.Select(b => b.name).Distinct().Count());
		Assert.Contains(save.Buildings, b => b.name == "Barracks (2)");
		Assert.All(save.Buildings.Where(b => b.requiredBuilding != null),
			b => Assert.Contains(save.Buildings, other => other.name == b.requiredBuilding));

		C7GameData.GameData gameData = ScenarioEditing.Open(save, fixture.behaviors);
		Assert.Equal(save.Buildings.Count, gameData.Buildings.Count);
	}
}
