using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;
using C7GameData;
using Serilog;

namespace C7Engine {

	// Civ3's debug mode and the editing commands it unlocks, from the
	// Conquests 1.22 executable:
	//
	// - It is switched on only by the scenario's GAME "Debug Mode" flag
	//   (QueryCiv3.Biq.GAME.DebugMode): when a game starts, Civ3 FUN_004ff550
	//   calls FUN_004eba60 ("DEBUG") if the flag is set and the game isn't a
	//   multiplayer one. There is no key that turns it on or off, and every
	//   check of it first clears it in a multiplayer game, so it never works
	//   there.
	// - FUN_004eba60 also lets the player see everything (the map's fog and
	//   the "have we met them?" checks of the advisors are skipped).
	// - On the map, Shift+F1 opens the EDITORUNIT list ("Select type of unit
	//   to create", script.txt), and a click then puts a unit of that type on
	//   the tile (FUN_004ebed0, FUN_004ef820, FUN_004ef720); "$" adds 100 gold
	//   to the treasury (the key handler at 0x4ea5af).
	// - In the city screen, A adds a citizen, S adds 100 shields to the
	//   production box and B opens the EDITORBLDG list ("Select type of
	//   improvement/wonder to add") (the city screen's key handler, cases at
	//   0x421020, 0x420f82 and 0x42110c).
	public static class DebugMode {
		private static readonly ILogger log = Log.ForContext(typeof(DebugMode));

		// What "$" adds to the treasury: the handler at 0x4ea5af sets the
		// gold to the old amount plus 0x64.
		public const int GoldPerPress = 100;

		// What S adds to the production box: the handler at 0x420f82 adds
		// 0x64 to the city's stored shields, stopping at the cost of what it
		// builds (FUN_004b3d30).
		public const int ShieldsPerPress = 100;

		// Civ3's FUN_004c15d0 adds no citizen to a city above 0xfe.
		public const int MaxCitizens = 255;

		// The experience level EDITORUNIT starts with: DAT_0074621c is 1, the
		// second of the BIQ's levels (Regular).
		public const int DefaultExperienceIndex = 1;

		// Whether the game's debug commands work: in a game whose scenario
		// turned debug mode on, and never in a LAN game.
		public static bool IsOn(GameData gameData) {
			return gameData != null && gameData.debugMode && !EngineStorage.IsNetworked;
		}

		// The experience level EDITORUNIT picks unless told otherwise.
		public static ExperienceLevel DefaultExperience(GameData gameData) {
			if (gameData.experienceLevels.Count > DefaultExperienceIndex) {
				return gameData.experienceLevels[DefaultExperienceIndex];
			}
			return gameData.defaultExperienceLevel;
		}

		// The players a unit can be made for: EDITORWHO (FUN_004ebbc0) lists
		// the players still in the game.
		public static List<Player> UnitOwners(GameData gameData) {
			return gameData.players.Where(p => !p.defeated).ToList();
		}

		// Why a unit can't be put on the tile, or null if it can. Civ3's
		// FUN_00575900 won't put one on a tile another player's units hold.
		public static string WhyCannotCreateUnit(Player owner, UnitPrototype type, Tile tile) {
			if (owner == null || type == null || !Tile.IsTileValid(tile)) {
				return "There is no such unit or tile";
			}
			if (tile.unitsOnTile.Any(u => u.owner != owner)) {
				return "Another player's units are on the tile";
			}
			if (tile.cityAtTile != null && tile.cityAtTile.owner != owner) {
				return "Another player's city is on the tile";
			}
			return null;
		}

		// Puts a unit of the type, at the experience level, on the tile for
		// the owner (Civ3 FUN_004ef720), returning it, or null if it can't go
		// there.
		public static MapUnit CreateUnit(GameData gameData, Player owner, UnitPrototype type, Tile tile, ExperienceLevel experience) {
			string why = WhyCannotCreateUnit(owner, type, tile);
			if (why != null) {
				log.Warning("Not creating a {Type} for {Player} at {Tile}: {Why}", type?.name, owner, tile, why);
				return null;
			}
			MapUnit unit = gameData.SpawnUnit(owner, type, tile);
			if (experience != null) {
				unit.experienceLevel = experience;
				unit.experienceLevelKey = experience.key;
				unit.hitPointsRemaining = unit.maxHitPoints;
			}
			owner.tileKnowledge.RecomputeActiveTiles();
			log.Information("Debug mode: created a {Type} for {Player} at {Tile}", type.name, owner, tile);
			return unit;
		}

		// "$": adds GoldPerPress to the player's treasury.
		public static void AddGold(Player player) {
			player.gold += GoldPerPress;
		}

		// S in the city screen: adds ShieldsPerPress to the production box,
		// up to the cost of what the city builds.
		public static void AddShields(City city) {
			if (city.itemBeingProduced == null) {
				return;
			}
			int cost = city.owner.ShieldCost(city.itemBeingProduced);
			city.SetStoredShields(System.Math.Min(city.shieldsStored + ShieldsPerPress, cost));
		}

		// A in the city screen: adds a citizen of the owner's nationality
		// (FUN_004c15d0(1, -1)), with no aqueduct or hospital needed. Returns
		// whether one was added.
		public static bool AddCitizen(GameData gameData, City city) {
			if (city.residents.Count >= MaxCitizens) {
				return false;
			}
			CityResident resident = new() {
				citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen),
				nationality = city.owner.civilization,
				city = city,
			};
			city.AddCitizen(resident);
			CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
			city.RecalculateCitizenMoods(gameData);
			return true;
		}

		// The improvements and wonders EDITORBLDG offers: every one the city
		// doesn't have. Spaceship parts, which belong to the owner's ship
		// rather than the city here, aren't offered.
		public static List<Building> BuildingsToAdd(GameData gameData, City city) {
			return gameData.Buildings
				.Where(b => !b.IsSpaceshipPart && !city.constructed_buildings.Any(cb => cb.building == b))
				.ToList();
		}

		// B in the city screen: adds the improvement or wonder to the city
		// (FUN_004b3f20). A palace moves the capital there, and a great
		// wonder counts as built. Returns whether it was added.
		public static bool AddBuilding(GameData gameData, City city, Building building) {
			if (building == null || building.IsSpaceshipPart || city.constructed_buildings.Any(cb => cb.building == building)) {
				return false;
			}
			if (building.isCenterOfEmpire) {
				city.owner.MovePalaceTo(city, building);
			} else {
				city.AddBuilding(building);
			}
			if (building.greatWonderProperties != null) {
				gameData.GreatWondersBuilt.Add(building.name);
			}
			log.Information("Debug mode: added the {Building} to {City}", building.name, city);
			return true;
		}
	}

	// The debug commands as messages, so they go through the engine like
	// any other order. Each is refused unless debug mode is on (see
	// DebugMode.IsOn), so never in a LAN game. The city screen's keys work
	// only in the player's own cities: in another civ's, Civ3's city screen
	// takes only Escape and Enter.
	public abstract class DebugMessage : MessageToEngine {
		// Cheats aren't game actions (see IsGameAction).
		public override bool IsGameAction => false;

		protected override bool IsAllowed() {
			return DebugMode.IsOn(EngineStorage.gameData) && base.IsAllowed();
		}
	}

	// EDITORUNIT's click on the map.
	public class MsgDebugCreateUnit : DebugMessage {
		public Player owner;
		public string unitType;
		public string experienceKey;
		public Tile tile;

		public MsgDebugCreateUnit(Player owner, string unitType, string experienceKey, Tile tile) {
			this.owner = owner;
			this.unitType = unitType;
			this.experienceKey = experienceKey;
			this.tile = tile;
		}

		protected override void ProcessAllowed() {
			GameData gameData = EngineStorage.gameData;
			UnitPrototype type = gameData.unitPrototypes.Find(p => p.name == unitType);
			ExperienceLevel experience = gameData.experienceLevels.Find(e => e.key == experienceKey);
			MapUnit unit = DebugMode.CreateUnit(gameData, owner, type, tile, experience);
			if (unit != null) {
				new MsgUnitMoved(unit).send();
			}
		}
	}

	// "$" on the map.
	public class MsgDebugAddGold : DebugMessage {
		protected override void ProcessAllowed() {
			// The status box shows the new treasury by itself.
			DebugMode.AddGold(Sender);
		}
	}

	// S in the city screen.
	public class MsgDebugAddShields : DebugMessage {
		public City city;
		public MsgDebugAddShields(City city) {
			this.city = city;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city)) return;
			DebugMode.AddShields(city);
			new MsgCityChanged(city).send();
		}
	}

	// A in the city screen.
	public class MsgDebugAddCitizen : DebugMessage {
		public City city;
		public MsgDebugAddCitizen(City city) {
			this.city = city;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city)) return;
			DebugMode.AddCitizen(EngineStorage.gameData, city);
			new MsgCityChanged(city).send();
		}
	}

	// EDITORBLDG's choice in the city screen.
	public class MsgDebugAddBuilding : DebugMessage {
		public City city;
		public string building;
		public MsgDebugAddBuilding(City city, string building) {
			this.city = city;
			this.building = building;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city)) return;
			GameData gameData = EngineStorage.gameData;
			DebugMode.AddBuilding(gameData, city, gameData.Buildings.Find(b => b.name == building));
			new MsgCityChanged(city).send();
		}
	}
}
