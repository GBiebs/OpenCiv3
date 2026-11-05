using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace C7Engine {
	using System;
	using System.Collections.Generic;
	using C7GameData;

	public abstract class MessageToEngine {
		public abstract void process();

		// The order's work, done when it's done: an order whose work goes on
		// after process() returns (a move's animation, a turn of AIs)
		// overrides this, and process() starts it. Online play waits for it
		// before the next order, so every game does the same in the same order.
		public virtual Task ProcessAsync() {
			process();
			return Task.CompletedTask;
		}

		// Whether this is a command the player gives (moves, orders, builds,
		// research, sliders), counted as a game action in the statistics, rather
		// than UI housekeeping.
		public virtual bool IsPlayerAction => false;

		// (Online, an order that changes the game goes to the host, and comes
		// back in sequence: EngineStorage.sendOrder.)
		public void send() {
			if (EngineStorage.sendOrder != null && !Online.OrderCodec.IsLocal(this)) {
				EngineStorage.sendOrder(this);
				return;
			}
			EngineStorage.pendingMessages.Enqueue(this);
		}
	}

	public class MsgShutdownEngine : MessageToEngine {
		private ILogger log = Log.ForContext<MsgShutdownEngine>();

		public override void process() {
			log.Information("Engine received shutdown message.");
		}
	}

	public class MsgSetFortification : MessageToEngine {
		public override bool IsPlayerAction => true;

		private ID unitID;
		private bool fortifyElseWake;

		public MsgSetFortification(ID unitID, bool fortifyElseWake) {
			this.unitID = unitID;
			this.fortifyElseWake = fortifyElseWake;
		}

		public override void process() {
			MapUnit unit = EngineStorage.gameData.GetUnit(unitID);

			// Simply do nothing if we weren't given a valid GUID. TODO: Maybe this is an error we need to handle? In an MP game, we should reject
			// invalid actions at the server level but at the client level an invalid action received from the server indicates a desync.
			if (unit != null) {
				if (fortifyElseWake)
					unit.Fortify();
				else
					unit.Wake();
			}
		}
	}

	public class MsgMoveUnit : MessageToEngine {
		public override bool IsPlayerAction => true;

		private ID unitID;
		private TileDirection dir;

		public MsgMoveUnit(ID unitID, TileDirection dir) {
			this.unitID = unitID;
			this.dir = dir;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			MapUnit unit = EngineStorage.gameData.GetUnit(unitID);
			if (unit == null) return;

			await unit.Move(dir, true);
		}
	}

	public class MsgSetUnitPath : MessageToEngine {
		public override bool IsPlayerAction => true;

		private ID unitID;
		private TilePath path;

		public MsgSetUnitPath(ID unitID, TilePath path) {
			this.unitID = unitID;
			this.path = path;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			MapUnit unit = EngineStorage.gameData.GetUnit(unitID);
			if (unit == null) return;

			await unit.SetUnitPath(path);
		}
	}

	public class MsgBombard : MessageToEngine {
		public override bool IsPlayerAction => true;

		private ID unitID;
		private readonly int tileX;
		private readonly int tileY;


		public MsgBombard(ID unitID, Tile tile) {
			this.unitID = unitID;
			this.tileX = tile.XCoordinate;
			this.tileY = tile.YCoordinate;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			MapUnit unit = EngineStorage.gameData.GetUnit(unitID);
			Tile tile = EngineStorage.gameData.map.tileAt(tileX, tileY);
			if (unit == null || tile == null) return;

			await unit.Bombard(tile);
		}
	}

	// The human's air unit flies a mission (see Air).
	public class MsgFlyMission : MessageToEngine {
		public override bool IsPlayerAction => true;
		private readonly ID unitId;
		private readonly Air.Mission mission;
		private readonly int tileX;
		private readonly int tileY;

		public MsgFlyMission(ID unitId, Air.Mission mission, Tile target) {
			this.unitId = unitId;
			this.mission = mission;
			(tileX, tileY) = target == null ? (-1, -1) : (target.XCoordinate, target.YCoordinate);
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			GameData gameData = EngineStorage.gameData;
			if (gameData.GetUnit(unitId) is MapUnit plane) {
				await Air.Fly(gameData, plane, mission, tileX < 0 ? null : gameData.map.tileAt(tileX, tileY));
			}
		}
	}

	// The human's unit airdrops, or airlifts, to a tile (see Air).
	public class MsgAirMove : MessageToEngine {
		public override bool IsPlayerAction => true;
		private readonly ID unitId;
		private readonly bool airlift;
		private readonly int tileX;
		private readonly int tileY;

		public MsgAirMove(ID unitId, bool airlift, Tile target) {
			this.unitId = unitId;
			this.airlift = airlift;
			(tileX, tileY) = (target.XCoordinate, target.YCoordinate);
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			if (gameData.GetUnit(unitId) is MapUnit unit) {
				Tile target = gameData.map.tileAt(tileX, tileY);
				if (airlift) {
					Air.Airlift(gameData, unit, target);
				} else {
					Air.Airdrop(gameData, unit, target);
				}
			}
		}
	}

	public class MsgLoadToTransport : MessageToEngine {
		public override bool IsPlayerAction => true;

		private ID unitID;
		private ID transportUnitId;

		public MsgLoadToTransport(ID unitID, ID transportUnitId = null) {
			this.unitID = unitID;
			this.transportUnitId = transportUnitId;
		}

		public override void process() {
			MapUnit unit = EngineStorage.gameData.GetUnit(unitID);
			if (this.transportUnitId != null) {
				// Only board a transport that can still take the unit, the choice may be out of date
				unit.BoardTransport(unit.GetTransportsToBoard(unit.location).Find(t => t.id == transportUnitId));
			} else if (unit.GetTransportsToBoard(unit.location).FirstOrDefault() is MapUnit best) {
				// The best candidate, also an army on the tile (which moving
				// onto a tile can't board, so TryBoardingTransportOnTile won't).
				unit.BoardTransport(best);
			}
		}
	}

	public class MsgUnloadTransport : MessageToEngine {
		public override bool IsPlayerAction => true;

		private ID transportUnitId;

		public MsgUnloadTransport(ID transportUnitId) {
			this.transportUnitId = transportUnitId;
		}

		public override void process() {
			// TODO: more selective unload, let human player choose
			MapUnit transportUnit = EngineStorage.gameData.GetUnit(transportUnitId);
			foreach (MapUnit unit in transportUnit.location.unitsOnTile) {
				if (unit.loadedOnUnitId == transportUnit.id) {
					unit.UnboardTransport(transportUnit);
				}
			}
			new
				MsgTransportUnloaded(transportUnit).send();
		}
	}

	// A generic class that allows the UI to have the game engine run some
	// action, assumed to be on a unit.
	//
	// Actions that require more than a 1 or 2 line lambda should probably use
	// a custom subclass.
	// The UI's unit orders: hold, explore, automate, disband.
	public class MsgUnitOrder : MessageToEngine {
		public enum Order { Hold, Explore, Automate, Disband }

		public override bool IsPlayerAction => true;

		private readonly ID unitId;
		private readonly Order order;

		public MsgUnitOrder(ID unitId, Order order) {
			this.unitId = unitId;
			this.order = order;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			MapUnit unit = EngineStorage.gameData.GetUnit(unitId);
			if (unit == null) {
				return;
			}
			switch (order) {
				case Order.Hold:
					unit.SkipTurn();
					break;
				case Order.Explore:
					unit.Explore();
					break;
				case Order.Automate:
					unit.Automate();
					break;
				case Order.Disband:
					await unit.Disband();
					break;
			}
		}
	}

	// The city screen: a click on one of the city's tiles (see
	// CityInteractions.WorkTile).
	public class MsgWorkTile : MessageToEngine {
		public override bool IsPlayerAction => true;

		private readonly ID cityId;
		private readonly int tileX;
		private readonly int tileY;

		public MsgWorkTile(ID cityId, Tile tile) {
			this.cityId = cityId;
			tileX = tile.XCoordinate;
			tileY = tile.YCoordinate;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			City city = gameData.cities.Find(c => c.id == cityId);
			Tile tile = gameData.map.tileAt(tileX, tileY);
			if (city != null && tile != null && CityInteractions.WorkTile(gameData, city, tile)) {
				new MsgCityChanged(city.id).send();
			}
		}
	}

	// The city screen: a specialist becomes the next kind of specialist.
	public class MsgCycleSpecialist : MessageToEngine {
		public override bool IsPlayerAction => true;

		private readonly ID cityId;
		private readonly int resident;

		public MsgCycleSpecialist(ID cityId, int resident) {
			this.cityId = cityId;
			this.resident = resident;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			City city = gameData.cities.Find(c => c.id == cityId);
			if (city != null && CityInteractions.CycleSpecialist(gameData, city, resident)) {
				new MsgCityChanged(city.id).send();
			}
		}
	}

	// Switches the player government to anarchy and determines when the player
	// can exit anarchy.
	public class StartGovernmentTransitionMsg : MessageToEngine {
		public override bool IsPlayerAction => true;
		private Player player;

		public StartGovernmentTransitionMsg(Player p) {
			player = p;
		}

		public override void process() {
			player.StartRevolution(EngineStorage.gameData);

			// Update the domestic advisor once we know how long the anarchy is.
			new MsgUpdateUiAfterDomesticChange().send();
		}
	}

	public class SelectGovernmentMsg : MessageToEngine {
		public override bool IsPlayerAction => true;
		private Player player;
		private Government government;

		public SelectGovernmentMsg(Player player, Government government) {
			this.player = player;
			this.government = government;
		}

		public override void process() {
			player.government = government;
		}
	}

	// A Class that allows the UI to have the game engine run some
	// terraform action.
	public class MsgStartWorkerJob : MessageToEngine {
		public override bool IsPlayerAction => true;

		private ID UnitID;
		private Terraform Action;
		public MsgStartWorkerJob(ID unitID, Terraform action) {
			this.UnitID = unitID;
			this.Action = action;
		}

		public override void process() {
			MapUnit unit = EngineStorage.gameData.GetUnit(UnitID);
			unit?.PerformTerraformAction(Action);
		}
	}

	// The player tries a diplomatic or spy mission (see Espionage).
	public class MsgEspionageMission : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID playerId;
		private ID targetId;
		private ID cityId;
		private string mission;
		private Espionage.Risk risk;

		public MsgEspionageMission(ID playerId, ID targetId, ID cityId, string mission, Espionage.Risk risk) {
			this.playerId = playerId;
			this.targetId = targetId;
			this.cityId = cityId;
			this.mission = mission;
			this.risk = risk;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			Player us = gameData.GetPlayer(playerId);
			Player them = gameData.GetPlayer(targetId);
			City city = cityId == null ? null : gameData.cities.Find(c => c.id == cityId);
			if (us == null || them == null) {
				return;
			}
			Espionage.Result result = Espionage.Attempt(gameData, us, them, mission, city, risk, GameData.rng);
			if (result != null) {
				new MsgEspionageResult(us.id, result).send();
			}
		}
	}

	// The player sends home its troops in another civ's land (see
	// BorderWarnings).
	public class MsgSendTroopsHome : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID playerId;
		private ID ownerId;

		public MsgSendTroopsHome(ID playerId, ID ownerId) {
			this.playerId = playerId;
			this.ownerId = ownerId;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			Player player = gameData.GetPlayer(playerId);
			Player owner = gameData.GetPlayer(ownerId);
			if (player != null && owner != null) {
				BorderWarnings.SendHome(gameData, BorderWarnings.Intruders(gameData, owner, player));
			}
		}
	}

	// The target the human's stealth attacker picked (MsgChooseStealthTarget),
	// or null to call the attack off. MapUnit.Move waits for it.
	public class MsgStealthTarget : MessageToEngine {
		public override bool IsPlayerAction => true;
		public readonly ID targetId;

		public MsgStealthTarget(ID targetId) {
			this.targetId = targetId;
		}

		public override void process() { }
	}

	// The human launches their complete spaceship, winning the game.
	public class MsgLaunchSpaceship : MessageToEngine {
		public override bool IsPlayerAction => true;
		private readonly ID playerId;

		public MsgLaunchSpaceship(ID playerId) {
			this.playerId = playerId;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			if (gameData.GetPlayer(playerId) is Player player) {
				Spaceship.Launch(gameData, player);
			}
		}
	}

	// Whether the human, owning the United Nations, calls an election
	// (MsgElectionOffer). UnitedNations waits for it.
	public class MsgCallElection : MessageToEngine {
		public override bool IsPlayerAction => true;
		public readonly bool call;

		public MsgCallElection(bool call) {
			this.call = call;
		}

		public override void process() { }
	}

	// The human's vote in a United Nations election (MsgVoteRequest): a
	// candidate, or null to abstain. UnitedNations waits for it.
	public class MsgVote : MessageToEngine {
		public override bool IsPlayerAction => true;
		public readonly ID candidateId;

		public MsgVote(ID candidateId) {
			this.candidateId = candidateId;
		}

		public override void process() { }
	}

	// The human's answer to an AI's demand for tribute (MsgTributeDemanded).
	public class MsgTributeAnswer : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID demanderId;
		private ID victimId;
		private TradeOffer demand;
		private bool pay;

		public MsgTributeAnswer(ID demanderId, ID victimId, TradeOffer demand, bool pay) {
			this.demanderId = demanderId;
			this.victimId = victimId;
			this.demand = demand;
			this.pay = pay;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			Player demander = gameData.GetPlayer(demanderId);
			Player victim = gameData.GetPlayer(victimId);
			if (demander == null || victim == null) {
				return;
			}
			if (pay) {
				C7Engine.AI.TributeAI.Pay(gameData, demander, victim, demand);
			} else {
				C7Engine.AI.TributeAI.Refuse(gameData, demander, victim);
			}
		}
	}

	// The player builds an embassy with another civ (see Embassies).
	public class MsgEstablishEmbassy : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID playerId;
		private ID otherId;

		public MsgEstablishEmbassy(ID playerId, ID otherId) {
			this.playerId = playerId;
			this.otherId = otherId;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			Player us = gameData.GetPlayer(playerId);
			Player them = gameData.GetPlayer(otherId);
			if (us == null || them == null) {
				return;
			}
			int cost = Embassies.Cost(gameData, us, them);
			if (Embassies.Establish(gameData, us, them)) {
				new MsgEmbassyEstablished(us.id, them, Embassies.Capital(them), cost).send();
			}
		}
	}

	// The player set a city's governor; or every city's, and the default
	// for cities to come.
	public class MsgSetGovernor : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID cityID;
		private CityGovernor governor;
		private bool allCities;

		public MsgSetGovernor(ID cityID, CityGovernor governor, bool allCities) {
			this.cityID = cityID;
			this.governor = governor.Copy();
			this.allCities = allCities;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			City city = gameData.cities.Find(c => c.id == cityID);
			if (city == null) {
				return;
			}
			Player owner = city.owner;
			foreach (City c in allCities ? owner.cities : [city]) {
				c.governor = governor.Copy();
				if (c.governor.manageMoods) {
					c.governor.ManageMoods(c, gameData);
				}
			}
			if (allCities) {
				owner.defaultGovernor = governor.Copy();
			}
		}
	}

	public class MsgChooseProduction : MessageToEngine {
		public override bool IsPlayerAction => true;

		private ID cityID;
		private string producibleName;

		public MsgChooseProduction(ID cityID, string producibleName) {
			this.cityID = cityID;
			this.producibleName = producibleName;
		}

		public override void process() {
			City city = EngineStorage.gameData.cities.Find(c => c.id == cityID);
			if (city != null) {
				foreach (IProducible producible in city.ListProductionOptions(EngineStorage.gameData)) {
					if (producible.name == producibleName) {
						city.SetItemBeingProduced(producible);
						new MsgCityChanged(city.id).send();
						break;
					}
				}
			}
		}
	}

	public class MsgChooseResearch : MessageToEngine {
		public override bool IsPlayerAction => true;

		private Tech tech;
		private AdvisorState advisorState;
		private SelectionMode selectionMode;

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

		public override void process() {
			// Whoever's playing chose it (with several humans, not always the first).
			Player player = EngineStorage.GetUIController();

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
			}
		}
	}

	/// <summary>
	/// Spends a free tech advance on the given tech, then asks what to research next.
	/// </summary>
	public class MsgClaimFreeTech : MessageToEngine {
		public override bool IsPlayerAction => true;

		private Tech tech;

		public MsgClaimFreeTech(Tech tech) {
			this.tech = tech;
		}

		public override void process() {
			Player player = EngineStorage.GetUIController();
			if (!player.ClaimFreeTech(EngineStorage.gameData, tech)) {
				return;
			}
			new MsgPromptResearch().send();
		}
	}

	public class MsgChangeSliders : MessageToEngine {
		public override bool IsPlayerAction => true;

		public enum DomesticPolicyChoice {
			MoreScience,
			LessScience,
			MoreLuxury,
			LessLuxury,
		}

		private ID controllerId;
		private DomesticPolicyChoice policyChoice;

		public MsgChangeSliders(ID controllerId, DomesticPolicyChoice policyChoice) {
			this.controllerId = controllerId;
			this.policyChoice = policyChoice;
		}

		public override void process() {
			Player player = null;
			EngineStorage.ReadGameData(data => {
				player = data.players.First(p => p.id == controllerId);
			});

			switch (policyChoice) {
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
					throw new ArgumentOutOfRangeException();
			}

			// Update the ui to reflect our changes.
			new MsgUpdateUiAfterDomesticChange().send();
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
		// up the tax rate.
		private static void LessScience(Player player) {
			if (player.scienceRate == player.minScienceRate) {
				return;
			}

			player.scienceRate--;
			player.taxRate++;
		}

		private static void LessLuxury(Player player) {
			if (player.luxuryRate == player.minLuxuryRate) {
				return;
			}

			player.luxuryRate--;
			player.taxRate++;
		}
	}

	// Hotseat: the player in control sets (or clears, with an empty one)
	// their password; no one else's.
	public class MsgSetPassword : MessageToEngine {
		private readonly ID playerId;
		private readonly string password;

		public MsgSetPassword(ID playerId, string password) {
			this.playerId = playerId;
			this.password = password;
		}

		public override void process() {
			if (playerId != EngineStorage.uiControllerID) {
				return;
			}
			EngineStorage.gameData.GetPlayer(playerId)?.SetPassword(password);
		}
	}

	// Hotseat and play by email: the human in control is at the computer
	// (has given their password), so the other humans' deals and (by email)
	// the AIs can reach them (TurnHandling.HearFromOthers).
	public class MsgHearFromOthers : MessageToEngine {
		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			if (EngineStorage.gameData.GetPlayer(EngineStorage.uiControllerID) is Player human) {
				await TurnHandling.HearFromOthers(human);
			}
		}
	}

	// The human in control answers the deal on the table with another human,
	// or makes them one (HumanDiplomacy.Answer).
	public class MsgHumanDeal : MessageToEngine {
		public override bool IsPlayerAction => true;

		private readonly ID otherId;
		private readonly HumanDealAnswer answer;
		private readonly TradeOffer weGive;
		private readonly TradeOffer theyGive;

		public MsgHumanDeal(ID otherId, HumanDealAnswer answer, TradeOffer weGive, TradeOffer theyGive) {
			this.otherId = otherId;
			this.answer = answer;
			this.weGive = weGive;
			this.theyGive = theyGive;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			if (gameData.GetPlayer(EngineStorage.uiControllerID) is Player us && gameData.GetPlayer(otherId) is Player them) {
				HumanDiplomacy.Answer(gameData, us, them, answer, weGive ?? new(), theyGive ?? new());
			}
		}
	}

	// The human in control answers another human's envoy (MsgEnvoyArrives).
	public class MsgEnvoyAnswer : MessageToEngine {
		public override bool IsPlayerAction => true;

		private readonly ID fromId;
		private readonly EnvoyAnswer answer;

		public MsgEnvoyAnswer(ID fromId, EnvoyAnswer answer) {
			this.fromId = fromId;
			this.answer = answer;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			if (gameData.GetPlayer(EngineStorage.uiControllerID) is Player us && gameData.GetPlayer(fromId) is Player them) {
				HumanDiplomacy.AnswerEnvoy(gameData, us, them, answer);
			}
		}
	}

	// The administrator's changes (Administration), with their password. A
	// human whose turn it is and who is handed to the AI has it played for
	// them at once.
	public class MsgAdministerGame : MessageToEngine {
		private readonly string adminPassword;
		private readonly List<Administration.Change> changes;
		private readonly string newAdminPassword;

		public MsgAdministerGame(string adminPassword, List<Administration.Change> changes, string newAdminPassword = null) {
			this.adminPassword = adminPassword;
			this.changes = changes;
			this.newAdminPassword = newAdminPassword;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			GameData gameData = EngineStorage.gameData;
			Player playing = gameData.GetPlayer(EngineStorage.uiControllerID);
			bool wasHuman = playing?.isHuman == true;
			if (!Administration.Apply(gameData, adminPassword, changes, newAdminPassword)) {
				return;
			}
			if (wasHuman && !playing.isHuman) {
				await TurnHandling.AdvanceTurn();
			}
		}
	}

	// Play by email: the human has read the news from while they were away.
	public class MsgNewsRead : MessageToEngine {
		public override void process() { }
	}

	public class MsgEndTurn : MessageToEngine {
		private ILogger log = Log.ForContext<MsgEndTurn>();

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			Player controller = EngineStorage.gameData.GetPlayer(EngineStorage.uiControllerID);

			TurnHandling.OnEndTurn(controller);

			// Reorder the unit list so that non-busy units will be selected
			// first.
			controller.units.Sort((x, y) => x.IsBusy().CompareTo(y.IsBusy()));

			controller.hasPlayedThisTurn = true;
			await TurnHandling.AdvanceTurn();
		}
	}

	public class MsgPerformUnitAction : MessageToEngine {
		public override bool IsPlayerAction => true;

		private MapUnit unit;
		public MsgPerformUnitAction(MapUnit unit) {
			this.unit = unit;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			await unit.PerformBusyAction();
		}
	}

	public class MsgDoHurryProduction : MessageToEngine {
		public override bool IsPlayerAction => true;

		private City city;
		public MsgDoHurryProduction(City c) {
			city = c;
		}

		public override void process() {
			city.HurryProduction();
		}
	}

	public class MsgDoStopWorkerAction : MessageToEngine {
		public override bool IsPlayerAction => true;

		private MapUnit worker;
		public MsgDoStopWorkerAction(MapUnit worker) {
			this.worker = worker;
		}

		public override void process() {
			this.worker.resetWorkerJob();
			this.worker.isAutomated = false;
			if (!this.worker.movementPoints.canMove)
				new MsgShowTemporaryPopup("This unit has already moved.", this.worker.location).send();
		}
	}

	public class MsgSetAnimationsEnabled : MessageToEngine {
		private bool enabled;

		public MsgSetAnimationsEnabled(bool enabled) {
			this.enabled = enabled;
		}

		public override void process() {
			EngineStorage.animationsEnabled = enabled;
		}
	}

	public class MsgToggleAnimationsEnabled : MessageToEngine {

		public MsgToggleAnimationsEnabled() {
		}

		public override void process() {
			EngineStorage.animationsEnabled = !EngineStorage.animationsEnabled;
		}
	}

	public class MsgBuildCity : MessageToEngine {
		public override bool IsPlayerAction => true;

		private MapUnit unit;
		private string name;

		public MsgBuildCity(MapUnit unit, string name) {
			this.unit = unit;
			this.name = name;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() {
			City? city = await unit.BuildCity(name);
			if (city != null) {
				new MsgCityCreated(city).send();
			}
		}
	}

	public class MsgPillage : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID unitId;

		public MsgPillage(ID unitId) {
			this.unitId = unitId;
		}

		public override void process() {
			EngineStorage.gameData.GetUnit(unitId)?.Pillage();
		}
	}

	public class MsgSetMobilization : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID playerId;
		private bool mobilize;

		public MsgSetMobilization(ID playerId, bool mobilize) {
			this.playerId = playerId;
			this.mobilize = mobilize;
		}

		public override void process() {
			GameData gameData = EngineStorage.gameData;
			gameData.players.Find(p => p.id == playerId)?.SetMobilized(mobilize, gameData);
		}
	}

	public class MsgBuildArmy : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID unitId;

		public MsgBuildArmy(ID unitId) {
			this.unitId = unitId;
		}

		public override void process() {
			EngineStorage.gameData.GetUnit(unitId)?.BuildArmy();
		}
	}

	public class MsgUnitHurryProduction : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID unitId;

		public MsgUnitHurryProduction(ID unitId) {
			this.unitId = unitId;
		}

		public override void process() {
			EngineStorage.gameData.GetUnit(unitId)?.HurryProduction();
		}
	}

	public class MsgStartAgeOfScience : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID unitId;

		public MsgStartAgeOfScience(ID unitId) {
			this.unitId = unitId;
		}

		public override void process() {
			EngineStorage.gameData.GetUnit(unitId)?.StartAgeOfScience();
		}
	}

	public class MsgJoinCity : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID unitId;

		public MsgJoinCity(ID unitId) {
			this.unitId = unitId;
		}

		public override void process() {
			EngineStorage.gameData.GetUnit(unitId)?.JoinCity();
		}
	}

	public class MsgUpgradeUnit : MessageToEngine {
		public override bool IsPlayerAction => true;
		private ID unitId;

		public MsgUpgradeUnit(ID unitId) {
			this.unitId = unitId;
		}

		public override void process() {
			MapUnit unit = EngineStorage.gameData.GetUnit(unitId);
			if (unit != null && unit.Upgrade()) {
				// Refreshes the unit's buttons and info.
				new MsgUnitMoved(unit).send();
			}
		}
	}

	// The player drafts a citizen of one of their cities.
	public class MsgDraft : MessageToEngine {
		public override bool IsPlayerAction => true;
		private City city;
		private ID playerId;

		public MsgDraft(City city, ID playerId) {
			this.city = city;
			this.playerId = playerId;
		}

		public override void process() {
			if (city.owner.id != playerId) {
				return;
			}
			city.Draft(EngineStorage.gameData);
			// Refresh the city screen, which is open.
			new MsgShowCityScreen(city).send();
		}
	}

	// The player sells a building in one of their cities.
	public class MsgSellBuilding : MessageToEngine {
		public override bool IsPlayerAction => true;
		private City city;
		private Building building;
		private ID playerId;

		public MsgSellBuilding(City city, Building building, ID playerId) {
			this.city = city;
			this.building = building;
			this.playerId = playerId;
		}

		public override void process() {
			if (city.owner.id != playerId) {
				return;
			}
			city.SellBuildingByChoice(building);
			// Refresh the city screen, which is open.
			new MsgShowCityScreen(city).send();
		}
	}

	// The player chose to raze a city they've just captured.
	public class MsgRazeCity : MessageToEngine {
		public override bool IsPlayerAction => true;
		private City city;
		private ID playerId;

		public MsgRazeCity(City city, ID playerId) {
			this.city = city;
			this.playerId = playerId;
		}

		public override void process() {
			Player player = EngineStorage.gameData.GetPlayer(playerId);
			CityInteractions.RazeCity(city, player);
		}
	}

	// The human refuses a city that flipped to them (CultureFlip.Rebuff).
	public class MsgRebuffRebels : MessageToEngine {
		public override bool IsPlayerAction => true;
		private City city;

		public MsgRebuffRebels(City city) {
			this.city = city;
		}

		public override void process() {
			CultureFlip.Rebuff(EngineStorage.gameData, city);
		}
	}

	public class MsgDiplomacyCompleted : MessageToEngine {
		public override void process() { }
	}
	public class MsgUiDisengaged : MessageToEngine {
		public override void process() { }
	}
}
