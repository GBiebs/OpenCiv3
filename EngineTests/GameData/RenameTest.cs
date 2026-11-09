using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's renaming of a unit (Shift-N) or a city (its right-click menu).
public class RenameTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public RenameTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
	}

	[Fact]
	public void AUnitTakesItsNewNameAndABlankOneGivesBackItsType() {
		MapUnit unit = us.units[0];
		MsgRename.Unit(unit.id, "  Old Ironsides  ").process();
		Assert.Equal("Old Ironsides", unit.name);
		Assert.Contains(UnitAction.Rename, unit.GetAvailableActions());

		MsgRename.Unit(unit.id, "   ").process();
		Assert.Equal(unit.unitType.name, unit.name);
	}

	[Fact]
	public void ACityTakesItsNewNameButNotABlankOne() {
		City city = CityInteractions.BuildCity(us.units[0].location, us, "Seat");
		MsgRename.City(city.id, "New Seat").process();
		Assert.Equal("New Seat", city.name);

		MsgRename.City(city.id, "").process();
		Assert.Equal("New Seat", city.name);
	}

	[Theory]
	[InlineData("Name\twith\ncontrols", "Namewithcontrols")]
	[InlineData("A name far longer than anyone needs it to be", "A name far longer than a")]
	[InlineData(null, "")]
	public void NamesAreCleaned(string given, string kept) {
		Assert.Equal(kept, MsgRename.Clean(given));
		Assert.True(MsgRename.Clean(given).Length <= MsgRename.MaxLength);
	}
}
