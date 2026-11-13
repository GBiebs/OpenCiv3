using C7GameData;
using Xunit;

namespace EngineTests.GameData;

// A building's culture doubling with age, and the city's culture level
// (Civ3 binary, doc/perez-findings/Civ3_Findings.md, "Culture and
// flipping").
public class CultureAgeAndLevelTest {
	// "Doubled when the building is 1000+ years old".
	[Theory]
	[InlineData(999, 1)]
	[InlineData(1000, 2)]
	[InlineData(3000, 2)]
	public void BuildingCultureDoublesAtAThousandYears(int age, int multiplier) {
		CityBuilding cb = new() { year = -age };
		Assert.Equal(multiplier, City.AgeMultiplier(cb, 0));
	}

	// "Culture level = 1 + number of times g_RuleCultureBase fits into the
	// owner's city culture, max 6".
	[Theory]
	[InlineData(0, 1)]
	[InlineData(9, 1)]
	[InlineData(10, 2)]
	[InlineData(99999, 5)]
	[InlineData(100000, 6)]
	[InlineData(10000000, 6)]
	[InlineData(int.MaxValue, 6)]
	public void TheCultureLevelIsAtMostSix(int culture, int level) {
		Player owner = new();
		City city = new(Tile.NONE, owner, "Memphis", ID.None("city"));
		city.perPlayerCulture[owner] = culture;
		Assert.Equal(level, city.GetBorderExpansionLevel());
	}

	// The thresholds are powers of the rules' Border Factor.
	[Theory]
	[InlineData(4, 1)]
	[InlineData(5, 2)]
	[InlineData(25, 3)]
	[InlineData(3124, 5)]
	[InlineData(3125, 6)]
	[InlineData(1000000, 6)]
	public void TheBorderFactorSetsTheThresholds(int culture, int level) {
		Player owner = new() { rules = new Rules { BorderFactor = 5 } };
		City city = new(Tile.NONE, owner, "Memphis", ID.None("city"));
		city.perPlayerCulture[owner] = culture;
		Assert.Equal(level, city.GetBorderExpansionLevel());
	}
}
