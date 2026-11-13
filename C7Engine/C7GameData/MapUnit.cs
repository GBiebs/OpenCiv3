using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using C7Engine;
using Serilog;
using static C7GameData.PlayerRelationship;

namespace C7GameData {
	/**
	 * A unit on the map.  Not to be confused with a unit prototype.
	 **/
	public partial class MapUnit {
		private static ILogger log = Log.ForContext<MapUnit>();
		public ID id { get; internal set; }
		public string name { get; internal set; }
		public Civilization nationality { get; set; }
		public UnitPrototype unitType { get; set; }
		public Player owner { get; set; }
		public Tile previousLocation { get; internal set; }
		private Tile currentLocation;

		public Tile location {
			get => currentLocation;
			set {
				previousLocation = location;
				currentLocation = value;
				NoteLeavingArmyOrigin();
			}
		}
		public TilePath path { get; set; }

		public string experienceLevelKey;
		[JsonIgnore]
		public ExperienceLevel experienceLevel { get; set; }

		public MovementPoints movementPoints = new MovementPoints();
		public int hitPointsRemaining { get; set; }
		public int maxHitPoints {
			get {
				// The unit's HP bonus can be negative, but "the unit's total
				// number of HP will never drop below 1" (Civ3 editor help,
				// HP Bonus).
				return Math.Max(1, this.experienceLevel.baseHitPoints + this.unitType.hpBonus);
			}
		}
		public bool isFortified { get; set; }

		// True if the unit held this turn before moving, so it still heals.
		public bool heldWithoutMoving { get; set; }
		// A sentried unit sleeps until a unit it should notice comes next to
		// it: any foreign unit, or with sentryEnemyOnly only an enemy one.
		public bool isSentried { get; set; }
		public bool sentryEnemyOnly { get; set; }

		// Taking a unit out of automation ends its kind of worker automation
		// too.
		public bool isAutomated {
			get => automated;
			set {
				automated = value;
				if (!value) {
					workerAutomation = null;
				}
			}
		}
		private bool automated;

		// One of Civ3's kinds of worker automation while the unit is
		// automated, or null for plain automation (see WorkerAutomation).
		public WorkerAutomation workerAutomation { get; set; }

		//sentry, etc. will come later.  For now, let's just have a couple things so we can cycle through units that aren't fortified.
		public int defensiveBombardsRemaining;

		// Whether the unit has attacked or bombarded this turn. Only units with
		// blitz can attack more than once per turn.
		public bool hasAttackedThisTurn;

		// Whether the unit has pillaged this turn. Only armies can pillage more
		// than once per turn.
		public bool hasPillagedThisTurn;

		public bool CanAttackAgainThisTurn() {
			return !hasAttackedThisTurn || unitType.hasBlitz;
		}

		// Whether one of this unit's victories has produced a great leader.
		// Each unit produces at most one (see MapUnit_Leader.cs).
		public bool hasProducedLeader;

		// Whether this great leader is a scientific one (see
		// MapUnit_ScientificLeader.cs). As in Conquests, both kinds are the
		// same unit type, the leader, told apart only by this.
		public bool isScientificLeader;

		public TileDirection facingDirection = TileDirection.SOUTHEAST;

		public float WorkerProgressTowardsJob { get; set; }
		public Terraform WorkerJob { get; set; }

		public ID loadedOnUnitId { get; set; }

		public UnitAI currentAI;
		// The automation a unit loaded from a save resumes on its next turn
		// ("work" or "explore", see AutomationKind), since its plan isn't
		// saved.
		public string resumeAutomation;

		public MapUnit(ID id) {
			this.id = id;
		}

		internal MapUnit() { }

		public static MapUnit NONE = new MapUnit(ID.None("unit"));

		public static bool IsMapUnitValid(MapUnit mapUnit) {
			return mapUnit != null && mapUnit != NONE;
		}

		public bool IsBusy() {
			return isFortified || isSentried || IsAutoBombarding() || (path != null && path.PathLength() > 0) || WorkerJob != null || isAutomated;
		}

		public bool IsLandUnit() {
			return this.unitType.IsLandUnit();
		}
		public bool IsWaterUnit() {
			return this.unitType.IsSeaUnit();
		}
		public bool IsAirUnit() {
			return this.unitType.IsAirUnit();
		}

		public bool CanDefendOnLand() {
			return IsLandUnit() && CombatBaseStrength(CombatRole.Defense) > 0;
		}

		public bool IsCombatUnit() {
			// An army fights with its members, so an empty one can't fight.
			if (IsArmy()) {
				if (!Tile.IsTileValid(location))
					return false;
				foreach (MapUnit u in location.unitsOnTile) {
					if (IsPassenger(u) && u.IsCombatUnit())
						return true;
				}
				return false;
			}
			return this.unitType.attack > 0 || this.unitType.defense > 0;
		}

		// Whether this unit can start a fight.
		public bool CanAttack() {
			return CombatBaseStrength(CombatRole.Attack) > 0;
		}

		public bool CanBeActive() {
			return !this.IsBusy() && this.movementPoints.canMove;
		}

		// Whether the unit was taken from another civ (a captured worker).
		// Nationalities are the players' own civilization objects, also
		// after loading a save (see SaveUnit), so identity decides; two civs
		// that merely share a name are still different nations.
		public bool IsCaptive() {
			return this.nationality != null && !ReferenceEquals(this.nationality, this.owner.civilization);
		}

		public bool IsArmy() {
			return this.unitType.isArmy;
		}

		// Whether this unit can carry other units: transports and armies.
		public bool CanCarryUnits() {
			return this.unitType.capacity > 0 && (this.unitType.HasUnloadAction() || IsArmy());
		}

		// Whether this unit is a transport its passengers can be unloaded
		// from. Armies are rigid unless the game option allows unloading them,
		// in which case they're treated as having the unload action.
		public bool CanTransport() {
			if (this.unitType.capacity <= 0)
				return false;
			if (IsArmy())
				return owner?.rules?.AllowUnloadFromArmy ?? false;
			return this.unitType.HasUnloadAction();
		}

		// The units loaded on this one. They always share its tile.
		public List<MapUnit> Passengers() {
			if (!Tile.IsTileValid(location))
				return [];
			List<MapUnit> passengers = new();
			foreach (MapUnit u in location.unitsOnTile) {
				if (IsPassenger(u))
					passengers.Add(u);
			}
			return passengers;
		}

		// Whether u, a unit on this unit's tile, is one of Passengers().
		private bool IsPassenger(MapUnit u) {
			return u != this && u.IsLoadedIn(this);
		}

		// The first of Passengers(), without building the list, or null.
		public MapUnit FirstPassenger() {
			if (!Tile.IsTileValid(location))
				return null;
			foreach (MapUnit u in location.unitsOnTile) {
				if (IsPassenger(u))
					return u;
			}
			return null;
		}

		// Passengers().Count, without building the list.
		public int PassengerCount() {
			if (!Tile.IsTileValid(location))
				return 0;
			int count = 0;
			foreach (MapUnit u in location.unitsOnTile) {
				if (IsPassenger(u))
					count++;
			}
			return count;
		}

		// The unit this one is loaded on, or null.
		public MapUnit Carrier() {
			if (!IsLoaded() || !Tile.IsTileValid(location))
				return null;
			foreach (MapUnit u in location.unitsOnTile) {
				if (u != this && IsLoadedIn(u))
					return u;
			}
			return null;
		}

		public bool IsInArmy() {
			return Carrier()?.IsArmy() ?? false;
		}

		// A unit in an army that can't be unloaded takes no orders of its own;
		// it goes where the army goes.
		public bool IsLockedInArmy() {
			MapUnit carrier = Carrier();
			return carrier != null && carrier.IsArmy() && !carrier.CanTransport();
		}

		// How many units this unit can carry. The Pentagon (or any building
		// that allows larger armies) lets each of the owner's armies take one
		// more.
		public int Capacity() {
			int capacity = this.unitType.capacity;
			if (IsArmy() && capacity > 0 && (owner?.HasLargerArmies() ?? false))
				capacity += 1;
			return capacity;
		}

		// The movement allowance this unit gets each turn. An army moves at the
		// pace of its slowest member; an empty army just has its own allowance.
		public int MaxMovementPoints() {
			if (IsArmy() && Tile.IsTileValid(location)) {
				bool any = false;
				int min = 0;
				foreach (MapUnit m in location.unitsOnTile) {
					if (!IsPassenger(m))
						continue;
					if (!any || m.unitType.movement < min)
						min = m.unitType.movement;
					any = true;
				}
				// An army moves one faster than its slowest member: "In
				// Conquests, an Army will get an additional movement point
				// above the movement point of the slowest unit in the Army"
				// (https://civfanatics.com/civ3/faq/).
				if (any)
					return min + 1;
			}
			return this.unitType.movement + ShipMovementBonus();
		}

		// Ships get extra movement from their owner's wonders, like the Great
		// Lighthouse and Magellan's Voyage, and from the seafaring trait.
		private int ShipMovementBonus() {
			if (owner == null || !IsWaterUnit())
				return 0;
			return owner.ShipMovementBonus();
		}

		// An army's hit points are the total of its members'. An empty army
		// falls back to its own, as does any other unit.
		public int CompositeHitPoints() {
			if (IsArmy() && Tile.IsTileValid(location)) {
				bool any = false;
				int sum = 0;
				foreach (MapUnit m in location.unitsOnTile) {
					if (!IsPassenger(m))
						continue;
					sum = checked(sum + m.hitPointsRemaining);
					any = true;
				}
				if (any)
					return sum;
			}
			return this.hitPointsRemaining;
		}

		public int CompositeMaxHitPoints() {
			if (IsArmy() && Tile.IsTileValid(location)) {
				bool any = false;
				int sum = 0;
				foreach (MapUnit m in location.unitsOnTile) {
					if (!IsPassenger(m))
						continue;
					sum = checked(sum + m.maxHitPoints);
					any = true;
				}
				if (any)
					return sum;
			}
			return this.maxHitPoints;
		}

		public bool IsLoadable() {
			return this.unitType.HasLoadAction();
		}

		public bool IsLoaded() {
			return this.loadedOnUnitId != null;
		}

		public bool IsLoadedIn(MapUnit transport) {
			return transport.id == this.loadedOnUnitId;
		}

		public override string ToString() {
			if (this != NONE) {
				return $"{this.owner} {this.GetDisplayName()} at [{this.location.XCoordinate}, {this.location.YCoordinate}] " +
					   $"with {this.movementPoints.getMixedNumber()} MP and {this.hitPointsRemaining} HP, id = {id}";
			} else {
				return "This is the NONE unit";
			}
		}

		public string GetDisplayName() {
			return this.IsCaptive() ? $"{this.name} ({this.nationality.name})" : this.name;
		}

		// TODO: best move this to lua at some point
		public string GetArtName() {
			Dictionary<string, string> variations = this.unitType.art.mainArt.variations;
			if (variations != null) {
				if (this.unitType.isWorker && this.IsCaptive()) {
					string slaveArt = this.unitType.GetSlaveArtName();
					if (slaveArt != null)
						return slaveArt;
				}

				// A scientific leader has the leader type's science art.
				if (this.isScientificLeader && variations.TryGetValue(ScientificLeaderArt, out string sci))
					return sci;

				if (variations.TryGetValue(this.owner.eraCivilopediaName ?? "", out var value))
					return value;
			}

			return this.unitType.art.mainArt.defaultName;
		}

		public string Describe() {
			UnitPrototype type = this.unitType;
			string exp = this.IsCombatUnit() ? $"{this.experienceLevel.displayName}" : "";
			string hPDesc = IsCombatUnit() ? $" ({this.CompositeHitPoints()}/{this.CompositeMaxHitPoints()})" : "";
			string displayName = this.IsCaptive() ? $" ({this.nationality.adjective}) {this.name}" : $" {this.name}";
			string attackDesc = (type.bombard > 0) ? $"{type.attack}({type.bombard})" : type.attack.ToString();
			string stats = $" ({attackDesc}.{type.defense}.{(EngineStorage.uiControllerID == this.owner.id ? $"{this.movementPoints.getMixedNumber()}/" : "")}{MaxMovementPoints()})";
			return $"{exp}{hPDesc}{displayName}{stats}".Trim();
		}

		// TODO: The contents of this enum are copy-pasted from UnitAction in Civ3UnitSprite.cs. We should unify these so we don't have two different
		// but virtually identical enums.
		public enum AnimatedAction {
			BLANK,
			DEFAULT,
			WALK,
			RUN,
			ATTACK1,
			ATTACK2,
			ATTACK3,
			DEFEND,
			DEATH,
			DEAD,
			FORTIFY,
			FORTIFYHOLD,
			FIDGET,
			VICTORY,
			TURNLEFT,
			TURNRIGHT,
			BUILD,
			ROAD,
			MINE,
			IRRIGATE,
			FORTRESS,
			CAPTURE,
			JUNGLE,
			FOREST,
			PLANT
		}

		public struct Appearance {
			public AnimatedAction action;
			public TileDirection direction;
			public float progress; // Varies 0 to 1
			public float offsetX, offsetY; // Offset is in grid cells from the unit's location
			public AnimationEnding ending;

			// When true, indicates that the animation is still playing (f.e. a unit is still running between tiles) so the UI shouldn't yet
			// autoselect another unit.
			public bool DeservesPlayerAttention() {
				// TODO: Special rules for different animations. We don't need to see workers do their thing but we do want to watch units
				// move. IMO we should also not show units fortifying even though I know the original game does.
				// This may also be the culprit behind why we can fortify a unit that is in motion.
				if (ending == AnimationEnding.Repeat) {
					return false;
				}
				return progress < 1.0;
			}
		}

		// Civ3's terrain job progress per turn for a plain worker when the
		// government doesn't give its worker rate (2 under most governments
		// in the standard rules). Foreign (captured) workers work at half
		// speed: "all slaves, regardless of their nationality, have a
		// production value of 50%"
		// (https://forums.civfanatics.com/threads/worker-actions.82982/,
		// a chart for Vanilla and PTW), and in Conquests "Captured workers
		// work at 50% the speed of regular workers." (Conquests/readme.txt).
		private const int DefaultWorkerRate = 2;
		private const float SlaveWorkerFactor = 0.5f;

		private static int GetWorkerJobCost(Tile tile, Terraform workerJob) {
			// For the movement cost multiplier, see note 7
			// (https://apolyton.net/forum/civilization-series/civilization-iii/59815-civilization-iii-bic-file-format-2nd-thread?p=1362768#post1362768)
			// For example, clearing a forest has a cost of 4, but with a normal
			// worker that would take 2 turns. In order for the job to take the
			// expected 4 turns we need to multiply by the movement cost of the
			// terrain. This also makes roading hills/mountains more expensive.
			return tile.overlayTerrainType.movementCost * workerJob.TurnsToComplete;
		}

		public async Task animateAsync(AnimatedAction action, AnimationEnding ending = AnimationEnding.Stop) {
			var animationsEnabled = EngineStorage.animationsEnabled && !EngineStorage.gameData.observerMode;
			var skipAnimations = SkipAnimations(action);

			if (animationsEnabled && !skipAnimations && IsAnimationVisibleToUIPlayer()
					&& (EngineStorage.uiShowsUnitAnimation?.Invoke(this, action) ?? true)) {
				var msg = new MsgStartUnitAnimation(this, action, ending);
				msg.send();

				await EngineStorage.WaitForAnimationFinished(msg.animationId);
			}
			if (this.owner.isHuman)
				new MsgUnitMoved(this).send();
		}

		// Whether the UI would play an animation of this unit. The UI only
		// shows unit animations on tiles its player can see (the unit's tile
		// or, for a move, the tile it left) and just marks the others
		// completed on its next frame. Skipping those here saves the engine,
		// and so every AI unit's step, from waiting a frame for nothing. The
		// UI's player's own units are always animated.
		private bool IsAnimationVisibleToUIPlayer() {
			Player uiPlayer = EngineStorage.gameData.GetUIControllerPlayer();
			if (uiPlayer?.tileKnowledge == null || uiPlayer == this.owner)
				return true;
			return uiPlayer.tileKnowledge.isActiveTile(this.location) || uiPlayer.tileKnowledge.isActiveTile(this.previousLocation);
		}

		public void animate(AnimatedAction action, AnimationEnding ending = AnimationEnding.Stop) {
			_ = animateAsync(action, ending);
		}

		private bool SkipAnimations(AnimatedAction action) {
			if (action != AnimatedAction.RUN) return false;

			// as soon as we move, the tile we were just on becomes the previous tile
			var isOnRailroad = Tile.IsTileValid(this.previousLocation) && this.previousLocation.HasRailroad();
			if (!isOnRailroad) return false;

			// and the tile we are moving towards, becomes the current tile
			var movingOnRailroad = Tile.IsTileValid(this.location) && this.location.HasRailroad();
			if (!movingOnRailroad) return false;

			var canMoveFreely = Player.CanMoveFreely(this.owner, this.previousLocation, this.location);
			if (!canMoveFreely) return false;

			return true;
		}

		public void ResetFacingDirection() {
			facingDirection = TileDirection.SOUTHEAST;
		}

		public IEnumerable<StrengthBonus> ListStrengthBonusesVersus(MapUnit opponent, CombatRole role, TileDirection? attackDirection) {
			GameData gD = EngineStorage.gameData;

			if (role.Defending()) {
				// "There is no defensive bonus for fortified ships"
				// (https://forums.civfanatics.com/threads/do-ships-get-a-defense-bonus-when-fortified.95400/).
				// The bonus also needs movement points left: "the fortified
				// bonus requires the unit to be fortified *and have movement
				// points remaining*" (Perez's decompile of Civ3 Conquests,
				// doc/perez-findings/Civ3_Findings.md, 3.2 "Combat";
				// authoritative per the owner's 2026-11-12 ruling). Moves are
				// restored as each round begins (TurnHandling.InitTurnData),
				// so a unit that fortifies having spent its moves only gets
				// the bonus from then on.
				if (isFortified && !IsWaterUnit() && movementPoints.canMove)
					yield return gD.fortificationBonus;

				yield return location.overlayTerrainType.defenseBonus;

				foreach (StrengthBonus sb in location.overlays.GetDefenseBonuses()) {
					yield return sb;
				}

				if ((!role.Bombarding()) && (attackDirection is TileDirection dir) && location.HasRiverCrossing(dir.Reversed()))
					yield return gD.riverCrossingBonus;

				if (location.cityAtTile != null) {
					foreach (StrengthBonus sb in location.cityAtTile.GetDefenseBonuses(opponent)) {
						yield return sb;
					}
				}
			}

			if (HasGreatWallBonusAgainst(opponent, role))
				yield return GreatWallBonus;

			if (HasRadarBonus(role))
				yield return RadarTowerBonus;

			if (HasKingsPresence(role))
				yield return KingsPresenceBonus;

			if (HoldsItsCityState(role))
				yield return CityStateHomeBonus;

			if (DifficultyBonusAgainst(opponent, role) is StrengthBonus difficultyBonus)
				yield return difficultyBonus;
		}

		// The difficulty level's bonus against barbarians, a percentage (800
		// at Chieftain down to 0 at Deity) that helps human players' units in
		// fights with barbarians, attacking or defending, despite the BIQ
		// field's name (Difficulty.AttackBonusAgainstBarbarians). Per the
		// project owner's knowledge of Civ3. Bombardment isn't a fight.
		//
		// The AI plays by Regent's rules whatever the difficulty, so "the AI
		// has still a 200% bonus against barbarians even though the human
		// player loses its entire bonus on deity"
		// (https://forums.civfanatics.com/threads/ai-difficulty-level-bonuses.37490/):
		// it takes Regent's field (GameData.DifficultyFor), or 200 without
		// one. Per the project owner, the AI gets it attacking and defending,
		// like the human's bonus.
		private const int AIBonusAgainstBarbariansPercent = 200;

		private StrengthBonus? DifficultyBonusAgainst(MapUnit opponent, CombatRole role) {
			if (role != CombatRole.Attack && role != CombatRole.Defense)
				return null;
			if (owner == null || owner.isBarbarians || opponent?.owner == null || !opponent.owner.isBarbarians)
				return null;
			GameData gameData = EngineStorage.gameData;
			int percent = owner.isHuman
				? gameData?.gameDifficulty?.AttackBonusAgainstBarbarians ?? 0
				: gameData?.difficulties.Count > 0 ? gameData.DifficultyFor(owner).AttackBonusAgainstBarbarians : AIBonusAgainstBarbariansPercent;
			if (percent <= 0)
				return null;
			return new StrengthBonus("Difficulty level against barbarians", percent / 100.0);
		}

		// The Great Wall doubles the strength of its owner's units, attacking
		// or defending, in fights with barbarians. Like Civ3's other combat
		// modifiers it adds to the others (+100%) rather than multiplying
		// them. Bombardment isn't a fight, so it doesn't count.
		private static readonly StrengthBonus GreatWallBonus = new("Great Wall against barbarians", 1.0);

		private bool HasGreatWallBonusAgainst(MapUnit opponent, CombatRole role) {
			if (role != CombatRole.Attack && role != CombatRole.Defense)
				return false;
			if (opponent?.owner == null || !opponent.owner.isBarbarians || owner == null || owner.isBarbarians)
				return false;
			return owner.HasDoubleCombatVsBarbarians();
		}

		// "Friendly units within the Radar Towers range get a +25% attack and
		// defense bonus. This bonus can only be applied once, even if multiple
		// towers overlap. Radar Towers can only be used by the player who
		// created them" (Conquests Civilopedia, GCON_Radar_Towers). Like the
		// other combat modifiers it adds to the rest. UNVERIFIED: whether it
		// counts in bombardments; here, as with the Great Wall, it doesn't.
		private static readonly StrengthBonus RadarTowerBonus = new("Radar tower", 0.25);

		private bool HasRadarBonus(CombatRole role) {
			if (role != CombatRole.Attack && role != CombatRole.Defense)
				return false;
			return owner != null && Tile.IsTileValid(location) && location.IsCoveredByRadarOf(owner);
		}

		// Kings & Nomads: the King leads from the front (owner's design,
		// 2026-10-23). Its civ's units on its tile or next to it fight at
		// +100%, attacking and defending, so there's reason to march it with
		// the army, at the risk of losing the game with it. Like the other
		// combat modifiers it adds to the rest; the King itself doesn't
		// count, nor does bombardment.
		private static readonly StrengthBonus KingsPresenceBonus = new("King's presence", 1.0);

		// Kings & Nomads: a city-state's units fight at +50% in their own
		// city (owner's design, 2026-10-23), so the city-states hold out
		// longer.
		private static readonly StrengthBonus CityStateHomeBonus = new("Defending their city-state", 0.5);

		// Kings & Nomads: a civ's units' strength against the city-states'
		// units, attacking or defending, by how many city-states the civ has
		// taken (see CityStates.StrengthAgainstCityStates): the first comes
		// easily, and after the second its soldiers, many of them of
		// conquered city-states, are loath to fight their brethren (owner's
		// design, 2026-10-25). It scales the unit's whole strength, bonuses
		// and all, rather than adding to them as Civ3's modifiers do.
		internal double BrethrenFactorAgainst(MapUnit opponent, CombatRole role) {
			if ((role != CombatRole.Attack && role != CombatRole.Defense) || owner == null || owner.IsIndependent
				|| opponent?.owner?.isCityStates != true || !CityStateTrade.IsOn(EngineStorage.gameData)) {
				return 1;
			}
			return CityStates.StrengthAgainstCityStates(owner.cityStatesCaptured);
		}

		private bool HoldsItsCityState(CombatRole role) {
			return (role == CombatRole.Attack || role == CombatRole.Defense) && owner?.isCityStates == true
				&& Tile.IsTileValid(location) && location.cityAtTile?.owner == owner && CityStateTrade.IsOn(EngineStorage.gameData);
		}

		private bool HasKingsPresence(CombatRole role) {
			if ((role != CombatRole.Attack && role != CombatRole.Defense) || unitType.isKing || owner == null
				|| !Tile.IsTileValid(location) || !CityStateTrade.IsOn(EngineStorage.gameData)) {
				return false;
			}
			foreach (MapUnit u in owner.units) {
				if (u.unitType.isKing && Tile.IsTileValid(u.location) && u.location.DistanceTo(location) <= 1) {
					return true;
				}
			}
			return false;
		}

		public double StrengthVersus(MapUnit opponent, CombatRole role, TileDirection? attackDirection) {
			return CombatBaseStrength(role) * StrengthBonus.ListToMultiplier(ListStrengthBonusesVersus(opponent, role, attackDirection))
				* BrethrenFactorAgainst(opponent, role);
		}

		// The unit's base strength in a fight. An army has none of its own and
		// uses that of the member currently fighting for it. Bonuses still come
		// from the army itself (its tile, whether it's fortified), so its
		// members' own fortification adds nothing.
		public double CombatBaseStrength(CombatRole role) {
			return Combatant(role).unitType.BaseStrength(role) + ArmyStrengthBonus(role);
		}

		// In Conquests an army's fighting member gets a share of the whole
		// army's strength on top of its own: "The bonus to A is equal to the
		// sum of the A values of all units in the army divided by 6 and
		// rounded down ... The Defense bonus follows similar calculation ...
		// Building the Military Academy makes the denominator 4 instead of 6"
		// (https://forums.civfanatics.com/threads/civ3-conquests-additions-changes-list.104294/),
		// and the Civ3 FAQ's "Attack Value = A+(TA/N)", TA being the total
		// of the army's units' attacks and N 6, or 4 with the Military
		// Academy, the same going for defense
		// (https://forums.civfanatics.com/threads/frequently-asked-questions-civ3-play-the-world-conquests.170282/).
		// The Military Academy is the building with the BIQ's "increased army
		// value" flag. UNVERIFIED: whether the defense bonus counts against
		// bombardment; here it does, as the defense is what's used.
		public int ArmyStrengthBonus(CombatRole role) {
			if (!IsArmy() || role.Bombarding() || !Tile.IsTileValid(location))
				return 0;
			int total = 0;
			foreach (MapUnit m in location.unitsOnTile) {
				if (IsPassenger(m))
					total += (int)m.unitType.BaseStrength(role);
			}
			int divisor = (owner?.HasIncreasedArmyValue() ?? false) ? 4 : 6;
			return total / divisor;
		}

		// An army's hit points are pooled, and which member fights is chosen
		// from the pool by "HP-band rotation". The stock routine is
		// Unit_select_army_member_for_combat (army, hp_remaining, attacking),
		// which takes the army's remaining hit points, and the C3X mod keeps
		// it to preserve "the army's HP-band rotation" and "the base
		// selector's HP allocation and tie-breaking"
		// (https://github.com/maxpetul/C3X/blob/master/injected_code.c). A
		// test of it (Theseus, https://codehappy.net/apolyton/threads/59461-1.htm,
		// #10): an army of an elite Modern Armor and an elite Mech Infantry
		// at 5 hit points "attacked again with 5 hp... the MA fought for 3,
		// handed off to the MI, which lost the remaining 2, and the Army
		// crumbled", so the hit points "are divided between all units at the
		// start of each battle event". Catt confirmed it in the same thread,
		// bombardment included ("HPs are shared among the constituent
		// units").
		//
		// So at the start of a battle the pool is divided evenly among the
		// members, the strongest for the job taking the larger shares, and
		// the strongest fights until its share is used up, then the next.
		// Members don't die one by one: the army and all of them die
		// together when the pool runs out. The pool is kept in the members'
		// own hit points, which are levelled after each battle (see
		// EndArmyBattle). UNVERIFIED: the exact order and tie-breaking of the
		// stock selector; here the type's strength for the job, then the
		// order the members joined in.
		//
		// Set during a battle: the role the army fights in and its pooled hit
		// points when the battle started.
		private CombatRole? armyBattleRole;
		private int armyBattleStartHitPoints;

		// The unit that actually fights when this one is in combat. For most
		// units that's the unit itself; for an army, the member whose band of
		// the pooled hit points the battle has reached (see above), or
		// outside a battle the one that would fight first. An empty army has
		// nobody to fight for it, so it returns itself.
		public MapUnit Combatant(CombatRole role) {
			if (!IsArmy() || !Tile.IsTileValid(location))
				return this;
			List<MapUnit> members = ArmyMembersByStrength(role);
			if (members.Count == 0)
				return this;
			int pool = CompositeHitPoints();
			int start = armyBattleRole == role ? armyBattleStartHitPoints : pool;
			return ArmyBandMember(members, start, start - pool);
		}

		// The army's members, strongest for the role first; between equals,
		// in the order they joined.
		private List<MapUnit> ArmyMembersByStrength(CombatRole role) {
			return Passengers().OrderByDescending(m => m.unitType.BaseStrength(role)).ToList();
		}

		// Of the members in order, the one whose band of the `start` pooled
		// hit points contains the `lost`th hit point lost in the battle: the
		// first takes the first share, and so on. The pool is shared out
		// evenly, the earlier members taking any remainder.
		private static MapUnit ArmyBandMember(List<MapUnit> members, int start, int lost) {
			int share = start / members.Count, extra = start % members.Count, bandEnd = 0;
			for (int i = 0; i < members.Count; ++i) {
				bandEnd += share + (i < extra ? 1 : 0);
				if (lost < bandEnd)
					return members[i];
			}
			return members[^1];
		}

		// Marks the start and end of a battle this unit fights in (see
		// Combatant). Nothing for a unit that isn't an army.
		internal void BeginArmyBattle(CombatRole role) {
			if (!IsArmy())
				return;
			armyBattleRole = role;
			armyBattleStartHitPoints = CompositeHitPoints();
		}

		internal void EndArmyBattle() {
			if (!IsArmy())
				return;
			armyBattleRole = null;
			LevelArmyHitPoints();
		}

		// Spreads the army's pooled hit points as evenly as the members'
		// own maximums allow, so that no member is left at none while the
		// pool could give it some, and they heal as the army does: "Armies
		// heal at the rate of one HP per unit in the army"
		// (https://civfanatics.com/civ3/strategy/introductory/the-basics-of-armies/).
		private void LevelArmyHitPoints() {
			List<MapUnit> members = Passengers();
			int remaining = CompositeHitPoints();
			if (members.Count == 0 || remaining <= 0)
				return;
			members.Sort((a, b) => a.maxHitPoints.CompareTo(b.maxHitPoints));
			for (int i = 0; i < members.Count; ++i) {
				int give = Math.Min(members[i].maxHitPoints, remaining / (members.Count - i));
				members[i].hitPointsRemaining = give;
				remaining -= give;
			}
		}

		// Takes one hit point of damage from a fight. In an army the hit comes
		// off the pool, from the member doing the fighting while it holds
		// some, and the army with all its members is beaten when the pool is
		// empty (see Combatant). Returns true if this unit has nothing left to
		// fight with.
		internal bool AbsorbCombatHit(MapUnit combatant) {
			if (combatant == this || !IsArmy()) {
				hitPointsRemaining -= 1;
				return hitPointsRemaining <= 0;
			}

			MapUnit payer = combatant.hitPointsRemaining > 0 ? combatant : MemberWithMostHitPoints();
			if (payer != null)
				payer.hitPointsRemaining -= 1;
			return LoseArmyIfEmpty();
		}

		// The first of the army's members with the most hit points, or null.
		private MapUnit MemberWithMostHitPoints() {
			MapUnit best = null;
			if (Tile.IsTileValid(location)) {
				foreach (MapUnit m in location.unitsOnTile) {
					if (IsPassenger(m) && (best == null || m.hitPointsRemaining > best.hitPointsRemaining))
						best = m;
				}
			}
			return best;
		}

		// Takes one hit point of bombardment damage. An army's comes off its
		// pool, from whichever member holds the most; unless the bombardment
		// is lethal, it can't take the pool's last hit point.
		internal bool AbsorbBombardHit(bool lethal) {
			MapUnit victim = IsArmy() ? MemberWithMostHitPoints() : null;
			if (victim == null) {
				hitPointsRemaining -= 1;
				return hitPointsRemaining <= 0;
			}
			if (CompositeHitPoints() <= 1 && !lethal)
				return false;
			return AbsorbCombatHit(victim);
		}

		// An army whose pool of hit points is empty is beaten. Its own hit
		// points are zeroed so that it reads as dead, like any other unit;
		// its members die with it: "If the unit has the army ability, the
		// game will always despawn the passengers" (C3X,
		// patch_Unit_has_army_ability_for_passenger_despawn).
		private bool LoseArmyIfEmpty() {
			if (Tile.IsTileValid(location)) {
				foreach (MapUnit m in location.unitsOnTile) {
					if (IsPassenger(m) && m.hitPointsRemaining > 0)
						return false;
				}
			}
			hitPointsRemaining = 0;
			return true;
		}

		public bool CanDefendAgainst(MapUnit attacker) {
			//Basically, unit type must match.  Sea/air units in a city/airfield can't defend against land units.
			//Land units on a boat or planes on a carrier can't defend against boats.  Anti-air is another category that should be checked before the direct combat.
			//Potential future hybrid units that have multiple categories (e.g. amphibious vehicles) may contain more than one category.
			if (attacker.unitType.IsLandUnit() && !unitType.IsLandUnit()) {
				return false;
			}
			if (attacker.unitType.IsSeaUnit() && !unitType.IsSeaUnit()) {
				return false;
			}
			if (attacker.unitType.IsAirUnit() && !unitType.IsAirUnit()) {
				return false;
			}
			return true;
		}

		// Answers the question: if "opponent" is attacking the tile that this unit is standing on, does this unit defend instead of "otherDefender"?
		// Note that otherDefender does not necessarily belong to the same civ as this  Under standard Civ 3 rules you can't have units belonging
		// to two different civs on the same tile, but we don't want to assume that. In that case, whoever is an enemy of "opponent" should get
		// priority. Otherwise it's just whoever is stronger on defense.
		public bool HasPriorityAsDefender(MapUnit otherDefender, MapUnit opponent) {
			bool weAreEnemy           = IsEnemyDefenderAgainst(opponent);
			bool otherDefenderIsEnemy = otherDefender.IsEnemyDefenderAgainst(opponent);
			if (weAreEnemy != otherDefenderIsEnemy)
				return weAreEnemy;

			return HasPriorityAsDefender(weAreEnemy, TotalDefensiveStrengthVersus(opponent),
				otherDefenderIsEnemy, otherDefender.TotalDefensiveStrengthVersus(opponent));
		}

		// HasPriorityAsDefender, given what it compares for both units, so
		// that choosing among many defenders works each unit's values out
		// only once (see Tile.FindTopDefender).
		internal static bool HasPriorityAsDefender(bool weAreEnemy, double ourTotalStrength, bool otherDefenderIsEnemy, double theirTotalStrength) {
			if (weAreEnemy && !otherDefenderIsEnemy)
				return true;
			if (otherDefenderIsEnemy && !weAreEnemy)
				return false;
			return ourTotalStrength > theirTotalStrength;
		}

		// Whether the opponent attacking this unit's tile is not at peace with
		// this unit's owner, or may attack it anyway (hidden nationality).
		internal bool IsEnemyDefenderAgainst(MapUnit opponent) {
			return (!opponent.owner?.IsAtPeaceWith(owner) ?? false) || IsFairGameFor(opponent.owner);
		}

		// This unit's defensive strength against the opponent, times its hit
		// points.
		internal double TotalDefensiveStrengthVersus(MapUnit opponent) {
			return StrengthVersus(opponent, CombatRole.Defense, null) * CompositeHitPoints();
		}


		// Rolls for a promotion after winning a fight. The animation plays on
		// animatedUnit if given, for example the army a member fought for.
		public void RollToPromote(MapUnit opponent, MapUnit animatedUnit = null) {
			// Barbarians can't promote.
			if (owner.isBarbarians) {
				return;
			}

			double promotionChance = experienceLevel.promotionChance;
			if (opponent.owner.isBarbarians)
				promotionChance /= 2.0;
			// A militaristic civ's promotions are twice as likely, and halved
			// against barbarians like anyone's: 1 in 1, 2 and 4 against civs,
			// 1 in 2, 4 and 8 against barbarians
			// (https://forums.civfanatics.com/threads/frequently-asked-questions-civ3-play-the-world-conquests.170282/).
			if (owner.civilization.traits.Contains(Civilization.Trait.Militaristic))
				promotionChance *= 2;
			if (GameData.rng.NextDouble() < promotionChance) {
				Promote();
				(animatedUnit ?? this).animate(AnimatedAction.VICTORY);
			}
		}

		// The chance, out of 100, a unit that enslaves makes a slave of a foe
		// it beats attacking: "a 33% chance", which "is hardcoded"
		// (https://forums.civfanatics.com/threads/anyone-know-exactly-how-enslavement-works.107253/).
		// The roll is "lim is 100, enslaving happens if the return value is
		// < 33" (C3X, patch_rand_int_to_enslave,
		// https://github.com/maxpetul/C3X/blob/master/injected_code.c), so
		// 33%, not a third.
		public const int EnslaveChancePercent = 33;

		// Rolls, after this unit beat the opponent attacking, for the unit
		// its type enslaves to (the Javelin Thrower's Worker, the Privateer's
		// Privateer), which joins its owner on this unit's tile with the
		// loser's nationality, like a captured worker. "It's the unit that
		// is doing the attacking" that enslaves (same thread). "Barbarian
		// units are not able to enslave other units"
		// (https://forums.civfanatics.com/threads/allowing-barbarians-to-enslave-units.677365/),
		// but barbarian units can be enslaved (players note enslaving them
		// early is valuable, first thread). A kill by bombardment can enslave
		// too: C3X offers, as a change to the base game, an option that
		// "Stops units from enslaving a target they've destroyed by
		// bombarding as opposed to direct combat"
		// (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini;
		// see BombardUnits). UNVERIFIED: where the slave appears; here it's
		// on the winner's tile, and none is made if it couldn't stand there
		// (a land unit at sea).
		public MapUnit RollToEnslave(MapUnit opponent) {
			if (unitType.enslavesTo == null || owner.IsIndependent) {
				return null;
			}
			GameData gameData = EngineStorage.gameData;
			UnitPrototype slaveType = gameData.unitPrototypes.FirstOrDefault(p => p.name == unitType.enslavesTo);
			if (slaveType == null || GameData.rng.Next(100) >= EnslaveChancePercent) {
				return null;
			}
			if ((slaveType.IsLandUnit() && !location.IsLand()) || (slaveType.IsSeaUnit() && !location.IsWater() && !location.HasCity())) {
				return null;
			}
			MapUnit slave = gameData.SpawnUnit(owner, slaveType, location);
			slave.nationality = opponent.nationality ?? opponent.owner.civilization;
			log.Information("{Unit} enslaved {Opponent} as {Slave}", this, opponent, slave);
			return slave;
		}

		public void Promote() {
			ExperienceLevel nextLevel = EngineStorage.gameData.GetExperienceLevelAfter(experienceLevel);
			if (nextLevel != null) {
				experienceLevelKey = nextLevel.key;
				experienceLevel = nextLevel;
				hitPointsRemaining++;
			}
		}

		// The chance of a losing unit withdrawing. Only fast units withdraw,
		// "unless fighting another fast unit" (Conquests Civilopedia,
		// PRTO_Horseman). The roll compares the two units' experience level
		// retreat bonuses (the BIQ's, 34/50/58/66 for conscript to elite):
		// "Cavalry retreats only if random(Hoplite.Retreat_Bonus + 50) <
		// Cavalry.Retreat_Bonus" (Antal1987, reading process_Combat in the
		// 1.22 executable,
		// https://forums.civfanatics.com/threads/useful-patches-for-civ-3-conquests-v1-22.526285/page-4,
		// #62). So a regular has an even chance against a regular, and a
		// conscript opponent makes it likelier than a veteran one.
		// Barbarians never retreat from an attack (see Fight).
		public double RetreatChance(MapUnit opponent, bool isAttacking) {
			if (!IsFastAgainst(opponent) || (isAttacking && owner?.isBarbarians == true))
				return 0.0;
			(int own, int theirs) = RetreatBonuses(opponent, isAttacking);
			return Math.Min(1.0, (double)own / (theirs + 50));
		}

		// Rolls the retreat as the game does: a random number below the
		// opponent's bonus plus 50 must come out under this unit's bonus.
		public bool RollToRetreat(MapUnit opponent, bool isAttacking) {
			if (!IsFastAgainst(opponent))
				return false;
			(int own, int theirs) = RetreatBonuses(opponent, isAttacking);
			return GameData.rng.Next(theirs + 50) < own;
		}

		private bool IsFastAgainst(MapUnit opponent) {
			return RetreatMovement() > 1 && opponent.RetreatMovement() <= 1;
		}

		// The retreat bonuses of the members doing the fighting, this unit's
		// and the opponent's (an army fights with one member at a time).
		private (int own, int theirs) RetreatBonuses(MapUnit opponent, bool isAttacking) {
			MapUnit combatant = Combatant(isAttacking ? CombatRole.Attack : CombatRole.Defense);
			MapUnit opposing = opponent.Combatant(isAttacking ? CombatRole.Defense : CombatRole.Attack);
			return (combatant.experienceLevel?.RetreatBonus() ?? 0, opposing.experienceLevel?.RetreatBonus() ?? 0);
		}

		// The movement that decides whether the unit is fast enough to
		// withdraw. An army is fast only if all its members are, and its
		// extra movement point doesn't count: "The only unit abilities that
		// an army can inherit is mobility, and only when all units in that
		// army have that ability" (Civ3 readme). So it's the slowest
		// member's.
		private int RetreatMovement() {
			if (IsArmy() && Tile.IsTileValid(location)) {
				bool any = false;
				int min = 0;
				foreach (MapUnit m in location.unitsOnTile) {
					if (!IsPassenger(m))
						continue;
					if (!any || m.unitType.movement < min)
						min = m.unitType.movement;
					any = true;
				}
				if (any)
					return min;
			}
			return MaxMovementPoints();
		}

		internal TileDirection GetAttackAnimationDirection(TileDirection attackDirection) {
			return unitType.rotateBeforeAttack ? attackDirection.RotatedCounterClockwise90Degrees() : attackDirection;
		}

		internal TileDirection GetDefenseAnimationDirection(TileDirection attackDirection) {
			return GetAttackAnimationDirection(attackDirection.Reversed());
		}

		public int HealRateAt(Tile location) {
			GameData gD = EngineStorage.gameData;
			City city = location.cityAtTile;
			bool inFriendlyCity = (city != null) && (city != City.NONE) && owner.IsAtPeaceWith(city.owner);
			if (inFriendlyCity) {
				// Barracks fully heal land units in their own city, and harbors
				// do the same for ships.
				if (city.owner == owner && HasVeteranBuildingFor(city)) {
					return maxHitPoints;
				}
				return gD.healRateInCity;
			}

			// Units heal faster in their own territory and not at all in the
			// territory of a civ they're at war with. Ships too: a unit heals
			// if it "must be in friendly or neutral territory" and "must not
			// move for a complete turn" (Conquests Civilopedia, GCON_Combat),
			// with no exception for ships.
			Player territoryOwner = location.OwningPlayer();
			if (territoryOwner == owner)
				return gD.healRateInFriendlyField;
			if (territoryOwner != null && AtWar(owner, territoryOwner)) {
				// Except in an army: "Armies also heal in enemy territory
				// (one hp/unit pre-Battlefield Medicine and two hp/unit
				// after)" (https://forums.civfanatics.com/threads/civ3-conquests-additions-changes-list.104294/).
				if (IsInArmy())
					return CanHealInEnemyTerritory() ? 2 : 1;
				if (!CanHealInEnemyTerritory())
					return gD.healRateInHostileField;
			}
			return gD.healRateInNeutralField;
		}

		// Battlefield Medicine lets a civ's units heal in enemy territory.
		private bool CanHealInEnemyTerritory() {
			// Plain loops: no closures or enumerator boxing, and it stops at
			// the first such building. Only reached for units in the
			// territory of a civ their owner is at war with.
			List<City> cities = owner.cities;
			for (int i = 0; i < cities.Count; i++) {
				List<CityBuilding> buildings = cities[i].constructed_buildings;
				for (int j = 0; j < buildings.Count; j++) {
					if (buildings[j].building.allowsEnemyTerritoryHealing && buildings[j].building.TakesEffectFor(owner))
						return true;
				}
			}
			return false;
		}

		public enum Intent {
			Disabled,         // do nothing, don't move
			MoveFreely,       // enter tile
			Fight,            // move and fight an enemy unit, capture city/unit, pillage freely, etc
			Load,             // load a unit on a friendly transport on this tile
			Unload,           // unload a unit from a transport on this tile
			NoticeUnit,       // a non-combat unit tries to move on another owner's unit tile
			NoticeCity,       // a non-combat unit tries to move on another owner's city tile
			NoticeAlliance,   // a combat unit tries to move on an ally's city/unit tile
			WarDeclaration,   // a combat unit tries to move on another owner's city/unit tile (TODO: pillage non-enemy road/colony etc for example)
		}

		/// <summary>
		/// Returns a unit's intent trying to move in on a tile.
		/// </summary>
		/// <param name="tile"></param>
		/// <returns></returns>
		// private Intent ResolveIntent(Tile tile, bool tset = false) {
		private Intent ResolveIntent(Tile tile) {
			if (!Tile.IsTileValid(tile))
				return Intent.Disabled;

			if (IsLockedInArmy())
				return Intent.Disabled;

			// "Immobile: Can't move"
			// (https://forums.civfanatics.com/threads/is-there-a-guide-on-unit-abilities.57822/).
			if (unitType.isImmobile)
				return Intent.Disabled;

			var unitOwner = this.owner;

			// TODO: Perhaps this is not sufficient, but it is for now,
			// since otherwise we can move air units on land and sea
			if (this.IsAirUnit())
				return Intent.Disabled;

			if (unitOwner.isHuman && !unitOwner.HasExploredTile(tile))
				return Intent.MoveFreely;

			var hasOwnCity = HasOwnCity(tile, unitOwner);

			// Wheeled units stay off mountains, jungle and the like without a
			// road, and land units off impassable terrain (in some mods)
			// without one. Whether the tile they come from has one too is checked by
			// the move itself (see UnitPrototype.CanMoveBetween), since this
			// is also asked about tiles further away.
			if (!unitType.CanEnterTerrainOf(tile))
				return Intent.Disabled;

			// Keep land units on land and sea units on water
			if (this.IsWaterUnit() && tile.IsLand()) {
				if (hasOwnCity) return Intent.MoveFreely;

				return Intent.Disabled;
			}

			if (this.CanBoardTransportOnTile(tile))
				return Intent.Load;

			if (this.CanUnloadToTile(tile))
				return Intent.Unload;

			if (this.IsLandUnit() && !tile.IsLand())
				return Intent.Disabled;

			// Nothing on the tile to notice or fight: everything below comes
			// down to moving freely.
			// Invisible units the owner can't see don't count (see
			// MapUnit_Invisible.cs).
			if (tile.UnitsSeenBy(unitOwner).Count == 0 && !tile.HasCity() && !tile.hasBarbarianCamp)
				return Intent.MoveFreely;

			var hasForeignCity = HasForeignCity(tile, unitOwner);
			var isHumanOwner = this.owner.isHuman;
			var isActiveTile = this.owner.tileKnowledge.isActiveTile(tile);

			if (isHumanOwner && !isActiveTile && hasForeignCity) {
				return Intent.Disabled;
			}

			if (isHumanOwner && !isActiveTile) {
				return Intent.MoveFreely;
			}

			var hasHostileUnits = HasHostileUnits(tile, unitOwner);
			var hasForeignUnits = HasForeignUnits(tile, unitOwner);
			var foreignOwner = ForeignOwner(tile, unitOwner);
			var hasHostileCity = HasHostileCity(tile, unitOwner);
			var cityOwner = tile.cityAtTile?.owner;
			var hasBarbCamp = tile.hasBarbarianCamp;
			var isCombatUnit = this.IsCombatUnit();
			var distanceToTile = this.location.DistanceTo(tile);

			if (isHumanOwner && (hasForeignCity || hasHostileCity || hasForeignUnits || hasHostileUnits) && distanceToTile > 1) {
				return Intent.Disabled;
			}

			if (!isCombatUnit) {
				if (hasForeignUnits || hasHostileUnits) {
					return Intent.NoticeUnit;
				}
				if (hasForeignCity || hasHostileCity || hasBarbCamp) {
					return Intent.NoticeCity;
				}
			}

			// TODO: add check for when we want to pillage something

			if (isCombatUnit) {
				if (hasHostileUnits || hasHostileCity) {
					// Only amphibious units can attack straight off a ship.
					if (this.IsLandUnit() && !this.location.IsLand() && !this.unitType.isAmphibious) {
						return Intent.Disabled;
					}
					return Intent.Fight;
				}
				// Hidden nationality units attack units of civs they're at
				// peace with, outside cities, without war (see
				// MapUnit_HiddenNationality.cs).
				if (hasForeignUnits && !tile.HasCity() && unitType.hasHiddenNationality && distanceToTile == 1) {
					if (this.IsLandUnit() && !this.location.IsLand() && !this.unitType.isAmphibious) {
						return Intent.Disabled;
					}
					return Intent.Fight;
				}
				if (hasForeignUnits) {
					if (distanceToTile == 1) {
						if (EngineStorage.gameData.AreInLockedPeace(unitOwner, foreignOwner)) {
							return Intent.NoticeAlliance;
						}
						return Intent.WarDeclaration;
					}

					if (isActiveTile) {
						return Intent.Disabled;
					}
				}
				if (hasForeignCity) {
					if (distanceToTile == 1) {
						if (EngineStorage.gameData.AreInLockedPeace(unitOwner, cityOwner)) {
							return Intent.NoticeAlliance;
						}
						return Intent.WarDeclaration;
					}
					return Intent.Disabled;
				}
			}

			return Intent.MoveFreely;
		}

		// Whether the tile is water this ship risks sinking in. Ships that
		// "sink in sea" (the Galley) are at risk on Sea tiles, and those that
		// "sink in ocean" (the Galley, the Caravel) on Ocean tiles, but only
		// until their owner learns to sail those waters (see
		// Player.CanSailSafelyOn): a Galley is safe anywhere once its owner
		// can build Galleons. Ships may still sail into unsafe water, but may
		// sink if they end their turn there (see SinkShipsInUnsafeWater).
		public bool IsUnsafeWater(Tile tile) {
			if (!IsWaterUnit())
				return false;
			TerrainType terrain = tile.baseTerrainType;
			bool atRisk = (terrain.IsOcean && unitType.sinksInOcean) || (terrain.IsSea && unitType.sinksInSea);
			return atRisk && !owner.CanSailSafelyOn(terrain);
		}

		public bool CanEnterPeacefully(Tile tile) {
			return CanEnterPeacefully(tile, out _);
		}
		public bool CanEnterPeacefully(Tile tile, out Intent intent) {
			intent = this.ResolveIntent(tile);
			return intent == Intent.MoveFreely || intent == Intent.Load || intent == Intent.Unload;
		}

		public bool CanEnter(Tile tile) {
			return CanEnter(tile, out _);
		}
		public bool CanEnter(Tile tile, out Intent intent) {
			var canEnterPeacefully = CanEnterPeacefully(tile, out var it);
			intent = it;
			return canEnterPeacefully || it == Intent.Fight;
		}

		public bool CanEnterForcefully(Tile tile) {
			return CanEnterForcefully(tile, out _);
		}
		public bool CanEnterForcefully(Tile tile, out Intent intent) {
			var canEnter = CanEnter(tile, out var it);
			intent = it;
			return canEnter || it == Intent.WarDeclaration;
		}

		// Whether this unit can board a transport on the tile. Units board
		// ships just by moving onto them, but only join an army when ordered
		// to (explicitLoad), so walking onto an army's tile doesn't load them.
		private bool CanBoardTransportOnTile(Tile tile, bool explicitLoad = false) {
			if (!IsLoadable())
				return false;

			foreach (MapUnit transport in tile.unitsOnTile) {
				if (transport != this && transport.CanCarryUnits() && (explicitLoad || !transport.IsArmy()) && transport.CanLoad(this))
					return true;
			}

			return false;
		}

		private bool CanUnboardTransportToTile(Tile tile) {
			return IsLoadable() && IsLoaded() && tile.IsLand();
		}

		// The transports (or, on the player's order, armies) on the tile this
		// unit could board, the one it boards unasked first: one that already
		// carries units, then the one with the most room. The Load order asks
		// which when there are several (Civ3's "What Transport?" popup,
		// script.txt's SELECT_TRANSPORT).
		public List<MapUnit> TransportsToBoard(Tile tile, bool explicitLoad = false) {
			if (!IsLoadable() || !Tile.IsTileValid(tile))
				return [];
			return tile.unitsOnTile
				.Where(u => u != this && u.CanCarryUnits() && (explicitLoad || !u.IsArmy()) && u.CanLoad(this))
				.OrderBy(t => t.IsEmpty())
				.ThenByDescending(t => t.FreeCapacity())
				.ToList();
		}

		// UNVERIFIED (no Civ3 source found): moving onto a tile with several
		// transports picks one unasked, rather than asking as Load does.
		private MapUnit SelectTransportToBoard(Tile tile, bool explicitLoad = false) {
			return TransportsToBoard(tile, explicitLoad).FirstOrDefault();
		}

		private MapUnit FindTransportToUnboard(Tile tile, ID transport) {
			return tile.unitsOnTile.FirstOrDefault(t => t.id == transport);
		}

		private bool CanLoad(MapUnit mapUnit) {
			if (owner != mapUnit.owner)
				return false;

			if (!mapUnit.IsLoadable())
				return false;

			if (mapUnit == this)
				return false;

			// Units join an army only in the city that made it, until it
			// first leaves (see CanTakeMembersHere).
			if (IsArmy() && !CanTakeMembersHere())
				return false;

			var hasRoom = !IsFull();

			// Armies only take land combat units. Units that carry tactical
			// missiles (Nuclear Submarines) take only those, and carriers take
			// only aircraft. Transports that "can only transport units w/
			// Foot unit Flag" (the Helicopter;
			// https://forums.civfanatics.com/threads/is-there-a-guide-on-unit-abilities.57822/)
			// take only foot soldiers. Other transports take any land unit
			// for now.
			bool suitableUnit;
			if (IsArmy())
				suitableUnit = mapUnit.unitType.CanJoinArmy();
			else if (unitType.canCarryFootUnitsOnly)
				suitableUnit = mapUnit.IsLandUnit() && mapUnit.unitType.isFootSoldier;
			else if (unitType.canCarryTacticalMissiles)
				suitableUnit = mapUnit.unitType.isTacticalMissile;
			else if (unitType.canCarryAircraft)
				suitableUnit = mapUnit.IsAirUnit();
			else
				suitableUnit = mapUnit.IsLandUnit();
			return hasRoom && suitableUnit;
		}

		// TODO: Transport chaining
		// TODO: Amphibious assault

		private bool CanUnloadToTile(Tile tile) {
			if (!CanTransport())
				return false;

			var isValidLanding = tile.IsLand();
			return !IsEmpty() && isValidLanding;
		}

		public int FreeCapacity() {
			return Capacity() - PassengerCount();
		}

		private bool IsEmpty() => Capacity() > 0 && PassengerCount() == 0;
		private bool IsFull() => Capacity() > 0 && FreeCapacity() <= 0;

		private static bool HasHostileUnits(Tile tile, Player player) {
			foreach (MapUnit other in tile.unitsOnTile) {
				if (player != other.owner && (AtWar(player, other.owner) || other.IsFairGameFor(player)) && other.IsSeenBy(player))
					return true;
			}
			return false;
		}
		private static bool HasForeignUnits(Tile tile, Player player) {
			foreach (MapUnit other in tile.unitsOnTile) {
				if (player != other.owner && other.IsSeenBy(player))
					return true;
			}
			return false;
		}

		// The owner of the first foreign unit on the tile the player sees.
		private static Player ForeignOwner(Tile tile, Player player) {
			foreach (MapUnit other in tile.unitsOnTile) {
				if (other.owner != player && other.IsSeenBy(player))
					return other.owner;
			}
			return null;
		}

		private static bool HasHostileCity(Tile tile, Player player) {
			return tile.HasCity() && AtWar(player, tile.cityAtTile.owner);
		}
		private static bool HasForeignCity(Tile tile, Player player) {
			return tile.HasCity() && tile.cityAtTile.owner != player;
		}
		private static bool HasOwnCity(Tile tile, Player player) {
			return tile.HasCity() && tile.cityAtTile.owner == player;
		}

		private float SumWorkerProgress(Tile tile, Terraform workerJob) {
			float result = 0;
			foreach (MapUnit unit in tile.unitsOnTile) {
				if (unit.WorkerJob == workerJob) {
					result += unit.WorkerProgressTowardsJob;
				}
			}
			return result;
		}

		public int TurnsToCompleteTerraform(Terraform t) {
			// Figure out how much work remains to do on this particular job.
			int remainingTerraformCost = GetWorkerJobCost(location, t) - (int)this.SumWorkerProgress(location, t);

			// Figure out how fast all of the wokers doing this particular
			// terraform will work.
			float combinedWorkerSpeed = this.workerSpeed();
			foreach (MapUnit unit in location.unitsOnTile) {
				if (unit.id != this.id && unit.WorkerJob == t) {
					combinedWorkerSpeed += unit.workerSpeed();
				}
			}

			// Divide the two, rounding up.
			return (int)Math.Ceiling(remainingTerraformCost / combinedWorkerSpeed);
		}

		public bool canBuildCity() {
			if (!unitType.actions.Contains(UnitAction.BuildCity)) {
				return false;
			}
			if (location.HasCity() || !location.IsAllowCities()) {
				return false;
			}
			// A city may be founded inside another civ's borders, which at
			// peace declares war on it (see BuildCity), so not in the land of
			// a civ we're locked in peace with, such as a teammate.
			if (location.PeacefulOwnerFor(owner) is Player landOwner && EngineStorage.gameData?.AreInLockedPeace(owner, landOwner) == true) {
				return false;
			}
			foreach (Tile tile in location.neighbors.Values) {
				if (tile.HasCity())
					return false;
			}
			return true;
		}

		public bool CanPerformTerraformAction(Terraform terraform) {
			return CanPerformTerraformAction(terraform, location);
		}

		public bool CanPerformTerraformAction(Terraform terraform, Tile tile) {
			// The cheap checks go first: the requirements can run Lua and
			// look up trade access.
			return unitType.terraformActions.Contains(terraform)
				&& !tile.HasCity()
				&& terraform.MeetsRequirements(owner, tile);
		}

		public float workerSpeed() {
			// The government's worker rate, scaled by the unit's worker
			// strength (Engineers work twice as fast as Workers). The
			// standard rules' rates of 1, 2, 3 and 4 are the Civilopedia's
			// "Worker Efficiency" of 50% (Anarchy), 100%, 150% (Democracy)
			// and 200% (Fascism)
			// (https://civfanatics.com/civ3/civilopedia/governments/).
			int rate = owner?.government?.workerRate > 0 ? owner.government.workerRate : DefaultWorkerRate;
			float strength = unitType.workerStrength > 0 ? unitType.workerStrength : 1;
			float progressPerTurn = rate * strength;
			// A tech with the BIQ's "doubles worker rate" flag doubles it,
			// with the government's rate and the unit's strength
			// (https://forums.civfanatics.com/threads/doubles-worker-speed-does-not-work-for-all-workers.409473/).
			// Replaceable Parts speeds up workers and slaves alike, "the
			// RATIO remains constant (slave = 50% worker)" (the thread at
			// SlaveWorkerFactor, Vanilla and PTW); the BIQ decides which tech.
			if (owner != null && owner.KnowsTechWhere(t => t.DoublesWorkerRate)) {
				progressPerTurn *= 2;
			}
			if (this.IsCaptive()) {
				progressPerTurn *= SlaveWorkerFactor;
			}
			// Industrious workers are "50% faster in Conquests"
			// (https://civfanatics.com/civ3/faq/).
			if (owner.civilization.traits.Contains(Civilization.Trait.Industrious)) {
				progressPerTurn *= 1.5f;
			}
			// Each worker makes a whole number of points a turn, rounded
			// down but never below one: "Every worker produces every turn an
			// integer number." and "The number is always rounded down, but
			// the total minimum of 1 is never violated." (justanick,
			// https://forums.civfanatics.com/threads/how-many-workers-do-i-need-to-do-this.452798/,
			// Conquests). So an Industrious civ's slave makes 1 (1.5 rounded
			// down) before Replaceable Parts, like anyone's slave, and an
			// Anarchy slave still makes 1.
			return Math.Max(1, (int)Math.Floor(progressPerTurn));
		}

		public void resetWorkerJob() {
			WorkerJob = null;
			WorkerProgressTowardsJob = 0;
			animate(AnimatedAction.BLANK, AnimationEnding.Repeat);
		}

		public bool canAutomate() {
			return unitType.actions.Contains(UnitAction.Automate);
		}

		public bool canExplore() {
			return unitType.actions.Contains(UnitAction.Explore);
		}

		public List<Terraform> GetAvailableTerraforms() {
			return EngineStorage.gameData.Terraforms.Where(CanPerformTerraformAction).ToList();
		}

		/**
		 * Helper function to get the available actions for a unit
		 * based on what terrain it is on.
		 **/
		public List<UnitAction> GetAvailableActions() {
			List<UnitAction> result = new();

			// A unit in a rigid army takes no orders of its own.
			if (IsLockedInArmy())
				return result;

			// Eventually, we should look this up somewhere to see what all actions we have (and mods might add more)
			// For now, this is still an improvement over the last iteration.
			UnitAction[] implementedActions = { UnitAction.Hold, UnitAction.Wait, UnitAction.Fortify, UnitAction.Disband, UnitAction.Goto, UnitAction.Bombard, UnitAction.Sentry };
			foreach (UnitAction action in implementedActions) {
				if (unitType.actions.Contains(action)) {
					result.Add(action);
				}
			}

			if (unitType.actions.Contains(UnitAction.Sentry)) {
				result.Add(UnitAction.SentryEnemyOnly);
			}
			if (canBuildCity()) {
				result.Add(UnitAction.BuildCity);
			}
			if (canExplore()) {
				result.Add(UnitAction.Explore);
			}
			if (canAutomate()) {
				result.Add(UnitAction.Automate);
			}
			if (CanBuildColony()) {
				result.Add(UnitAction.BuildColony);
			}
			if (CanJoinCity()) {
				result.Add(UnitAction.JoinCity);
			}

			if (CanBoardTransportOnTile(this.location, explicitLoad: true) && this.loadedOnUnitId == null) {
				result.Add(UnitAction.Load);
			}
			// Ships unload in port; an army that allows unloading can do so anywhere.
			if (CanUnloadToTile(this.location) && (this.location.HasCity() || IsArmy())) {
				result.Add(UnitAction.Unload);
			}
			if (GetAvailableUpgrade() != null) {
				result.Add(UnitAction.Upgrade);
			}
			if (CanPillage()) {
				result.Add(UnitAction.Pillage);
			}
			if (CanFormArmy()) {
				result.Add(UnitAction.BuildArmy);
			}
			if (CanOfferHurryProduction()) {
				result.Add(UnitAction.HurryBuilding);
			}
			if (CanStartScienceAge()) {
				result.Add(UnitAction.ScienceAge);
			}
			if (CanSacrifice()) {
				result.Add(UnitAction.Sacrifice);
			}
			AddAvailableAirMissions(result);
			foreach (AutoBombardMode mode in new[] { AutoBombardMode.Bombard, AutoBombardMode.Bombing, AutoBombardMode.PrecisionBombing }) {
				if (CanAutomateBombard(mode) && movementPoints.canMove) {
					result.Add(ActionFor(mode));
				}
			}

			// Eventually we will have advanced actions too, whose availability will rely on their base actions' availability.
			// unit.availableActions.Add("rename");

			return result;
		}
	}
}
