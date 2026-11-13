using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Pathing;
using C7GameData.AIData;

namespace C7GameData;

public partial class MapUnit {
	public void OnBeginTurn(bool skipTurn = false) {
		int maxMP = MaxMovementPoints();
		bool restedLastTurn = movementPoints.remaining >= maxMP || heldWithoutMoving;
		if (restedLastTurn && !skipTurn) {
			bool wasHurt = IsHurt();
			// The members of an army heal when the army rests, since they don't
			// move by themselves.
			if (IsArmy()) {
				foreach (MapUnit member in Passengers())
					member.Heal();
			}
			if (!IsInArmy())
				Heal();
			// "A fully healed fortified unit of a human player wakes up"
			// (Perez's decompile of Civ3 Conquests,
			// doc/perez-findings/Civ3_Findings.md, Round 2b "Units";
			// authoritative per the owner's 2026-11-12 ruling), so the
			// player is asked for its orders again. The AI's stay put.
			if (wasHurt && !IsHurt() && isFortified && owner?.isHuman == true) {
				Wake();
			}
		}
		heldWithoutMoving = false;

		if (skipTurn) {
			movementPoints.skipTurn();
		} else {
			movementPoints.reset(maxMP);
		}

		defensiveBombardsRemaining = 1;
		hasAttackedThisTurn = false;
		hasPillagedThisTurn = false;
		// Air missions end when the owner's own turn begins, not at the end
		// of the round (see BeginOwnersTurn).

		if (isSentried && location.neighbors.Values.Any(t => t.unitsOnTile.Any(ShouldWakeSentryFor))) {
			Wake();
		}
	}

	// Whether the unit, or for an army any of its members, has lost hit
	// points.
	private bool IsHurt() {
		if (IsArmy()) {
			return Passengers().Any(m => m.hitPointsRemaining < m.maxHitPoints);
		}
		return hitPointsRemaining < maxHitPoints;
	}

	private void Heal() {
		int maxHP = maxHitPoints;
		if (hitPointsRemaining < maxHP)
			hitPointsRemaining += HealRateAt(location);
		if (hitPointsRemaining > maxHP)
			hitPointsRemaining = maxHP;
	}

	// `movedThere` is whether the unit got there by its own move, rather
	// than being put there, as by retreating from combat or withdrawing.
	public void OnEnterTile(Tile tile, bool movedThere) {
		//Add to player knowledge of tiles
		owner.tileKnowledge.AddTilesToKnown(tile);

		// Disperse barb camp
		if (BarbarianInteractions.DisperseCamp(EngineStorage.gameData, tile, owner)) {
			animate(MapUnit.AnimatedAction.VICTORY);
			if (owner.IsToldNews) {
				// Civ3's news (Conquests' Text/script.txt,
				// #CAPTURE_BARBARIAN): "We dispersed a $BARBARIAN0
				// encampment and took 25 gold!"
				string tribe = BarbarianInteractions.TribeName(EngineStorage.gameData, tile);
				new MsgShowMilitaryAdvisorPopup(owner, $"We dispersed a {tribe} encampment and took {BarbarianInteractions.CampDispersalGold} gold!", happy: true).send();
			}
			// The survivors move to a new site.
			BarbarianInteractions.RelocateCamp(EngineStorage.gameData);
		}

		// See what's in the goody hut, if there is one. Only a unit that
		// chose to go there opens it, not one retreating from a battle onto
		// it.
		if (movedThere) {
			GoodyHuts.Enter(EngineStorage.gameData, this, tile);
		}

		// Capture the enemy city on the tile unless we're the barbarians,
		// who don't hold cities: they sack it and leave (see
		// City.SackedByBarbarians).
		if (tile.HasCity() && !owner.IsAtPeaceWith(tile.cityAtTile.owner)) {
			if (owner.isBarbarians) {
				City sacked = tile.cityAtTile;
				Player victim = sacked.owner;
				City.BarbarianSack sack = sacked.SackedByBarbarians();
				this.RemoveFromPlay();
				if (victim.IsToldNews) {
					string loss = sack.outcome switch {
						City.BarbarianSackOutcome.Gold => $"stolen {sack.amount} gold",
						City.BarbarianSackOutcome.Citizens => "killed some of its people",
						City.BarbarianSackOutcome.Production => $"destroyed {sack.amount} shields of its production",
						_ => "found nothing to take",
					};
					new MsgShowMilitaryAdvisorPopup(victim, $"Barbarians have sacked {sacked.name} and {loss}!\nWe need a stronger military.", happy: false).send();
				}
			} else {
				City city = tile.cityAtTile;
				Player formerOwner = city.owner;
				Player.AddWarWearinessForLostCity(formerOwner, owner, city.residents.Count);
				PlayerRelationship.RecordWarLoss(formerOwner, owner, cities: 1);
				CityInteractions.CaptureCity(city, owner);
				// A city that survived being taken may resist its new owner.
				if (tile.cityAtTile == city && city.owner == owner) {
					city.StartResistance(formerOwner);
				}
			}
		}

		// Check to see if we've discovered a new civ.
		//
		// TODO: this should really be based on interactions with our "visible"
		// tiles. Also civ3 only counts border-based discovery from rank 1
		// tiles, not rank 2+.
		foreach (Tile t in tile.neighbors.Values) {
			if (t.unitsOnTile.Count > 0 && owner != t.unitsOnTile[0].owner) {
				owner.EnsureRelationshipExists(t.unitsOnTile[0].owner);
			}
			if (t.owningCity != null && owner != t.owningCity.owner) {
				owner.EnsureRelationshipExists(t.owningCity.owner);
			}
		}
	}

	public void Fortify() {
		StopAutoBombard();
		ResetFacingDirection();
		isFortified = true;
		animate(MapUnit.AnimatedAction.FORTIFY);
	}

	public void Wake() {
		isFortified = false;
		isSentried = false;
		sentryEnemyOnly = false;
		StopAutoBombard();
	}

	public void Sentry(bool enemyOnly) {
		StopAutoBombard();
		ResetFacingDirection();
		isSentried = true;
		sentryEnemyOnly = enemyOnly;
	}

	private bool ShouldWakeSentryFor(MapUnit other) {
		if (other.owner == owner) {
			return false;
		}
		return !sentryEnemyOnly || !owner.IsAtPeaceWith(other.owner);
	}

	// Wakes sentries next to a tile this unit just entered.
	private void WakeNearbySentries(Tile tile) {
		foreach (Tile t in tile.neighbors.Values) {
			foreach (MapUnit u in t.unitsOnTile) {
				if (u.isSentried && u.ShouldWakeSentryFor(this)) {
					u.Wake();
				}
			}
		}
	}

	// Automates the worker in one of Civ3's kinds of automation, or plain
	// automation if null. The kinds about a city take the nearest one;
	// road and rail to need their destination.
	public void Automate(WorkerAutomation how) {
		if (how != null && how.NeedsCity && how.target == null) {
			how.target = WorkerAutomation.NearestCity(this)?.location;
		}
		if (how != null && (how.NeedsCity || how.NeedsDestination) && (how.target == null || how.target == Tile.NONE)) {
			log.Information("{Unit} has no city or destination for {Automation}", this, how);
			return;
		}
		workerAutomation = how;
		Automate();
	}

	// Automates the worker in its kind of automation (workerAutomation),
	// if it has one.
	public void Automate() {
		Wake();
		isAutomated = true;
		WorkerAIData? maybeAiData = WorkerAI.MakeAiData(this, owner);
		if (maybeAiData == null) {
			log.Information("Could not find anything to automate for {Unit} owned by {Player}", this, owner);
			isAutomated = false;
			return;
		}
		currentAI = new WorkerAI(maybeAiData);
		PlayAutomatedTurn();
	}

	public void Explore() {
		Wake();
		workerAutomation = null;
		isAutomated = true;
		ExplorerAIData? maybeAiData = ExplorerAI.MaybeMakeAiData(this, owner);
		if (maybeAiData == null) {
			log.Information("Could not find anything to explore for {Unit} owned by {Player}", this, owner);
			isAutomated = false;
			return;
		}
		currentAI = new ExplorerAI(maybeAiData);
		PlayAutomatedTurn();
	}

	public void SkipTurn() {
		// Holding uses up the unit's movement points, but a unit that holds
		// without having moved has still rested and should heal.
		if (movementPoints.remaining >= MaxMovementPoints()) {
			heldWithoutMoving = true;
		}
		movementPoints.skipTurn();
	}

	public async Task Disband() {
		await EngineStorage.gameData.DisbandUnit(this);
	}

	public void RemoveFromPlay() {
		EngineStorage.gameData.RemoveUnit(this);
	}

	// Moves the unit, and anything it carries, straight to the nearest tile
	// outside every other civ's borders that it could stand on and get to,
	// as when a civ agrees to take its units out of another's territory.
	// Returns false, leaving the unit where it is, if there is no such tile.
	public bool WithdrawToNearestFreeTile() {
		Tile destination = FindNearestFreeTile();
		if (destination == null) {
			return false;
		}
		MoveStraightTo(destination);
		return true;
	}

	// Moves the unit, and anything it carries, straight to the city, as
	// when the city it was in is given away (see
	// CityInteractions.TransferCity).
	public void WithdrawToCity(City city) {
		MoveStraightTo(city.location);
	}

	private void MoveStraightTo(Tile destination) {
		path = null;
		isFortified = false;
		if (WorkerJob != null) {
			resetWorkerJob();
		}
		RelocateTo(destination);
		if (owner.isHuman) {
			new MsgUnitMoved(this).send();
		}
	}

	// Takes a worker beaten in battle as a slave worker of the winner's, in
	// the winner's city nearest the battle, keeping its nationality
	// (Rules.DefeatedWorkersBecomeSlaves). Nothing for a winner without
	// cities, such as the barbarians.
	private static void EnslaveInNearestCity(MapUnit worker, Player winner) {
		GameData gameData = EngineStorage.gameData;
		Tile battle = worker.location;
		City city = winner.cities.OrderBy(c => c.location.DistanceTo(battle)).FirstOrDefault();
		UnitPrototype slaveType = CityInteractions.SlaveWorkerType(gameData);
		if (city == null || slaveType == null || winner.IsIndependent) {
			return;
		}
		MapUnit slave = gameData.SpawnUnit(winner, slaveType, city.location);
		slave.nationality = worker.nationality ?? worker.owner.civilization;
		winner.tileKnowledge.RecomputeActiveTiles();
		log.Information("{Winner} takes a beaten {Worker} as a slave worker to {City}", winner, worker, city);
	}

	// The nearest of the owner's cities, other than `except`, that the unit
	// could be in (a ship only in a city by the water): the one it could
	// get to in the fewest turns, then steps, along a path over its own
	// kind of terrain. Failing any it can get to, the nearest in a straight
	// line. Null if there is none.
	public City FindNearestOwnCity(City except = null) {
		PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(this);
		int maxMovementPoints = MaxMovementPoints();
		City nearest = null;
		int nearestTurns = int.MaxValue, nearestSteps = int.MaxValue;
		City nearestInALine = null;
		int nearestDistance = int.MaxValue;
		foreach (City city in owner.cities) {
			if (city == except || city.location == location || (IsWaterUnit() && !city.location.NeighborsWater())) {
				continue;
			}
			int distance = location.DistanceTo(city.location);
			if (distance < nearestDistance) {
				nearestInALine = city;
				nearestDistance = distance;
			}
			TilePath p = algorithm.PathFrom(location, city.location, this);
			int steps = p?.PathLength() ?? -1;
			if (steps <= 0) {
				continue;
			}
			int turns = p.PathCost(owner, location, maxMovementPoints, maxMovementPoints, unitType);
			if (turns < nearestTurns || (turns == nearestTurns && steps < nearestSteps)) {
				nearest = city;
				nearestTurns = turns;
				nearestSteps = steps;
			}
		}
		return nearest ?? nearestInALine;
	}

	// Whether WithdrawToNearestFreeTile would find somewhere to go.
	public bool CanWithdraw() {
		return FindNearestFreeTile() != null;
	}

	// The nearest free tile the unit could get to over its own kind of
	// terrain, so land units stay on their landmass and ships on their body
	// of water. Failing that, a land unit goes home to its nearest city,
	// wherever it is.
	//
	// A ship goes to the nearest free tile it can't sink in, as ships only
	// end their turns in safe water, and only to water it could sink in when
	// there is no safe one.
	private Tile FindNearestFreeTile() {
		if (IsWaterUnit()) {
			Tile safe = FindNearestTile(location, CanCrossWhileWithdrawing, tile => IsFreeTileFor(tile) && !IsUnsafeWater(tile));
			if (safe != null) {
				return safe;
			}
		}
		Tile reachable = FindNearestTile(location, CanCrossWhileWithdrawing, IsFreeTileFor);
		if (reachable != null || !IsLandUnit()) {
			return reachable;
		}
		return FindNearestTile(location, _ => true,
			tile => tile.HasCity() && tile.cityAtTile.owner == owner && IsFreeTileFor(tile));
	}

	// Searches outward from the start, through tiles that can be crossed,
	// for the nearest other tile that will do.
	private static Tile FindNearestTile(Tile start, Func<Tile, bool> canCross, Func<Tile, bool> willDo) {
		HashSet<Tile> seen = new() { start };
		Queue<Tile> frontier = new();
		frontier.Enqueue(start);
		while (frontier.Count > 0) {
			Tile tile = frontier.Dequeue();
			if (tile != start && willDo(tile)) {
				return tile;
			}
			if (tile != start && !canCross(tile)) {
				continue;
			}
			foreach (Tile neighbor in tile.neighbors.Values) {
				if (neighbor != null && neighbor != Tile.NONE && seen.Add(neighbor)) {
					frontier.Enqueue(neighbor);
				}
			}
		}
		return null;
	}

	// Air units fly over anything. Ships may pass through their own cities,
	// as through a canal.
	private bool CanCrossWhileWithdrawing(Tile tile) {
		if (IsAirUnit()) {
			return true;
		}
		if (IsWaterUnit()) {
			return tile.IsWater() || (tile.HasCity() && tile.cityAtTile.owner == owner);
		}
		return tile.IsLand() && !tile.IsImpassable();
	}

	// Whether the unit could be put on the tile without being in someone
	// else's territory or sharing it with someone else's units.
	private bool IsFreeTileFor(Tile tile) {
		Player tileOwner = tile.OwningPlayer();
		if (tileOwner != null && tileOwner != owner) {
			return false;
		}
		if (tile.hasBarbarianCamp || tile.unitsOnTile.Any(u => u.owner != owner)) {
			return false;
		}
		bool ownCity = tile.HasCity() && tile.cityAtTile.owner == owner;
		if (IsAirUnit()) {
			return ownCity;
		}
		if (IsWaterUnit()) {
			return tile.IsWater() || ownCity;
		}
		return tile.IsLand() && !tile.IsImpassable();
	}

	public async Task MoveAlongPath() {
		while (movementPoints.canMove && path?.PathLength() > 0) {
			// Water the path crossed while it was unexplored may turn out to
			// be unsafe once seen. A goto never risks a ship unless told to,
			// so go around it, or stop if there's no way around.
			Tile peeked = path.PeekNext();
			if (peeked != null && peeked != Tile.NONE && PathAvoids(peeked, path.destination)) {
				TilePath detour = Tile.IsTileValid(location) && Tile.IsTileValid(path.destination)
					? PathingAlgorithmChooser.GetAlgorithm(this).PathFrom(location, path.destination, this)
					: null;
				Tile first = detour?.PeekNext() ?? Tile.NONE;
				if (first == Tile.NONE || PathAvoids(first, detour.destination)) {
					log.Information("{Unit} stopped its goto short of unsafe water at {Tile}", this, peeked);
					path = null;
					return;
				}
				path = detour;
				continue;
			}

			Tile next = path.Next();
			// A step off the map has no direction to move in. Paths from
			// elsewhere (over the network, or from a save) are checked with
			// IsFollowablePath first, but a step after a move that failed
			// need not be to a neighbor, so only leaving the map is fatal.
			if (next == null || next == Tile.NONE || location == Tile.NONE) {
				log.Warning("{Unit} can't follow its path from {From} to {To}; dropping the path", this, location, next);
				path = null;
				return;
			}
			TileDirection dir = location.DirectionTo(next);
			await Move(dir, true); //TODO: don't wait on last move animation?
		}
	}

	private static bool IsStepOnMap(Tile from, Tile to) {
		return to != null && to != Tile.NONE && from != null && from != Tile.NONE && from.neighbors.ContainsValue(to);
	}

	// Whether every step of the path, starting from the unit's tile, is to a
	// neighboring tile on the map, so that the unit could follow it.
	public bool IsFollowablePath(TilePath path) {
		if (path?.path == null) {
			return false;
		}
		Tile from = location;
		foreach (Tile tile in path.path) {
			if (!IsStepOnMap(from, tile)) {
				return false;
			}
			from = tile;
		}
		return true;
	}

	public async Task SetUnitPath(TilePath path) {
		StopAutoBombard();
		this.path = path;
		await MoveAlongPath();
	}

	// What an automated unit is doing, as saved: "work" or "explore" (null if
	// not automated). Its plan isn't saved, so after a load it makes a new
	// one of the same kind. Adapted from GBiebs/OpenCiv3.
	public string AutomationKind() {
		if (!isAutomated) {
			return null;
		}
		return currentAI switch {
			WorkerAI => "work",
			ExplorerAI => "explore",
			_ => resumeAutomation,
		};
	}

	public async void PlayAutomatedTurn() {
		try {
			if (currentAI == null) {
				// Loaded from a save (play by email loads one every turn):
				// carry on with the same kind of automation. A save that
				// doesn't say which (an older one, or a Civ3 SAV) means work
				// for a worker and exploring for anything else.
				string resume = resumeAutomation ?? (unitType.isWorker ? "work" : "explore");
				resumeAutomation = null;
				if (resume == "work") {
					Automate();
				} else if (resume == "explore") {
					Explore();
				} else {
					isAutomated = false;
				}
				return;
			}
			UnitAI.Result result = await currentAI.PlayTurn(owner, this);
			if (result == UnitAI.Result.Done) {
				if (currentAI is WorkerAI) {
					Automate();
				} else if (currentAI is ExplorerAI) {
					Explore();
				}
			} else if (result == UnitAI.Result.Error) {
				// The plan failed (its job became unsafe, or it can't get
				// there): a worker looks for another job, to start next turn,
				// and with none (or for any other unit) control returns to
				// the player rather than the unit failing silently each turn.
				WorkerAIData? next = currentAI is WorkerAI ? WorkerAI.MakeAiData(this, owner) : null;
				if (next != null) {
					currentAI = new WorkerAI(next);
				} else {
					isAutomated = false;
					currentAI = null;
				}
			}
		} catch (Exception e) {
			// Nothing awaits this, so make the failure visible.
			EngineStorage.ReportUnhandledException(e, $"{nameof(PlayAutomatedTurn)} of {this}");
		}

		// Do nothing more after a progress result, so that next turn
		// continues the AI action.
	}

	public async Task PerformBusyAction() {
		if (isFortified) {
			return;
		}

		if (IsAutoBombarding()) {
			await PerformAutoBombard();
			return;
		}

		if (path != null && path.PathLength() > 0) {
			await MoveAlongPath();
			return;
		}

		if (isAutomated) {
			// workers contribute their work at the end of the turn, not when assigned
			if (this.unitType.isWorker && WorkerJob != null) {
				return;
			}
			PlayAutomatedTurn();
			return;
		}
	}

	public void PerformEndOfTurnAction() {
		// Busy Worker
		if (WorkerJob != null) {
			WorkerProgressTowardsJob += workerSpeed();
			movementPoints.onConsumeAll();

			// See if this worker finished the job.
			if ((int)SumWorkerProgress(location, WorkerJob) >= GetWorkerJobCost(location, WorkerJob)) {
				FinishWorkerJobAt(location, WorkerJob);
			}
		}
	}

	// Finishes a worker job, noting any change to the terrain, since that
	// changes what units nearby can see.
	private static void FinishWorkerJobAt(Tile tile, Terraform job) {
		TerrainType baseTerrain = tile.baseTerrainType;
		TerrainType overlayTerrain = tile.overlayTerrainType;
		tile.FinishWorkerJob(job);
		if (tile.baseTerrainType != baseTerrain || tile.overlayTerrainType != overlayTerrain) {
			TileChangeJournal.RecordTerrainChange(tile);
		}
	}

	/// <summary>
	/// Moves the unit in the given direction
	/// </summary>
	/// <param name="unit"></param>
	/// <param name="dir">Which direction to move, e.g. northeast, west, etc.</param>
	/// <param name="wait">Whether the method should wait to return until animations complete</param>
	/// <param name="stealthTarget">The unit a stealth attack singles out, if its owner chose one (see MapUnit_StealthAttack.cs)</param>
	/// <returns>True if the unit is alive after the movement, false otherwise</returns>
	/// <exception cref="Exception"></exception>
	public async Task<bool> Move(TileDirection dir, bool wait = false, ID stealthTarget = null) {
		(int dx, int dy) = dir.ToCoordDiff();

		Tile newLoc = EngineStorage.gameData.map.tileAt(dx + location.XCoordinate, dy + location.YCoordinate);

		var canMove = (newLoc != Tile.NONE) && this.CanEnter(newLoc) && unitType.CanMoveBetween(location, newLoc) && (movementPoints.canMove);
		if (!canMove) return false;

		facingDirection = dir;
		Wake();

		// An invisible unit we couldn't see, of a civ we aren't at war with,
		// is in the way: the move stops there (see MapUnit_Invisible.cs).
		if (newLoc.unitsOnTile.Any(u => u.owner != owner && owner.IsAtPeaceWith(u.owner) && !u.IsSeenBy(owner))) {
			this.path = TilePath.NONE;
			return true;
		}

		// Trigger combat if the tile we're moving into has an enemy  Or if this unit can't fight, do nothing.
		MapUnit defender = DefenderAgainstAttackOn(newLoc, stealthTarget);
		bool fought = false;
		bool enemyOnTile = defender != MapUnit.NONE && MayAttack(defender);
		// An attack on a civ at peace, by or on a hidden nationality unit
		// (see MapUnit_HiddenNationality.cs).
		bool hiddenAttack = AttacksWithoutWar(defender);
		bool armedEnemyOnTile = enemyOnTile && HasArmedEnemyDefender(newLoc);
		// Units that can't attack, including empty armies, don't start fights
		// or take captives, and without Blitz a unit only attacks once per
		// turn. Taking captives isn't an attack, so it's allowed after one.
		if (enemyOnTile && (!CanAttack() || (armedEnemyOnTile && !CanAttackAgainThisTurn()))) {
			return true;
		}

		if (hiddenAttack && !armedEnemyOnTile) {
			// Nothing to fight, and nobody at peace is captured.
			return true;
		} else if (enemyOnTile && !armedEnemyOnTile) {
			// Units that can't defend themselves, like workers and settlers,
			// are captured rather than fought. Fighting them would always win,
			// and could be farmed for promotions.
			CaptureDefencelessUnits(newLoc);
		} else if (enemyOnTile) {
			CombatResult combatResult = await Fight(defender);
			hasAttackedThisTurn = true;
			fought = true;
			this.path = TilePath.NONE;
			// If we were killed then of course there's nothing more to do. If the combat couldn't happen for whatever
			// reason, just give up on trying to move.
			if (combatResult == CombatResult.AttackerKilled) {
				return false;
			}
			if (combatResult == CombatResult.Impossible) {
				return true;
			}

			// If the enemy was defeated, check if there is another enemy on the tile that can fight. If so we can't
			// complete the move but still pay one movement point for the combat. Otherwise we capture whatever is
			// left, like workers, and move in below, paying only for the move. That includes taking the city.
			if (combatResult == CombatResult.DefenderKilled || combatResult == CombatResult.DefenderRetreated) {
				// After a hidden nationality attack, the attacker doesn't
				// join the units left there.
				if (HasArmedEnemyDefender(newLoc) || (hiddenAttack && newLoc.unitsOnTile.Any(u => u.owner != owner))) {
					this.movementPoints.onUnitMove(1);
					this.facingDirection = this.facingDirection.Reversed();
					return true;
				}
				CaptureDefencelessUnits(newLoc);

				// Similarly if we retreated, pay one MP for the combat but don't move.
			} else if (combatResult == CombatResult.AttackerRetreated) {
				this.movementPoints.onUnitMove(1);
				this.facingDirection = this.facingDirection.Reversed();
				return true;
			}
		}

		facingDirection = dir;
		float movementCost = TilePath.GetMovementCost(this.owner, location, dir, newLoc, unitType);
		// A won fight costs a full movement point even along a road or
		// railroad, so moving into the tile takes at least that; a unit with
		// one move ends its turn (owner's decision, 2026-10-21).
		if (fought) {
			movementCost = Math.Max(movementCost, 1);
		}
		List<MapUnit> zoneOfControlAttackers = FindZoneOfControlAttackers(location, newLoc);
		List<City> coastalFortresses = FindCoastalFortressesAlong(location, newLoc);

		RelocateTo(newLoc, movedThere: true);
		// Rising Tide's sea sinks a ship that sails out (see RisingTide).
		if (RisingTide.SinksAt(this, newLoc)) {
			RisingTide.Sink(this);
			return false;
		}
		WakeNearbySentries(newLoc);

		if (wait)
			await animateAsync(MapUnit.AnimatedAction.RUN);
		else
			animate(MapUnit.AnimatedAction.RUN);

		movementPoints.onUnitMove(movementCost);

		foreach (MapUnit zocUnit in zoneOfControlAttackers) {
			await zocUnit.ZoneOfControlAttack(this);
		}
		// Coastal fortresses bombard enemy ships sailing past (see
		// MapUnit_CoastalFortress.cs).
		foreach (City fortress in coastalFortresses) {
			await TakeCoastalFortressShot(fortress);
		}

		return true;
	}

	// Moves the unit, and anything it carries, onto a neighboring tile. This
	// doesn't check whether the move is allowed, start combat, or use
	// movement points; callers handle those. `movedThere` is whether this
	// is the unit's own move (see OnEnterTile).
	private void RelocateTo(Tile newLoc, bool movedThere = false) {
		// Leave old tile
		if (!location.unitsOnTile.Remove(this))
			throw new System.Exception("Failed to remove unit from tile it's supposed to be on");
		TileChangeJournal.Record(location);
		TileChangeJournal.Record(newLoc);

		// Move transported units, too
		CarryPassengersTo(newLoc);

		TryBoardingTransportOnTile(newLoc);
		TryUnboardingTransportToTile(newLoc);

		// Enter new tile
		// Make sure the unit is on the new location before claiming we have entered the tile
		newLoc.unitsOnTile.Add(this);
		location = newLoc;
		OnEnterTile(newLoc, movedThere);
	}

	// Moves everything loaded on this unit, and anything loaded on that (like
	// an army aboard a ship), to the new tile along with it.
	private void CarryPassengersTo(Tile newLoc) {
		foreach (MapUnit passenger in Passengers()) {
			passenger.CarryPassengersTo(newLoc);
			if (!location.unitsOnTile.Remove(passenger))
				throw new System.Exception("Failed to remove unit from tile during transport move");
			newLoc.unitsOnTile.Add(passenger);
			passenger.location = newLoc;
		}
	}

	// True if an enemy unit on the tile can actually put up a fight against
	// this unit, as opposed to only workers, settlers, empty armies and the
	// like. Units in an army fight through the army.
	private bool HasArmedEnemyDefender(Tile tile) {
		return tile.unitsOnTile.Any(u => MayAttack(u)
			&& !u.IsInArmy()
			&& u.CanDefendAgainst(this)
			&& u.CombatBaseStrength(CombatRole.Defense) > 0);
	}

	// Captures the enemy workers on the tile, turns enemy settlers into two
	// slave workers, captures other zero-defence units (like catapults) the
	// captor could build, and destroys the rest. Barbarians don't take
	// captives.
	// https://civfanatics.com/civ3/infocenter/
	// https://forums.civfanatics.com/threads/capturing-units-with-zero-defense.631539/
	private void CaptureDefencelessUnits(Tile tile) {
		GameData gameData = EngineStorage.gameData;
		// The units each player lost to capture, for their news.
		Dictionary<Player, List<string>> captured = new();
		foreach (MapUnit enemy in tile.unitsOnTile.Where(u => !owner.IsAtPeaceWith(u.owner)).ToList()) {
			// Taking the last of a civ's units destroys the civ, removing the
			// rest of its units, these too: one already gone isn't taken.
			if (!tile.unitsOnTile.Contains(enemy)) {
				continue;
			}
			enemy.owner.AddWarWeariness(owner, Player.WarWearinessForLostUnitWithoutDefence);
			C7Engine.AI.AiExperiments.onUnitTaken?.Invoke(enemy, owner);
			if (owner.IsIndependent) {
				gameData.RemoveUnit(enemy);
			} else if (enemy.unitType.isSettler || enemy.unitType.isWorker || enemy.unitType.CanBeCapturedBy(owner)) {
				Player loser = enemy.owner;
				if (!captured.TryGetValue(loser, out List<string> names)) {
					names = new();
					captured[loser] = names;
				}
				names.Add(enemy.unitType.name);
				if (enemy.unitType.isSettler) {
					gameData.CaptureSettler(enemy, owner);
				} else {
					gameData.CaptureUnit(enemy, owner);
				}
			} else {
				gameData.RemoveUnit(enemy);
			}
		}
		// Civ3's UNITCAPTURED and UNITSCAPTURED (script.txt), where it
		// happened (see LossNews).
		foreach ((Player loser, List<string> names) in captured) {
			if (loser.IsToldNews) {
				new MsgShowMilitaryAdvisorPopup(loser, LossNews.UnitsCaptured(names), happy: false) { location = tile }.send();
			}
		}
	}

	// Enemy units with a zone of control that are next to both tiles of a
	// move get a free attack on the moving unit. Land units only watch land
	// units, and ships only watch ships. Any unit in a fortress or barricade
	// has one: units in a fortress "have a zone of control and fire at
	// passing enemy units" (Conquests Civilopedia, GCON_Fortresses; a
	// barricade likewise, GCON_Barricades). So do armies: "Armies now have
	// the zone of control and blitz ability" (Civ3 readme, 1.17f). An army
	// has no attack of its own and fights with its members, so whether it
	// can attack is CanAttack's, not its type's.
	//
	// Units in a town with walls have one as well, until the town grows into
	// a city: "All units in a settlement (size 1 to 6) with a city wall
	// automatically have a control zone." (Ozymandias, quoting civ-wiki.de,
	// https://forums.civfanatics.com/threads/zones-of-control.682034/); "This
	// is also true for towns with walls. Notably this effect is lost when the
	// town becomes a city(size 7+)." (justanick,
	// https://forums.civfanatics.com/threads/coastal-forts-and-bombardment.690690/, #5).
	//
	// Air units have none, and a land unit aboard a transport still has its
	// own. The C3X mod's notes on the 1.22 executable list, as changes to
	// the base game, "aerial: Air units can exert zone of control" and
	// "not-from-inside: Land units may not exert zone of control while
	// inside a transport", with "amphibious: Land units may exert zone of
	// control over sea units and vice-versa" and "lethal: Units may be
	// destroyed by zone of control" (neither happens here; see
	// ZoneOfControlAttack)
	// (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
	// An army's members fight through the army, so they have none of their
	// own.
	//
	// Each such tile fires once, with its strongest unit: "The game will
	// always select the strongest available unit to try to do damage."
	// (Flintlock, https://forums.civfanatics.com/threads/zones-of-control.682034/, #3).
	public List<MapUnit> FindZoneOfControlAttackers(Tile from, Tile to) {
		List<MapUnit> result = new();
		foreach (Tile t in from.neighbors.Values) {
			if (t == to || !to.neighbors.ContainsValue(t)) {
				continue;
			}
			bool fortified = t.HasFortress() || IsWalledTown(t);
			MapUnit strongest = null;
			foreach (MapUnit u in t.unitsOnTile) {
				if ((u.unitType.hasZoneOfControl || fortified) && !u.IsInArmy() && !u.IsAirUnit() && u.CanAttack()
					&& u.IsLandUnit() == IsLandUnit() && u.IsWaterUnit() == IsWaterUnit()
					&& u.owner != owner && !owner.IsAtPeaceWith(u.owner)
					&& (strongest == null || u.ZoneOfControlStrength() > strongest.ZoneOfControlStrength())) {
					strongest = u;
				}
			}
			if (strongest != null) {
				result.Add(strongest);
			}
		}
		return result;
	}

	// The strength a unit's zone of control shot hits with: the higher of
	// its attack and bombard ("A = the higher of the ZOC unit's attack or
	// bombard", compendium 01, section 1.8, after Flintlock's formula below).
	public double ZoneOfControlStrength() {
		return Math.Max(CombatBaseStrength(CombatRole.Attack), unitType.bombard);
	}

	// The chance a zone of control shot of strength A hits a unit whose
	// defence is D. Flintlock, reading the 1.22 code
	// (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16537815):
	// "The formula for the chance to do damage by ZoC is: 1 - (D*16 / (D*16
	// + A))", which "also applies to coastal fortresses"; his examples are
	// 55.6% for strength 160 against defence 8 and 5.9% for strength 8. D
	// is the moving unit's own defence: "The defense value of the moving
	// units does not enjoy bonuses from the terrain." (Ozymandias,
	// https://forums.civfanatics.com/threads/zones-of-control.682034/).
	public static double ZoneOfControlHitChance(double attack, double defence) {
		if (attack <= 0) {
			return 0;
		}
		return 1 - (defence * 16 / (defence * 16 + attack));
	}

	// Whether the tile holds a town (size 6 or less in the standard rules)
	// with walls, built or granted by a wonder like the Great Wall.
	private static bool IsWalledTown(Tile t) {
		if (!t.HasCity()) {
			return false;
		}
		City city = t.cityAtTile;
		return city.residents.Count <= EngineStorage.gameData.rules.MaximumLevel1CitySize && city.HasWalls();
	}

	// A single round of combat against a unit moving through our zone of
	// control. It can wound the unit but never kills it.
	public async Task ZoneOfControlAttack(MapUnit target) {
		if (target.CompositeHitPoints() <= 1) {
			return;
		}

		double hitChance = ZoneOfControlHitChance(ZoneOfControlStrength(), target.CombatBaseStrength(CombatRole.Defense));
		var originalDirection = facingDirection;
		facingDirection = GetAttackAnimationDirection(location.DirectionTo(target.location));
		await animateAsync(AnimatedAction.ATTACK1);
		facingDirection = originalDirection;

		if (GameData.rng.NextDouble() < hitChance) {
			// An army's hit comes off its pooled hit points (see
			// MapUnit.Combatant).
			target.AbsorbBombardHit(lethal: false);
			log.Information("{Unit} hit {Target} moving through its zone of control", this, target);
		}
	}

	// Whether the two units are of the same class (land, sea or air), as a
	// defensive bombarder must be with the attacker (see Fight).
	private static bool IsSameUnitClass(MapUnit a, MapUnit b) {
		return a.IsLandUnit() == b.IsLandUnit() && a.IsWaterUnit() == b.IsWaterUnit() && a.IsAirUnit() == b.IsAirUnit();
	}

	public async Task<CombatResult> Fight(MapUnit defender) {
		var attacker = this;

		// Armies fight with one member at a time; for other units the
		// combatant is the unit itself. See Combatant().
		MapUnit attackingMember = attacker.Combatant(CombatRole.Attack);
		MapUnit defendingMember = defender.Combatant(CombatRole.Defense);

		// Set combat animation facing. We'll restore the defender's original facing direction at the end of the battle.
		TileDirection attackerAttackDirection = attacker.location.DirectionTo(defender.location);
		TileDirection defenderDefenseDirection = attackerAttackDirection.Reversed();
		var defenderOriginalDirection = defender.facingDirection;
		attacker.facingDirection = attacker.GetAttackAnimationDirection(attackerAttackDirection);
		defender.facingDirection = defender.GetAttackAnimationDirection(defenderDefenseDirection);

		IEnumerable<StrengthBonus> attackBonuses  = attacker.ListStrengthBonusesVersus(defender, CombatRole.Attack , attackerAttackDirection),
								   defenseBonuses = defender.ListStrengthBonusesVersus(attacker, CombatRole.Defense, attackerAttackDirection);
		// Kings & Nomads' soldiers loath to fight their brethren (see
		// MapUnit.BrethrenFactorAgainst) scale the rest.
		double attackMultiplier  = StrengthBonus.ListToMultiplier(attackBonuses) * attacker.BrethrenFactorAgainst(defender, CombatRole.Attack),
			   defenseMultiplier = StrengthBonus.ListToMultiplier(defenseBonuses) * defender.BrethrenFactorAgainst(attacker, CombatRole.Defense);

		// An army's member also gets its share of the army's strength (see
		// ArmyStrengthBonus).
		double attackerStrength = (attackingMember.unitType.attack  + attacker.ArmyStrengthBonus(CombatRole.Attack )) * attackMultiplier,
			   defenderStrength = (defendingMember.unitType.defense + defender.ArmyStrengthBonus(CombatRole.Defense)) * defenseMultiplier;

		if (log.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
			log.Information("Combat log: {Attacker} ({AttackerStrength}) attacking {Defender} ({DefenderStrength})", attacker, attackerStrength, defender, defenderStrength);
			log.Information("\tAttacker: {AttackerType}, base strength {AttackerBaseStrength}", attackingMember.unitType.name, attackingMember.unitType.BaseStrength(CombatRole.Attack));
			foreach (StrengthBonus bonus in attackBonuses)
				log.Information("\t\t+{BonusPercent}%\t{BonusDescription}", 100.0 * bonus.amount, bonus.description);
			log.Information("\tDefender: {DefenderType}, base strength {DefenderBaseStrength}", defendingMember.unitType.name, defendingMember.unitType.BaseStrength(CombatRole.Defense));
			foreach (StrengthBonus bonus in defenseBonuses)
				log.Information("\t\t+{BonusPercent}%\t{BonusDescription}", 100.0 * bonus.amount, bonus.description);
		}

		CombatResult result = CombatResult.Impossible;

		double attackerOdds = attackerStrength / (attackerStrength + defenderStrength);
		if (Double.IsNaN(attackerOdds))
			return result;

		// The play log notes fights a human is in (see PlayLog.PlayRecorder).
		C7Engine.PlayLog.PlayRecorder.FightEntry recordedFight = C7Engine.PlayLog.PlayRecorder.BeginFight(attacker, defender, attackerOdds);

		// For the statistics: the battle's chance before any dice are rolled.
		double startingAttackerOdds = attackerOdds, defensiveBombardHitChance = 0;
		int attackerStartingHitPoints = attacker.CompositeHitPoints(), defenderStartingHitPoints = defender.CompositeHitPoints();

		// When an army rotates in another member, the odds change with it.
		void UpdateOdds() {
			attackerStrength = (attackingMember.unitType.attack + attacker.ArmyStrengthBonus(CombatRole.Attack)) * attackMultiplier;
			defenderStrength = (defendingMember.unitType.defense + defender.ArmyStrengthBonus(CombatRole.Defense)) * defenseMultiplier;
			attackerOdds = attackerStrength / (attackerStrength + defenderStrength);
			if (Double.IsNaN(attackerOdds))
				attackerOdds = 0.5;
		}

		// Defensive bombard: "When a land tile with a bombard capable unit in
		// it is attacked, that bombard unit will take one free shot at the
		// attacking unit" (Civ3 readme, "Artillery Free Shot").
		//
		// Each attack draws at most one shot, from the strongest eligible
		// bombarder on the defender's tile. The stock routine,
		// Fighter_find_defensive_bombarder, returns a single unit. The C3X
		// mod's re-creation of it (patch_Fighter_find_defensive_bombarder,
		// https://github.com/maxpetul/C3X/blob/master/injected_code.c) gives
		// no shot "if attacker cannot defend" (defence below 1) or when the
		// attacker has one hit point left. Otherwise it takes the unit with
		// the highest bombard strength among those with bombard that aren't
		// cruise missiles, haven't fired one this turn
		// (can_do_defensive_bombard), aren't the defender or in the
		// defender's army, and are of the attacker's class
		// ("attacker_class == candidate_type->Unit_Class"): land against
		// land, and ships against ships, on a sea tile too.
		//
		// So aircraft don't fire it, and ships in port don't fire at land
		// attackers. The C3X mod lists, as changes to the base game, "aerial:
		// Air units may perform defensive bombard" and "docked-vs-land: Docked
		// ships (i.e. in a city) can perform defensive bombard against land
		// units attacking their city", alongside "lethal" (the shot never
		// kills; see below) and "blitz: Units with blitz may perform
		// defensive bombard multiple times per turn" (one a turn here, see
		// defensiveBombardsRemaining)
		// (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
		MapUnit defensiveBombarder = MapUnit.NONE;
		double defensiveBombarderStrength = 0.0;
		if (attacker.CombatBaseStrength(CombatRole.Defense) >= 1) {
			int highestBombard = 0;
			foreach (MapUnit candidate in defender.location.unitsOnTile) {
				if (candidate == defender || (defender.IsArmy() && defender.IsPassenger(candidate))
						|| candidate.owner.IsAtPeaceWith(attacker.owner) || candidate.defensiveBombardsRemaining <= 0
						|| candidate.unitType.isCruiseMissile || !IsSameUnitClass(candidate, attacker)
						|| candidate.unitType.bombard <= highestBombard) {
					continue;
				}
				defensiveBombarder = candidate;
				highestBombard = candidate.unitType.bombard;
			}
			if (defensiveBombarder != MapUnit.NONE) {
				defensiveBombarderStrength = defensiveBombarder.StrengthVersus(attacker, CombatRole.DefensiveBombard, defenderDefenseDirection);
			}
		}
		// In the original game, defensive bombard does not trigger against attackers with 1 HP. See:
		// https://github.com/C7-Game/Prototype/pull/250#discussion_r893051111
		// An army's hit points are pooled (see Combatant), so the shot comes
		// off the pool, here from the member holding the most of it, and
		// never takes the army's last hit point.
		MapUnit bombardedUnit = attacker.IsArmy()
			? attacker.Passengers().OrderByDescending(m => m.hitPointsRemaining).FirstOrDefault() ?? attacker
			: attacker;
		if (defensiveBombarder != MapUnit.NONE && defensiveBombarderStrength > 0 && attacker.CompositeHitPoints() > 1) {
			var dBOriginalDirection = defensiveBombarder.facingDirection;
			TileDirection defensiveBombardDirection = defenderDefenseDirection;
			defensiveBombarder.facingDirection = defensiveBombarder.GetAttackAnimationDirection(defensiveBombardDirection);

			await defensiveBombarder.animateAsync(MapUnit.AnimatedAction.ATTACK1);

			// dADB = defense Against Defensive Bombard
			double dADB = attacker.StrengthVersus(defensiveBombarder, CombatRole.DefensiveBombardDefense, defensiveBombardDirection);
			defensiveBombardHitChance = defensiveBombarderStrength / (defensiveBombarderStrength + dADB);
			if (GameData.rng.NextDouble() < defensiveBombardHitChance)
				bombardedUnit.hitPointsRemaining -= 1;

			defensiveBombarder.defensiveBombardsRemaining -= 1;
			defensiveBombarder.facingDirection = dBOriginalDirection;

			attackingMember = attacker.Combatant(CombatRole.Attack);
			UpdateOdds();
		}

		// An army shares its pooled hit points out among its members as the
		// battle starts (see MapUnit.Combatant).
		attacker.BeginArmyBattle(CombatRole.Attack);
		defender.BeginArmyBattle(CombatRole.Defense);
		attackingMember = attacker.Combatant(CombatRole.Attack);
		defendingMember = defender.Combatant(CombatRole.Defense);
		UpdateOdds();

		// A losing fast unit gets its retreat roll as it drops from 2 hit
		// points to 1, and none against an opponent down to 1: "Retreat
		// possible if attacking unit drops from 2 HP to 1 HP, and is
		// implemented before the attacker attacks with 1HP left." / "Zero
		// chance if defender has 1 HP" (Offa,
		// https://forums.civfanatics.com/threads/retreating-units.104978/).
		// The retreating unit keeps its last hit point. An attacker also
		// needs moves to spare: the roll applies only if it "would have at
		// least 1/3 movement point left after the attack" (boogaboo,
		// https://forums.civfanatics.com/threads/how-do-retreats-work.100878),
		// the attack costing one (see Move).
		//
		// A defender can withdraw too, by the same rule: "They seem to use the
		// same formula for slow-attacker v. fast-defender" (BomberEscort,
		// https://forums.civfanatics.com/threads/comprehensive-retreat-study.64755/,
		// #51), given "(3) When there is no available tile for retreat" (#19;
		// see below). But never out of a city, fortified or not, nor off a
		// water tile. The C3X mod's notes on the 1.22 executable say that
		// under the base game rules "units cannot retreat when they start
		// combat with 1 HP or are defending a city", and "no unit may retreat
		// defensively if it's located on a water tile"
		// (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
		// That is stricter than BomberEscort's "(4) When the fast-mover is
		// fortified in a city" (#19), which an earlier version here followed;
		// the code reading wins. The 1 HP rule is the 2-to-1 drop below.
		bool defenderEligibleToRetreat = !defender.location.HasCity() && !defender.location.IsWater();
		// Barbarians never retreat from an attack: "Retreat (only the
		// attacker, never barbarians)" (Perez's decompile of Civ3 Conquests,
		// doc/perez-findings/Civ3_Findings.md, 3.2 "Combat"; authoritative
		// per the owner's 2026-11-12 ruling).
		bool attackerEligibleToRetreat = attacker.movementPoints.remaining - 1 >= 1f / 3 - 0.0001f
			&& attacker.owner?.isBarbarians != true;

		bool defenderHasRetreat = defenderEligibleToRetreat && defender.location.neighbors.TryGetValue(attackerAttackDirection, out Tile behindDefender)
			&& behindDefender != Tile.NONE && defender.CanEnterPeacefully(behindDefender);
		double attackerWinChance = BattleOdds.AttackerWinChance(startingAttackerOdds, attackerStartingHitPoints, defenderStartingHitPoints,
			defensiveBombardHitChance,
			attackerEligibleToRetreat ? attacker.RetreatChance(defender, isAttacking: true) : 0,
			defenderHasRetreat ? defender.RetreatChance(attacker, isAttacking: false) : 0);

		// Do combat rounds
		while (true) {
			defender.animate(MapUnit.AnimatedAction.ATTACK1);
			await attacker.animateAsync(MapUnit.AnimatedAction.ATTACK1);
			if (GameData.rng.NextDouble() < attackerOdds) {
				bool defenderMayRetreat = defenderEligibleToRetreat && defender.CompositeHitPoints() == 2 && attacker.CompositeHitPoints() > 1;
				if (defender.AbsorbCombatHit(defendingMember)) {
					result = CombatResult.DefenderKilled;
					break;
				}
				defendingMember = defender.Combatant(CombatRole.Defense);
				UpdateOdds();
				if (defenderMayRetreat && defender.RollToRetreat(attacker, false)) {
					// TODO: Defender retreat behavior requires some more work. There's an issue for it here:
					// https://github.com/C7-Game/Prototype/issues/274
					//
					// Retreat straight onto the tile behind the defender. It has
					// to be one we can enter peacefully, so the retreat can't
					// start another battle, and we move the defender directly
					// rather than with Move(), which needs movement points the
					// defender may not have during the attacker's turn.
					if (defender.location.neighbors.TryGetValue(attackerAttackDirection, out Tile retreatDestination)
						&& retreatDestination != Tile.NONE
						&& defender.CanEnterPeacefully(retreatDestination)) {
						defender.facingDirection = attackerAttackDirection;
						defender.RelocateTo(retreatDestination);
						await defender.animateAsync(MapUnit.AnimatedAction.RUN);
						result = CombatResult.DefenderRetreated;
						break;
					}
				}
			} else {
				bool attackerMayRetreat = attackerEligibleToRetreat && attacker.CompositeHitPoints() == 2 && defender.CompositeHitPoints() > 1;
				if (attacker.AbsorbCombatHit(attackingMember)) {
					result = CombatResult.AttackerKilled;
					break;
				}
				attackingMember = attacker.Combatant(CombatRole.Attack);
				UpdateOdds();
				if (attackerMayRetreat && attacker.RollToRetreat(defender, true)) {
					result = CombatResult.AttackerRetreated;
					break;
				}
			}
		}

		RecordBattleStats(attacker, defender, result, attackerWinChance);

		if ((result == CombatResult.AttackerKilled) || (result == CombatResult.DefenderKilled)) {
			var (dead, alive) = (result == CombatResult.AttackerKilled) ? (attacker, defender) : (defender, attacker);
			VictoryPoints.OnUnitDefeated(alive.owner, dead, EngineStorage.gameData.victoryConditions);
			if (alive.owner != dead.owner) {
				alive.owner.unitsKilled++;
				PlayerRelationship.RecordWarLoss(dead.owner, alive.owner, units: 1);
			}

			// In an army, the member that won the last round gets the chance
			// to be promoted, and the army plays the victory animation.
			MapUnit survivingMember = (alive == attacker) ? attackingMember : defendingMember;
			// Only a unit that was already elite can produce a leader, not
			// one this victory promotes to elite.
			bool wasElite = survivingMember.IsElite();
			survivingMember.RollToPromote(dead, alive);
			survivingMember.RollForLeader(dead, wasElite, defending: alive == defender);
			if (alive == attacker) {
				survivingMember.RollToEnslave(defendingMember);
			}
			if (dead.unitType.isWorker && EngineStorage.gameData.rules.DefeatedWorkersBecomeSlaves) {
				EnslaveInNearestCity(dead, alive.owner);
			}

			// Winning a battle with an army is what lets a civ build the
			// Military Academy.
			if (alive.IsArmy())
				alive.owner.hasVictoriousArmy = true;


			// A unique unit beating another civ (not barbarians) starts a golden age.
			if (survivingMember.unitType.startsGoldenAge && !dead.owner.IsIndependent) {
				alive.owner.StartGoldenAge(EngineStorage.gameData, $"Our {survivingMember.unitType.name} has won a great victory.");
			}
			await PauseOnCombatResult(dead, alive);
			// Pause on the last frame, so the dead unit doesn't stand up again
			// before it's removed.
			await dead.animateAsync(MapUnit.AnimatedAction.DEATH, AnimationEnding.Pause);
			dead.RemoveFromPlay();
		}

		attacker.EndArmyBattle();
		defender.EndArmyBattle();

		if (result.DefenderWon())
			defender.facingDirection = defenderOriginalDirection;

		// War weariness for the attack (see Player.WarWearinessForUnitAttacked).
		Player.AddWarWearinessForAttack(attacker, defender, attackerDefeated: result == CombatResult.AttackerKilled);
		C7Engine.AI.AiExperiments.onCombat?.Invoke(attacker, defender, result);
		C7Engine.PlayLog.PlayRecorder.EndFight(recordedFight, result);

		return result;
	}

	// With animations off, a fight the UI's player can see holds a moment
	// once it's decided, both units still on the map with what hit points
	// they have left, before the loser is removed, so the player can see
	// who won (owner's request, 2026-10-23). With animations on, the death
	// animation shows it.
	private static async Task PauseOnCombatResult(MapUnit dead, MapUnit alive) {
		int pause = EngineStorage.combatResultPauseMs;
		bool animating = EngineStorage.animationsEnabled && !EngineStorage.gameData.observerMode;
		if (pause <= 0 || animating || !(dead.IsAnimationVisibleToUIPlayer() || alive.IsAnimationVisibleToUIPlayer())) {
			return;
		}
		new MsgUnitMoved(dead).send();
		new MsgUnitMoved(alive).send();
		await Task.Delay(pause);
	}

	public async Task<City?> BuildCity(string cityName) {
		if (!canBuildCity()) {
			log.Warning("can't build city at {Location}", location);
			return null;
		}

		// Inside the borders of a civ at peace with us, founding a city is
		// an act of war: "If you build a city inside someone else's border,
		// and you are at peace, the act of building a city is considered an
		// act of war" (CommandoBob, CFC "Anybody seen this?", #3; compendium
		// 6.10). A human was asked first, with the Foreign Advisor's
		// DECLARE_WAR question. UNVERIFIED in the game (compendium 6, open
		// question 13). Adapted from GBiebs/OpenCiv3 (9b0a25ae).
		if (location.PeacefulOwnerFor(owner) is Player landOwner) {
			log.Information("{Owner} founds a city in {LandOwner}'s land, which means war", owner, landOwner);
			owner.DeclareWarOn(landOwner, EngineStorage.gameData.turn);
			MsgWarDeclaration.Announce(owner, landOwner);
		}

		await animateAsync(MapUnit.AnimatedAction.BUILD);

		City city = CityInteractions.BuildCity(location, owner, cityName);
		this.RemoveFromPlay();

		return city;
	}

	// entry point for "manual" job assignment
	public void PerformTerraformAction(Terraform terraform) {
		if (!CanPerformTerraformAction(terraform)) {
			log.Warning("can't perform {Terraform} by {Unit}", terraform.Name, this);
			return;
		}
		WorkerJob = terraform;

		if (terraform.Animation is AnimatedAction animation)
			animate(animation, AnimationEnding.Repeat);

		movementPoints.onConsumeAll();

		// See if this worker finished the job.
		var terraformProgress = this.SumWorkerProgress(this.location, this.WorkerJob);
		var turnProgress = this.location.GetCurrentUnaccountedJobProgress(terraform);
		var totalCost = (float)GetWorkerJobCost(this.location, this.WorkerJob);

		// Use >= rather than ==, since faster (e.g. Industrious) workers can
		// overshoot the cost.
		if (terraformProgress + turnProgress >= totalCost) {
			FinishWorkerJobAt(location, WorkerJob);
			// A job like an outpost uses up the worker.
			if (hitPointsRemaining <= 0) {
				return;
			}
		}

		Wake();
		EngineStorage.ObserveTask(PerformBusyAction(), nameof(PerformBusyAction));
	}

	public void BoardTransport(MapUnit t) {
		if (t == null) {
			// TODO: throw new System.Exception("Failed to find a transport to move to");
			log.Warning("Failed to find a transport to board");
			return;
		}
		if (!t.CanCarryUnits() || !t.CanLoad(this)) {
			log.Warning("{Unit} can't board {Transport}", this, t);
			return;
		}
		t.Board(this);
		isFortified = true;
		ResetFacingDirection();
		if (this.owner.isHuman)
			new MsgUnitMoved(this).send();
	}

	public void UnboardTransport(MapUnit t) {
		if (t == null) {
			// TODO: throw new System.Exception("Failed to find the transport to unboard from");
			log.Warning("Failed to find a transport to unboard");
			return;
		}
		t.Unboard(this);
		Wake();
		if (this.owner.isHuman)
			new MsgUnitMoved(this).send();
	}

	// Loads this unit, on the player's order, onto a transport or army on its
	// own tile.
	public void LoadOntoTransportHere() {
		if (IsLoaded() || !CanBoardTransportOnTile(location, explicitLoad: true))
			return;
		BoardTransport(SelectTransportToBoard(location, explicitLoad: true));
	}

	public void TryBoardingTransportOnTile(Tile newLoc) {
		var enteringCity = newLoc.HasCity() && newLoc != location;
		if (enteringCity || !CanBoardTransportOnTile(newLoc))
			return;

		var t = SelectTransportToBoard(newLoc);
		BoardTransport(t);
	}

	public void TryUnboardingTransportToTile(Tile newLoc) {
		if (!CanUnboardTransportToTile(newLoc))
			return;

		var t = FindTransportToUnboard(this.location, this.loadedOnUnitId);
		UnboardTransport(t);
	}

	/// <summary>
	/// Boards unit into this transport, without checking that it may (see
	/// CanLoad). Internal so tests can put together an army anywhere.
	/// </summary>
	/// <param name="mapUnit">The unit to load on a transport</param>
	internal void Board(MapUnit mapUnit) {
		if (!IsArmy()) {
			mapUnit.loadedOnUnitId = this.id;
			// TODO: consume moves?
			return;
		}

		// An army's movement depends on its members, so joining one changes
		// it. The army keeps what it has already spent this turn, and can't
		// go further than a member that has already moved.
		float spent = MaxMovementPoints() - movementPoints.remaining;
		mapUnit.loadedOnUnitId = this.id;
		float remaining = MaxMovementPoints() - spent;
		if (mapUnit.movementPoints.remaining < mapUnit.MaxMovementPoints())
			remaining = Math.Min(remaining, mapUnit.movementPoints.remaining);
		movementPoints.reset(Math.Max(0, remaining));
	}

	/// <summary>
	/// Unloads a unit from this transport
	/// </summary>
	/// <param name="mapUnit">The unit to unload from a transport</param>
	private void Unboard(MapUnit mapUnit) {
		mapUnit.loadedOnUnitId = null;
		// TODO: consume moves?
	}
}
