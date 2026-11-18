namespace C7GameData {
	using QueryCiv3;
	using QueryCiv3.Biq;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json.Serialization;

	public class TerrainType {
		//The "key" is a language-independent name for the terrain.  Civ3 relies on their index in the list to know
		//what they are; we'll use the key.  This allows adding custom terrain types in the future, including having
		//different custom terrains in Mod A than Mod B, while still allowing internationalized versions of their
		//names that don't break the scenario.  E.g. "ocean"
		public string Key {
			get => key;
			set {
				key = value;
				UpdateKeyFlags();
			}
		}
		private string key = "";
		//The name is the display name.  E.g. "Ocean" in English scenarios, "Hochsee" in German scenarios.
		public string DisplayName { get; set; } = "";
		public int baseFoodProduction { get; set; }
		public int baseShieldProduction { get; set; }
		public int baseCommerceProduction { get; set; }
		public int movementCost { get; set; }
		public bool allowCities { get; set; } = true;
		public bool impassable { get; set; }
		// Wheeled units can't enter the terrain except along a road (see
		// UnitPrototype.CanMoveBetween).
		public bool impassableByWheeled { get; set; }
		// Which of a worker's holdings and forts may go up on the terrain,
		// from the BIQ's TERR flags (AllowForts, AllowAirfields,
		// AllowRadarTowers, AllowOutposts, AllowColonies). In conquests.biq
		// every land terrain allows them all except Marsh (outposts only) and
		// Volcano (none); Marsh and Volcano allow no colonies either. Saves
		// made before these were saved allow them all.
		public bool allowForts { get; set; } = true;
		public bool allowAirfields { get; set; } = true;
		public bool allowRadarTowers { get; set; } = true;
		public bool allowOutposts { get; set; } = true;
		public bool allowColonies { get; set; } = true;
		public StrengthBonus defenseBonus;
		public HashSet<string> allowedResources = new();
		public int height = -1;

		// Disease (see Disease): whether cities near the terrain and units
		// fortified on it can be struck by it, whether a tech ends it (the
		// tech with the "Disables Diseases" flag, Sanitation; see Disease), and how
		// strong it is. From the BIQ's terrain
		// flags and "disease strength". Saves made before these were saved
		// have no disease.
		public bool causesDisease { get; set; }
		public bool diseaseCanBeCured { get; set; }
		public int diseaseStrength { get; set; }

		// What global warming turns the terrain into (see GlobalWarming):
		// the BIQ's TERR "Pollution Effect", "the terrain type (if any) that
		// a tile of the selected terrain type will become" (editor help). The
		// key of another terrain; PollutionEffectBaseTerrain, the BIQ's
		// "Base Terrain Type" (index 14), for the terrain under the tile's
		// forest or jungle; or PollutionEffectNone. conquests.biq has
		// Grassland -> Plains, Plains -> Desert, Forest and Jungle -> base
		// terrain, and Marsh -> Coast. Saves made before this was saved have
		// null, read as conquests.biq's (see PollutionEffect).
		public string pollutionEffect { get; set; }
		public const string PollutionEffectNone = "none";
		public const string PollutionEffectBaseTerrain = "base terrain";

		// These enum and field are kept for compatibility with CIV3 saves.
		public enum Civ3FoliageAction {
			None,
			ClearForest,
			ClearWetlands,
			PlantForest
		}
		public Civ3FoliageAction allowedFoliageAction = Civ3FoliageAction.None;

		// Civ3's landmark terrain: a second set of values for the type,
		// painted as its own terrain (the BIQ TERR's landmark fields). Kept
		// for scenarios; the game has no landmark tiles yet. Null if the
		// rules give none.
		public TerrainLandmark landmark;

		//some stuff about graphics would probably make sense, too

		// Checks of the key are made in very hot code (pathing, visibility,
		// yields), so they are computed once whenever the key is set rather
		// than comparing strings on every call. Key is the only input and it
		// can only change through its setter, so these are always current,
		// however the terrain was constructed or deserialized.
		[JsonIgnore] public bool IsDesert { get; private set; }
		[JsonIgnore] public bool IsPlains { get; private set; }
		[JsonIgnore] public bool IsGrassland { get; private set; }
		[JsonIgnore] public bool IsTundra { get; private set; }
		[JsonIgnore] public bool IsFloodPlain { get; private set; }
		[JsonIgnore] public bool IsHills { get; private set; }
		[JsonIgnore] public bool IsMountains { get; private set; }
		[JsonIgnore] public bool IsForest { get; private set; }
		[JsonIgnore] public bool IsJungle { get; private set; }
		[JsonIgnore] public bool IsMarsh { get; private set; }
		[JsonIgnore] public bool IsVolcano { get; private set; }
		[JsonIgnore] public bool IsCoast { get; private set; }
		[JsonIgnore] public bool IsSea { get; private set; }
		[JsonIgnore] public bool IsOcean { get; private set; }
		// Coast, sea or ocean.
		[JsonIgnore] public bool IsWater { get; private set; }
		// Mountains, hills or volcano.
		[JsonIgnore] public bool IsHilly { get; private set; }

		private void UpdateKeyFlags() {
			string k = key;
			IsDesert = k == "desert";
			IsPlains = k == "plains";
			IsGrassland = k == "grassland";
			IsTundra = k == "tundra";
			IsFloodPlain = k == "flood plain";
			IsHills = k == "hills";
			IsMountains = k == "mountains";
			IsForest = k == "forest";
			IsJungle = k == "jungle";
			IsMarsh = k == "marsh";
			IsVolcano = k == "volcano";
			IsCoast = k == "coast";
			IsSea = k == "sea";
			IsOcean = k == "ocean";
			IsWater = IsCoast || IsSea || IsOcean;
			IsHilly = IsMountains || IsHills || IsVolcano;
		}

		public bool isHilly() {
			return IsHilly;
		}

		public bool isVolcano() {
			return IsVolcano;
		}

		public bool isWater() {
			return IsWater;
		}

		public bool isCoast() {
			return IsCoast;
		}

		public bool isSea() {
			return IsSea;
		}

		public override string ToString() {
			return DisplayName + "(" + baseFoodProduction + ", " + baseShieldProduction + ", " + baseCommerceProduction + ")";
		}

		// The terrain's pollution effect (see pollutionEffect), with
		// conquests.biq's for saves that don't have one.
		public string PollutionEffect() {
			if (pollutionEffect != null) {
				return pollutionEffect;
			}
			if (IsGrassland) return "plains";
			if (IsPlains) return "desert";
			if (IsForest || IsJungle) return PollutionEffectBaseTerrain;
			if (IsMarsh) return "coast";
			return PollutionEffectNone;
		}

		public static TerrainType NONE = new TerrainType();


		public static TerrainType ImportFromCiv3(int civ3Index, TERR civ3Terrain) {
			TerrainType c7Terrain = new() {
				Key = civTerrainKeyLookup[civ3Index],
				DisplayName = civ3Terrain.Name,
				baseFoodProduction = civ3Terrain.Food,
				baseShieldProduction = civ3Terrain.Shields,
				baseCommerceProduction = civ3Terrain.Commerce,
				movementCost = civ3Terrain.MovementCost,
				allowCities = civ3Terrain.AllowCities != 0,
				impassable = civ3Terrain.Impassable != 0,
				impassableByWheeled = civ3Terrain.ImpassableByWheeled != 0,
				allowForts = civ3Terrain.AllowForts != 0,
				allowAirfields = civ3Terrain.AllowAirfields != 0,
				allowRadarTowers = civ3Terrain.AllowRadarTowers != 0,
				allowOutposts = civ3Terrain.AllowOutposts != 0,
				allowColonies = civ3Terrain.AllowColonies != 0,
				defenseBonus = new StrengthBonus {
					description = civ3Terrain.Name,
					amount = civ3Terrain.DefenseBonus / 100.0
				},
				allowedFoliageAction = LoadFoliageAction(civ3Terrain),
				causesDisease = civ3Terrain.CausesDisease,
				diseaseCanBeCured = civ3Terrain.DiseaseCanBeCured,
				diseaseStrength = civ3Terrain.DiseaseStrength,
				pollutionEffect = civ3Terrain.PollutionEffect switch {
					14 => PollutionEffectBaseTerrain,
					int i when civTerrainKeyLookup.TryGetValue(i, out string target) => target,
					_ => PollutionEffectNone,
				},
			};

			if (c7Terrain.Key == "mountains" || c7Terrain.Key == "volcano") {
				c7Terrain.height = 4;
			} else if (c7Terrain.Key == "hills") {
				c7Terrain.height = 2;
			} else if (c7Terrain.Key == "ocean" || c7Terrain.Key == "sea" || c7Terrain.Key == "coast" || c7Terrain.Key == "marsh") {
				c7Terrain.height = -2;
			} else if (c7Terrain.Key == "forest" || c7Terrain.Key == "jungle") {
				c7Terrain.height = 1;
			} else {
				c7Terrain.height = 0;
			}

			return c7Terrain;
		}

		private static Civ3FoliageAction LoadFoliageAction(TERR civ3Terrain) {
			if (civ3Terrain.CanChopForest) return Civ3FoliageAction.ClearForest;
			if (civ3Terrain.CanClearWetlands) return Civ3FoliageAction.ClearWetlands;
			if (civ3Terrain.CanPlantForest) return Civ3FoliageAction.PlantForest;

			return Civ3FoliageAction.None;
		}

		//This only works for Conquests due to the new terrains being added in the middle of the list.
		private static Dictionary<int, string> civTerrainKeyLookup = new Dictionary<int, string>() {
			{ 0,  "desert"},
			{ 1,  "plains"},
			{ 2,  "grassland"},
			{ 3,  "tundra"},
			{ 4,  "flood plain"},
			{ 5,  "hills"},
			{ 6,  "mountains"},
			{ 7,  "forest"},
			{ 8,  "jungle"},
			{ 9,  "marsh"},
			{ 10, "volcano"},
			{ 11, "coast"},
			{ 12, "sea"},
			{ 13, "ocean"}
		};
	}

	// See TerrainType.landmark.
	public class TerrainLandmark {
		public bool enabled;
		public string name = "";
		public string civilopediaEntry = "";
		public int food, shields, commerce;
		public int irrigationBonus, miningBonus, roadBonus;
		public int movementCost, defenseBonus;
	}
}
