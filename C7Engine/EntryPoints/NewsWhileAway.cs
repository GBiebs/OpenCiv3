using C7GameData;

namespace C7Engine {
	// Play by email: the news a human misses while the others play (who it
	// concerns: MessageRecipients) is written down in their part of the save
	// as a line of text, and shown at the start of their next turn
	// (TurnHandling.HearFromAIs). Hotseat keeps the messages themselves
	// instead, since the human is at the same computer.
	public static class NewsWhileAway {
		// Called for every message to the UI.
		public static void Record(MessageToUI msg) {
			GameData gameData = EngineStorage.gameData;
			if (gameData?.playByEmail != true) {
				return;
			}
			foreach (ID id in MessageRecipients.HeldFor(msg, gameData, EngineStorage.uiControllerID)) {
				Player player = gameData.GetPlayer(id);
				if (player != null && Describe(msg, player) is string line) {
					player.newsWhileAway.Add(line);
				}
			}
		}

		// The news `msg` is for `viewer`, or null if it's nothing to them.
		public static string Describe(MessageToUI msg, Player viewer) {
			GameData gameData = EngineStorage.gameData;
			string Noun(ID id) => gameData.GetPlayer(id)?.civilization.noun;
			string Adjective(ID id) => gameData.GetPlayer(id)?.civilization.adjective;
			bool Knows(Player other) => other == viewer || viewer.playerRelationships.ContainsKey(other.id);

			switch (msg) {
				case MsgCityDestroyed m:
					return m.city.owner == viewer ? $"{m.city.name} has been destroyed." : null;
				case MsgCityRazed m:
					if (m.razerId == viewer.id) {
						return $"We burned {m.city.name} to the ground." + (m.slavesTaken > 0 ? $" We took {m.slavesTaken} of its people as slaves." : "");
					}
					return m.formerOwnerId == viewer.id
						? $"The {Adjective(m.razerId)} burned {m.city.name} to the ground" + (m.goldTaken > 0 ? $" and stole {m.goldTaken} gold!" : "!")
						: null;
				case MsgCityFlipped m:
					if (m.toId == viewer.id) {
						return $"The people of {m.city.name} have pledged allegiance to us!";
					}
					return m.fromId == viewer.id ? $"The people of {m.city.name} have deposed our governor and joined the {Noun(m.toId)}!" : null;
				case MsgCityCaptured m:
					if (m.conquerorId == viewer.id) {
						return $"We captured {m.city.name}.";
					}
					return m.formerOwnerId == viewer.id
						? $"{m.city.name} was captured by the {Noun(m.conquerorId)}" + (m.goldTaken > 0 ? $", who stole {m.goldTaken} gold!" : "!")
						: null;
				case MsgCityRansacked m:
					return $"{m.city.name} was ransacked by the {m.barbTribe} tribe, who carried away {m.goldLiberated} gold!";
				case MsgBarbarianCampDispersed m:
					return m.goldEarned > 0 ? $"We cleared a barbarian encampment and earned {m.goldEarned} gold." : "We cleared a barbarian encampment.";
				case MsgBuildingSold m:
					return $"Our treasury was empty! We sold the {m.buildingName} in {m.cityName} for {m.goldReceived} gold to pay our upkeep.";
				case MsgDescendIntoAnarchy m:
					return $"Our people overthrew our {m.government?.name ?? "government"}. We have descended into Anarchy!";
				case MsgGoldenAge m:
					return m.started ? "Our civilization has entered a Golden Age!" : "Our Golden Age has ended.";
				case MsgAgeOfScience m:
					return m.started ? "Our civilization has entered an Age of Scientific Discovery!" : "Our Age of Scientific Discovery has ended.";
				case MsgTechsFromWonder m:
					return $"{m.wonder} has given us {string.Join(", ", m.techs)}!";
				case MsgCelebration m:
					return m.Text();
				case MsgGoodyHut m:
					return m.text;
				case MsgMeltdown m:
					return m.Text();
				case MsgEmbassyEstablished m:
					return m.Text();
				case MsgSpyCaught m:
					return m.Text();
				case MsgBorderWarning m:
					return m.intruder == viewer
						? $"The {m.owner.civilization.noun} {(m.order ? "ordered" : "asked")} us to take our troops out of their territory near {m.city?.name}."
						: null;
				case MsgAirMission m:
					if (m.target == null || m.outcome is not (Air.Outcome.Intercepted or Air.Outcome.ShotDown)) {
						return null;
					}
					return m.outcome == Air.Outcome.ShotDown ? $"Our {m.plane} was shot down!" : $"Our {m.plane} was intercepted!";
				case MsgShipSank m:
					return $"Our {m.ship} was lost in treacherous waters!";
				case MsgSpaceshipComplete:
					return "Our spaceship is complete! It can be launched from the spaceship screen.";
				case MsgSpaceshipLost m:
					if (m.loser == viewer) {
						return $"The {m.captor.civilization.noun} have destroyed our spaceship!";
					}
					return m.captor == viewer ? $"We have destroyed the {m.loser.civilization.adjective} spaceship!" : null;
				case MsgDisease m:
					return m.Text();
				case MsgVolcano m:
					return m.Text();
				case MsgPlague m:
					return m.Text();
				case MsgPollution m:
					return m.Text();
				case MsgResistanceQuelled m:
					return m.resistanceContinues
						? $"Our troops have quelled {m.stopped} {(m.stopped == 1 ? "resister" : "resisters")} in {m.city.name}."
						: $"The resistance in {m.city.name} has ended.";
				case MsgTilePillaged m:
					return m.TextFor(viewer.id);
				case MsgUnitsCaptured m:
					return m.TextFor(viewer.id);
				case MsgLeaderBorn m:
					return m.scientific
						? $"A great scientific leader, {m.leaderName}, has risen among us!"
						: $"Our victory has produced a great military leader, {m.leaderName}!";
				case MsgUnitEnslaved m:
					return m.TextFor(viewer.id);

				// Everyone's news, for those who know the civs involved.
				case MsgCivilizationDestroyed m:
					return $"The {m.civilization.noun} have been destroyed.";
				case MsgWarDeclarationNotification m:
					if (m.opponent == viewer) {
						return $"The {m.aggressor.civilization.noun} have declared war on us!";
					}
					return m.aggressor != viewer && Knows(m.aggressor) && Knows(m.opponent)
						? $"The {m.aggressor.civilization.noun} declared war on the {m.opponent.civilization.noun}."
						: null;
				case MsgNuclearAttack m:
					if (!Knows(m.attacker)) {
						return null;
					}
					return m.outcome == Nuclear.Outcome.ShotDown
						? $"A {m.attacker.civilization.adjective} nuclear missile was shot down by a missile defense."
						: $"The {m.attacker.civilization.noun} launched a nuclear attack! {m.unitsKilled} units and {m.citizensKilled} citizens were lost.";
				case MsgPrincessDelivered m:
					if (m.captor == viewer) {
						return $"We have brought the {m.princess.adjective} princess to {m.capital.name}!";
					}
					return m.princess == viewer.civilization ? $"The {m.captor.civilization.noun} have taken our princess to {m.capital.name}!" : null;
				case MsgSpaceshipPart m:
					return m.owner != viewer && Knows(m.owner) ? $"The {m.owner.civilization.noun} have added {m.part.name} to their spaceship." : null;
				case MsgElectionResults m:
					return m.winner == null
						? "No one won the United Nations election."
						: $"{m.winner.civilization.leader} of the {m.winner.civilization.noun} was elected Secretary-General of the United Nations!";
				case MsgPeaceTreatySigned m:
					if (m.a == viewer || m.b == viewer) {
						return $"We made peace with the {(m.a == viewer ? m.b : m.a).civilization.noun}.";
					}
					return Knows(m.a) && Knows(m.b) ? m.Text() : null;
				case MsgGlobalWarming m:
					return m.knownBy == null || m.knownBy.Contains(viewer.id) ? m.Text() : null;
				case MsgVictory m:
					return $"The {m.winner.civilization.noun} have won a {m.victory.Header()} victory!";
				default:
					return null;
			}
		}
	}
}
