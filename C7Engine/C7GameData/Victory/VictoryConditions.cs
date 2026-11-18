using C7Engine;

namespace C7GameData;

public class VictoryConditions {
	// TODO: default/preferred victory conditions
	public bool AllowDominationVictory { get; set; }
	public bool AllowSpaceRaceVictory { get; set; }
	public bool AllowDiplomaticVictory { get; set; }
	public bool AllowConquestVictory { get; set; }
	public bool AllowCulturalVictory { get; set; }
	public bool AllowWonderVictory { get; set; }
	// Won by holding half the world's cities (Kings & Nomads; see
	// EmpireVictory).
	public bool AllowEmpireVictory { get; set; }
	public bool CityElimination { get; set; }
	public bool Regicide { get; set; }
	public bool MassRegicide { get; set; }
	public bool VictoryLocations { get; set; }
	public bool CaptureTheFlag { get; set; }
	public bool ReverseCaptureTheFlag { get; set; }

	// Capture the Flag (see CaptureTheFlag): whether each civ's flag is
	// placed when the game starts (the GAME section's "place capture
	// units"), whether any civ may take any flag, its own too, whether a
	// flag brought in comes back at its civ's capital, and the victory
	// points and gold a flag brought in scores (the GAME section's
	// "capturing special unit", 1000 in conquests.biq, and "gold for
	// capture").
	public bool AutoPlaceCaptureUnits { get; set; } = true;
	public bool AnyoneCapturesAnyFlag { get; set; }
	public bool RespawnFlagUnits { get; set; } = true;
	public int CaptureFlagVictoryPoints { get; set; } = 1000;
	public int GoldForCapture { get; set; }

	// Whether each civ is given its King units when a game starts with
	// Regicide or Mass Regicide (the BIQ GAME section's "autoplace kings";
	// see InstantElimination.PlaceKings). Scenarios that place their kings
	// on the map leave it off.
	public bool AutoPlaceKings { get; set; } = true;

	// For Elimination, how many cities a civ may lose before it is
	// eliminated (the BIQ GAME section's "city elimination count", labelled
	// "Lose cities:" on Civ3's victory status screen). 1 in conquests.biq.
	public int CityEliminationCount { get; set; } = 1;

	// Victory points (see VictoryPoints): the limit that wins under Victory
	// Point Scoring (the BIQ's VictoryLocations flag, "Victory Point
	// Scoring" in Civ3's editor and setup screen), and the BIQ GAME
	// section's multiples for wonders (in tenths), defeated units, advances
	// and conquered cities' population.
	public int VictoryPointLimit { get; set; } = VictoryPoints.DefaultLimit;
	public int WonderVictoryPoints { get; set; } = VictoryPoints.DefaultWonderMultiple;
	public int DefeatVictoryPoints { get; set; } = VictoryPoints.DefaultDefeatMultiple;
	public int AdvanceVictoryPoints { get; set; } = VictoryPoints.DefaultAdvanceMultiple;
	public int CityConquestVictoryPoints { get; set; } = VictoryPoints.DefaultCityConquestMultiple;

	// Victory Locations: whether each civ's starting location is made one
	// when a game starts with Victory Point Scoring (the BIQ GAME section's
	// "autoplace victory locations"), and the victory points holding one
	// scores each turn (its "victory point scoring", 25 in conquests.biq).
	public bool AutoPlaceVictoryLocations { get; set; } = true;
	public int VictoryLocationPoints { get; set; } = VictoryPoints.DefaultLocationPoints;

	// Whether the game ends once the time options' turn limit is reached.
	// Off unless chosen: the game goes on until someone wins (the project
	// owner's choice). Saves made before this was saved have no limit.
	public bool UseTurnLimit { get; set; }

	// The shares of the world's land and population needed for a
	// domination victory.
	public int DominationTerritoryPercent { get; set; } = 66;
	public int DominationPopulationPercent { get; set; } = 66;

	// The culture needed for a cultural victory (see CulturalVictory): by
	// one city, or by a whole civilization. The defaults are Conquests'
	// (the BIQ's "one city culture win" and "all cities culture win").
	public int CultureOneCityWin { get; set; } = CulturalVictory.DefaultOneCityWin;
	public int CultureAllCitiesWin { get; set; } = CulturalVictory.DefaultAllCitiesWin;

	// Takes the thresholds (domination, culture, elimination, victory
	// points) and king
	// placement a scenario or ruleset sets, keeping which victory types are
	// allowed. Returns this.
	public VictoryConditions CopyThresholdsFrom(VictoryConditions other) {
		if (other != null) {
			DominationTerritoryPercent = other.DominationTerritoryPercent;
			DominationPopulationPercent = other.DominationPopulationPercent;
			CultureOneCityWin = other.CultureOneCityWin;
			CultureAllCitiesWin = other.CultureAllCitiesWin;
			CityEliminationCount = other.CityEliminationCount;
			AutoPlaceKings = other.AutoPlaceKings;
			VictoryPointLimit = other.VictoryPointLimit;
			WonderVictoryPoints = other.WonderVictoryPoints;
			DefeatVictoryPoints = other.DefeatVictoryPoints;
			AdvanceVictoryPoints = other.AdvanceVictoryPoints;
			CityConquestVictoryPoints = other.CityConquestVictoryPoints;
			AutoPlaceVictoryLocations = other.AutoPlaceVictoryLocations;
			AutoPlaceCaptureUnits = other.AutoPlaceCaptureUnits;
			AnyoneCapturesAnyFlag = other.AnyoneCapturesAnyFlag;
			RespawnFlagUnits = other.RespawnFlagUnits;
			CaptureFlagVictoryPoints = other.CaptureFlagVictoryPoints;
			GoldForCapture = other.GoldForCapture;
			VictoryLocationPoints = other.VictoryLocationPoints;
		}
		return this;
	}

	// The victory types a new game allows unless the player says otherwise,
	// and a BIQ that uses the default victory conditions gets.
	// UNVERIFIED (no Civ3 source found): which victory types Civ3 turns on
	// by default. conquests.biq doesn't say: it uses the default victory
	// conditions, and its own victory flags are all clear.
	public static VictoryConditions NewGameDefaults() {
		return new VictoryConditions {
			AllowConquestVictory = true,
			AllowDominationVictory = true,
			// Civ3 allows the space race by default.
			AllowSpaceRaceVictory = true,
			// Civ3 allows a diplomatic victory (through the United Nations)
			// by default.
			AllowDiplomaticVictory = true,
			// The project owner's choice: new games allow a cultural
			// victory.
			AllowCulturalVictory = true,
		};
	}
}
