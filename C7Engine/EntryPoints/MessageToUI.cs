namespace C7Engine {
	using C7GameData;
	using System;
	using System.Collections.Generic;
	using System.Linq;

	public interface IMessageToUI {
		public void send();
	}

	public class MessageToUI : IMessageToUI {
		public void send() {
			EngineStorage.messagesToUI.Enqueue(this);
		}
	}

	public class AnimationMessage : IMessageToUI {
		internal Guid animationId = Guid.NewGuid();

		public void send() {
			EngineStorage.animationMessages.Enqueue(this);
		}

		public void markCompleted() {
			if (EngineStorage.pendingAnimations.TryGetValue(animationId, out var tcs)) {
				EngineStorage.pendingAnimations.Remove(animationId);
				tcs.TrySetResult(true);
			}
		}
	}

	public class MsgStartUnitAnimation : AnimationMessage {
		public ID unitID;
		public MapUnit.AnimatedAction action;
		public AnimationEnding ending;

		public MsgStartUnitAnimation(MapUnit unit, MapUnit.AnimatedAction action, AnimationEnding ending) {
			this.unitID = unit.id;
			this.action = action;
			this.ending = ending;
		}
	}

	public class MsgStartEffectAnimation : AnimationMessage {
		public int tileIndex;
		public AnimatedEffect effect;
		public AnimationEnding ending;

		public MsgStartEffectAnimation(Tile tile, AnimatedEffect effect, AnimationEnding ending) {
			this.tileIndex = EngineStorage.gameData.map.tileCoordsToIndex(tile.XCoordinate, tile.YCoordinate);
			this.effect = effect;
			this.ending = ending;
		}
	}

	public class MsgNewGame : MessageToUI {
		public ID controllerId;

		public MsgNewGame(ID controllerId) {
			this.controllerId = controllerId;
		}
	}
	public class MsgStartTurn : MessageToUI { }
	public class MsgGameMainMenu : MessageToUI { }

	public class MsgDiplomacyPopUp : MessageToUI { }

	public class MsgRefuseContact : MessageToUI {
		public ID opponentId;

		public MsgRefuseContact(ID opponentId) {
			this.opponentId = opponentId;
		}
	}

	public class MsgAlreadyInRevolution : MessageToUI {
		public ID controllerId;

		public MsgAlreadyInRevolution(ID controllerId) {
			this.controllerId = controllerId;
		}
	}

	public class MsgConfirmStartRevolution : MessageToUI {
		public ID controllerId;

		public MsgConfirmStartRevolution(ID controllerId) {
			this.controllerId = controllerId;
		}
	}
	public class MsgDescendIntoAnarchy : MessageToUI {
		public ID controllerId;
		public Government government;

		public MsgDescendIntoAnarchy(ID controllerId, Government government) {
			this.controllerId = controllerId;
			this.government = government;
		}
	}

	public class MsgScienceGuidance : MessageToUI { }
	public class MsgShowScienceAdvisor : MessageToUI { }

	// Asks the player to pick a tech, or to spend a free tech advance.
	public class MsgPromptResearch : MessageToUI { }

	public class MsgUpdateUiAfterDomesticChange : MessageToUI { }

	public class MsgWarDeclarationNotification : MessageToUI {
		public Player aggressor;
		public Player opponent;

		public MsgWarDeclarationNotification(Player aggressor, Player opponent) {
			this.aggressor = aggressor;
			this.opponent = opponent;
		}
	}

	public class MsgNameCity : MessageToUI {
		public ID controllerId;
		public string nextCityName;

		public MsgNameCity(ID controllerId, string nextCityName) {
			this.controllerId = controllerId;
			this.nextCityName = nextCityName;
		}
	}

	// Foreign
	public class MsgWarDeclarationConfirmation : MessageToUI {
		public ID aggressorId;
		public ID opponentId;
		public Action callback;

		public MsgWarDeclarationConfirmation(ID aggressorId, ID opponentId, Action callback) {
			this.aggressorId = aggressorId;
			this.opponentId = opponentId;
			this.callback = callback;
		}
	}

	// Military
	public class MsgDiplomacyWarDeclarationConfirmation(ID aggressorId, ID opponentId, Action callback)
		: MsgWarDeclarationConfirmation(aggressorId, opponentId, callback);

	public class MsgCityDestroyed : MessageToUI {
		public City city;

		public MsgCityDestroyed(City city) {
			this.city = city;
		}
	}

	// A city was captured and its conqueror keeps it, for now: a human
	// conqueror is asked whether to keep or raze it (answering with
	// MsgRazeCity), and its former owner is told.
	public class MsgCityCaptured : MessageToUI {
		public City city;
		public ID conquerorId;
		public ID formerOwnerId;
		public int goldTaken;
		public List<string> wonders;
		public int resisters;

		public MsgCityCaptured(City city, ID conquerorId, ID formerOwnerId, int goldTaken, List<string> wonders, int resisters) {
			this.city = city;
			this.conquerorId = conquerorId;
			this.formerOwnerId = formerOwnerId;
			this.goldTaken = goldTaken;
			this.wonders = wonders;
			this.resisters = resisters;
		}
	}

	// Some of a city's resisters stopped resisting: the garrison quelled them,
	// or their civ made peace.
	// A city flipped to another civ by culture, with the great wonders in it.
	public class MsgCityFlipped : MessageToUI {
		public City city;
		public ID fromId;
		public ID toId;
		public List<string> wonders;

		public MsgCityFlipped(City city, ID fromId, ID toId, List<string> wonders) {
			this.city = city;
			this.fromId = fromId;
			this.toId = toId;
			this.wonders = wonders;
		}
	}

	// A flipped city's new owner refused it, and it went back.
	public class MsgCityRebuffed : MessageToUI {
		public City city;

		public MsgCityRebuffed(City city) {
			this.city = city;
		}
	}

	// A unit of the player's entered a goody hut; the text is Civ3's.
	public class MsgGoodyHut : MessageToUI {
		public ID playerId;
		public GoodyHuts.Outcome outcome;
		public string text;
		public Tile tile;

		public MsgGoodyHut(ID playerId, GoodyHuts.Outcome outcome, string text, Tile tile) {
			this.playerId = playerId;
			this.outcome = outcome;
			this.text = text;
			this.tile = tile;
		}
	}

	// `playerId` built an embassy with `other` in `capital`, for `cost` gold.
	public class MsgEmbassyEstablished : MessageToUI {
		public ID playerId;
		public Player other;
		public City capital;
		public int cost;

		public MsgEmbassyEstablished(ID playerId, Player other, City capital, int cost) {
			this.playerId = playerId;
			this.other = other;
			this.capital = capital;
			this.cost = cost;
		}

		public string Text() {
			return $"We have established an embassy with the {other.civilization.name} in {capital?.name} for {cost} gold.";
		}
	}

	// The human's stealth attacker picks its target in a stack (Civilopedia
	// "Stealth Attack"); the answer comes back as MsgStealthTarget.
	public class MsgChooseStealthTarget : MessageToUI {
		public ID playerId;
		public MapUnit attacker;
		public List<MapUnit> targets;

		public MsgChooseStealthTarget(ID playerId, MapUnit attacker, List<MapUnit> targets) {
			this.playerId = playerId;
			this.attacker = attacker;
			this.targets = targets;
		}
	}

	// A ship of the player's sank at sea, with this many units aboard (see
	// Naval).
	public class MsgShipSank : MessageToUI {
		public ID playerId;
		public Tile location;
		public string ship;
		public int cargo;

		public MsgShipSank(ID playerId, Tile location, string ship, int cargo) {
			this.playerId = playerId;
			this.location = location;
			this.ship = ship;
			this.cargo = cargo;
		}
	}

	// `captor` brought the princess of the `princess` civ to its capital
	// (see Princess).
	public class MsgPrincessDelivered : MessageToUI {
		public Player captor;
		public Civilization princess;
		public City capital;

		public MsgPrincessDelivered(Player captor, Civilization princess, City capital) {
			this.captor = captor;
			this.princess = princess;
			this.capital = capital;
		}
	}

	// A civ added a part to its spaceship (Civ3's SUMMARY_THEIR_SPACESHIP_PART
	// for other civs).
	public class MsgSpaceshipPart : MessageToUI {
		public Player owner;
		public Building part;

		public MsgSpaceshipPart(Player owner, Building part) {
			this.owner = owner;
			this.part = part;
		}
	}

	// The human's spaceship is complete and can be launched (answered with
	// MsgLaunchSpaceship, or later from the victory status screen).
	public class MsgSpaceshipComplete : MessageToUI {
		public ID playerId;

		public MsgSpaceshipComplete(ID playerId) {
			this.playerId = playerId;
		}
	}

	// `captor` took `loser`'s capital and destroyed its spaceship (Civ3's
	// LOSTSPACESHIP, DESTROYEDSPACESHIP).
	public class MsgSpaceshipLost : MessageToUI {
		public Player loser;
		public Player captor;

		public MsgSpaceshipLost(Player loser, Player captor) {
			this.loser = loser;
			this.captor = captor;
		}
	}

	// The human owns the United Nations and may call an election (Civ3's
	// DIPLOVICTORYVOTEOPTION); the answer comes back as MsgCallElection.
	public class MsgElectionOffer : MessageToUI {
		public ID playerId;

		public MsgElectionOffer(ID playerId) {
			this.playerId = playerId;
		}
	}

	// The human votes in a United Nations election (DIPLOVICTORYVOTE); the
	// vote comes back as MsgVote.
	public class MsgVoteRequest : MessageToUI {
		public ID voterId;
		public List<Player> candidates;

		public MsgVoteRequest(ID voterId, List<Player> candidates) {
			this.voterId = voterId;
			this.candidates = candidates;
		}
	}

	// How each civ voted (a null candidate: abstained) and who won, if
	// anyone (DIPLOVICTORYVOTERESULTS, NODIPLOVICTORY).
	public class MsgElectionResults : MessageToUI {
		public List<Player> candidates;
		public List<(Player voter, Player candidate)> votes;
		public Player winner;

		public MsgElectionResults(List<Player> candidates, List<(Player voter, Player candidate)> votes, Player winner) {
			this.candidates = candidates;
			this.votes = votes;
			this.winner = winner;
		}
	}

	// An AI demands tribute of the human (see TributeAI); the answer comes
	// back as MsgTributeAnswer.
	public class MsgTributeDemanded : MessageToUI {
		public Player demander;
		public Player victim;
		public TradeOffer demand;

		public MsgTributeDemanded(Player demander, Player victim, TradeOffer demand) {
			this.demander = demander;
			this.victim = victim;
			this.demand = demand;
		}
	}

	// An AI asks (or orders) the human's troops out of its land near `city`
	// (see BorderWarnings).
	public class MsgBorderWarning : MessageToUI {
		public Player owner;
		public Player intruder;
		public City city;
		public bool order;

		public MsgBorderWarning(Player owner, Player intruder, City city, bool order) {
			this.owner = owner;
			this.intruder = intruder;
			this.city = city;
			this.order = order;
		}
	}

	// The result of one of our spy missions (see Espionage).
	public class MsgEspionageResult : MessageToUI {
		public ID playerId;
		public Espionage.Result result;

		public MsgEspionageResult(ID playerId, Espionage.Result result) {
			this.playerId = playerId;
			this.result = result;
		}
	}

	// We caught another civ's agent on a mission against us.
	public class MsgSpyCaught : MessageToUI {
		public ID victimId;
		public Player spymaster;
		public string mission;

		public MsgSpyCaught(ID victimId, Player spymaster, string mission) {
			this.victimId = victimId;
			this.spymaster = spymaster;
			this.mission = mission;
		}

		public string Text() {
			return $"We have caught a {spymaster.civilization.adjective} spy ({mission})!";
		}
	}

	// Two civs at war made peace.
	public class MsgPeaceTreatySigned : MessageToUI {
		public Player a;
		public Player b;

		public MsgPeaceTreatySigned(Player a, Player b) {
			this.a = a;
			this.b = b;
		}

		public string Text() {
			return $"The {a.civilization.noun} and the {b.civilization.noun} have signed a peace treaty.";
		}
	}

	// A nuclear plant melted down (script.txt MELTDOWN).
	public class MsgMeltdown : MessageToUI {
		public ID playerId;
		public City city;
		public string building;

		public MsgMeltdown(ID playerId, City city, string building) {
			this.playerId = playerId;
			this.city = city;
			this.building = building;
		}

		public string Text() {
			return $"The {building} in {city.name} has malfunctioned! Thousands are dead, and pollution has crippled production!";
		}
	}

	// Disease killed a citizen of `city`, or killed `unit`, on `terrain`
	// (script.txt VERBOSE_DISEASE_TERRAIN, UNITJUNGLEDISEASE).
	public class MsgDisease : MessageToUI {
		public ID playerId;
		public City city;
		public MapUnit unit;
		public Tile tile;
		public string terrain;

		public MsgDisease(ID playerId, City city, MapUnit unit, string terrain) {
			this.playerId = playerId;
			this.city = city;
			this.unit = unit;
			this.tile = city?.location ?? unit?.location;
			this.terrain = terrain;
		}

		public string Text() {
			return city != null
				? $"The citizens of {city.name} are dying of disease. Our wise men think that conditions in the surrounding {terrain} may be unhealthy."
				: $"Our {unit.unitType.name} has died of disease from the {terrain.ToLower()}!";
		}
	}

	// A volcano the player knows smokes or erupts, or its lava struck the
	// player's units or city (script.txt VLC_*).
	public class MsgVolcano : MessageToUI {
		public enum Event { Active, Erupted, KilledUnits, TorchedCity }

		public ID playerId;
		public Tile tile;
		public Event what;
		public string city;

		public MsgVolcano(ID playerId, Tile tile, Event what, string city = null) {
			this.playerId = playerId;
			this.tile = tile;
			this.what = what;
			this.city = city;
		}

		public string Text() {
			return what switch {
				Event.Active => "Volcano is now active!",
				Event.Erupted => "Volcano has erupted!",
				Event.KilledUnits => "Our units were burned to a crisp by a lava flow!",
				_ => $"{city} is torched by the volcanic eruption!",
			};
		}
	}

	// Global warming changed a tile's terrain (script.txt GLOBALWARMING,
	// GLOBALWARMING_FOREST_JUNGLE). Sent once for every player: the UI
	// redraws the tile, and tells the players who know it.
	public class MsgGlobalWarming : MessageToUI {
		public Tile tile;
		public string from;
		public string to;
		public bool vegetationLost;
		public List<ID> knownBy;

		public MsgGlobalWarming(Tile tile, string from, string to, bool vegetationLost, List<ID> knownBy) {
			this.tile = tile;
			this.from = from;
			this.to = to;
			this.vegetationLost = vegetationLost;
			this.knownBy = knownBy;
		}

		public string Text() {
			return vegetationLost ? $"Global Warming has destroyed {from}!" : $"Global Warming has turned {from} into {to}!";
		}
	}

	// The plague struck one of the player's cities, killing a citizen and
	// these units (script.txt SUMMARY_PLAGUE, PLAGUE_KILL).
	public class MsgPlague : MessageToUI {
		public ID playerId;
		public City city;
		public string plague;
		public List<string> unitsKilled;

		public MsgPlague(ID playerId, City city, string plague, List<string> unitsKilled) {
			this.playerId = playerId;
			this.city = city;
			this.plague = plague;
			this.unitsKilled = unitsKilled;
		}

		public string Text() {
			return string.Join(" ", new[] { $"Horrors! {plague} strikes {city.name}!" }
				.Concat(unitsKilled.Select(u => $"{u} has been killed by {plague}!")));
		}
	}

	// Pollution struck one of the player's cities' tiles.
	public class MsgPollution : MessageToUI {
		public ID playerId;
		public City city;

		public MsgPollution(ID playerId, City city) {
			this.playerId = playerId;
			this.city = city;
		}

		// Civ3's text (script.txt POLLUTION).
		public string Text() {
			return $"Pollution strikes {city.name}.";
		}
	}

	// A wonder (the Great Library) gave the player advances.
	public class MsgTechsFromWonder : MessageToUI {
		public ID playerId;
		public string wonder;
		public List<string> techs;

		public MsgTechsFromWonder(ID playerId, string wonder, List<string> techs) {
			this.playerId = playerId;
			this.wonder = wonder;
			this.techs = techs;
		}
	}

	// The player's golden age started or ended.
	public class MsgGoldenAge : MessageToUI {
		public ID playerId;
		public bool started;

		public MsgGoldenAge(ID playerId, bool started) {
			this.playerId = playerId;
			this.started = started;
		}
	}

	// A city of the player's started or ended We Love the King Day.
	public class MsgCelebration : MessageToUI {
		public ID playerId;
		public City city;
		public bool started;

		public MsgCelebration(ID playerId, City city, bool started) {
			this.playerId = playerId;
			this.city = city;
			this.started = started;
		}

		// Civ3's text (script.txt WELOVEKING, WELOVEKINGOVER), with the city.
		public string Text() {
			string title = string.IsNullOrEmpty(city.owner.civilization.leaderTitle) ? "King" : city.owner.civilization.leaderTitle;
			return started
				? $"'We love the {title}' day celebrated in {city.name}."
				: $"'We love the {title}' celebration has ended in {city.name}.";
		}
	}

	public class MsgResistanceQuelled : MessageToUI {
		public ID playerId;
		public City city;
		public int stopped;
		public bool resistanceContinues;

		public MsgResistanceQuelled(ID playerId, City city, int stopped, bool resistanceContinues) {
			this.playerId = playerId;
			this.city = city;
			this.stopped = stopped;
			this.resistanceContinues = resistanceContinues;
		}
	}

	// A city was razed by the civ that captured it, or destroyed as it was
	// captured (a size 1 city with no culture).
	public class MsgCityRazed : MessageToUI {
		public City city;
		public ID razerId;
		public ID formerOwnerId;
		public int goldTaken;
		public List<string> wondersDestroyed;
		public int slavesTaken;
		public bool destroyedOnCapture;

		public MsgCityRazed(City city, ID razerId, ID formerOwnerId, int goldTaken, List<string> wondersDestroyed, int slavesTaken, bool destroyedOnCapture = false) {
			this.city = city;
			this.razerId = razerId;
			this.formerOwnerId = formerOwnerId;
			this.goldTaken = goldTaken;
			this.wondersDestroyed = wondersDestroyed;
			this.slavesTaken = slavesTaken;
			this.destroyedOnCapture = destroyedOnCapture;
		}
	}

	public class MsgCityRansacked : MessageToUI {
		public ID controllerId;
		public City city;
		public string barbTribe;
		public int goldLiberated;

		public MsgCityRansacked(ID controllerId, City city, string barbTribe, int goldLiberated) {
			this.controllerId = controllerId;
			this.city = city;
			this.barbTribe = barbTribe;
			this.goldLiberated = goldLiberated;
		}
	}

	public class MsgCityRiotWarning : MessageToUI {
		public ID controllerId;
		public City city;

		public MsgCityRiotWarning(ID controllerId, City city) {
			this.controllerId = controllerId;
			this.city = city;
		}
	}

	// Asks the UI to offer the player a change of mobilization level.
	public class MsgChangeMobilization : MessageToUI {
		public ID controllerId;

		public MsgChangeMobilization(ID controllerId) {
			this.controllerId = controllerId;
		}
	}

	public class MsgSelectGovernment : MessageToUI {
		public ID controllerId;

		public MsgSelectGovernment(ID controllerId) {
			this.controllerId = controllerId;
		}
	}

	public class MsgSelectTransport : MessageToUI {
		public MapUnit unit;
		public List<MapUnit> transports;

		public MsgSelectTransport(MapUnit unit, List<MapUnit> transports) {
			this.unit = unit;
			this.transports = transports;
		}
	}

	public class MsgCivilizationDestroyed : MessageToUI {
		public ID controllerId;
		public Civilization civilization;

		public MsgCivilizationDestroyed(ID controllerId, Civilization civ) {
			this.controllerId = controllerId;
			this.civilization = civ;
		}
	}

	public class MsgCityCreated : MessageToUI {
		public City city;

		public MsgCityCreated(City city) {
			this.city = city;
		}
	}

	public class MsgDisplayHurryProductionPopup : MessageToUI {
		public City city;
		public City.HurryProductionDetails details;

		public MsgDisplayHurryProductionPopup(City c, City.HurryProductionDetails d) {
			city = c;
			details = d;
		}
	}

	// The player clicked a building in the city screen: offer to sell it.
	public class MsgDisplaySellBuildingPopup : MessageToUI {
		public City city;
		public Building building;

		public MsgDisplaySellBuildingPopup(City city, Building building) {
			this.city = city;
			this.building = building;
		}
	}

	public class MsgDisplayStopWorkerActionPopup : MessageToUI {
		public MapUnit worker;
		public Terraform workerJob;
		public float turnsLeft;

		public MsgDisplayStopWorkerActionPopup(MapUnit worker, Terraform workerJob, float turnsLeft) {
			this.worker = worker;
			this.workerJob = workerJob;
			this.turnsLeft = turnsLeft;
		}
	}

	public class MsgShowCityScreen : MessageToUI {
		public City city;

		public MsgShowCityScreen(City city) {
			this.city = city;
		}
	}

	public class MsgBarbarianCampDispersed : MessageToUI {
		public ID controllerId;
		public int goldEarned;

		public MsgBarbarianCampDispersed(ID controllerId, int goldEarned) {
			this.controllerId = controllerId;
			this.goldEarned = goldEarned;
		}
	}

	// Units on a tile couldn't defend themselves and were captured or
	// destroyed by an enemy moving in. The names are the units' names before
	// capture (a captured Settler becomes two Workers).
	public class MsgUnitsCaptured : MessageToUI {
		public ID capturerId;
		public ID victimId;
		public Tile location;
		public List<string> captured;
		public List<string> destroyed;

		public MsgUnitsCaptured(ID capturerId, ID victimId, Tile location, List<string> captured, List<string> destroyed) {
			this.capturerId = capturerId;
			this.victimId = victimId;
			this.location = location;
			this.captured = captured;
			this.destroyed = destroyed;
		}

		// What to tell the given player, or null if it doesn't concern them.
		public string TextFor(ID playerId) {
			bool weTookThem = playerId == capturerId;
			if (!weTookThem && playerId != victimId) {
				return null;
			}
			List<string> lines = new();
			if (captured.Count > 0) {
				lines.Add(weTookThem
					? (captured.Count == 1 ? $"We captured an enemy {captured[0]}!" : "We captured enemy units!")
					: (captured.Count == 1 ? $"Our {captured[0]} was captured!" : "Our units were captured!"));
			}
			if (destroyed.Count > 0) {
				lines.Add(weTookThem
					? (destroyed.Count == 1 ? $"We destroyed an enemy {destroyed[0]}!" : "We destroyed enemy units!")
					: (destroyed.Count == 1 ? $"Our {destroyed[0]} was destroyed!" : "Our units were destroyed!"));
			}
			return lines.Count == 0 ? null : string.Join(" ", lines);
		}
	}

	// One of the player's elite units produced a great leader at `location`.
	public class MsgLeaderBorn : MessageToUI {
		public ID playerId;
		public Tile location;
		public string leaderName;
		public bool scientific;

		public MsgLeaderBorn(ID playerId, Tile location, string leaderName, bool scientific = false) {
			this.playerId = playerId;
			this.location = location;
			this.leaderName = leaderName;
			this.scientific = scientific;
		}
	}

	// The player's Age of Science started or ended.
	public class MsgAgeOfScience : MessageToUI {
		public ID playerId;
		public bool started;

		public MsgAgeOfScience(ID playerId, bool started) {
			this.playerId = playerId;
			this.started = started;
		}
	}

	// A unit that won a battle enslaved the unit it defeated, which became a
	// `result` (by unit type name) for the winner.
	public class MsgUnitEnslaved : MessageToUI {
		public ID enslaverId;
		public ID victimId;
		public Tile location;
		public string enslaver;
		public string defeated;
		public string result;

		public MsgUnitEnslaved(ID enslaverId, ID victimId, Tile location, string enslaver, string defeated, string result) {
			this.enslaverId = enslaverId;
			this.victimId = victimId;
			this.location = location;
			this.enslaver = enslaver;
			this.defeated = defeated;
			this.result = result;
		}

		// What to tell the given player, or null if it doesn't concern them.
		public string TextFor(ID playerId) {
			if (playerId == enslaverId) {
				return $"Our {enslaver} enslaved the defeated {defeated}! It now serves us as a {result}.";
			}
			return playerId == victimId ? $"Our defeated {defeated} was enslaved by the enemy!" : null;
		}
	}

	// A unit pillaged a tile's improvements (by their keys, like "road").
	public class MsgTilePillaged : MessageToUI {
		public Tile location;
		public ID pillagerId;
		public ID landOwnerId;
		public List<string> destroyed;

		public MsgTilePillaged(Tile location, ID pillagerId, ID landOwnerId, List<string> destroyed) {
			this.location = location;
			this.pillagerId = pillagerId;
			this.landOwnerId = landOwnerId;
			this.destroyed = destroyed;
		}

		// What to tell the given player, or null if it doesn't concern them.
		public string TextFor(ID playerId) {
			string what = string.Join(" and ", destroyed);
			if (playerId == pillagerId) {
				return $"We pillaged the {what}!";
			}
			if (playerId == landOwnerId) {
				return $"Enemy troops pillaged our {what}!";
			}
			return null;
		}
	}

	// A building was sold because the treasury couldn't pay the upkeep.
	public class MsgBuildingSold : MessageToUI {
		public ID playerId;
		public string cityName;
		public string buildingName;
		public int goldReceived;

		public MsgBuildingSold(ID playerId, string cityName, string buildingName, int goldReceived) {
			this.playerId = playerId;
			this.cityName = cityName;
			this.buildingName = buildingName;
			this.goldReceived = goldReceived;
		}
	}

	public class MsgShowTemporaryPopup : MessageToUI {
		public string message;
		public Tile location;

		public MsgShowTemporaryPopup(string message, Tile location) {
			this.message = message;
			this.location = location;
		}
	}

	public class MsgShowTradeOffer : MessageToUI {
		public Player aiPlayer;
		public Player humanPlayer;
		public TradeOffer aiWant;
		public TradeOffer aiGive;

		public MsgShowTradeOffer(Player aiPlayer, Player humanPlayer, TradeOffer aiWant, TradeOffer aiGive) {
			this.aiPlayer = aiPlayer;
			this.humanPlayer = humanPlayer;
			this.aiWant = aiWant;
			this.aiGive = aiGive;
		}
	}

	public class MsgUnitMoved : MessageToUI {
		public MapUnit Unit;
		public MsgUnitMoved(MapUnit unit) {
			this.Unit = unit;
		}
	}

	public class MsgTransportUnloaded : MessageToUI {
		public MapUnit Unit;
		public MsgTransportUnloaded(MapUnit unit) {
			this.Unit = unit;
		}
	}

	public class MsgDisplayAbandonCityPopup : MessageToUI {
		public City city;
		public MsgDisplayAbandonCityPopup(City city) {
			this.city = city;
		}
	}

	public class MsgDisbandUnitConfirmation : MessageToUI {
		public MapUnit mapUnit;
		public MsgDisbandUnitConfirmation(MapUnit mapUnit) {
			this.mapUnit = mapUnit;
		}
	}

	public class MsgReplaceTerrainImprovementConfirmation : MessageToUI {
		public TerrainImprovement terrainImprovement;
		public Terraform terraform;
		public MsgReplaceTerrainImprovementConfirmation(TerrainImprovement terrainImprovement, Terraform terraform) {
			this.terrainImprovement = terrainImprovement;
			this.terraform = terraform;
		}
	}

	public class MsgVictory : MessageToUI {
		public Player winner;
		public IVictory victory;

		public MsgVictory(Player winner, IVictory victory) {
			this.winner = winner;
			this.victory = victory;
		}
	}
}
