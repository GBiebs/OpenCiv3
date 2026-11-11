using System;
using System.Linq;
using C7GameData;
using Xunit;

namespace EngineTests.GameData;

// The citizens each luxury makes happy, as the city screen's Luxuries Box
// shows them (City.LuxuryFaces), add up to what the moods count.
public class LuxuryFacesTest {
	[Fact]
	public void OneEachWithoutAMarketplace() {
		Assert.All(Enumerable.Range(1, 8), nth => Assert.Equal(1, City.FacesOfLuxury(nth, marketplace: false)));
	}

	// Civ3's city screen with a Marketplace and three luxuries: 1, 1, 2.
	[Fact]
	public void AMarketplaceGivesOneOneTwoTwoThree() {
		Assert.Equal([1, 1, 2, 2, 3, 3], Enumerable.Range(1, 6).Select(nth => City.FacesOfLuxury(nth, marketplace: true)));
	}

	// The total the moods used before the faces were counted per luxury:
	// floor(n/2) * ceil(n/2) + ceil(n/2) with a Marketplace, else n.
	[Fact]
	public void TheFacesAddUpToTheMarketplaceFormula() {
		for (int n = 0; n <= 12; ++n) {
			int half = (int)Math.Ceiling(n / 2f);
			Assert.Equal(n / 2 * half + half, Enumerable.Range(1, n).Sum(nth => City.FacesOfLuxury(nth, marketplace: true)));
			Assert.Equal(n, Enumerable.Range(1, n).Sum(nth => City.FacesOfLuxury(nth, marketplace: false)));
		}
	}
}
