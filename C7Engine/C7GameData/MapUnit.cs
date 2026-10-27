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
				// An army's hit points are its members' pooled (see MapUnit_Army.cs).
				if (IsArmy() && location != null) {
					List<MapUnit> members = Passengers();
					if (members.Count > 0) {
						return members.Sum(m => m.maxHitPoints);
					}
				}
				return this.experienceLevel.baseHitPoints + this.unitType.hpBonus;
			}
		}
		public bool isFortified { get; set; }

		public bool isAutomated { get; set; }

		//sentry, etc. will come later.  For now, let's just have a couple things so we can cycle through units that aren't fortified.
		public int defensiveBombardsRemaining;

		public TileDirection facingDirection = TileDirection.SOUTHEAST;

		public float WorkerProgressTowardsJob { get; set; }
		public Terraform WorkerJob { get; set; }

		public ID loadedOnUnitId { get; set; }

		// Whether this unit has produced a great leader (Civ3's "Elite*"): each
		// unit can produce only one.
		public bool hasProducedLeader { get; set; }
		// A great leader born of research rather than battle (Civ3 uses the
		// one Leader unit for both): he can start an Age of Science and hurry
		// great wonders, but can't lead an army.
		public bool scientificLeader { get; set; }

		// Whether this unit has attacked or bombarded this turn. Without blitz,
		// a unit does so once a turn (Civilopedia: blitz units "can attack
		// multiple times in a single turn").
		public bool attackedThisTurn { get; set; }

		public bool CanAttackThisTurn() {
			return !attackedThisTurn || unitType.blitz;
		}

		public UnitAI currentAI;

		public MapUnit(ID id) {
			this.id = id;
		}

		internal MapUnit() { }

		public static MapUnit NONE = new MapUnit(ID.None("unit"));

		public static bool IsMapUnitValid(MapUnit mapUnit) {
			return mapUnit != null && mapUnit != NONE;
		}

		public bool IsBusy() {
			return isFortified || (path != null && path.PathLength() > 0) || WorkerJob != null || isAutomated;
		}

		public bool IsLandUnit() {
			return this.unitType.categories.Contains("Land");
		}
		public bool IsWaterUnit() {
			return this.unitType.categories.Contains("Sea");
		}
		public bool IsAirUnit() {
			return this.unitType.categories.Contains("Air");
		}

		public bool CanDefendOnLand() {
			return IsLandUnit() && CombatType(CombatRole.Defense).defense > 0;
		}

		public bool IsCombatUnit() {
			if (IsArmy()) {
				return Passengers().Count > 0;
			}
			return this.unitType.attack > 0 || this.unitType.defense > 0;
		}

		public bool CanBeActive() {
			return !this.IsBusy() && this.movementPoints.canMove && !IsInArmy();
		}

		public bool IsCaptive() {
			return !string.Equals(this.nationality.name, this.owner.civilization.name, StringComparison.CurrentCultureIgnoreCase);
		}

		// Whether units can load into this one: a transport with the unload
		// order, or an army (which they can't leave).
		public bool CanTransport() {
			return this.unitType.capacity > 0 && (this.unitType.actions.Contains(UnitAction.Unload) || IsArmy());
		}

		public bool IsLoadable() {
			return this.unitType.actions.Contains(UnitAction.Load);
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
			if (this.unitType.art.mainArt.variations != null) {
				if (this.unitType.isWorker && this.IsCaptive()) {
					if (this.unitType.art.mainArt.variations.FirstOrDefault(s => s.Key.EndsWith("SLAVE")).Value != null)
						return this.unitType.art.mainArt.variations.First(s => s.Key.EndsWith("SLAVE")).Value;
				}

				if (this.unitType.art.mainArt.variations.TryGetValue($"{this.owner.eraCivilopediaName}", out var value))
					return value;

				//TODO: add military + science leader variation
			}

			return this.unitType.art.mainArt.defaultName;
		}

		public string Describe() {
			if (IsArmy()) {
				return DescribeArmy();
			}
			UnitPrototype type = this.unitType;
			string exp = this.IsCombatUnit() ? $"{this.experienceLevel.displayName}" : "";
			string hPDesc = ((type.attack > 0) || (type.defense > 0)) ? $" ({this.hitPointsRemaining}/{this.maxHitPoints})" : "";
			string displayName = this.IsCaptive() ? $" ({this.nationality.adjective}) {this.name}" : $" {this.name}";
			string attackDesc = (type.bombard > 0) ? $"{type.attack}({type.bombard})" : type.attack.ToString();
			string stats = $" ({attackDesc}.{type.defense}.{(EngineStorage.uiControllerID == this.owner.id ? $"{this.movementPoints.getMixedNumber()}/" : "")}{type.movement})";
			return $"{exp}{hPDesc}{displayName}{stats}".Trim();
		}

		// Like "(9/12) Army of Swordsman, Swordsman, Horseman (3.2.1/2)": the
		// pooled hit points, the members, and the strengths it fights with now.
		private string DescribeArmy() {
			List<MapUnit> members = Passengers();
			string contents = members.Count > 0 ? $" of {string.Join(", ", members.Select(m => m.name))}" : " (empty)";
			string moves = EngineStorage.uiControllerID == owner.id ? $"{movementPoints.getMixedNumber()}/" : "";
			string stats = $"({BaseCombatStrength(CombatRole.Attack)}.{BaseCombatStrength(CombatRole.Defense)}.{moves}{MovementPerTurn()})";
			return $"({hitPointsRemaining}/{maxHitPoints}) {name}{contents} {stats}";
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

		private const int JOB_PROGRESS_WORKER = 2;
		private const int JOB_PROGRESS_SLAVE = 1;

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

			if (animationsEnabled && !skipAnimations) {
				var msg = new MsgStartUnitAnimation(this, action, ending);
				msg.send();

				await EngineStorage.WaitForAnimationFinished(msg.animationId);
			}
			if (this.owner.isHuman)
				new MsgUnitMoved(this).send();
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

		// The Great Wall "doubles unit combat values versus Barbarians": +100%,
		// added to the other bonuses as Civ3's bonuses are.
		public static readonly StrengthBonus GreatWallBonus = new("Great Wall", 1.0);

		public IEnumerable<StrengthBonus> ListStrengthBonusesVersus(MapUnit opponent, CombatRole role, TileDirection? attackDirection) {
			GameData gD = EngineStorage.gameData;

			// (The map asks about MapUnit.NONE, which has no owner.)
			if (opponent?.owner?.isBarbarians == true
				&& owner.OwnsBuilding(b => b.HasWonderFlag(Save.SaveBuilding.WonderFlag.DoubleCombatVsBarbarians))) {
				yield return GreatWallBonus;
			}

			if (role.Defending()) {
				if (isFortified)
					yield return gD.fortificationBonus;

				yield return location.overlayTerrainType.defenseBonus;

				foreach (StrengthBonus sb in location.overlays.GetDefenseBonuses()) {
					yield return sb;
				}

				if ((!role.Bombarding()) && (attackDirection is TileDirection dir) && location.HasRiverCrossing(dir.Reversed()))
					yield return gD.riverCrossingBonus;

				if (location.cityAtTile != null) {
					foreach (StrengthBonus sb in location.cityAtTile.GetDefenseBonuses()) {
						yield return sb;
					}
				}
			}
		}

		public double StrengthVersus(MapUnit opponent, CombatRole role, TileDirection? attackDirection) {
			return BaseCombatStrength(role) * StrengthBonus.ListToMultiplier(ListStrengthBonusesVersus(opponent, role, attackDirection));
		}

		public bool CanDefendAgainst(MapUnit attacker) {
			//Basically, unit type must match.  Sea/air units in a city/airfield can't defend against land units.
			//Land units on a boat or planes on a carrier can't defend against boats.  Anti-air is another category that should be checked before the direct combat.
			//Potential future hybrid units that have multiple categories (e.g. amphibious vehicles) may contain more than one category.
			if (attacker.unitType.categories.Contains("Land") && !unitType.categories.Contains("Land")) {
				return false;
			}
			if (attacker.unitType.categories.Contains("Sea") && !unitType.categories.Contains("Sea")) {
				return false;
			}
			if (attacker.unitType.categories.Contains("Air") && !unitType.categories.Contains("Air")) {
				return false;
			}
			return true;
		}

		// Answers the question: if "opponent" is attacking the tile that this unit is standing on, does this unit defend instead of "otherDefender"?
		// Note that otherDefender does not necessarily belong to the same civ as this  Under standard Civ 3 rules you can't have units belonging
		// to two different civs on the same tile, but we don't want to assume that. In that case, whoever is an enemy of "opponent" should get
		// priority. Otherwise it's just whoever is stronger on defense.
		public bool HasPriorityAsDefender(MapUnit otherDefender, MapUnit opponent) {
			Player opponentPlayer = opponent.owner;
			bool weAreEnemy           = !opponentPlayer?.IsAtPeaceWith(owner) ?? false;
			bool otherDefenderIsEnemy = !opponentPlayer?.IsAtPeaceWith(otherDefender.owner) ?? false;

			if (weAreEnemy && !otherDefenderIsEnemy)
				return true;
			if (otherDefenderIsEnemy && !weAreEnemy)
				return false;

			double ourTotalStrength = StrengthVersus(opponent, CombatRole.Defense, null) * hitPointsRemaining;
			double theirTotalStrength = otherDefender.StrengthVersus(opponent, CombatRole.Defense, null) * otherDefender.hitPointsRemaining;
			return ourTotalStrength > theirTotalStrength;
		}


		public void RollToPromote(MapUnit opponent) {
			// Barbarians can't promote.
			if (owner.isBarbarians) {
				return;
			}

			double promotionChance = experienceLevel.promotionChance;
			if (opponent.owner.isBarbarians)
				promotionChance /= 2.0;
			if (owner.civilization.traits.Contains(Civilization.Trait.Militaristic))
				promotionChance *= 2;
			if (GameData.rng.NextDouble() < promotionChance) {
				Promote();
				animate(AnimatedAction.VICTORY);
			}
		}

		public void Promote() {
			ExperienceLevel nextLevel = EngineStorage.gameData.GetExperienceLevelAfter(experienceLevel);
			if (nextLevel != null) {
				experienceLevelKey = nextLevel.key;
				experienceLevel = nextLevel;
				hitPointsRemaining++;
			}
		}

		// An army retreats like its fighting member, at the army's speed.
		public double RetreatChance(MapUnit opponent, bool isAttacking) {
			ExperienceLevel experience = ArmyFighter(isAttacking ? CombatRole.Attack : CombatRole.Defense).experienceLevel;
			return ((MovementPerTurn() > 1) && (opponent.MovementPerTurn() <= 1)) ? experience.retreatChance : 0.0;
		}

		internal TileDirection GetAttackAnimationDirection(TileDirection attackDirection) {
			return unitType.rotateBeforeAttack ? attackDirection.RotatedCounterClockwise90Degrees() : attackDirection;
		}

		internal TileDirection GetDefenseAnimationDirection(TileDirection attackDirection) {
			return GetAttackAnimationDirection(attackDirection.Reversed());
		}

		// Civilopedia "Repairing Damage": a unit that stays put for a turn
		// heals in friendly or neutral territory, not in enemy territory unless
		// its civ has Battlefield Medicine; in one of our cities with a
		// Barracks (a Harbor for ships, an Airport for planes) it heals
		// completely.
		public int HealRateAt(Tile location) {
			GameData gD = EngineStorage.gameData;
			City city = location.cityAtTile;
			if (city != null && city != City.NONE && city.owner == owner && CityTrainsMyKind(city))
				return maxHitPoints;
			bool inFriendlyCity = (city != null) && (city != City.NONE) && owner.IsAtPeaceWith(city.owner);
			if (inFriendlyCity)
				return gD.healRateInCity;
			if (unitType.categories.Contains("Sea"))
				return 0;
			Player territory = location.OwningPlayer();
			if (territory == owner)
				return gD.healRateInFriendlyField;
			if (territory != null && !owner.IsAtPeaceWith(territory)) {
				bool medicine = owner.OwnsBuilding(b => b.HasWonderFlag(Save.SaveBuilding.WonderFlag.AllowsEnemyTerritoryHealing));
				return medicine ? gD.healRateInNeutralField : gD.healRateInHostileField;
			}
			return gD.healRateInNeutralField;
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
			NoticeAttacked,   // a unit without blitz tries to fight a second battle this turn
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

			// Terrain can be impassable to every unit (e.g. some mods'
			// deserts) or only to some of them (mountains, jungle, marsh and
			// volcano are impassable to wheeled units, unless the tile is
			// roaded). A barbarian camp tile stays enterable either way, like a
			// city, so the camp's garrison can move out and back in.
			if (tile.IsImpassableTo(this.unitType) && !tile.hasBarbarianCamp)
				return Intent.Disabled;

			var unitOwner = this.owner;

			// TODO: Perhaps this is not sufficient, but it is for now,
			// since otherwise we can move air units on land and sea
			if (this.IsAirUnit())
				return Intent.Disabled;

			if (unitOwner.isHuman && !unitOwner.HasExploredTile(tile))
				return Intent.MoveFreely;

			var hasOwnCity = HasOwnCity(tile, unitOwner);

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
			var foreignOwner = ForeignOwner(tile);
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
				// Without blitz, a unit fights one battle a turn (Civ3); taking
				// units or a city that can't defend themselves isn't a battle.
				if (hasHostileUnits && !CanAttackThisTurn()) {
					MapUnit defender = tile.FindTopDefender(this);
					if (defender != NONE && defender.CanDefendItself()) {
						return Intent.NoticeAttacked;
					}
				}
				if (hasHostileUnits || hasHostileCity) {
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

		private bool CanBoardTransportOnTile(Tile tile) {
			if (!IsLoadable())
				return false;

			var availableTransports = tile.unitsOnTile.Where(u => u.CanTransport());
			foreach (var transport in availableTransports) {
				if (transport.CanLoad(this))
					return true;
			}

			return false;
		}

		private bool CanUnboardTransportToTile(Tile tile) {
			return IsLoadable() && IsLoaded() && tile.IsLand();
		}

		/// <summary>
		/// The transports on the tile that this unit can board, best candidate first.
		/// </summary>
		internal List<MapUnit> GetTransportsToBoard(Tile tile) {
			// Sort candidates by free capacity, but prefer transports that already have units
			return tile.unitsOnTile
				.Where(u => u.CanTransport() && u.CanLoad(this))
				.OrderBy(t => !t.IsEmpty())
				.ThenByDescending(t => t.FreeCapacity())
				.ToList();
		}

		private MapUnit SelectTransportToBoard(Tile tile) {
			// TODO: Let the human player choose when moving onto a tile with several transports
			return GetTransportsToBoard(tile).FirstOrDefault();
		}

		private MapUnit FindTransportToUnboard(Tile tile, ID transport) {
			return tile.unitsOnTile.FirstOrDefault(t => t.id == transport);
		}

		private bool CanLoad(MapUnit mapUnit) {
			if (IsArmy())
				return CanTakeIntoArmy(mapUnit);

			if (owner != mapUnit.owner)
				return false;

			if (!mapUnit.IsLoadable())
				return false;

			// An army needs a place for itself and one for each member.
			bool hasRoom = FreeCapacity() >= 1 + mapUnit.Passengers().Count;

			// TODO: type restrictions: only subs can carry nukes, carriers take aircraft, etc.
			var suitableUnit = mapUnit.IsLandUnit();  // only land units in transports for now
			return hasRoom && suitableUnit;
		}

		// TODO: Transport chaining
		// TODO: Amphibious assault

		private bool CanUnloadToTile(Tile tile) {
			// Units can't leave an army.
			if (!CanTransport() || IsArmy())
				return false;

			var isValidLanding = tile.IsLand();
			return !IsEmpty() && isValidLanding;
		}

		// Counting an army on board as itself and its members.
		public int FreeCapacity() {
			int capacity = IsArmy() ? ArmyCapacity() : this.unitType.capacity;
			return capacity - Passengers().Sum(u => 1 + u.Passengers().Count);
		}

		private bool IsEmpty() => unitType.capacity > 0 && Passengers().Count == 0;

		private static bool HasHostileUnits(Tile tile, Player player) {
			foreach (MapUnit other in tile.unitsOnTile) {
				if (player != other.owner && AtWar(player, other.owner))
					return true;
			}
			return false;
		}
		private static bool HasForeignUnits(Tile tile, Player player) {
			foreach (MapUnit other in tile.unitsOnTile) {
				if (player != other.owner)
					return true;
			}
			return false;
		}

		private static Player ForeignOwner(Tile tile) {
			return tile.unitsOnTile.FirstOrDefault()?.owner;
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
			foreach (MapUnit unit in location.unitsOnTile.Where(u => u.id != this.id)) {
				if (unit.WorkerJob == t) {
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
			return location.neighbors.Values.All(tile => !tile.HasCity());
		}

		public bool CanPerformTerraformAction(Terraform terraform) {
			return CanPerformTerraformAction(terraform, location);
		}

		public bool CanPerformTerraformAction(Terraform terraform, Tile tile) {
			var containsTerraform = unitType.terraformActions.Contains(terraform);
			var meetsRequirements = terraform.MeetsRequirements(owner, tile);
			var hasCity = tile.HasCity();
			return containsTerraform && meetsRequirements && !hasCity;
		}

		public float workerSpeed() {
			float progressPerTurn = this.IsCaptive() ? JOB_PROGRESS_SLAVE : JOB_PROGRESS_WORKER;
			if (owner.civilization.traits.Contains(Civilization.Trait.Industrious)) {
				progressPerTurn *= 1.5f;
			}
			return progressPerTurn;
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

			// Units in an army only act with it.
			if (IsInArmy()) {
				return result;
			}

			// Eventually, we should look this up somewhere to see what all actions we have (and mods might add more)
			// For now, this is still an improvement over the last iteration.
			UnitAction[] implementedActions = { UnitAction.Hold, UnitAction.Wait, UnitAction.Fortify, UnitAction.Disband, UnitAction.Goto, UnitAction.Bombard };
			foreach (UnitAction action in implementedActions) {
				if (unitType.actions.Contains(action)) {
					result.Add(action);
				}
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

			// The load order also puts a unit into an army on its tile.
			if (GetTransportsToBoard(this.location).Count > 0 && this.loadedOnUnitId == null) {
				result.Add(UnitAction.Load);
			}
			if (CanUnloadToTile(this.location) && this.location.HasCity()) {
				result.Add(UnitAction.Unload);
			}

			if (CanPillage()) {
				result.Add(UnitAction.Pillage);
			}
			if (CanJoinCity()) {
				result.Add(UnitAction.JoinCity);
			}
			// Shown whenever the unit could be upgraded here, even without the
			// gold, so the player can see the price.
			if (UpgradeOffer() != null) {
				result.Add(UnitAction.Upgrade);
			}
			if (CanBuildArmy()) {
				result.Add(UnitAction.BuildArmy);
			}
			if (CanHurryProduction()) {
				result.Add(UnitAction.HurryProduction);
			}
			if (CanStartAgeOfScience()) {
				result.Add(UnitAction.StartAgeOfScience);
			}

			// Eventually we will have advanced actions too, whose availability will rely on their base actions' availability.
			// unit.availableActions.Add("rename");

			return result;
		}
	}
}
