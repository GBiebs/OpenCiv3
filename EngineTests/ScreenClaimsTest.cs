using System.Collections.Generic;
using C7Engine;
using Xunit;

namespace EngineTests;

// A flow on screen (the end of the game, the Science Advisor for "What's
// the big picture?", a city screen opened from a popup) has the screen to
// itself: what waits comes up only once it ends, in order (owner's report,
// 2026-11-12, "a consistent problem with order of operations").
public class ScreenClaimsTest {
	private readonly ScreenClaims claims = new();

	[Fact]
	public void WithNoClaimEveryPopupMayComeUpInOrder() {
		Assert.False(claims.Any);
		Assert.True(claims.Admits(null));
		Assert.Equal(0, claims.NextToShow([null, null]));
		Assert.Equal(-1, claims.NextToShow([]));
	}

	[Fact]
	public void AClaimHoldsBackOtherPopupsButLetsItsOwnThrough() {
		ScreenClaim endOfGame = claims.Claim("end of game", holdsGame: true);
		Assert.True(claims.Any);
		Assert.True(claims.HoldsGame);
		Assert.False(claims.Admits(null));
		Assert.True(claims.Admits(endOfGame));
		// The palace upgrade and the United Nations wait; the end of the
		// game's own question comes first, though it was asked for last.
		Assert.Equal(2, claims.NextToShow([null, null, endOfGame]));
		Assert.Equal(-1, claims.NextToShow([null, null]));

		endOfGame.Release();
		Assert.False(claims.Any);
		Assert.False(claims.HoldsGame);
		Assert.False(endOfGame.Active);
		Assert.Equal(0, claims.NextToShow([null, null]));
	}

	[Fact]
	public void ReleasingRunsAfterwardsThenTellsWhatWaits() {
		List<string> happened = [];
		claims.Released += () => happened.Add("released");
		ScreenClaim claim = claims.Claim("big picture", afterwards: () => happened.Add("asked again"));
		claim.Release();
		claim.Release();
		Assert.Equal(["asked again", "released"], happened);
	}

	[Fact]
	public void OnlyTheFrontClaimsPopupsComeUp() {
		ScreenClaim outer = claims.Claim("big picture");
		ScreenClaim inner = claims.Claim("end of game", holdsGame: true);
		Assert.Same(inner, claims.Top);
		Assert.False(claims.Admits(outer));
		Assert.Equal(1, claims.NextToShow([outer, inner]));
		inner.Release();
		Assert.Equal(0, claims.NextToShow([outer, null]));
		Assert.False(claims.HoldsGame);
	}

	[Fact]
	public void AScreenClaimLastsFromOpeningToClosing() {
		double now = 0;
		bool open = false;
		bool ended = false;
		claims.ClaimWhileOpen("science advisor", () => open, () => now, afterwards: () => ended = true);

		// The advisor opens a moment later, when the engine answers.
		now = 1;
		claims.Poll();
		Assert.True(claims.Any);
		open = true;
		claims.Poll();
		// However long the player looks at it.
		now = 60;
		claims.Poll();
		Assert.True(claims.Any);
		Assert.False(ended);

		open = false;
		claims.Poll();
		Assert.False(claims.Any);
		Assert.True(ended);
	}

	[Fact]
	public void AScreenThatNeverOpensGivesTheClaimUp() {
		double now = 0;
		claims.ClaimWhileOpen("science advisor", () => false, () => now, openWithin: 3);
		now = 2.9;
		claims.Poll();
		Assert.True(claims.Any);
		now = 3;
		claims.Poll();
		Assert.False(claims.Any);
	}

	[Fact]
	public void AClaimMayLetItsScreensOwnPopupsComeUp() {
		// The city screen's questions (hurrying, say) come up over it; what
		// waited before it opened still waits.
		ScreenClaim city = claims.Claim("city screen", admitsNewPopups: true);
		Assert.Same(city, claims.OwnerOf(null));
		Assert.Equal(1, claims.NextToShow([null, claims.OwnerOf(null)]));

		// The Science Advisor's claim lets nothing new up.
		city.Release();
		ScreenClaim advisor = claims.Claim("big picture");
		Assert.Null(claims.OwnerOf(null));
		Assert.Same(advisor, claims.OwnerOf(advisor));
		Assert.Null(new ScreenClaims().OwnerOf(null));
	}
}
