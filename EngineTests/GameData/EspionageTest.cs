using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

public class EspionageTest : IClassFixture<SaveGameFixture>, System.IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player actor;
	private readonly Player target;
	private readonly Player third;

	// Rolls that always succeed and always fail.
	private const int Succeed = 0;
	private const int Fail = 99;

	public EspionageTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler)).ToArray();
		actor = civs[0];
		target = civs[1];
		third = civs[2];
		Tech writing = gameData.techs.Find(t => t.Name == Espionage.EmbassyTechName);
		if (writing != null) {
			actor.knownTechs.Add(writing.id);
		}
		actor.gold = 100000;
		target.gold = 100;
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private City FoundCity(Player player, int size, Tile site = null) {
		Tile tile = site ?? player.units.First(u => u.unitType.isSettler).location;
		City city = CityInteractions.BuildCity(tile, player, player.GetNextCityName());
		while (city.residents.Count < size) {
			city.AddCitizen(new CityResident() {
				city = city,
				citizenType = city.residents[0].citizenType,
				tileWorked = city.residents[0].tileWorked,
				nationality = player.civilization,
			});
		}
		foreach (Player p in gameData.players) {
			p.tileKnowledge.knownTiles.Add(tile);
		}
		return city;
	}

	// A second city for the player, next to its first.
	private City FoundSecondCity(Player player, int size) {
		City capital = player.cities.First();
		Tile site = capital.location.neighbors.Values.SelectMany(t => t.neighbors.Values)
			.First(t => t != Tile.NONE && t.IsLand() && !t.HasCity() && t.DistanceTo(capital.location) >= 2);
		return FoundCity(player, size, site);
	}

	private void Meet(Player a, Player b) {
		a.EnsureRelationshipExists(b);
	}

	private void GiveIntelligenceAgency(Player player) {
		SaveBuilding sb = new() { name = "Intelligence Agency" };
		sb.flags.Add(SaveBuilding.Flag.AllowsSpyMissions);
		player.cities.First().AddBuilding(new Building(sb, gameData));
	}

	private City SetUpEmbassy() {
		FoundCity(actor, 3);
		City city = FoundCity(target, 4);
		Meet(actor, target);
		actor.playerRelationships[target.id].hasEmbassy = true;
		return city;
	}

	// The embassy is the builder's alone: the civ it is built with gets
	// none (no report, no Foreign Advisor knowledge of the builder), but is
	// told, and agreements open to both: "When your civilization builds an
	// embassy in a rival's capital city, or when a rival builds one in your
	// capital, you can negotiate" (Conquests Civilopedia, GCON_Agreements).
	[Fact]
	public void AnEmbassyIsTheBuildersAloneButOpensAgreementsToBoth() {
		FoundCity(actor, 3);
		FoundCity(target, 3);
		Meet(actor, target);
		target.isHuman = true;
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }

		Assert.True(Espionage.Perform(gameData, actor, EspionageMission.EstablishEmbassy, target, null, Fail).success);

		Assert.True(Espionage.HasEmbassy(actor, target));
		Assert.False(Espionage.HasEmbassy(target, actor));
		Assert.True(ForeignReport.HasEmbassyWith(actor, target));
		Assert.False(ForeignReport.HasEmbassyWith(target, actor));
		Assert.DoesNotContain("We have: An Embassy", ForeignReport.Details(gameData, target, actor));
		Assert.Null(target.playerRelationships[actor.id].embassyReport);

		Assert.True(Pacts.HasEmbassyEitherWay(actor, target));
		Assert.True(Pacts.HasEmbassyEitherWay(target, actor));
		Assert.Null(RightOfPassage.ProblemSigning(gameData, actor, target));
		Assert.Null(RightOfPassage.ProblemSigning(gameData, target, actor));

		List<MessageToUI> messages = new();
		while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI m)) {
			messages.Add(m);
		}
		Assert.Contains(messages, m => m is MsgShowForeignAdvisorPopup p && p.recipient == target
			&& p.message.Contains("established an embassy in our capital city"));
	}

	[Fact]
	public void EmbassyNeedsContactAndThenIsEstablished() {
		FoundCity(actor, 3);
		FoundCity(target, 3);
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.EstablishEmbassy, target, null));

		Meet(actor, target);
		int cost = Espionage.Cost(gameData, actor, EspionageMission.EstablishEmbassy, target, null);
		Assert.Equal(100, Espionage.SuccessPercent(gameData, actor, EspionageMission.EstablishEmbassy, target, null));

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.EstablishEmbassy, target, null, Fail);
		Assert.True(result.success);
		Assert.True(Espionage.HasEmbassy(actor, target));
		Assert.False(Espionage.HasEmbassy(target, actor));
		Assert.Equal(100000 - cost, actor.gold);

		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.EstablishEmbassy, target, null));
	}

	[Fact]
	public void EmbassyReportsTheCapitalAndRevealsItsRadius() {
		FoundCity(actor, 3);
		City capital = FoundCity(target, 4);
		capital.SetStoredShields(12);
		Meet(actor, target);
		gameData.turn = 7;
		List<Tile> radius = capital.location.GetTilesWithinRankDistance(target.rules.MaxRankOfWorkableTiles)
			.Where(t => t != Tile.NONE).ToList();
		Assert.Contains(radius, t => !actor.tileKnowledge.isTileKnown(t));

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.EstablishEmbassy, target, null, Succeed);
		Assert.True(result.success);
		Espionage.CityReport report = actor.playerRelationships[target.id].embassyReport;
		Assert.NotNull(report);
		Assert.Equal(7, report.turn);
		Assert.Equal(capital.name, report.cityName);
		Assert.Equal(4, report.size);
		Assert.Equal(12, report.shieldsStored);
		Assert.All(radius, t => Assert.True(actor.tileKnowledge.isTileKnown(t)));
		Assert.Equal(capital, result.city);

		// The report is of the capital as it was, not as it is.
		capital.SetStoredShields(30);
		Assert.Equal(12, actor.playerRelationships[target.id].embassyReport.shieldsStored);
	}

	// The land around the capital is shown as it is while the embassy's
	// view of it is open, and then is fogged, though still known.
	[Fact]
	public void PeekedTilesAreInViewUntilThePeekEnds() {
		FoundCity(actor, 3);
		City capital = FoundCity(target, 4);
		Meet(actor, target);
		Espionage.Perform(gameData, actor, EspionageMission.EstablishEmbassy, target, null, Succeed);
		List<Tile> radius = Espionage.CityRadius(capital);
		Tile far = radius.First(t => !actor.tileKnowledge.isActiveTile(t));

		actor.tileKnowledge.Peek(radius);
		Assert.All(radius, t => Assert.True(actor.tileKnowledge.isActiveTile(t)));

		actor.tileKnowledge.EndPeek();
		Assert.False(actor.tileKnowledge.isActiveTile(far));
		Assert.True(actor.tileKnowledge.isTileKnown(far));
	}

	[Fact]
	public void EmbassyNeedsWriting() {
		Tech writing = gameData.techs.Find(t => t.Name == Espionage.EmbassyTechName);
		Assert.NotNull(writing);
		FoundCity(actor, 3);
		FoundCity(target, 3);
		Meet(actor, target);
		actor.knownTechs.Remove(writing.id);
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.EstablishEmbassy, target, null));
	}

	[Fact]
	public void DiplomaticMissionsNeedAnEmbassyAndGold() {
		FoundCity(actor, 3);
		City city = FoundCity(target, 4);
		Meet(actor, target);
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.InvestigateCity, target, city));

		actor.playerRelationships[target.id].hasEmbassy = true;
		Assert.Null(Espionage.Unavailable(gameData, actor, EspionageMission.InvestigateCity, target, city));

		actor.gold = 0;
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.InvestigateCity, target, city));
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.InvestigateCity, target, city, Succeed);
		Assert.False(result.performed);
		Assert.Equal(0, actor.gold);
	}

	[Fact]
	public void InvestigatingReportsTheCity() {
		City city = SetUpEmbassy();
		city.SetStoredShields(17);

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.InvestigateCity, target, city, Fail);
		Assert.True(result.success);
		Assert.Equal(city.name, result.report.cityName);
		Assert.Equal(4, result.report.size);
		Assert.Equal(17, result.report.shieldsStored);
		Assert.Contains(result.report.buildings, b => b == city.constructed_buildings[0].building.name);
		Assert.Equal(target.gold, result.report.ownerGold);
		// As in Civ3, the city is shown on the city screen, with Civ3's words.
		Assert.Equal(city, result.city);
		Assert.Equal($"We have conducted an investigation of the city of {city.name}.", result.message);
	}

	[Fact]
	public void StealingTechnologyGivesAnAdvanceTheTargetKnows() {
		City city = SetUpEmbassy();
		Tech stealable = gameData.techs.First(t => t.Prerequisites.All(p => actor.knownTechs.Contains(p.id)) && !actor.knownTechs.Contains(t.id));
		foreach (Tech t in gameData.techs) {
			if (t != stealable && !actor.knownTechs.Contains(t.id)) {
				target.knownTechs.Remove(t.id);
			}
		}
		target.knownTechs.Add(stealable.id);

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.StealTechnology, target, city, Succeed);
		Assert.True(result.success);
		Assert.Equal(stealable, result.stolenTech);
		Assert.Contains(stealable.id, actor.knownTechs);

		// Nothing left to steal.
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.StealTechnology, target, city));
	}

	// A city with a spy planted in its civ, and the Intelligence Agency.
	private City SetUpSpy() {
		City city = SetUpEmbassy();
		GiveIntelligenceAgency(actor);
		actor.playerRelationships[target.id].hasSpy = true;
		return city;
	}

	[Fact]
	public void CaughtMissionsCostGoldCauseAnIncidentAndLoseTheSpy() {
		City city = SetUpSpy();
		city.SetStoredShields(20);
		int cost = Espionage.Cost(gameData, actor, EspionageMission.SabotageProduction, target, city);

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.SabotageProduction, target, city, Fail);
		Assert.True(result.performed);
		Assert.False(result.success);
		Assert.True(result.caught);
		Assert.Equal(20, city.shieldsStored);
		Assert.Equal(100000 - cost, actor.gold);
		Assert.Equal(1, target.playerRelationships[actor.id].espionageIncidents);
		Assert.False(Espionage.HasSpy(actor, target));
	}

	// Between the success chance and the chance of getting away, a theft
	// fails but nobody notices: the 80% second roll is for Steal Technology
	// and Steal World Map only (Perez's decompile, Round 2d).
	[Fact]
	public void FailedTheftsCanGoUnnoticed() {
		SetUpSpy();
		int success = Espionage.SuccessPercent(gameData, actor, EspionageMission.StealWorldMap, target, null);
		Assert.Equal(Espionage.GetawayPercent(Espionage.Agent.RegularSpy) * 80 / 100, success);

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.StealWorldMap, target, null, success);
		Assert.False(result.success);
		Assert.False(result.caught);
		Assert.Equal(0, target.playerRelationships[actor.id].espionageIncidents);
		Assert.True(Espionage.HasSpy(actor, target));
	}

	// Steal Plans, Sabotage Production and Expose Enemy Spy use the success
	// roll alone: failing it is being caught (Perez's decompile, Round 2d).
	[Fact]
	public void SabotageHasNoSecondRoll() {
		City city = SetUpSpy();
		city.SetStoredShields(20);
		int success = Espionage.SuccessPercent(gameData, actor, EspionageMission.SabotageProduction, target, city);
		Assert.Equal(Espionage.GetawayPercent(Espionage.Agent.RegularSpy), success);

		Espionage.MissionResult worked = Espionage.Perform(gameData, actor, EspionageMission.SabotageProduction, target, city, success - 1, exposedRoll: 99);
		Assert.True(worked.success);
		Assert.True(Espionage.HasSpy(actor, target));

		city.SetStoredShields(20);
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.SabotageProduction, target, city, success);
		Assert.False(result.success);
		Assert.True(result.caught);
		Assert.Equal(20, city.shieldsStored);
		Assert.False(Espionage.HasSpy(actor, target));
	}

	// The spy may be exposed and lost even though the mission worked: Steal
	// Plans 10%, Sabotage Production 20%, Expose Enemy Spy 10% (Perez's
	// decompile, Round 2d).
	[Fact]
	public void ASuccessfulSpyMayBeExposedAnyway() {
		Assert.Equal(10, Espionage.ExposedAfterSuccessPercent(EspionageMission.StealPlans));
		Assert.Equal(20, Espionage.ExposedAfterSuccessPercent(EspionageMission.SabotageProduction));
		Assert.Equal(10, Espionage.ExposedAfterSuccessPercent(EspionageMission.ExposeEnemySpy));
		Assert.Equal(0, Espionage.ExposedAfterSuccessPercent(EspionageMission.StealTechnology));

		City city = SetUpSpy();
		city.SetStoredShields(20);
		Espionage.MissionResult kept = Espionage.Perform(gameData, actor, EspionageMission.SabotageProduction, target, city, Succeed, exposedRoll: 20);
		Assert.True(kept.success);
		Assert.False(kept.exposed);
		Assert.True(Espionage.HasSpy(actor, target));

		city.SetStoredShields(20);
		Espionage.MissionResult lost = Espionage.Perform(gameData, actor, EspionageMission.SabotageProduction, target, city, Succeed, exposedRoll: 19);
		Assert.True(lost.success);
		Assert.True(lost.exposed);
		Assert.Equal(0, city.shieldsStored);
		Assert.False(Espionage.HasSpy(actor, target));
		Assert.Equal(1, target.playerRelationships[actor.id].espionageIncidents);
	}

	[Fact]
	public void SabotageNeedsTheAgencyAndAPlantedSpy() {
		City city = SetUpEmbassy();
		city.SetStoredShields(20);
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.SabotageProduction, target, city));
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.SabotageProduction, target, city, Succeed);
		Assert.False(result.performed);
		Assert.Equal(20, city.shieldsStored);

		GiveIntelligenceAgency(actor);
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.SabotageProduction, target, city));

		actor.playerRelationships[target.id].hasSpy = true;
		Assert.Null(Espionage.Unavailable(gameData, actor, EspionageMission.SabotageProduction, target, city));
	}

	[Fact]
	public void SpyMissionsNeedAPlantedSpy() {
		City city = SetUpEmbassy();
		GiveIntelligenceAgency(actor);
		foreach (EspionageMission mission in new[] { EspionageMission.StealWorldMap, EspionageMission.StealPlans,
			EspionageMission.Propaganda, EspionageMission.ExposeEnemySpy }) {
			Assert.NotNull(Espionage.Unavailable(gameData, actor, mission, target, city));
		}
	}

	[Fact]
	public void SabotageDestroysProduction() {
		City city = SetUpSpy();
		city.SetStoredShields(20);

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.SabotageProduction, target, city, Succeed);
		Assert.True(result.success);
		Assert.Equal(0, city.shieldsStored);
	}

	// The study's chances for missions sent immediately: 80% of getting
	// away, which is 40% from an embassy and 60% for a regular spy.
	[Fact]
	public void ASpyStealsBetterThanAnEmbassy() {
		City city = SetUpEmbassy();
		Assert.Equal(32, Espionage.SuccessPercent(gameData, actor, EspionageMission.StealTechnology, target, city));
		GiveIntelligenceAgency(actor);
		actor.playerRelationships[target.id].hasSpy = true;
		Assert.Equal(48, Espionage.SuccessPercent(gameData, actor, EspionageMission.StealTechnology, target, city));
	}

	// "3/2 for carefully and 2 for" safely, for the study's better odds:
	// an embassy gets away 40/55/65% of the time, succeeding 32/44/52%.
	// Missions that always succeed are sent immediately whatever is asked.
	[Fact]
	public void CarefulAndSafeMissionsCostMoreForBetterOdds() {
		City city = SetUpEmbassy();
		actor.gold = 100000;
		int immediate = Espionage.Cost(gameData, actor, EspionageMission.StealTechnology, target, city);
		Assert.Equal(immediate * 3 / 2, Espionage.Cost(gameData, actor, EspionageMission.StealTechnology, target, city, EspionageRisk.Carefully));
		Assert.Equal(immediate * 2, Espionage.Cost(gameData, actor, EspionageMission.StealTechnology, target, city, EspionageRisk.Safely));
		Assert.Equal(44, Espionage.SuccessPercent(gameData, actor, EspionageMission.StealTechnology, target, city, EspionageRisk.Carefully));
		Assert.Equal(52, Espionage.SuccessPercent(gameData, actor, EspionageMission.StealTechnology, target, city, EspionageRisk.Safely));

		int investigate = Espionage.Cost(gameData, actor, EspionageMission.InvestigateCity, target, city);
		Assert.Equal(investigate, Espionage.Cost(gameData, actor, EspionageMission.InvestigateCity, target, city, EspionageRisk.Safely));

		// The gold spent is the safe price.
		int gold = actor.gold;
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.StealTechnology, target, city, Fail, EspionageRisk.Safely);
		Assert.Equal(immediate * 2, result.goldSpent);
		Assert.Equal(gold - immediate * 2, actor.gold);
	}

	// "Plant Spy  60 dist*level + base": a base of 60 plus the distance
	// from their capital to our nearest city times the capital's level.
	[Fact]
	public void CostsFollowTheStudysFormulas() {
		City capital = SetUpEmbassy();
		int distance = actor.cities.Min(c => c.location.DistanceTo(capital.location));
		Assert.Equal(1, Espionage.SettlementLevel(capital));
		Assert.Equal(distance + 60, Espionage.Cost(gameData, actor, EspionageMission.PlantSpy, target, null));
		Assert.Equal(distance + 80, Espionage.Cost(gameData, actor, EspionageMission.ExposeEnemySpy, target, null));
		Assert.Equal(distance + 20 + 4, Espionage.Cost(gameData, actor, EspionageMission.EstablishEmbassy, target, null));
		Assert.Equal(distance + 10 * 4, Espionage.Cost(gameData, actor, EspionageMission.InvestigateCity, target, capital));
		capital.SetStoredShields(25);
		Assert.Equal(distance + 10 * 25, Espionage.Cost(gameData, actor, EspionageMission.SabotageProduction, target, capital));
	}

	// The final price is "((2*size - nativesOfSpyCiv + 1) * (cost +
	// (sizeClass+1)*C)) / (2*size + 1)" (Perez's decompile, Round 2d): a city
	// of our own people is cheaper to work on.
	[Fact]
	public void CitizensOfOurNationalityMakeMissionsCheaper() {
		City capital = SetUpEmbassy();
		int distance = actor.cities.Min(c => c.location.DistanceTo(capital.location));
		capital.residents[0].nationality = actor.civilization;
		capital.residents[1].nationality = actor.civilization;
		// Size 4, 2 of ours: (8 - 2 + 1) / (8 + 1).
		Assert.Equal((distance + 60) * 7 / 9, Espionage.Cost(gameData, actor, EspionageMission.PlantSpy, target, null));
		Assert.Equal((distance + 10 * 4) * 7 / 9, Espionage.Cost(gameData, actor, EspionageMission.InvestigateCity, target, capital));
	}

	// The editor help's propaganda cost has a gold term: "BaseCost *
	// Target City Size + ( BaseCost * Target City Size * Culture Ratio) +
	// ( ( BaseCost * Target City Size) / 2 * Gold Ratio )".
	[Fact]
	public void PropagandaCostsMoreAgainstARicherTarget() {
		City capital = SetUpEmbassy();
		int distance = actor.cities.Min(c => c.location.DistanceTo(capital.location));
		int pop = capital.residents.Count;
		target.gold = 0;
		actor.gold = 100;
		int poor = Espionage.Cost(gameData, actor, EspionageMission.Propaganda, target, capital);
		Assert.Equal(distance + 100 * pop, poor);

		// Three times our gold adds half the size term three times over.
		target.gold = 300;
		Assert.Equal(poor + 100 * pop / 2 * 3, Espionage.Cost(gameData, actor, EspionageMission.Propaganda, target, capital));
	}

	[Fact]
	public void StealingTheWorldMapRevealsTheirTiles() {
		SetUpSpy();
		List<Tile> theirs = gameData.map.tiles.Where(t => !actor.tileKnowledge.isTileKnown(t)).Take(5).ToList();
		Assert.Equal(5, theirs.Count);
		foreach (Tile t in theirs) {
			target.tileKnowledge.knownTiles.Add(t);
		}

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.StealWorldMap, target, null, Succeed);
		Assert.True(result.success);
		Assert.All(theirs, t => Assert.True(actor.tileKnowledge.isTileKnown(t)));
		Assert.True(result.tilesRevealed >= 5);
	}

	[Fact]
	public void StealingPlansShowsTheirTroopsUntilNextTurn() {
		City capital = SetUpSpy();
		UnitPrototype warrior = capital.ListProductionOptions(gameData).OfType<UnitPrototype>().First(u => u.attack > 0);
		capital.AddUnit(warrior, gameData);
		Tile tile = capital.location;
		actor.tileKnowledge.knownTiles.Remove(tile);

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.StealPlans, target, null, Succeed);
		Assert.True(result.success);
		Assert.Contains(tile, result.unitTiles);
		Assert.True(actor.tileKnowledge.isTileKnown(tile));
		Assert.True(actor.tileKnowledge.isActiveTile(tile));

		Espionage.DoPerTurnUpdates(actor, gameData);
		Assert.True(actor.tileKnowledge.isTileKnown(tile));
	}

	// "If at least 3/4 of the citizen are persuading, the city will flip."
	[Fact]
	public void PropagandaFlipsTheCityAtThreeQuarters() {
		Assert.True(Espionage.PropagandaFlips(3, 4));
		Assert.False(Espionage.PropagandaFlips(2, 4));
		Assert.False(Espionage.PropagandaFlips(2, 3));
		Assert.True(Espionage.PropagandaFlips(3, 3));

		SetUpSpy();
		City city = FoundSecondCity(target, 4);
		Espionage.ApplyPropaganda(gameData, actor, city, 2);
		Assert.Equal(target, city.owner);
		Assert.Equal(2, city.propagandaConfusedCitizens);

		// The confusion fades a citizen a turn, and isn't cumulative.
		Espionage.ApplyPropaganda(gameData, actor, city, 1);
		Assert.Equal(2, city.propagandaConfusedCitizens);
		Espionage.DoPerTurnUpdates(target, gameData);
		Assert.Equal(1, city.propagandaConfusedCitizens);

		Espionage.ApplyPropaganda(gameData, actor, city, 3);
		Assert.Equal(actor, city.owner);
		Assert.Contains(city, actor.cities);
	}

	// "You will never get caught."
	[Fact]
	public void PropagandaIsNeverCaught() {
		SetUpSpy();
		City city = FoundSecondCity(target, 4);
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.Propaganda, target, city, Fail);
		Assert.True(result.performed);
		Assert.False(result.success);
		Assert.Equal(target, city.owner);
		Assert.Equal(0, target.playerRelationships[actor.id].espionageIncidents);
		Assert.True(Espionage.HasSpy(actor, target));
	}

	[Fact]
	public void PropagandaWinsOverPersuadedCities() {
		SetUpSpy();
		City city = FoundSecondCity(target, 4);
		// Our own people are easier to persuade.
		foreach (CityResident resident in city.residents) {
			resident.nationality = actor.civilization;
		}
		Assert.True(Espionage.PersuasionPercent(gameData, actor, city, city.residents[0]) > 0);

		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.Propaganda, target, city, Succeed);
		Assert.True(result.success);
		Assert.Equal(4, result.persuaded);
		Assert.Equal(actor, city.owner);
	}

	[Fact]
	public void DemocraciesAreImmuneToPropaganda() {
		SetUpSpy();
		City city = FoundSecondCity(target, 4);
		Government democracy = gameData.governments.Find(g => g.name == "Democracy");
		Assert.NotNull(democracy);
		target.government = democracy;
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.Propaganda, target, city));
	}

	[Fact]
	public void AFailedPlantKeepsUsFromTryingAgainForAWhile() {
		SetUpEmbassy();
		GiveIntelligenceAgency(actor);
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.PlantSpy, target, null, Fail);
		Assert.False(result.success);
		Assert.True(actor.playerRelationships[target.id].spyPlantFailed);
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.PlantSpy, target, null));

		// A one in three chance each turn of clearing.
		for (int i = 0; i < 200 && actor.playerRelationships[target.id].spyPlantFailed; i++) {
			Espionage.DoPerTurnUpdates(actor, gameData);
		}
		Assert.False(actor.playerRelationships[target.id].spyPlantFailed);
		Assert.Null(Espionage.Unavailable(gameData, actor, EspionageMission.PlantSpy, target, null));
	}

	[Fact]
	public void ATransferredCityForgetsTheOldOwnersProductionQueue() {
		SetUpEmbassy();
		City city = FoundSecondCity(target, 3);
		city.EnqueueProduction(city.ListProductionOptions(gameData).First());
		MapUnit defender = target.units.First(u => !u.unitType.isSettler && u.location != null);
		defender.location.unitsOnTile.Remove(defender);
		defender.location = city.location;
		city.location.unitsOnTile.Add(defender);

		CityInteractions.TransferCity(city, actor);

		Assert.Equal(actor, city.owner);
		Assert.Empty(city.productionQueue);
		Assert.Equal(actor, defender.owner);
		Assert.DoesNotContain(city.location.unitsOnTile, u => u.owner == target);
	}

	[Fact]
	public void UndefinedMissionsAreRejected() {
		City city = SetUpEmbassy();
		EspionageMission bogus = (EspionageMission)999;

		Assert.NotNull(Espionage.Unavailable(gameData, actor, bogus, target, city));
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, bogus, target, city, Succeed);
		Assert.False(result.performed);
	}

	[Fact]
	public void SpiesNeedTheIntelligenceAgency() {
		SetUpEmbassy();
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.PlantSpy, target, null));

		GiveIntelligenceAgency(actor);
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.PlantSpy, target, null, Succeed);
		Assert.True(result.success);
		Assert.True(Espionage.HasSpy(actor, target));
	}

	// "If you suspect a rival has planted a spy in one of your cities you
	// can attempt to expose the intruder."
	[Fact]
	public void ExposingAnEnemySpyRemovesTheirSpyFromUs() {
		SetUpSpy();
		Meet(target, actor);
		Assert.Equal(0, Espionage.SuccessPercent(gameData, actor, EspionageMission.ExposeEnemySpy, target, null));
		Espionage.MissionResult nothing = Espionage.Perform(gameData, actor, EspionageMission.ExposeEnemySpy, target, null, Succeed);
		Assert.False(nothing.success);
		Assert.False(nothing.caught);

		target.playerRelationships[actor.id].hasSpy = true;
		Espionage.MissionResult result = Espionage.Perform(gameData, actor, EspionageMission.ExposeEnemySpy, target, null, Succeed);
		Assert.True(result.success);
		Assert.Equal(target, result.exposedSpyOwner);
		Assert.False(target.playerRelationships[actor.id].hasSpy);
		Assert.Equal(1, actor.playerRelationships[target.id].espionageIncidents);
	}

	[Fact]
	public void AIEstablishesEmbassiesItCanAfford() {
		FoundCity(actor, 3);
		FoundCity(target, 3);
		Meet(actor, target);
		actor.isHuman = false;
		actor.gold = 0;

		C7Engine.AI.EspionageAI.PlayTurn(actor, gameData);
		Assert.False(Espionage.HasEmbassy(actor, target));

		actor.gold = 1000;
		C7Engine.AI.EspionageAI.PlayTurn(actor, gameData);
		Assert.True(Espionage.HasEmbassy(actor, target));
		Assert.True(actor.gold >= 100);
	}

	// "your embassy in their capitol is automatically closed", but "Even if
	// you go to war and your embassy is closed, your spy will continue to
	// function" (Civ3.com FAQ).
	[Fact]
	public void WarClosesEmbassiesButPlantedSpiesKeepWorking() {
		City city = SetUpSpy();
		target.playerRelationships[actor.id].hasEmbassy = true;
		actor.playerRelationships[target.id].embassyReport = Espionage.Investigate(city, gameData.turn);
		city.SetStoredShields(20);

		actor.DeclareWarOn(target, gameData.turn);

		Assert.False(Espionage.HasEmbassy(actor, target));
		Assert.False(Espionage.HasEmbassy(target, actor));
		Assert.Null(actor.playerRelationships[target.id].embassyReport);
		Assert.True(Espionage.HasSpy(actor, target));
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.InvestigateCity, target, city));
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.StealTechnology, target, null));
		Assert.NotNull(Espionage.Unavailable(gameData, actor, EspionageMission.EstablishEmbassy, target, null));
		Assert.Null(Espionage.Unavailable(gameData, actor, EspionageMission.SabotageProduction, target, city));
	}

	// An embassy reopened at peace has the capital report it had when it
	// was founded (owner's decision, 2026-10-21), kept through the war and
	// across a save.
	[Fact]
	public void AReopenedEmbassyKeepsItsCapitalReport() {
		City city = SetUpEmbassy();
		actor.playerRelationships[target.id].embassyReport = Espionage.Investigate(city, 3);
		Assert.True(Espionage.HasEmbassy(actor, target));

		actor.DeclareWarOn(target, gameData.turn);
		Assert.Null(actor.playerRelationships[target.id].embassyReport);
		SaveGame save = SaveGame.FromGameData(gameData);
		SavePlayer saved = SaveGame.FromJSON(save.ToCompactJSON()).Players.First(p => p.id.ToString() == actor.id.ToString());
		Assert.Equal(3, saved.playerRelationships[target.id.ToString()].closedEmbassyReport.turn);

		PlayerRelationship.SignPeaceAfterWar(actor, target, gameData);
		Assert.True(Espionage.HasEmbassy(actor, target));
		Espionage.CityReport report = actor.playerRelationships[target.id].embassyReport;
		Assert.NotNull(report);
		Assert.Equal(3, report.turn);
		Assert.Equal(city.name, report.cityName);
		Assert.Null(actor.playerRelationships[target.id].closedEmbassyReport);
	}

	// After peace the embassy must be established again, which the AI does.
	[Fact]
	public void AIReestablishesItsEmbassyAfterPeace() {
		SetUpEmbassy();
		actor.isHuman = false;
		actor.DeclareWarOn(target, gameData.turn);
		C7Engine.AI.EspionageAI.PlayTurn(actor, gameData);
		Assert.False(Espionage.HasEmbassy(actor, target));

		PlayerRelationship.SignPeaceAfterWar(actor, target, gameData);
		C7Engine.AI.EspionageAI.PlayTurn(actor, gameData);
		Assert.True(Espionage.HasEmbassy(actor, target));
	}

	// Civ3's AI_ConsiderEspionage rolls Rand(range), range 200000 .. 20 by
	// relationship level, halved with an embassy; 0-1 steal tech, 2 steal
	// map, 3 steal plans, 4-5 propaganda, 6-7 sabotage, 8-9 expose mole
	// (Perez's decompile, Round 2d). The level is the AI's attitude, most
	// hostile most often.
	[Fact]
	public void AIEspionageFollowsCiv3sRolls() {
		Assert.Equal(200000, C7Engine.AI.EspionageAI.RollRange(AttitudeLevel.Gracious, hasEmbassy: false));
		Assert.Equal(1000, C7Engine.AI.EspionageAI.RollRange(AttitudeLevel.Cautious, hasEmbassy: true));
		Assert.Equal(20, C7Engine.AI.EspionageAI.RollRange(AttitudeLevel.Furious, hasEmbassy: false));
		Assert.Equal(10, C7Engine.AI.EspionageAI.RollRange(AttitudeLevel.Furious, hasEmbassy: true));
		EspionageMission?[] expected = [
			EspionageMission.StealTechnology, EspionageMission.StealTechnology, EspionageMission.StealWorldMap,
			EspionageMission.StealPlans, EspionageMission.Propaganda, EspionageMission.Propaganda,
			EspionageMission.SabotageProduction, EspionageMission.SabotageProduction,
			EspionageMission.ExposeEnemySpy, EspionageMission.ExposeEnemySpy, null,
		];
		for (int roll = 0; roll < expected.Length; roll++) {
			Assert.Equal(expected[roll], C7Engine.AI.EspionageAI.MissionForRoll(roll));
		}
	}

	// Steal Technology needs only an embassy, which war closes, so the AI
	// steals from civs at peace with it. A furious AI, with an embassy,
	// rolls Rand(10), stealing on 0-1.
	[Fact]
	public void AIStealsTechnologyThroughItsEmbassyAtPeace() {
		SetUpEmbassy();
		actor.isHuman = false;
		actor.playerRelationships[target.id].espionageIncidents = 50;
		Assert.Equal(AttitudeLevel.Furious, Attitudes.Level(gameData, actor, target));
		Tech stealable = gameData.techs.First(t => t.Prerequisites.All(p => actor.knownTechs.Contains(p.id)) && !actor.knownTechs.Contains(t.id));
		foreach (Tech t in gameData.techs) {
			if (t != stealable && !actor.knownTechs.Contains(t.id)) {
				target.knownTechs.Remove(t.id);
			}
		}
		target.knownTechs.Add(stealable.id);

		for (int i = 0; i < 300 && !actor.knownTechs.Contains(stealable.id); i++) {
			actor.gold = 100000;
			C7Engine.AI.EspionageAI.PlayTurn(actor, gameData);
		}
		Assert.True(PlayerRelationship.AtPeace(actor, target));
		Assert.Contains(stealable.id, actor.knownTechs);
	}

	[Fact]
	public void EmbassiesAndSpiesAreSaved() {
		SetUpEmbassy();
		actor.playerRelationships[target.id].hasSpy = true;
		actor.playerRelationships[target.id].spyPlantFailed = true;
		target.cities.First().propagandaConfusedCitizens = 2;
		actor.playerRelationships[target.id].embassyReport = Espionage.Investigate(target.cities.First(), 5);
		target.playerRelationships[actor.id].espionageIncidents = 2;
		gameData.unitedNations.votingTurn = 42;
		gameData.unitedNations.offerTurn = 41;
		gameData.unitedNations.humanVotes["x"] = "y";

		SaveGame save = SaveGame.FromGameData(gameData);
		SaveGame reloaded = SaveGame.FromJSON(save.ToCompactJSON());
		SavePlayer savedActor = reloaded.Players.First(p => p.id.ToString() == actor.id.ToString());
		SavePlayer savedTarget = reloaded.Players.First(p => p.id.ToString() == target.id.ToString());
		Assert.True(savedActor.playerRelationships[target.id.ToString()].hasEmbassy);
		Assert.True(savedActor.playerRelationships[target.id.ToString()].hasSpy);
		Assert.True(savedActor.playerRelationships[target.id.ToString()].spyPlantFailed);
		Assert.Equal(2, reloaded.Cities.First(c => c.name == target.cities.First().name).propagandaConfusedCitizens);
		Espionage.CityReport savedReport = savedActor.playerRelationships[target.id.ToString()].embassyReport;
		Assert.Equal(5, savedReport.turn);
		Assert.Equal(target.cities.First().name, savedReport.cityName);
		Assert.Equal(2, savedTarget.playerRelationships[actor.id.ToString()].espionageIncidents);
		Assert.Equal(42, reloaded.UnitedNations.votingTurn);
		Assert.Equal(41, reloaded.UnitedNations.offerTurn);
		Assert.Equal("y", reloaded.UnitedNations.humanVotes["x"]);
	}
}
