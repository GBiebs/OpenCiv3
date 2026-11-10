using C7GameData;
using Xunit;

namespace EngineTests.GameData;

// How a city's commerce is split by the sliders (City.SplitCommerce):
// science's and luxury's shares to the nearest, a half up; taxes the rest.
public class CommerceSplitTest {
	[Theory]
	// 10% luxury of 5 commerce gives one face (rounded down, none).
	[InlineData(5, 0, 1, 0, 1, 4)]
	// A player's example: 40% science and 20% luxury of 14.
	[InlineData(14, 4, 2, 6, 3, 5)]
	// Halves both rounded up would make 6 of 5: luxury gets what's left.
	[InlineData(5, 5, 5, 3, 2, 0)]
	[InlineData(0, 5, 5, 0, 0, 0)]
	[InlineData(23, 6, 0, 14, 0, 9)]
	public void SharesAreRoundedToTheNearest(int commerce, int science, int luxury, int beakers, int faces, int taxes) {
		Assert.Equal((beakers, faces, taxes), City.SplitCommerce(commerce, science, luxury));
	}
}
