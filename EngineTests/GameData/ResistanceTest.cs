using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's resistance in captured cities (see Resistance and the skill's
// civ3-rules.md): who resists, what resisters don't do, and how garrisons and
// peace end it.
public class ResistanceTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile cityTile;

	public ResistanceTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		// A clone, because some tests change the rules' data.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.DeclareWarOn(them, gameData.turn);
		cityTile = gameData.map.tiles.First(t => IsEmptyLand(t)
			&& t.neighbors.TryGetValue(TileDirection.EAST, out Tile east) && IsEmptyLand(east)
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 6));
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	// A city of `owner` with `size` citizens of `nationality`.
	private City CityOf(Player owner, int size, Player nationality, Tile tile = null, string name = "Target") {
		City city = CityInteractions.BuildCity(tile ?? cityTile, owner, name);
		city.residents[0].nationality = nationality.civilization;
		while (city.residents.Count < size) {
			city.AddCitizen(new CityResident { city = city, nationality = nationality.civilization, citizenType = gameData.citizenTypes.Find(c => c.IsDefaultCitizen) });
		}
		return city;
	}

	// Every citizen resists on capture with `initial`%, and keeps resisting
	// with `continued`%, whatever the culture.
	private void SetResistanceChances(int initial, int continued) {
		foreach (CultureOpinion level in gameData.cultureOpinions) {
			level.initialResistanceChance = initial;
			level.continuedResistanceChance = continued;
		}
		foreach (Government g in gameData.governments) {
			g.resistanceModifiers.Clear();
		}
	}

	private void Garrison(City city, int units) {
		for (int i = 0; i < units; ++i) {
			gameData.SpawnUnit(city.owner, gameData.unitPrototypes.Find(u => u.name == "Spearman"), city.location);
		}
	}

	private Tile Elsewhere() {
		return gameData.map.tiles.First(t => IsEmptyLand(t) && t.DistanceTo(cityTile) >= 4 && t.DistanceTo(cityTile) <= 8);
	}

	[Fact]
	public void TheCiv3RulesetHasConquestsCultureLevelsAndGovernmentModifiers() {
		Assert.Equal(["in awe of", "admirers of", "impressed with", "unimpressed by", "dismissive of", "disdainful of"],
			gameData.cultureOpinions.Select(o => o.name));
		Assert.Equal([40, 50, 60, 70, 80, 90], gameData.cultureOpinions.Select(o => o.initialResistanceChance));
		Assert.Equal([30, 40, 50, 60, 70, 80], gameData.cultureOpinions.Select(o => o.continuedResistanceChance));
		Government republic = gameData.governments.Find(g => g.name == "Republic");
		Government monarchy = gameData.governments.Find(g => g.name == "Monarchy");
		// Republics conquering monarchies face more resistance; monarchies
		// conquering despotisms face less.
		Assert.Equal(5, republic.resistanceModifiers["Monarchy"]);
		Assert.Equal(-5, monarchy.resistanceModifiers["Despotism"]);
		Assert.Equal(1, gameData.gameDifficulty.MilitaryLaw);
	}

	[Theory]
	[InlineData(300, 100, "in awe of")]
	[InlineData(299, 100, "admirers of")]
	[InlineData(100, 100, "impressed with")]
	[InlineData(75, 100, "unimpressed by")]
	[InlineData(50, 100, "dismissive of")]
	[InlineData(20, 100, "disdainful of")]
	[InlineData(0, 0, "disdainful of")]
	public void TheCultureLevelFollowsTheRatioOfTotalCulture(int ourCulture, int theirCulture, string expected) {
		City ours = CityOf(us, 1, us);
		City theirs = CityOf(them, 1, them, Elsewhere(), "Theirs");
		ours.perPlayerCulture[us] = ourCulture;
		theirs.perPlayerCulture[them] = theirCulture;

		Assert.Equal(expected, Resistance.OpinionOf(gameData, them, us).name);
	}

	[Fact]
	public void TheChancesAddTheConquerorsGovernmentModifier() {
		City ours = CityOf(us, 1, us);
		City theirs = CityOf(them, 1, them, Elsewhere(), "Theirs");
		ours.perPlayerCulture[us] = 100;
		theirs.perPlayerCulture[them] = 100; // "impressed with": 60% / 50%
		us.government = gameData.governments.Find(g => g.name == "Republic");
		them.government = gameData.governments.Find(g => g.name == "Monarchy");

		Assert.Equal(65, Resistance.InitialChance(gameData, them, us));
		Assert.Equal(55, Resistance.ContinuedChance(gameData, them, us));
	}

	[Fact]
	public async Task CapturedCitizensOfTheLoserResist() {
		SetResistanceChances(initial: 100, continued: 100);
		City target = CityOf(them, 4, them);
		CityOf(them, 1, them, Elsewhere(), "Elsewhere");
		MapUnit warrior = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Warrior"), cityTile.neighbors[TileDirection.EAST]);

		await warrior.Move(TileDirection.WEST);

		Assert.Equal(us, target.owner);
		Assert.Equal(3, target.residents.Count(r => r.resisting));
		Assert.True(target.IsInResistance());
		Assert.All(target.residents, r => Assert.Equal(Tile.NONE, r.tileWorked));
		Assert.Equal(3, Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityCaptured>()).resisters);
	}

	[Fact]
	public async Task NoCitizenResistsWhenTheChanceIsZero() {
		SetResistanceChances(initial: 0, continued: 100);
		City target = CityOf(them, 4, them);
		CityOf(them, 1, them, Elsewhere(), "Elsewhere");
		MapUnit warrior = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Warrior"), cityTile.neighbors[TileDirection.EAST]);

		await warrior.Move(TileDirection.WEST);

		Assert.False(target.IsInResistance());
	}

	[Fact]
	public void CitizensOfCivsAtPeaceWithTheConquerorDoNotResist() {
		SetResistanceChances(initial: 100, continued: 100);
		Player neutral = gameData.players.First(p => p != us && p != them && !p.isBarbarians);
		City city = CityOf(us, 3, neutral);
		Assert.False(PlayerRelationship.AtWar(neutral, us));

		Assert.Equal(0, Resistance.StartResisting(gameData, city));
	}

	[Fact]
	public void ResistersDoNotWorkEatOrCountTowardsTheMood() {
		SetResistanceChances(initial: 100, continued: 100);
		City city = CityOf(us, 4, them);
		int resisters = Resistance.StartResisting(gameData, city);

		Assert.Equal(4, resisters);
		Assert.Equal(0, city.FoodConsumedPerTurn());
		Assert.All(city.residents, r => Assert.Equal(Tile.NONE, r.tileWorked));
		Assert.Equal(City.Mood.Happy, city.RecalculateCitizenMoods(gameData));
		Assert.Equal("We cannot hurry production while our city is in resistance.", city.GetHurryProductionDetails().errorMessage);
	}

	[Fact]
	public void AGarrisonQuellsAtMostOneResisterPerUnitEachTurn() {
		SetResistanceChances(initial: 100, continued: 0);
		City city = CityOf(us, 3, them);
		Resistance.StartResisting(gameData, city);
		Garrison(city, 1);

		Assert.Equal(1, Resistance.Update(gameData, city));
		Assert.Equal(2, city.residents.Count(r => r.resisting));

		Garrison(city, 1);
		Assert.Equal(2, Resistance.Update(gameData, city));
		Assert.False(city.IsInResistance());
		// Quelled citizens go back to work and eat again.
		Assert.Equal(6, city.FoodConsumedPerTurn());
	}

	[Fact]
	public void WithoutAGarrisonNobodyIsQuelled() {
		SetResistanceChances(initial: 100, continued: 0);
		City city = CityOf(us, 3, them);
		Resistance.StartResisting(gameData, city);

		Assert.Equal(0, Resistance.Update(gameData, city));
		Assert.Equal(3, city.residents.Count(r => r.resisting));
	}

	[Fact]
	public void PeaceEndsResistance() {
		SetResistanceChances(initial: 100, continued: 100);
		City city = CityOf(us, 3, them);
		Resistance.StartResisting(gameData, city);

		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);

		Assert.Equal(3, Resistance.Update(gameData, city));
		Assert.False(city.IsInResistance());
	}

	[Fact]
	public void ResistersStarveFirst() {
		SetResistanceChances(initial: 100, continued: 100);
		City city = CityOf(us, 3, them);
		Resistance.StartResisting(gameData, city);
		CityResident loyal = new() { city = city, nationality = us.civilization, citizenType = gameData.citizenTypes.Find(c => c.IsDefaultCitizen) };
		city.AddCitizen(loyal);
		city.foodStored = -100;

		city.HandleCityGrowth(gameData);

		Assert.Contains(loyal, city.residents);
		Assert.Equal(2, city.residents.Count(r => r.resisting));
	}

	[Fact]
	public void ResistersAreSavedAndLoaded() {
		SetResistanceChances(initial: 100, continued: 100);
		City city = CityOf(us, 3, them);
		Resistance.StartResisting(gameData, city);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);

		City loaded = reloaded.cities.Find(c => c.name == "Target");
		Assert.Equal(3, loaded.residents.Count(r => r.resisting));
		Assert.Equal(6, reloaded.cultureOpinions.Count);
	}
}
