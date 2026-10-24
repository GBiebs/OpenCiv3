using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's draft (Civilopedia "Draft"): with Nationalism, a city larger than a
// town drafts citizens into conscripts of its best draftable unit, up to its
// government's limit each turn, at the cost of unhappiness.
public class DraftTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public DraftTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Recruits");
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < 8) {
			city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = laborer });
		}
		Learn("Bronze Working");
		us.government = gameData.governments.Find(g => g.name == "Despotism");
	}

	private void Learn(string tech) {
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == tech).id);
	}

	[Fact]
	public void TheDraftNeedsNationalism() {
		Assert.Equal("We don't know how to draft citizens yet.", city.WhyCannotDraft(gameData));
		Assert.Null(city.Draft(gameData));

		Learn("Nationalism");
		Assert.Null(city.WhyCannotDraft(gameData));
	}

	[Fact]
	public void ACityDraftsConscriptsUpToItsGovernmentsLimitEachTurn() {
		Learn("Nationalism");
		Assert.Equal(2, us.government.draftLimit);
		city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = gameData.citizenTypes.Find(c => c.IsDefaultCitizen) });

		MapUnit first = city.Draft(gameData);
		MapUnit second = city.Draft(gameData);

		// Nationalism itself brings the Rifleman.
		Assert.Equal("Rifleman", first.unitType.name);
		Assert.Equal("Conscript", first.experienceLevelKey);
		Assert.Equal(2, first.hitPointsRemaining);
		Assert.Equal(city.location, first.location);
		Assert.NotNull(second);
		Assert.Equal(7, city.residents.Count);
		Assert.Equal(40, city.turnsOfUnhappinessDueToDrafting);
		Assert.Equal("Recruits can't draft any more citizens this turn.", city.WhyCannotDraft(gameData));
		Assert.Null(city.Draft(gameData));

		gameData.turn++;
		Assert.Null(city.WhyCannotDraft(gameData));
		city.Draft(gameData);
		Assert.Equal("Only cities larger than 6 can draft citizens.", city.WhyCannotDraft(gameData));
	}

	[Fact]
	public void TheDraftNeedsTheUnitsResources() {
		Learn("Nationalism");
		// Infantry would be better, but needs rubber the city doesn't have.
		Learn("Replaceable Parts");
		Assert.DoesNotContain(gameData.Resources.Find(r => r.Key == "Rubber"), city.GetAccessibleResources(gameData));

		// Replaceable Parts also brings the Guerilla, as strong a defender as
		// the Rifleman and a better attacker.
		Assert.Equal("Guerilla", city.Draft(gameData).unitType.name);
	}

	[Fact]
	public void NothingIsDraftedUnderAnarchy() {
		Learn("Nationalism");
		us.government = gameData.governments.Find(g => g.name == "Anarchy");

		Assert.Equal("We can't draft citizens under this government.", city.WhyCannotDraft(gameData));
	}

	// Happy faces count +1, unhappy ones -1.
	private int MoodScore() {
		city.RecalculateCitizenMoods(gameData);
		return city.residents.Sum(r => r.mood switch {
			CityResident.Mood.Happy => 1,
			CityResident.Mood.Unhappy => -1,
			_ => 0,
		});
	}

	[Fact]
	public void EachDraftedCitizenCostsAFaceForTwentyTurns() {
		int before = MoodScore();

		city.turnsOfUnhappinessDueToDrafting = 20;
		Assert.Equal(before - 1, MoodScore());
		city.turnsOfUnhappinessDueToDrafting = 40;
		Assert.Equal(before - 2, MoodScore());

		us.DecrementCityUnhappinessPenalties(gameData);
		Assert.Equal(39, city.turnsOfUnhappinessDueToDrafting);
	}

	[Fact]
	public void TheDraftStateIsSaved() {
		Learn("Nationalism");
		city.Draft(gameData);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);

		City loaded = reloaded.cities.Find(c => c.name == "Recruits");
		Assert.Equal(20, loaded.turnsOfUnhappinessDueToDrafting);
		Assert.Equal(gameData.turn, loaded.draftTurn);
		Assert.Equal(1, loaded.draftedThatTurn);
	}

	[Fact]
	public void TheCiv3RulesetHasTheDraftAndTechAbilities() {
		Assert.True(gameData.techs.Find(t => t.Name == "Nationalism").HasFlag(SaveTech.Flag.EnablesConscription));
		Assert.True(gameData.techs.Find(t => t.Name == "Navigation").HasFlag(SaveTech.Flag.EnablesMapTrading));
		Assert.False(us.HasTechWithFlag(SaveTech.Flag.EnablesConscription));
		Learn("Nationalism");
		Assert.True(us.HasTechWithFlag(SaveTech.Flag.EnablesConscription));
		Assert.Equal(["Guerilla", "Infantry", "Mech Infantry", "Medieval Infantry", "Musketman", "Numidian Mercenary", "Pikeman",
			"Rifleman", "Spearman", "Swiss Mercenary", "TOW Infantry"],
			gameData.unitPrototypes.Where(u => u.draftable).Select(u => u.name).Distinct().Order());
		Assert.Equal(20, gameData.rules.TurnPenaltyForEachDraftedCitizen);
	}
}
