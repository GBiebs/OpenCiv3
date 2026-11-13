using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using C7Engine.AI;
using Serilog;

namespace C7Engine {
	using System;
	using C7GameData;

	// Messages from the UI to the engine. Each one must be serializable (see
	// C7Engine.Network.NetSerialization), because a LAN client sends its
	// messages to the host's engine rather than its own.
	public abstract class MessageToEngine {
		private static ILogger log = Log.ForContext<MessageToEngine>();

		// The player who sent this message. send() fills in the player at this
		// machine's UI unless it's set already (a hotseat player answering a
		// deal at someone else's screen), and a LAN host overwrites it with
		// the player of the connection it arrived on.
		public ID playerID;

		protected Player Sender => EngineStorage.gameData.GetPlayer(playerID);

		// Settings of this machine's engine, like animations, that are never
		// sent to a LAN host.
		[JsonIgnore]
		public virtual bool IsLocal => false;

		// Whether this is one of the player's orders or decisions, counted as a
		// game action in the statistics (see PlayerStats.actions), rather than
		// the UI's or the engine's own housekeeping.
		[JsonIgnore]
		public virtual bool IsGameAction => !IsLocal;

		public void send() {
			playerID ??= EngineStorage.uiControllerID;
			EngineStorage.SendToEngine(this);
		}

		// Returns false if the sender may not send this message now. Handling
		// that waits (for an animation, say) goes on after this returns, and
		// any exception it throws is reported to ReportUnhandledException.
		public bool process() {
			Task handling = Start();
			if (handling == null) {
				return false;
			}
			EngineStorage.ObserveTask(handling, GetType().Name);
			return true;
		}

		// Starts handling the message, returning the task of handling it, or
		// null if the sender may not send it now.
		internal Task Start() {
			if (!IsLocal && !IsAllowed()) {
				log.Warning("Ignoring {Message} from {Player}, who may not send it now", GetType().Name, playerID);
				return null;
			}
			// The play log notes what humans do (see PlayLog.PlayRecorder).
			PlayLog.PlayRecorder.Entry recorded = PlayLog.PlayRecorder.Active ? PlayLog.PlayRecorder.Begin(this) : null;
			if (IsGameAction && Sender is Player sender) {
				sender.stats.RecordAction(sender, EngineStorage.gameData);
			}
			Task handling = ProcessAllowedAsync();
			PlayLog.PlayRecorder.End(recorded, handling.IsCompleted);
			return handling;
		}

		// Handles the message. Messages whose handling waits, for animations
		// or for other players, override ProcessAllowedAsync instead, so the
		// engine knows when they are done.
		protected virtual void ProcessAllowed() { }

		protected virtual Task ProcessAllowedAsync() {
			ProcessAllowed();
			return Task.CompletedTask;
		}

		// Called on a LAN host for messages from clients, to drop anything
		// only players at the same machine may claim. playsAtSendersMachine
		// says whether a player is one of them.
		public virtual void DistrustRemoteSender(Func<ID, bool> playsAtSendersMachine) { }

		// Players may act on their own turn, until they end it, and answer an
		// AI that is waiting for them to respond to it.
		protected virtual bool IsAllowed() {
			return Sender != null
				&& (TurnHandling.IsPlayersTurn(EngineStorage.gameData, playerID) || AnswersWaitingAI());
		}

		// Whether the message is the sender's answer to an AI that, during
		// its turn, asked them something and waits for the answer: closing
		// the diplomacy screen, answering a demand, or accepting the AI's
		// offer. Nothing else is allowed out of turn.
		protected bool AnswersWaitingAI() {
			return EngineStorage.diplomacyPlayerID != null && playerID == EngineStorage.diplomacyPlayerID
				&& IsDiplomacyAnswer();
		}

		// Whether this kind of message can answer an AI waiting on its sender.
		protected virtual bool IsDiplomacyAnswer() => false;

		// Whether the engine may handle this message while an earlier one is
		// still being handled (see EngineStorage.ProcessNextMessageToEngine):
		// a setting of this machine, an answer the earlier message is waiting
		// for, or a message to turn down anyway.
		internal bool MayRunWhileBusy() {
			return IsLocal || AnswersWaitingAI() || !IsAllowed();
		}

		// The sender's unit with the given ID, or null if they have no such unit.
		protected MapUnit SendersUnit(ID unitID) {
			MapUnit unit = EngineStorage.gameData.GetUnit(unitID);
			return unit != null && unit.owner == Sender ? unit : null;
		}

		protected bool IsSendersCity(City city) {
			return city != null && city.owner == Sender && EngineStorage.gameData.GetCity(city.id) == city;
		}
	}

	public class MsgShutdownEngine : MessageToEngine {
		private ILogger log = Log.ForContext<MsgShutdownEngine>();

		public override bool IsLocal => true;

		protected override void ProcessAllowed() {
			log.Information("Engine received shutdown message.");
		}
	}

	public class MsgSetFortification : MessageToEngine {
		public ID unitID;
		public bool fortifyElseWake;

		public MsgSetFortification(ID unitID, bool fortifyElseWake) {
			this.unitID = unitID;
			this.fortifyElseWake = fortifyElseWake;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);

			// Simply do nothing if we weren't given a valid ID. A LAN host
			// rejects actions on units the sender doesn't own the same way.
			if (unit != null) {
				if (fortifyElseWake)
					unit.Fortify();
				else
					unit.Wake();
			}
		}
	}

	public class MsgUpgradeUnit : MessageToEngine {
		public ID unitID;

		public MsgUpgradeUnit(ID unitID) {
			this.unitID = unitID;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);
			if (unit != null && unit.Upgrade() && unit.owner.isHuman) {
				new MsgUnitMoved(unit).send();
			}
		}
	}

	// Civ3's Upgrade All (Shift-U): every unit of the given unit's type that
	// can upgrade, if the gold covers them all (MapUnit.UpgradeAll).
	public class MsgUpgradeAll : MessageToEngine {
		public ID unitID;

		public MsgUpgradeAll(ID unitID) {
			this.unitID = unitID;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null) {
				return;
			}
			foreach (MapUnit upgraded in MapUnit.UpgradeAll(unit.owner, unit.unitType)) {
				if (upgraded.owner.isHuman) {
					new MsgUnitMoved(upgraded).send();
				}
			}
		}
	}

	// Civ3's Rename Unit (Shift-N; MapUnit.Rename).
	public class MsgRenameUnit : MessageToEngine {
		public ID unitID;
		public string name;

		public MsgRenameUnit(ID unitID, string name) {
			this.unitID = unitID;
			this.name = name;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);
			if (unit != null && unit.Rename(name)) {
				new MsgUnitMoved(unit).send();
			}
		}
	}

	public class MsgSentry : MessageToEngine {
		public ID unitID;
		public bool enemyOnly;

		public MsgSentry(ID unitID, bool enemyOnly) {
			this.unitID = unitID;
			this.enemyOnly = enemyOnly;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);
			if (unit != null && unit.unitType.actions.Contains(UnitAction.Sentry)) {
				unit.Sentry(enemyOnly);
				if (unit.owner.isHuman) {
					new MsgUnitMoved(unit).send();
				}
			}
		}
	}

	public class MsgPillage : MessageToEngine {
		public ID unitID;

		public MsgPillage(ID unitID) {
			this.unitID = unitID;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);
			if (unit != null && unit.Pillage() && unit.owner.isHuman) {
				new MsgUnitMoved(unit).send();
			}
		}
	}

	public class MsgMoveUnit : MessageToEngine {
		public ID unitID;
		public TileDirection dir;
		// The unit a stealth attack singles out, or null (see
		// MapUnit_StealthAttack.cs).
		public ID stealthTarget;

		public MsgMoveUnit(ID unitID, TileDirection dir) {
			this.unitID = unitID;
			this.dir = dir;
		}

		protected override async Task ProcessAllowedAsync() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null) return;

			await unit.Move(dir, true, stealthTarget);
		}
	}

	public class MsgSetUnitPath : MessageToEngine {
		public ID unitID;
		public TilePath path;

		public MsgSetUnitPath(ID unitID, TilePath path) {
			this.unitID = unitID;
			this.path = path;
		}

		protected override async Task ProcessAllowedAsync() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null || path == null) return;
			// The path may come from another machine: every step has to be to
			// a neighboring tile on the map.
			if (!unit.IsFollowablePath(path)) {
				Log.Warning("Ignoring a path for {Unit} that leaves the map or skips tiles", unit);
				return;
			}

			await unit.SetUnitPath(path);
		}
	}

	public class MsgBombard : MessageToEngine {
		public ID unitID;
		public Tile tile;

		public MsgBombard(ID unitID, Tile tile) {
			this.unitID = unitID;
			this.tile = tile;
		}

		protected override async Task ProcessAllowedAsync() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null || tile == null || tile == Tile.NONE) return;

			await unit.Bombard(tile);
		}
	}

	// Sets a unit bombarding the tile automatically, every turn (see
	// MapUnit_AutoBombard.cs).
	public class MsgAutoBombard : MessageToEngine {
		public ID unitID;
		public MapUnit.AutoBombardMode mode;
		public Tile tile;

		public MsgAutoBombard(ID unitID, MapUnit.AutoBombardMode mode, Tile tile) {
			this.unitID = unitID;
			this.mode = mode;
			this.tile = tile;
		}

		protected override async Task ProcessAllowedAsync() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null || tile == null || tile == Tile.NONE) return;

			await unit.StartAutoBombard(mode, tile);
			if (unit.owner.isHuman) {
				new MsgUnitMoved(unit).send();
			}
		}
	}

	// An aircraft's mission (see MapUnit_Air.cs), a paratrooper's airdrop,
	// or an airlift (see MapUnit_Airlift.cs), at the tile. Air superiority needs no tile.
	public class MsgAirMission : MessageToEngine {
		public ID unitID;
		public UnitAction mission;
		public Tile tile;

		public MsgAirMission(ID unitID, UnitAction mission, Tile tile) {
			this.unitID = unitID;
			this.mission = mission;
			this.tile = tile;
		}

		protected override async Task ProcessAllowedAsync() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null) return;
			if (mission == UnitAction.Intercept) {
				unit.FlyAirSuperiority();
				return;
			}
			if (tile == null || tile == Tile.NONE) return;

			switch (mission) {
				case UnitAction.Bombing:
					await unit.AirBomb(tile);
					break;
				case UnitAction.PrecisionBombing:
					await unit.PrecisionBomb(tile);
					break;
				case UnitAction.Recon:
					unit.Recon(tile);
					break;
				case UnitAction.Rebase:
					unit.Rebase(tile);
					break;
				case UnitAction.Airdrop:
					unit.Airdrop(tile);
					break;
				case UnitAction.Airlift:
					unit.Airlift(tile);
					break;
			}
		}
	}

	public class MsgLoadToTransport : MessageToEngine {
		public ID unitID;
		public ID transportUnitId;

		public MsgLoadToTransport(ID unitID, ID transportUnitId = null) {
			this.unitID = unitID;
			this.transportUnitId = transportUnitId;
		}

		protected override async Task ProcessAllowedAsync() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null) return;

			if (this.transportUnitId != null) {
				// Boarding takes a unit that can still move, onto a transport
				// on its own tile or a neighbouring one (per the project
				// owner's knowledge of Civ3). From a neighbouring tile the
				// unit moves onto the transport's tile, which boards it and
				// costs the move like any other step.
				MapUnit transportUnit = EngineStorage.gameData.GetUnit(transportUnitId);
				if (transportUnit == null || transportUnit == unit || unit.IsLoaded() || !unit.movementPoints.canMove)
					return;
				if (transportUnit.location == unit.location) {
					unit.BoardTransport(transportUnit);
				} else if (unit.location.neighbors.Values.Contains(transportUnit.location)) {
					await unit.Move(unit.location.DirectionTo(transportUnit.location), true);
				}
			} else
				unit.LoadOntoTransportHere();
		}
	}

	// Unloads a transport's passengers: all of them, or with `only` just
	// that one, as Civ3's unload popup offers ("Disembark?", with "Unload
	// all." or one unit from the list; script.txt's DISEMBARK).
	public class MsgUnloadTransport : MessageToEngine {
		public ID transportUnitId;
		public ID only;

		public MsgUnloadTransport(ID transportUnitId, ID only = null) {
			this.transportUnitId = transportUnitId;
			this.only = only;
		}

		protected override void ProcessAllowed() {
			MapUnit transportUnit = SendersUnit(transportUnitId);
			// Armies can only be unloaded when the game option allows it.
			if (transportUnit == null || !transportUnit.CanTransport())
				return;
			foreach (MapUnit unit in transportUnit.Passengers()) {
				if (only == null || unit.id == only) {
					unit.UnboardTransport(transportUnit);
				}
			}
			new
				MsgTransportUnloaded(transportUnit).send();
		}
	}

	// Simple orders the player gives a unit.
	public class MsgUnitCommand : MessageToEngine {
		public enum Command {
			SkipTurn,
			Disband,
			Explore,
			Automate,
			// A great leader forms an army, or hurries its city's production.
			FormArmy,
			HurryProduction,
			// A scientific leader starts an Age of Science.
			StartScienceAge,
			// A worker founds a colony where it stands.
			BuildColony,
			// A unit adds its people to the city it stands in.
			JoinCity,
			// A captive is sacrificed in its owner's city (see
			// MapUnit_Sacrifice.cs).
			Sacrifice,
		}

		public ID unitID;
		public Command command;

		public MsgUnitCommand(ID unitID, Command command) {
			this.unitID = unitID;
			this.command = command;
		}

		protected override async Task ProcessAllowedAsync() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null) return;

			switch (command) {
				case Command.SkipTurn:
					unit.SkipTurn();
					break;
				case Command.Disband:
					await unit.Disband();
					break;
				case Command.Explore:
					unit.Explore();
					break;
				case Command.Automate:
					unit.Automate(null);
					break;
				case Command.FormArmy:
					MapUnit army = unit.FormArmy();
					if (army != null && army.owner.isHuman)
						new MsgUnitMoved(army).send();
					break;
				case Command.HurryProduction:
					unit.HurryProductionAsLeader();
					break;
				case Command.StartScienceAge:
					unit.StartScienceAge();
					break;
				case Command.BuildColony:
					unit.BuildColony();
					break;
				case Command.JoinCity:
					City city = unit.location.cityAtTile;
					if (unit.JoinCity()) {
						new MsgCityChanged(city).send();
					}
					break;
				case Command.Sacrifice:
					unit.Sacrifice();
					break;
			}
		}
	}

	// One of Civ3's kinds of worker automation (see WorkerAutomation): the
	// tile is the destination of road and rail to, and otherwise unused.
	public class MsgAutomateWorker : MessageToEngine {
		public ID unitID;
		public WorkerAutomation.Kind kind;
		public Tile tile;

		public MsgAutomateWorker(ID unitID, WorkerAutomation.Kind kind, Tile tile = null) {
			this.unitID = unitID;
			this.kind = kind;
			this.tile = tile;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null || !unit.unitType.isWorker) return;

			WorkerAutomation how = new(kind);
			if (how.NeedsDestination) {
				how.target = tile;
			}
			unit.Automate(how);
		}
	}

	// The player selected a unit, which takes it out of automation, cancels
	// its path and wakes it up.
	public class MsgSelectUnit : MessageToEngine {
		// Not a game action (see IsGameAction).
		public override bool IsGameAction => false;

		public ID unitID;

		public MsgSelectUnit(ID unitID) {
			this.unitID = unitID;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);
			if (unit == null) return;

			if ((unit.path?.PathLength() ?? -1) > 0) {
				unit.path = TilePath.NONE;
			}
			if (unit.WorkerJob != null) {
				return;
			}
			if (unit.isAutomated) {
				unit.isAutomated = false;
				unit.currentAI = null;
			}
			// Selecting a unit stops its automated bombarding, even once it
			// has fired for the turn.
			unit.StopAutoBombard();
			if (unit.movementPoints.canMove) {
				unit.Wake();
			}
		}
	}

	// Switches the player government to anarchy and determines when the player
	// can exit anarchy.
	// Puts the sender's economy on a war footing ("War-Time") or returns it
	// to Normalcy, from the Domestic Advisor (see Mobilization).
	public class MsgSetMobilization : MessageToEngine {
		public bool mobilize;

		public MsgSetMobilization(bool mobilize) {
			this.mobilize = mobilize;
		}

		protected override void ProcessAllowed() {
			Player player = Sender;
			if (player == null) return;
			bool changed = mobilize ? Mobilization.Mobilize(player) : Mobilization.Demobilize(player);
			if (changed) {
				new MsgUpdateUiAfterDomesticChange().send();
			}
		}
	}

	public class StartGovernmentTransitionMsg : MessageToEngine {
		protected override void ProcessAllowed() {
			GameData gD = EngineStorage.gameData;
			Player player = Sender;
			// Already in anarchy; don't restart the clock.
			if (player.government.transitionType) return;
			// Nothing to change to (see Player.CanStartRevolution).
			if (!player.CanStartRevolution(gD)) return;
			Government transitionGovt = gD.governments.Find(x => x.transitionType);
			player.government = transitionGovt;
			player.ApplyGovernmentRateCap();
			player.inAnarchyUntilTurn = gD.turn + player.GetTurnsOfAnarchyForTransition(gD);

			// Update the domestic advisor once we know how long the anarchy is.
			new MsgUpdateUiAfterDomesticChange().send();
		}
	}

	public class SelectGovernmentMsg : MessageToEngine {
		public Government government;

		public SelectGovernmentMsg(Government government) {
			this.government = government;
		}

		protected override void ProcessAllowed() {
			GameData gD = EngineStorage.gameData;
			Player player = Sender;
			if (government == null || !player.GetAvailableGovernments(gD).Contains(government)) {
				return;
			}
			// A new government can only be picked once the anarchy period is over.
			if (!player.government.transitionType || gD.turn < player.inAnarchyUntilTurn) {
				return;
			}
			player.AdoptGovernment(government);
		}
	}

	// A Class that allows the UI to have the game engine run some
	// terraform action.
	public class MsgStartWorkerJob : MessageToEngine {
		public ID unitID;
		public Terraform action;

		public MsgStartWorkerJob(ID unitID, Terraform action) {
			this.unitID = unitID;
			this.action = action;
		}

		protected override void ProcessAllowed() {
			MapUnit unit = SendersUnit(unitID);
			// Starting a job takes the rest of the unit's moves, so a unit
			// that has none left can't. In Civ3 a worker that has moved but
			// has moves left still does a full turn of work, while one that
			// spends its turn moving onto a tile starts work the next turn
			// ("one to move there and three to build the road"):
			// https://forums.civfanatics.com/threads/worker-moves.93408/ and
			// https://civfanatics.com/civ3/strategy/game-mechanics/worker-moves/
			if (action != null && unit != null && unit.movementPoints.canMove)
				unit.PerformTerraformAction(action);
		}
	}

	public class MsgChooseProduction : MessageToEngine {
		public ID cityID;
		public string producibleName;

		public MsgChooseProduction(ID cityID, string producibleName) {
			this.cityID = cityID;
			this.producibleName = producibleName;
		}

		protected override void ProcessAllowed() {
			City city = EngineStorage.gameData.GetCity(cityID);
			if (IsSendersCity(city)) {
				foreach (IProducible producible in city.ListProductionOptions(EngineStorage.gameData)) {
					if (producible.name == producibleName) {
						city.ChangeProduction(producible);
						new MsgCityChanged(city).send();
						break;
					}
				}
			}
		}
	}

	// Adds an item to the end of a city's production queue.
	public class MsgEnqueueProduction : MessageToEngine {
		public ID cityID;
		public string producibleName;

		public MsgEnqueueProduction(ID cityID, string producibleName) {
			this.cityID = cityID;
			this.producibleName = producibleName;
		}

		protected override void ProcessAllowed() {
			City city = EngineStorage.gameData.GetCity(cityID);
			if (IsSendersCity(city)) {
				foreach (IProducible producible in city.ListProductionOptions(EngineStorage.gameData)) {
					if (producible.name == producibleName) {
						city.EnqueueProduction(producible);
						new MsgCityChanged(city).send();
						break;
					}
				}
			}
		}
	}

	public class MsgClearProductionQueue : MessageToEngine {
		public ID cityID;

		public MsgClearProductionQueue(ID cityID) {
			this.cityID = cityID;
		}

		protected override void ProcessAllowed() {
			City city = EngineStorage.gameData.GetCity(cityID);
			if (IsSendersCity(city)) {
				city.ClearProductionQueue();
				new MsgCityChanged(city).send();
			}
		}
	}

	// Shift-Q on the city screen: the city's production queue saved under a
	// name, for the player to load into a city later (see
	// SavedProductionQueue).
	public class MsgSaveProductionQueue : MessageToEngine {
		public ID cityID;
		public string name;

		public MsgSaveProductionQueue(ID cityID, string name) {
			this.cityID = cityID;
			this.name = name;
		}

		protected override void ProcessAllowed() {
			City city = EngineStorage.gameData.GetCity(cityID);
			if (IsSendersCity(city)) {
				SavedProductionQueue.Save(city.owner, name, city.productionQueue);
			}
		}
	}

	// Q on the city screen: one of the player's saved production queues
	// becomes the city's.
	public class MsgLoadProductionQueue : MessageToEngine {
		public ID cityID;
		public string name;

		public MsgLoadProductionQueue(ID cityID, string name) {
			this.cityID = cityID;
			this.name = name;
		}

		protected override void ProcessAllowed() {
			City city = EngineStorage.gameData.GetCity(cityID);
			if (IsSendersCity(city) && SavedProductionQueue.Find(city.owner, name) is SavedProductionQueue saved) {
				saved.LoadInto(EngineStorage.gameData, city);
				new MsgCityChanged(city).send();
			}
		}
	}

	// The player clicked a tile on the city screen to move a citizen.
	public class MsgReassignCitizen : MessageToEngine {
		public City city;
		public Tile tile;

		public MsgReassignCitizen(City city, Tile tile) {
			this.city = city;
			this.tile = tile;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city) || tile == null || tile == Tile.NONE) return;

			CityInteractions.ReassignCitizen(EngineStorage.gameData, city, tile);
			new MsgCityChanged(city).send();
		}
	}

	// The player clicked a specialist on the city screen to change its kind.
	public class MsgCycleSpecialist : MessageToEngine {
		public City city;
		public int residentIndex;

		public MsgCycleSpecialist(City city, int residentIndex) {
			this.city = city;
			this.residentIndex = residentIndex;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city)) return;

			CityInteractions.CycleSpecialist(EngineStorage.gameData, city, residentIndex);
			new MsgCityChanged(city).send();
		}
	}

	// The player set their city governor's options ("Contact Governor"),
	// for this city or, as Civ3 offers, for all their cities or all those
	// on its continent.
	public class MsgSetCityGovernor : MessageToEngine {
		public City city;
		public CityGovernor governor;
		public GovernorScope scope;

		public MsgSetCityGovernor(City city, CityGovernor governor, GovernorScope scope = GovernorScope.ThisCity) {
			this.city = city;
			this.governor = governor;
			this.scope = scope;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city) || governor == null) return;

			foreach (City c in CityInteractions.CitiesInGovernorScope(Sender, city, scope)) {
				CityInteractions.SetGovernor(EngineStorage.gameData, c, governor);
				new MsgCityChanged(c).send();
			}
		}
	}

	// Civ3's "Rename" on a city's right-click menu.
	public class MsgRenameCity : MessageToEngine {
		public City city;
		public string name;

		public MsgRenameCity(City city, string name) {
			this.city = city;
			this.name = name;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city)) return;
			if (city.TryRename(name)) {
				new MsgCityChanged(city).send();
			}
		}
	}

	// Civ3's "Set Rally Point", "Set Continental Rally Point" (the same
	// tile for every city of the player on the city's continent) and "Clear
	// Continental Rally Point" (a null tile, clearing them all).
	public class MsgSetRallyPoint : MessageToEngine {
		public City city;
		public Tile tile;
		public bool continental;

		public MsgSetRallyPoint(City city, Tile tile, bool continental) {
			this.city = city;
			this.tile = tile;
			this.continental = continental;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city)) return;
			foreach (City c in CityInteractions.CitiesInRallyScope(Sender, city, continental)) {
				if (tile == null) {
					c.rallyPoint = null;
				} else if (c.WhyCannotRallyAt(tile) == null) {
					c.rallyPoint = tile;
				}
			}
		}
	}

	public class MsgAbandonCity : MessageToEngine {
		public City city;
		// Whether this razes a city just captured (which gives slave
		// workers, see CityInteractions.RazeCity) rather than abandons one.
		public bool raze;

		public MsgAbandonCity(City city, bool raze = false) {
			this.city = city;
			this.raze = raze;
		}

		protected override void ProcessAllowed() {
			GameData gameData = EngineStorage.gameData;
			if (!IsSendersCity(city) || !CityInteractions.MayAbandon(Sender, city, gameData)) return;

			if (raze && CityInteractions.MayRaze(Sender, city, gameData)) {
				CityInteractions.RazeCity(city);
			} else {
				CityInteractions.DestroyCity(city);
			}
		}
	}

	public class MsgChooseResearch : MessageToEngine {
		public Tech tech;
		public AdvisorState advisorState;
		public SelectionMode selectionMode;

		public enum AdvisorState : byte {
			DontShow,
			Show,
		}
		public enum SelectionMode : byte {
			Single,
			Multi,
		}

		public MsgChooseResearch(Tech tech, AdvisorState advisorState, SelectionMode selectionMode = SelectionMode.Single) {
			this.tech = tech;
			this.advisorState = advisorState;
			this.selectionMode = selectionMode;
		}

		protected override void ProcessAllowed() {
			Player player = Sender;
			if (tech == null) return;

			bool isTechEraBeyondPlayerEra = EraUtils.GetEraIndex(tech.EraCivilopediaName) > EraUtils.GetEraIndex(player.eraCivilopediaName);
			if (player.knownTechs.Contains(tech.id) || isTechEraBeyondPlayerEra)
				return;
			if (player.currentlyResearchedTech == tech.id && player.ResearchQueue.Count == 1) {
				return;
			}

			// Start the tech queueing process
			// and start researching the first tech in the queue
			// or append a new queue to the current one if it's a multiselection process
			if (selectionMode == SelectionMode.Single) {
				player.CalculateFreshTechQueueAndAssignNewCurrent(tech);
			} else {
				player.CalculateTechQueueAndAppendToCurrentQueue(tech);

			}

			// update the UI
			if (advisorState == AdvisorState.Show) {

				new MsgShowScienceAdvisor().send();
			} else if (player.currentlyResearchedTech == null
					&& player.GetAvailableTechsToResearch(EngineStorage.gameData.techs).Count > 0) {
				// The choice was learned at once with a free tech (such as
				// Philosophy's), so ask again.
				new MsgShowScienceSelection(player, player.lastDiscoveredTech).send();
			}
		}
	}

	// Sent by the UI at the start of the player's turn: asks them what to
	// research next if they have nothing to research.
	public class MsgAskWhatToResearch : MessageToEngine {
		// Not a game action (see IsGameAction).
		public override bool IsGameAction => false;

		protected override void ProcessAllowed() {
			Sender.AskWhatToResearch(EngineStorage.gameData);
		}
	}

	// Picks something to research for a player who hasn't chosen, so their
	// science isn't wasted if they dismiss the science selection popup. A free
	// tech the player has coming is left for them to choose.
	public class MsgPickDefaultResearch : MessageToEngine {
		// Not a game action (see IsGameAction).
		public override bool IsGameAction => false;

		protected override void ProcessAllowed() {
			Player player = Sender;
			GameData gameData = EngineStorage.gameData;
			if (player.currentlyResearchedTech == null && player.freeTechsRemaining == 0
					&& player.GetAvailableTechsToResearch(gameData.techs).Count > 0) {
				PlayerAI.MaybePickTechToResearch(player, gameData.techs);
			}
		}
	}

	public class MsgChangeSliders : MessageToEngine {
		public enum DomesticPolicyChoice {
			MoreScience,
			LessScience,
			MoreLuxury,
			LessLuxury,
		}

		public DomesticPolicyChoice policyChoice;

		// If set, the rate the slider the choice is about should end at: the
		// change is repeated until it gets there or can't go further. A slider
		// can jump several steps at once, and the UI's idea of the current
		// rate may be out of date (as on a LAN client).
		public int? targetRate;

		public MsgChangeSliders(DomesticPolicyChoice policyChoice, int? targetRate = null) {
			this.policyChoice = policyChoice;
			this.targetRate = targetRate;
		}

		protected override void ProcessAllowed() {
			Player player = Sender;

			if (targetRate is int target) {
				bool science = policyChoice is DomesticPolicyChoice.MoreScience or DomesticPolicyChoice.LessScience;
				for (int step = 0; step < 10; step++) {
					int rate = science ? player.scienceRate : player.luxuryRate;
					if (rate == target) {
						break;
					}
					DomesticPolicyChoice choice = science
						? (rate < target ? DomesticPolicyChoice.MoreScience : DomesticPolicyChoice.LessScience)
						: (rate < target ? DomesticPolicyChoice.MoreLuxury : DomesticPolicyChoice.LessLuxury);
					Step(player, choice);
					if ((science ? player.scienceRate : player.luxuryRate) == rate) {
						break;
					}
				}
			} else {
				Step(player, policyChoice);
			}

			// Update the ui to reflect our changes.
			new MsgUpdateUiAfterDomesticChange().send();
		}

		private void Step(Player player, DomesticPolicyChoice choice) {
			switch (choice) {
				case DomesticPolicyChoice.MoreScience:
					MoreScience(player);
					break;
				case DomesticPolicyChoice.LessScience:
					LessScience(player);
					break;
				case DomesticPolicyChoice.MoreLuxury:
					MoreLuxury(player);
					break;
				case DomesticPolicyChoice.LessLuxury:
					LessLuxury(player);
					break;
				default:
					// The choice may come from another machine.
					Log.Warning("Ignoring an unknown slider change {Choice}", policyChoice);
					return;
			}
		}

		// Increase our science rate, taking away from tax rate if we can,
		// otherwise decrease the luxury rate.
		private void MoreScience(Player player) {
			if (player.scienceRate == player.maxScienceRate) {
				return;
			}

			player.scienceRate++;
			if (player.taxRate > 0) {
				player.taxRate--;
			} else {
				player.luxuryRate--;
			}
		}

		// Ditto for luxury.
		private static void MoreLuxury(Player player) {
			if (player.luxuryRate == player.maxLuxuryRate) {
				return;
			}

			player.luxuryRate++;
			if (player.taxRate > 0) {
				player.taxRate--;
			} else {
				player.scienceRate--;
			}
		}

		// Decreasing is easier, we decrease the requested slider and bump
		// up the tax rate, or the other slider if tax is at the government's
		// rate cap.
		private static void LessScience(Player player) {
			if (player.scienceRate == player.minScienceRate) {
				return;
			}

			if (player.taxRate < player.maxRate) {
				player.taxRate++;
			} else if (player.luxuryRate < player.maxLuxuryRate) {
				player.luxuryRate++;
			} else {
				return;
			}
			player.scienceRate--;
		}

		private static void LessLuxury(Player player) {
			if (player.luxuryRate == player.minLuxuryRate) {
				return;
			}

			if (player.taxRate < player.maxRate) {
				player.taxRate++;
			} else if (player.scienceRate < player.maxScienceRate) {
				player.scienceRate++;
			} else {
				return;
			}
			player.luxuryRate--;
		}
	}

	// Playing by email: the human whose turn it is is at the computer (and
	// has given their password), so the computer players can have their say
	// with them (PlayByEmail.HearFromAIs) before the turn begins.
	public class MsgHearFromAIs : MessageToEngine {
		protected override Task ProcessAllowedAsync() {
			return PlayByEmail.HearFromAIs(Sender);
		}
	}

	// Hotseat and play by email: the sender's password (empty for none),
	// which they give before their empire is shown on their turns.
	public class MsgSetPassword : MessageToEngine {
		public string password;

		public MsgSetPassword(string password) {
			this.password = password;
		}

		// Only on the sender's own turn, whose handoff asks for it.
		protected override void ProcessAllowed() {
			Sender.SetPassword(password ?? "");
		}
	}

	public class MsgEndTurn : MessageToEngine {
		// Not a game action (see IsGameAction).
		public override bool IsGameAction => false;

		// The turn the sender means to end, if they say. An end that arrives
		// once that turn is over is ignored, rather than ending the next.
		public int? turn;

		// Only the player whose turn it is can end it, once. While the other
		// players take their turns it is nobody's to end.
		protected override bool IsAllowed() {
			return Sender != null && TurnHandling.IsPlayersTurn(EngineStorage.gameData, playerID)
				&& !TurnHandling.TurnInProgress
				&& (turn == null || turn == EngineStorage.gameData.turn);
		}

		protected override async Task ProcessAllowedAsync() {
			Player controller = Sender;

			// An observed player whose turn began before observing, as the
			// first turn of a game watched from the start does, has the AI
			// play it now rather than miss it.
			if (EngineStorage.gameData.observerMode && !controller.isHuman && !controller.IsIndependent
				&& TurnHandling.aiPlayedPausedTurn != controller.id) {
				await PlayerAI.PlayTurn(controller, EngineStorage.gameData);
			}
			TurnHandling.aiPlayedPausedTurn = null;

			TurnHandling.OnEndTurn(controller);

			// Reorder the unit list so that non-busy units will be selected
			// first.
			controller.units.Sort((x, y) => x.IsBusy().CompareTo(y.IsBusy()));

			controller.hasPlayedThisTurn = true;
			GameData gameData = EngineStorage.gameData;

			// A deal left unanswered by the end of the turn is refused.
			// With simultaneous turns, others' deals can wait for them.
			MsgProposeDeal deal = EngineStorage.pendingDeal;
			if (deal != null && (!gameData.simultaneousTurns || deal.Proposer == controller || deal.opponent == controller)) {
				EngineStorage.pendingDeal = null;
				new MsgDealResult(deal.Proposer, deal.opponent, false).send();
			}

			// With simultaneous turns, the round goes on once the last
			// human has finished.
			if (gameData.simultaneousTurns) {
				List<Player> stillToMove = TurnHandling.PlayersToMove(gameData);
				if (stillToMove.Count > 0) {
					EngineStorage.activePlayerID = stillToMove[0].id;
					return;
				}
			}

			// What happens during the other players' turns isn't a reply to
			// this player.
			EngineStorage.processingSenderID = null;
			await TurnHandling.AdvanceTurn();
		}
	}

	public class MsgPerformUnitAction : MessageToEngine {
		public MapUnit unit;
		public MsgPerformUnitAction(MapUnit unit) {
			this.unit = unit;
		}

		protected override async Task ProcessAllowedAsync() {
			if (unit == null || unit.owner != Sender) return;

			await unit.PerformBusyAction();
		}
	}

	public class MsgDoHurryProduction : MessageToEngine {
		public City city;
		public MsgDoHurryProduction(City city) {
			this.city = city;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city)) return;

			city.HurryProduction();
			new MsgCityChanged(city).send();
		}
	}

	// Sells an improvement in the sender's city (see City.SellBuilding).
	public class MsgSellBuilding : MessageToEngine {
		public City city;
		public Building building;
		public MsgSellBuilding(City city, Building building) {
			this.city = city;
			this.building = building;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city) || building == null) return;

			CityInteractions.SellBuilding(city, building);
			new MsgCityChanged(city).send();
		}
	}

	// Drafts a citizen of the sender's city (see City.Draft).
	public class MsgDoDraft : MessageToEngine {
		public City city;
		public MsgDoDraft(City city) {
			this.city = city;
		}

		protected override void ProcessAllowed() {
			if (!IsSendersCity(city)) return;

			MapUnit unit = city.Draft(EngineStorage.gameData);
			new MsgCityChanged(city).send();
			if (unit != null) {
				new MsgUnitMoved(unit).send();
			}
		}
	}

	public class MsgDoStopWorkerAction : MessageToEngine {
		public MapUnit worker;
		public MsgDoStopWorkerAction(MapUnit worker) {
			this.worker = worker;
		}

		protected override void ProcessAllowed() {
			if (worker == null || worker.owner != Sender) return;

			this.worker.resetWorkerJob();
			this.worker.isAutomated = false;
			if (!this.worker.movementPoints.canMove)
				new MsgShowTemporaryPopup("This unit has already moved.", this.worker.location).send();
		}
	}

	public class MsgSetAnimationsEnabled : MessageToEngine {
		// Not a game action (see IsGameAction).
		public override bool IsGameAction => false;

		public bool enabled;

		public MsgSetAnimationsEnabled(bool enabled) {
			this.enabled = enabled;
		}

		public override bool IsLocal => true;

		protected override void ProcessAllowed() {
			EngineStorage.animationsEnabled = enabled;
		}
	}

	public class MsgToggleAnimationsEnabled : MessageToEngine {
		public override bool IsLocal => true;

		protected override void ProcessAllowed() {
			EngineStorage.animationsEnabled = !EngineStorage.animationsEnabled;
		}
	}

	public class MsgBuildCity : MessageToEngine {
		public MapUnit unit;
		public string name;
		// The founder's "Capital Governor is Default for New Cities"
		// preference: the new city's governor starts as the capital's.
		public bool capitalGovernorIsDefault;

		public MsgBuildCity(MapUnit unit, string name) {
			this.unit = unit;
			this.name = name;
		}

		protected override async Task ProcessAllowedAsync() {
			if (unit == null || unit.owner != Sender || string.IsNullOrWhiteSpace(name)) return;

			Player founder = unit.owner;
			City? city = await unit.BuildCity(name);
			if (city != null) {
				City capital = founder.cities.Find(c => c.IsCapital());
				if (capitalGovernorIsDefault && capital != null && capital != city && city.owner == founder) {
					CityInteractions.SetGovernor(EngineStorage.gameData, city, capital.governor);
				}
				new MsgCityCreated(city).send();
			}
		}
	}

	public class MsgDeclareWar : MessageToEngine {
		public Player opponent;

		public MsgDeclareWar(Player opponent) {
			this.opponent = opponent;
		}

		protected override void ProcessAllowed() {
			GameData gameData = EngineStorage.gameData;
			Player us = Sender;
			// War is declared on a civ we've met, are at peace with and
			// aren't allied to. Barbarians are always at war with everyone.
			// Civs in the same alliance a scenario locks "can _never_ attack
			// one another"
			// (https://forums.civfanatics.com/threads/how-do-i-add-alliances-in-editor.188963/).
			// UNVERIFIED (no Civ3 source found): that a civ must have met
			// the other to declare war on it.
			if (opponent == null || opponent == us || gameData.GetPlayer(opponent.id) != opponent
				|| opponent.IsIndependent || opponent.defeated
				|| !PlayerRelationship.TryGetRelationship(us, opponent, out _)
				|| PlayerRelationship.AtWar(us, opponent) || gameData.AreInLockedPeace(us, opponent)) {
				Log.Warning("{Player} can't declare war on {Opponent}", us, opponent);
				return;
			}

			us.DeclareWarOn(opponent, gameData.turn);
			MsgWarDeclaration.Announce(us, opponent);
		}
	}

	// The sender proposes a deal. An AI opponent decides at once; a human
	// opponent is asked, and answers with MsgRespondToDeal.
	public class MsgProposeDeal : MessageToEngine {
		public Player opponent;
		public TradeOffer senderGives;
		public TradeOffer senderWants;

		// A hotseat opponent agrees at the proposer's screen, so the deal
		// needn't wait for them.
		public bool opponentAgreed;

		public MsgProposeDeal(Player opponent, TradeOffer senderGives, TradeOffer senderWants) {
			this.opponent = opponent;
			this.senderGives = senderGives;
			this.senderWants = senderWants;
		}

		// Accepting an offer an AI made during its turn is done by proposing
		// it back to that AI.
		protected override bool IsDiplomacyAnswer() {
			return opponent != null && opponent.id == EngineStorage.diplomacyAIPlayerID;
		}

		protected override void ProcessAllowed() {
			GameData gD = EngineStorage.gameData;
			Player proposer = Sender;
			if (opponent == null || opponent == proposer || senderGives == null || senderWants == null) return;

			string problem = TradeOffer.ProblemWithDeal(gD, proposer, opponent, senderGives, senderWants);
			if (problem != null) {
				Log.Warning("Turning down a deal {Player} proposed to {Opponent}: {Problem}", proposer, opponent, problem);
				new MsgDealResult(proposer, opponent, false).send();
				return;
			}

			// Playing by email, the other human isn't at this computer to agree.
			if (opponent.isHuman && opponentAgreed && !PlayByEmail.IsOn(gD)) {
				bool done = opponent.ExecuteDeal(gD, proposer, senderGives, senderWants);
				new MsgDealResult(proposer, opponent, done).send();
				return;
			}
			if (opponent.isHuman && !EngineStorage.IsPlayerReachable(opponent.id)) {
				// Nobody is at their machine to answer.
				new MsgDealResult(proposer, opponent, false).send();
				return;
			}
			if (opponent.isHuman) {
				// With simultaneous turns, another pair may be in talks
				// already, and only one deal can wait for an answer.
				MsgProposeDeal waiting = EngineStorage.pendingDeal;
				if (gD.simultaneousTurns && waiting != null && waiting.Proposer != proposer) {
					new MsgDealResult(proposer, opponent, false).send();
					return;
				}
				EngineStorage.pendingDeal = this;
				new MsgShowDealProposal(proposer, opponent, senderGives, senderWants) { recipient = opponent }.send();
				return;
			}

			bool accepted = opponent.WouldAcceptDealFrom(gD, proposer, senderGives, senderWants)
				&& opponent.ExecuteDeal(gD, proposer, senderGives, senderWants);
			new MsgDealResult(proposer, opponent, accepted).send();
		}

		internal Player Proposer => Sender;

		// The opponent agreed at the proposer's screen, which only counts if
		// they play there too.
		public override void DistrustRemoteSender(Func<ID, bool> playsAtSendersMachine) {
			if (opponent == null || !playsAtSendersMachine(opponent.id)) {
				opponentAgreed = false;
			}
		}
	}

	// A human answers a deal another human proposed with MsgProposeDeal.
	public class MsgRespondToDeal : MessageToEngine {
		public bool accept;

		public MsgRespondToDeal(bool accept) {
			this.accept = accept;
		}

		// The opponent answers whether or not it is their turn.
		protected override bool IsAllowed() {
			return Sender != null && EngineStorage.pendingDeal?.opponent == Sender;
		}

		protected override void ProcessAllowed() {
			MsgProposeDeal deal = EngineStorage.pendingDeal;
			EngineStorage.pendingDeal = null;

			// The deal is checked again: things may have changed while the
			// sender thought it over.
			bool accepted = accept && Sender.ExecuteDeal(EngineStorage.gameData, deal.Proposer, deal.senderGives, deal.senderWants);
			new MsgDealResult(deal.Proposer, Sender, accepted).send();
		}
	}

	// A human answers an AI's demand to leave its territory: withdraw their
	// units, or refuse and go to war.
	public class MsgRespondToTerritoryDemand : MessageToEngine {
		public bool withdraw;

		public MsgRespondToTerritoryDemand(bool withdraw) {
			this.withdraw = withdraw;
		}

		// Only the human the AI is waiting on may answer, whoever's turn it
		// was last.
		protected override bool IsAllowed() {
			return Sender != null && AnswersWaitingAI();
		}

		protected override bool IsDiplomacyAnswer() => true;

		protected override void ProcessAllowed() {
			EngineStorage.territoryDemandAnswer = withdraw;
		}
	}

	// A human answers an AI's demand for tribute: pay it, or refuse and go
	// to war.
	public class MsgRespondToTributeDemand : MessageToEngine {
		public bool pay;

		public MsgRespondToTributeDemand(bool pay) {
			this.pay = pay;
		}

		// Only the human the AI is waiting on may answer, whoever's turn it
		// was last.
		protected override bool IsAllowed() {
			return Sender != null && AnswersWaitingAI();
		}

		protected override bool IsDiplomacyAnswer() => true;

		protected override void ProcessAllowed() {
			EngineStorage.tributeDemandAnswer = pay;
		}
	}

	// A human at war with an AI asks what it wants for peace ("What do you
	// want for $GIVE0?", Conquests diplomacy.txt #USERASKFORCOUNTER). The AI
	// answers at once with MsgPeaceTerms (see PeaceAI.PeaceTermsFor).
	public class MsgAskForPeaceTerms : MessageToEngine {
		public Player opponent;

		public MsgAskForPeaceTerms(Player opponent) {
			this.opponent = opponent;
		}

		protected override void ProcessAllowed() {
			GameData gD = EngineStorage.gameData;
			if (opponent == null || opponent == Sender || opponent.isHuman || Sender == null) return;
			(TradeOffer humanGives, TradeOffer humanWants)? terms = PeaceAI.PeaceTermsFor(gD, opponent, Sender);
			new MsgPeaceTerms(Sender, opponent, terms?.humanGives, terms?.humanWants).send();
		}
	}

	// A human asks an AI, not at war with them, for a counter-offer to the
	// deal on the table: "What do you want for $GIVE0?" (exchange false: the
	// AI asks for more of the human's side) or "Would you care to offer
	// something in exchange for $GIVE0?" (exchange true: the AI adds to its
	// own side), Conquests diplomacy.txt #USERASKFORCOUNTER and
	// #USERASKFOREXCHANGE. The AI answers at once with MsgPeaceTerms, the
	// answer to any such ask (see CounterOfferAI).
	public class MsgAskForCounterOffer : MessageToEngine {
		public Player opponent;
		public TradeOffer humanGives;
		public TradeOffer humanWants;
		public bool exchange;

		public MsgAskForCounterOffer(Player opponent, TradeOffer humanGives, TradeOffer humanWants, bool exchange) {
			this.opponent = opponent;
			this.humanGives = humanGives;
			this.humanWants = humanWants;
			this.exchange = exchange;
		}

		protected override void ProcessAllowed() {
			GameData gD = EngineStorage.gameData;
			if (opponent == null || opponent == Sender || opponent.isHuman || Sender == null || humanGives == null || humanWants == null) return;
			(TradeOffer humanGives, TradeOffer humanWants)? terms = exchange
				? CounterOfferAI.Exchange(gD, opponent, Sender, humanGives, humanWants)
				: CounterOfferAI.Counter(gD, opponent, Sender, humanGives, humanWants);
			new MsgPeaceTerms(Sender, opponent, terms?.humanGives, terms?.humanWants).send();
		}
	}

	// A human tells an AI to take its units out of the human's territory or
	// prepare for war. The AI answers at once with MsgWithdrawalDemandResult.
	public class MsgDemandWithdrawal : MessageToEngine {
		public Player opponent;

		public MsgDemandWithdrawal(Player opponent) {
			this.opponent = opponent;
		}

		protected override void ProcessAllowed() {
			if (opponent == null || opponent == Sender) return;
			// Only a civ we've met and that will talk to us hears the demand.
			// A Civ3 AI refuses to meet for some turns after a war starts
			// (https://forums.civfanatics.com/threads/when-will-ai-sue-for-peace.85975/).
			// UNVERIFIED (no Civ3 source found): that this refusal, or not
			// having met, also stops a demand to withdraw.
			if (!PlayerRelationship.TryGetRelationship(Sender, opponent, out _)
				|| (!opponent.isHuman && !opponent.WillAcceptCommunicationFrom(Sender, EngineStorage.gameData.turn))) {
				return;
			}

			bool? withdrew = TerritoryDemands.DemandFromHuman(Sender, opponent, EngineStorage.gameData);
			if (withdrew.HasValue) {
				new MsgWithdrawalDemandResult(Sender, opponent, withdrew.Value).send();
			}
		}
	}

	// A human votes in the United Nations election; a null candidate
	// abstains.
	public class MsgCastUnitedNationsVote : MessageToEngine {
		public Player candidate;

		public MsgCastUnitedNationsVote(Player candidate) {
			this.candidate = candidate;
		}

		protected override void ProcessAllowed() {
			UnitedNations.CastHumanVote(EngineStorage.gameData, Sender, candidate);
		}
	}

	// The human who owns the United Nations answers whether to hold the
	// election they were offered (MsgShowUnitedNationsElectionOffer).
	public class MsgHoldUnitedNationsElection : MessageToEngine {
		public bool hold;

		public MsgHoldUnitedNationsElection(bool hold) {
			this.hold = hold;
		}

		// Only the owner, during their turn, while the offer stands.
		protected override bool IsAllowed() {
			return base.IsAllowed() && UnitedNations.FounderShouldBeAsked(EngineStorage.gameData, Sender);
		}

		protected override void ProcessAllowed() {
			UnitedNations.AnswerElectionOffer(EngineStorage.gameData, Sender, hold);
		}
	}

	// A human answers whether to take a city that rose up for them
	// (MsgShowCultureFlipOffer).
	public class MsgAnswerCultureFlip : MessageToEngine {
		public City city;
		public bool accept;

		public MsgAnswerCultureFlip(City city, bool accept) {
			this.city = city;
			this.accept = accept;
		}

		// Only the human the city rose up for, while the offer stands.
		protected override bool IsAllowed() {
			return base.IsAllowed() && CultureFlip.OfferStands(EngineStorage.gameData, city, Sender);
		}

		protected override void ProcessAllowed() {
			CultureFlip.AnswerOffer(EngineStorage.gameData, Sender, city, accept);
		}
	}

	// A human's bids in the Mercenary Market, gold for each unit on offer
	// in order, and the city each goes to if they win it (see Staging).
	public class MsgPlaceMarketBids : MessageToEngine {
		public List<int> bids;
		public List<City> cities;

		public MsgPlaceMarketBids(List<int> bids, List<City> cities = null) {
			this.bids = bids ?? new();
			this.cities = cities ?? new();
		}

		// Only a human the market waits for, while it does.
		protected override bool IsAllowed() {
			return Sender != null && Staging.IsBidding(EngineStorage.gameData, Sender) && !TurnHandling.TurnInProgress;
		}

		protected override async Task ProcessAllowedAsync() {
			GameData gameData = EngineStorage.gameData;
			if (!Staging.AnswerBids(gameData, Sender, bids, cities) || Staging.InProgress(gameData)) {
				return;
			}
			// What happens next isn't a reply to this player.
			EngineStorage.processingSenderID = null;
			await TurnHandling.AdvanceTurn();
		}
	}

	// The sender spends a palace upgrade on building a piece of their palace
	// in one of the palace cultures (see PalaceUpgrades.Build).
	public class MsgBuildPalacePiece : MessageToEngine {
		public int piece;
		public int culture;

		public MsgBuildPalacePiece(int piece, int culture) {
			this.piece = piece;
			this.culture = culture;
		}

		protected override bool IsAllowed() {
			return base.IsAllowed() && Sender.palace.CanBuild(piece, culture);
		}

		protected override void ProcessAllowed() {
			Sender.palace.Build(piece, culture);
		}
	}

	// The sender sends a diplomatic or espionage mission against another
	// civ, or one of its cities.
	public class MsgPerformEspionage : MessageToEngine {
		public EspionageMission mission;
		public Player target;
		public City city;
		// How carefully it is sent, where that matters (see
		// Espionage.RiskMatters).
		public EspionageRisk risk;

		public MsgPerformEspionage(EspionageMission mission, Player target, City city,
				EspionageRisk risk = EspionageRisk.Immediately) {
			this.mission = mission;
			this.target = target;
			this.city = city;
			this.risk = risk;
		}

		protected override void ProcessAllowed() {
			Espionage.MissionResult result = Espionage.Perform(EngineStorage.gameData, Sender, mission, target, city, risk: risk);
			new MsgEspionageResult(Sender, mission, result.performed, result.success, result.message, result.city) { newsEvent = result.newsEvent }.send();
		}
	}

	public class MsgDiplomacyCompleted : MessageToEngine {
		// Not a game action (see IsGameAction).
		public override bool IsGameAction => false;

		// While an AI waits on a human to answer it, only that human can end
		// the talks; the active player may still be the last human to play.
		protected override bool IsAllowed() {
			if (EngineStorage.diplomacyPlayerID != null) {
				return Sender != null && AnswersWaitingAI();
			}
			return base.IsAllowed();
		}

		protected override bool IsDiplomacyAnswer() => true;

		protected override void ProcessAllowed() { }
	}
}
