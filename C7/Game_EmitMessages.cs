using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using C7.UIElements;
using C7.UIElements.Popups;
using C7Engine;
using C7GameData;
using Godot;
using static AdvisorHead;

public partial class Game {
	public void EmitDawnOfCivilizationSignal(MsgNewGame msg) {
		var message = "PLACEHOLDER MSG";
		var delimeter = "and";
		EngineStorage.ReadGameData(data => {
			var controller = data.players.First(p => p.id == msg.controllerId);
			var year = data.timeOptions.GetDisplayTime(data.turn);
			var knownTechs = controller.knownTechs;

			var sb = new StringBuilder();
			for (int i = 0; i < controller.civilization.traits.Count; i++) {
				var trait = controller.civilization.traits.ElementAt(i);
				if (i > 0) {
					sb.Append(delimeter).Append(" ");
				}
				sb.Append($"[url={trait}][color=Blue]");
				sb.Append(trait);
				sb.Append("[/color][/url]");
				sb.Append(" ");
			}

			var traits = sb.ToString();

			var sbt = new StringBuilder();
			// [url=whatever][color=Blue]wrap[/color][/url]
			for (int i = 0; i < knownTechs.Count; i++) {
				var tech = data.techs.First(t => t.id == knownTechs.ElementAt(i));
				if (i > 0) {
					sbt.Append(delimeter).Append(" ");
				}
				sbt.Append($"[url={tech.CivilopediaEntry}][color=Blue]");
				sbt.Append(tech.Name);
				sbt.Append("[/color][/url]");
				sbt.Append(" ");
			}

			var techs = sbt.ToString();

			var king = $"[b]{controller.civilization.leader}[/b]";

			message = $"It is the year {year}. Your ancestors were nomads. " +
					  $"But over the generations your people have learned the secrets of [i]farming," +
					  $" road-building[/i], and [i]irrigation[/i], and they are ready to settle down." +
					  $"\n\n{king}, your people are {traits}and have recently mastered {techs}." +
					  $"\n\nThe people have vested [i]absolute power[/i] in you, trusting that you can build a Civilization to stand the test of time!";
		});

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Dawn of Civilization",
				message,
				Advisor.None,
				Mood.None,
				630,
				margins: new Margins(top: 50)
			)
		));
	}

	public void EmitGameMainMenuSignal() {
		var options = new List<ButtonAction>();

		var mapBtn = new ButtonAction() {
			message = "Map",
			action = () => EmitMapMenuSignal(),
		};
		options.Add(mapBtn);

		var loadGameBtn = new ButtonAction() {
			message = "Load Game (Ctrl-L)",
			action = () => OnLoadGame(),
		};
		options.Add(loadGameBtn);

		// TODO: add New Game option. This seems to do exactly the same as Retire in the original game

		var preferencesBtn = new ButtonAction() {
			message = "Preferences (Ctrl-P)",
			action = () => OnOpenPreferences(),
		};
		options.Add(preferencesBtn);

		var retireBtn = new ButtonAction() {
			message = "Retire (Ctrl-Q)",
			action = () => EmitRetireConfirmationSignal(),
		};
		options.Add(retireBtn);

		var saveBtn = new ButtonAction() {
			message = "Save Game (Ctrl-S)",
			action = () => OnSaveGame(),
		};
		options.Add(saveBtn);

		var quitBtn = new ButtonAction() {
			message = "Quit Game (ESC)",
			action = () => EmitQuitGameSignal(),
		};
		options.Add(quitBtn);

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(
				controller.id,
				"Main Menu",
				null,
				options,
				Advisor.None,
				Mood.None,
				hSize: 350,
				margins: new Margins(top: 100)
			)
		));
	}

	public void EmitRetireConfirmationSignal() {
		var message = "Retire from this game? All progress since your last save will be lost.";

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Do you want to retire?",
				message,
				Advisor.None,
				Mood.None,
				"Yes, retire from this game",
				"No, let me keep playing",
				() => OnRetire(),
				hSize: 500
			)
		));
	}

	public void EmitDiplomacyPopUpSignal() {
		var options = new List<ButtonAction>();

		EngineStorage.ReadGameData(data => {
			var allPlayers = data.players;
			var buttons = new List<ButtonAction>();
			foreach (KeyValuePair<ID, PlayerRelationship> kvp in controller.playerRelationships) {
				string status = kvp.Value.AtWar() ? "War" : "Peace";

				var btn = new ButtonAction() {
					message = $"{allPlayers.Find(x => x.id == kvp.Key).civilization.noun} (at {status})",
					action = () => {
                        // EmitSignal(PopupOverlay.SignalName.HidePopup);
                        OnDiplomacySelected(new ParameterWrapper<ID>(kvp.Key));
                        // EmitSignal(PopupOverlay.SignalName.DiplomacySelection, new ParameterWrapper<ID>(kvp.Key));
                    }
				};
				buttons.Add(btn);
			}
			options = buttons;
		});


		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(
				controller.id,
				"Pick the civilization...",
				null,
				options,
				Advisor.None,
				Mood.None,
				hSize: 550,
				margins: new Margins(top: 150)
			)
		));
	}

	public void EmitScienceGuidanceSignal() {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Science Advisor",
				"Sir, out Prophets need guidance. What shall we research?",
				Advisor.Science,
				Mood.Sad,
				350
			)
		));
	}

	public void EmitConfirmStopWorkerActionSignal(MsgDisplayStopWorkerActionPopup msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Domestic Advisor",
				$"This worker has been ordered to {C7Action.ToTooltip(msg.workerJob.UIAction)} and will be done in {msg.turnsLeft} turns." +
				$"\nDo you want them to stop?",
				Advisor.Domestic,
				Mood.Happy,
				"Yes, there is more important work to do!",
				"No, carry on.",
				() => {
					new MsgDoStopWorkerAction(msg.worker).send();
				},
				hSize: 350
			)
		));
	}

	public void EmitHurryProductionSignal(MsgDisplayHurryProductionPopup msg) {
		if (msg.details.errorMessage != null) {
			EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
				new InformationPopup(
					controller.id,
					"Domestic Advisor",
					msg.details.errorMessage,
					Advisor.Domestic,
					Mood.Sad,
					hSize: 300
				)
			));
			return;
		}

		string yesText = "Yes";
		string noText = "No";
		var mood = Mood.Sad;
		if (msg.details.hurryProductionType == Government.HurryProductionType.ForcedLabor) {
			yesText = "It's that important. Get out my whip!";
			noText = "Never mind.";
		}
		if (msg.details.hurryProductionType == Government.HurryProductionType.PaidLabor) {
			yesText = "Don't argue with me. Start counting!";
			noText = "Oh, I see. Never mind..";
			mood = Mood.Surprised;
		}
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Domestic Advisor",
				msg.details.costMessage,
				Advisor.Domestic,
				mood,
				yesText,
				noText,
				() => {
					new MsgDoHurryProduction(msg.city).send();
				},
				hSize: 350
			)
		));
	}

	// Offers to upgrade the unit, showing the price, or says why it can't be.
	public void EmitUpgradeSignal(MapUnit unit) {
		(UnitPrototype target, int cost)? offer = null;
		int gold = 0;
		EngineStorage.ReadGameData(data => {
			offer = unit.UpgradeOffer();
			gold = unit.owner.gold;
		});
		if (offer is not (UnitPrototype target, int cost)) {
			return;
		}
		if (cost > gold) {
			EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
				new InformationPopup(controller.id, "Military Advisor",
					$"Upgrading our {unit.unitType.name} to a {target.name} costs {cost} gold, but we only have {gold}.",
					Advisor.Military, Mood.Sad, hSize: 350)
			));
			return;
		}
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Military Advisor",
				$"Upgrade our {unit.unitType.name} to a {target.name} for {cost} gold?",
				Advisor.Military,
				Mood.Happy,
				"Yes, upgrade it.",
				"No, not now.",
				() => {
					new MsgUpgradeUnit(unit.id).send();
				},
				hSize: 350
			)
		));
	}

	public void EmitSellBuildingSignal(MsgDisplaySellBuildingPopup msg) {
		string reason = null;
		int gold = 0;
		EngineStorage.ReadGameData(data => {
			reason = msg.city.WhyBuildingCannotBeSold(msg.building);
			gold = msg.city.SaleValue(msg.building);
		});
		if (reason != null) {
			EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
				new InformationPopup(controller.id, "Domestic Advisor", reason, Advisor.Domestic, Mood.Sad, hSize: 300)
			));
			return;
		}
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Domestic Advisor",
				$"Sell the {msg.building.name} in {msg.city.name} for {gold} gold?",
				Advisor.Domestic,
				Mood.Surprised,
				"Yes, sell it.",
				"No, keep it.",
				() => {
					new MsgSellBuilding(msg.city, msg.building, controller.id).send();
				},
				hSize: 350
			)
		));
	}

	public void EmitCityRansackedSignal(MsgCityRansacked msg) {
		var ransacked = $"[url=no idea][color=Blue]ransacked[/color][/url]";
		var message = $"{msg.city.name} was {ransacked} by {msg.barbTribe} tribe!" +
					  $" They have carried away {msg.goldLiberated} gold!\nWe [i]must build[/i] our military!";

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Military Advisor",
				message,
				Advisor.Military,
				Mood.Angry,
				350
			)
		));
	}

	public void EmitBarbarianCampDispersedSignal(MsgBarbarianCampDispersed msg) {
		string message = msg.goldEarned > 0
			? $"We cleared a barbarian encampment and earned {msg.goldEarned} gold!"
			: "We cleared a barbarian encampment!";

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Military Advisor",
				message,
				Advisor.Military,
				Mood.Happy,
				350
			)
		));
	}

	// Civ3's texts (script.txt NEWMILLEADER, NEWSCILEADER).
	public void EmitLeaderBornSignal(MsgLeaderBorn msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				msg.scientific ? "Science Advisor" : "Military Advisor",
				msg.scientific
					? $"Our recent breakthrough in technology has proven the brilliance of a great scientific leader, {msg.leaderName}!"
					: $"Our victory has produced a great military leader, {msg.leaderName}!",
				msg.scientific ? Advisor.Science : Advisor.Military,
				Mood.Happy,
				350
			)
		));
	}

	// Civ3's texts (script.txt SUMMARY_SCIENCE_AGE, SUMMARY_END_SCIENCE_AGE).
	private void EmitAgeOfScienceSignal(MsgAgeOfScience msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Science Advisor",
				msg.started
					? "Our Great Civilization has entered an Age of Scientific Discovery!"
					: "Our Civilization's Age of Scientific Discovery has ended. So say our analysts...",
				Advisor.Science,
				msg.started ? Mood.Happy : Mood.Sad,
				350
			)
		));
	}

	public void EmitBuildingSoldSignal(MsgBuildingSold msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Domestic Advisor",
				$"Our treasury is empty! We had to sell the {msg.buildingName} in {msg.cityName} for {msg.goldReceived} gold to pay our upkeep.",
				Advisor.Domestic,
				Mood.Sad,
				350
			)
		));
	}

	public void EmitCivilizationDestroyedSignal(MsgCivilizationDestroyed msg) {
		// Sad for a civ we were at peace with, glad for an enemy.
		var friend = false;
		EngineStorage.ReadGameData(data => {
			var destroyedPlayer = data.players.First(p => p.civilization == msg.civilization);
			if (controller.IsAtPeaceWith(destroyedPlayer))
				friend = true;
		});

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Military Advisor",
				$"The {msg.civilization.noun} have been destroyed.",
				Advisor.Military,
				friend ? Mood.Sad : Mood.Happy,
				350
			)
		));
	}

	// The human has been destroyed and isn't watching in observer mode.
	private bool IsHumanDefeated(GameData data) {
		return controller.defeated && !data.observerMode;
	}

	private void EmitDefeatSignal(ID playerId) {
		var options = new List<ButtonAction>() {
			new ButtonAction() {
				message = "Return to the main menu",
				action = () => OnRetire()
			},
			new ButtonAction() {
				message = "Let the game play on without me",
				action = () => ToggleObserverMode()
			}
		};

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(
				controller.id,
				"Game Over",
				$"The {controller.civilization.noun} have been destroyed.\n"
				+ "This game is over.\n\n",
				options,
				Advisor.Military,
				Mood.Sad,
				hSize: 600,
				margins: new Margins(top: 100)
			) { hasCancel = false }
		));
	}

	public void EmitCityRazedSignal(MsgCityRazed msg) {
		Player razer = null;
		EngineStorage.ReadGameData(data => {
			razer = data.players.First(p => p.id == msg.razerId);
		});
		bool weRazedIt = msg.razerId == controller.id;
		string gold = msg.goldTaken > 0 ? $" and \"liberated\" {msg.goldTaken} gold" : "";
		string wonders = string.Join(" and ", msg.wondersDestroyed);

		string message;
		if (weRazedIt && msg.destroyedOnCapture) {
			message = $"Supreme Lord, once again our magnificent armies are victorious!\n" +
				$"We have destroyed {msg.city.name}{gold}!";
		} else if (weRazedIt) {
			message = $"{msg.city.name} has been burned to the ground." +
				(msg.slavesTaken > 0 ? $"\nWe have taken {msg.slavesTaken} of its people as slaves." : "") +
				(wonders != "" ? $"\n{wonders} lies in ruins." : "");
		} else {
			message = $"Terrible news, Sir!\n" +
				$"The evil {razer.civilization.adjective} have burned {msg.city.name} to the ground" +
				(msg.goldTaken > 0 ? $" and stolen [i]{msg.goldTaken} gold[/i] from its vaults" : "") + "!" +
				(wonders != "" ? $"\nThey destroyed {wonders}!" : "") +
				"\nThey should pay dearly for this atrocity!";
		}

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Military Advisor",
				message,
				Advisor.Military,
				weRazedIt ? Mood.Happy : Mood.Angry,
				350
			)
		));
	}

	// Civ3's words (script.txt WE_CONVERT_CITY, THEY_CONVERT_CITY and their
	// _WITH_WONDER versions). The civ that gains the city may refuse it.
	public void EmitCityFlippedSignal(MsgCityFlipped msg) {
		string wonders = string.Join(" and ", msg.wonders);
		Player gainer = null;
		EngineStorage.ReadGameData(data => {
			gainer = data.players.First(p => p.id == msg.toId);
		});
		if (msg.toId != controller.id) {
			EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
				new InformationPopup(
					controller.id,
					"Military Advisor",
					$"Terrible news, Sir!\n" +
						$"The people of {msg.city.name} have deposed our governor and pledged allegiance to the treacherous {gainer.civilization.noun}!" +
						(wonders != ""
							? $"\nBecause of this treachery, we have lost control of {wonders}!"
							: $"\nWord is, the people of {msg.city.name} \"admire\" the so called \"culture\" of the {gainer.civilization.noun}... Imagine."),
					Advisor.Military,
					Mood.Angry,
					350
				)
			));
			return;
		}

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Military Advisor",
				$"The loyal citizens of {msg.city.name} have overthrown their oppressors and have pledged allegiance to us!" +
					(wonders != "" ? $"\nAnd I am honored to report that the city contains {wonders}!" : "\nThey yearn to be with us."),
				Advisor.Military,
				Mood.Happy,
				"Great! Install a new governor.",
				$"We don't want {msg.city.name}. Rebuff the rebels.",
				yesAction: () => { },
				noAction: () => new MsgRebuffRebels(msg.city).send(),
				hSize: 350
			)
		));
	}

	// The conqueror chooses to keep or raze the city; its former owner hears
	// the bad news.
	public void EmitCityCapturedSignal(MsgCityCaptured msg) {
		string wonders = string.Join(" and ", msg.wonders);
		if (msg.conquerorId != controller.id) {
			Player conqueror = null;
			EngineStorage.ReadGameData(data => {
				conqueror = data.players.First(p => p.id == msg.conquerorId);
			});
			EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
				new InformationPopup(
					controller.id,
					"Military Advisor",
					$"Terrible news, Sir!\n" +
						$"{msg.city.name} was captured by the evil {conqueror.civilization.adjective}!" +
						(msg.goldTaken > 0 ? $"\nThey have stolen [i]{msg.goldTaken} gold[/i] from its vaults." : "") +
						(wonders != "" ? $"\nAnd they have taken {wonders}!" : "") +
						"\nWe must make them pay!",
					Advisor.Military,
					Mood.Angry,
					350
				)
			));
			return;
		}

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new KeepOrRazePopUp(
				controller.id,
				$"Supreme Lord, once again our magnificent armies are victorious!\n" +
					$"We have captured {msg.city.name}" +
					(msg.goldTaken > 0 ? $" and \"liberated\" {msg.goldTaken} gold" : "") + "!" +
					(wonders != "" ? $"\nAnd we now control {wonders}!" : "") +
					(msg.resisters > 0
						? $"\nThere {(msg.resisters == 1 ? "is 1 resister" : $"are {msg.resisters} resisters")} in {msg.city.name}. " +
							"We should garrison it with ground troops to quell the resistance."
						: ""),
				raze: () => new MsgRazeCity(msg.city, controller.id).send()
			)
		));
	}

	private void EmitWarDeclarationNotificationSignal(MsgWarDeclarationNotification msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Military Advisor",
				$"The {msg.aggressor.civilization.noun} declared war on the {msg.opponent.civilization.noun}",
				Advisor.Military,
				Mood.Sad,
				400
			)
		));
	}

	private void EmitWarDeclarationConfirmationSignal(MsgWarDeclarationConfirmation msg) {
		Player aggressor = null;
		Player opponent = null;

		Action yesAction = null;
		// "I said [i]DO IT![/i]",
		string yesText = "I said DO IT!";

		Action nosAction = null;
		string noText = "No. You're right, perhaps we should re-consider.";

		var message = "PLACEHOLDER MSG";

		EngineStorage.ReadGameData(data => {
			aggressor = data.players.First(p => p.id == controller.id);
			opponent = data.players.First(p => p.id == msg.opponentId);

			var currentTurn = data.turn;

			message = $"Sir, this will cause war with the {opponent.civilization.adjective} people.\nAre you sure?";

			yesAction = () => {
				aggressor.DeclareWarOn(opponent, currentTurn);
				msg.callback();
			};
		});

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Foreign Advisor",
				message,
				Advisor.Foreign,
				Mood.Sad,
				yesText,
				noText,
				yesAction
			)
		));
	}

	private void EmitDiplomaticWarDeclarationConfirmationSignal(MsgWarDeclarationConfirmation msg) {
		Player aggressor = null;
		Player opponent = null;

		Action yesAction = null;
		// "I said [i]DO IT![/i]",
		string yesText = "You 're right! They are scum!";

		Action nosAction = null;
		string noText = "No. We should respect our neighbors.";

		var message = "PLACEHOLDER MSG";

		EngineStorage.ReadGameData(data => {
			aggressor = data.players.First(p => p.id == controller.id);
			opponent = data.players.First(p => p.id == msg.opponentId);

			var currentTurn = data.turn;

			message = $"Let us destroy them, sir!";

			yesAction = () => {
				aggressor.DeclareWarOn(opponent, currentTurn);
				msg.callback();
			};
		});

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Military Advisor",
				message,
				Advisor.Military,
				Mood.Angry,
				yesText,
				noText,
				yesAction,
				hSize: 350,
				layoutPreset: Control.LayoutPreset.Center,
				margins: new Margins(top: -350, left: 650)
			)
		));
	}

	private void EmitRefuseContactSignal(MsgRefuseContact msg) {
		var noun = "Australians";
		EngineStorage.ReadGameData(data => {
			noun = data.players.First(p => p.id == msg.opponentId).civilization.noun;
		});
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Foreign Advisor",
				$"The {noun} refused to acknowledge our envoy!",
				Advisor.Foreign,
				Mood.Angry,
				350
			)
		));
	}

	private void EmitCityRiotWarningSignal(MsgCityRiotWarning msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Domestic Advisor",
				$"[i]{msg.city.name}[/i] will riot! Are you sure you want to end your turn?",
				Advisor.Domestic,
				Mood.Angry,
				"Yes, let them riot!",
				"No, you are right, I will handle it.",
				yesAction: () => { DoActualEndTurn(); },
				hSize: 350
			)
		));
	}

	private void EmitSelectGovernmentSignal(MsgSelectGovernment msg) {

		var options = new List<ButtonAction>();

		EngineStorage.ReadGameData(data => {
			var player = data.players.First(p => p.id == msg.controllerId);
			var governments = player.GetAvailableGovernments(data);
			var buttons = new List<ButtonAction>();
			foreach (Government g in governments) {
				var btn = new ButtonAction() {
					message = $"{g.name}",
					action = () => {
						new SelectGovernmentMsg(player, g).send();
					}
				};
				buttons.Add(btn);
			}

			options = buttons;
		});

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(
				controller.id,
				"Government Types",
				$"Select a new government type.",
				options,
				Advisor.None,
				Mood.None,
				350,
				margins: new Margins(top: 100)
			)
		));
	}

	// Civ3's "Set mobilization level to..." (Shift-M or the Domestic Advisor):
	// Normalcy or War-Time, whichever we may switch to.
	private void EmitChangeMobilizationSignal() {
		bool canMobilize = false;
		bool canDemobilize = false;
		bool mobilized = false;
		string enablingTech = null;
		EngineStorage.ReadGameData(data => {
			Player player = data.players.First(p => p.id == controller.id);
			canMobilize = player.CanMobilize();
			canDemobilize = player.CanDemobilize();
			mobilized = player.mobilized;
			enablingTech = data.techs.FirstOrDefault(t => t.HasFlag(C7GameData.Save.SaveTech.Flag.EnablesMobilization))?.Name;
		});

		if (!canMobilize && !canDemobilize) {
			string why = mobilized
				? "We are on a war footing, and can't return to normalcy until we sign a peace treaty."
				: $"We can't mobilize our economy for war until we learn {enablingTech ?? "how"}.";
			EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
				new InformationPopup(controller.id, "Domestic Advisor", why, Advisor.Domestic, Mood.Sad, 350)));
			return;
		}

		List<ButtonAction> options = [];
		if (canDemobilize) {
			options.Add(new ButtonAction() { message = "Normalcy", action = () => new MsgSetMobilization(controller.id, false).send() });
		}
		if (canMobilize) {
			options.Add(new ButtonAction() { message = "War-Time", action = () => new MsgSetMobilization(controller.id, true).send() });
		}
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(
				controller.id,
				"Mobilization",
				"Set mobilization level to...",
				options,
				Advisor.Domestic,
				Mood.None,
				350,
				margins: new Margins(top: 100)
			)
		));
	}

	private void EmitSelectTransportSignal(MsgSelectTransport msg) {
		// The load hotkey works even when there is no transport here
		if (msg.transports.Count == 0)
			return;

		// Nothing to choose from, load the unit right away
		if (msg.transports.Count == 1) {
			new MsgLoadToTransport(msg.unit.id, msg.transports[0].id).send();
			return;
		}

		var options = new List<ButtonAction>();

		EngineStorage.ReadGameData(data => {
			foreach (MapUnit transport in msg.transports) {
				var capacity = transport.unitType.capacity;
				options.Add(new ButtonAction() {
					message = $"{transport.GetDisplayName()} ({capacity - transport.FreeCapacity()}/{capacity})",
					action = () => {
						new MsgLoadToTransport(msg.unit.id, transport.id).send();
					}
				});
			}
		});

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(
				controller.id,
				"Transports",
				$"Select a transport for the {msg.unit.GetDisplayName()}.",
				options,
				Advisor.None,
				Mood.None,
				hSize: 350,
				margins: new Margins(top: 100)
			)
		));
	}

	private void EmitAlreadyInRevolutionSignal(MsgAlreadyInRevolution msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Domestic Advisor",
				$"Sir! We are already undergoing a revolt..!\n\nDid you take your medication?",
				Advisor.Domestic,
				Mood.Angry,
				350
			)
		));
	}

	// Civ3's words (script.txt SUMMARY_GOLDEN_AGE, SUMMARY_END_GOLDEN_AGE).
	private void EmitGoldenAgeSignal(MsgGoldenAge msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Domestic Advisor",
				msg.started
					? "Our Great Civilization has entered a Golden Age!"
					: "Our Civilization's Golden Age has ended. So say our analysts...",
				Advisor.Domestic,
				msg.started ? Mood.Happy : Mood.Sad,
				350
			)
		));
	}

	private void EmitGoodyHutSignal(MsgGoodyHut msg) {
		(string title, Advisor advisor, Mood mood) = msg.outcome switch {
			GoodyHuts.Outcome.Tech => ("Science Advisor", Advisor.Science, Mood.Happy),
			GoodyHuts.Outcome.Mercenaries => ("Military Advisor", Advisor.Military, Mood.Happy),
			GoodyHuts.Outcome.Barbarians => ("Military Advisor", Advisor.Military, Mood.Surprised),
			GoodyHuts.Outcome.Nothing => ("Domestic Advisor", Advisor.Domestic, Mood.Sad),
			_ => ("Domestic Advisor", Advisor.Domestic, Mood.Happy),
		};
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(controller.id, title, msg.text, advisor, mood, 350)
		));
	}

	// A disaster: the Domestic Advisor brings the bad news.
	private void EmitBadNewsSignal(string text) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(controller.id, "Domestic Advisor", text, Advisor.Domestic, Mood.Sad, 400)
		));
	}

	// An AI asks our troops to leave its land, or orders them home (Civ3's
	// AIBORDERWARNING, AIBORDERWAR, and the answers USERWILLRETREAT,
	// USERWILLRETURNHOME, USERWAR).
	private void EmitBorderWarningSignal(MsgBorderWarning msg) {
		string civ = msg.owner.civilization.noun;
		string text = msg.order
			? $"{msg.owner.civilization.leader} of the {civ}: \"Your troops near {msg.city?.name} are in our territory. Send them home immediately, or know that you will be held responsible for the conflict!\""
			: $"{msg.owner.civilization.leader} of the {civ}: \"Your troops seem to be within our territory near {msg.city?.name}. It is certainly a mistake on your generals' part, and we humbly request that you correct it.\"";
		List<ButtonAction> options = new();
		if (!msg.order) {
			options.Add(new ButtonAction() { message = "Our troops will leave soon.", action = () => { } });
		}
		options.Add(new ButtonAction() { message = "We will send our troops home.", action = () => new MsgSendTroopsHome(msg.intruder.id, msg.owner.id).send() });
		options.Add(new ButtonAction() {
			message = "This means war!",
			action = () => new MsgDiplomacyWarDeclarationConfirmation(controller.id, msg.owner.id, () => { }).send(),
		});
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(controller.id, "Foreign Advisor", text, options, Advisor.Foreign, Mood.Sad, hSize: 450)
		));
	}

	// Civilopedia "Stealth Attack": "a selection box appears listing the
	// target units".
	private void EmitChooseStealthTargetSignal(MsgChooseStealthTarget msg) {
		List<ButtonAction> options = msg.targets
			.Select(t => new ButtonAction() {
				message = $"{t.owner.civilization.adjective} {t.unitType.name} ({t.unitType.attack}.{t.unitType.defense}, {t.hitPointsRemaining} HP)",
				action = () => new MsgStealthTarget(t.id).send(),
			})
			.Append(new ButtonAction() { message = "Call off the attack.", action = () => new MsgStealthTarget(null).send() })
			.ToList();
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(controller.id, "Stealth Attack", $"Which unit should our {msg.attacker.unitType.name} attack?", options, Advisor.Military, Mood.Happy) {
				cancelAction = () => new MsgStealthTarget(null).send(),
			}
		));
	}

	private void EmitMilitaryAdvisorSignal(string text) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(controller.id, "Military Advisor", text, Advisor.Military, Mood.Surprised, 400)
		));
	}

	private void EmitScienceAdvisorSignal(string text, Mood mood) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(controller.id, "Science Advisor", text, Advisor.Science, mood, 400)
		));
	}

	// Our spaceship is complete: launch it now (and win), or later from the
	// victory status screen.
	private void EmitSpaceshipCompleteSignal() {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Science Advisor",
				"Our spaceship is complete! Shall we launch it for Alpha Centauri?",
				Advisor.Science,
				Mood.Happy,
				"Launch the spaceship!",
				"Not yet.",
				yesAction: () => new MsgLaunchSpaceship(controller.id).send(),
				noAction: () => { }
			)
		));
	}

	// The United Nations (Civ3's DIPLOVICTORYVOTEOPTION, DIPLOVICTORYVOTE,
	// DIPLOVICTORYVOTERESULTS and NODIPLOVICTORY).
	private void EmitElectionOfferSignal() {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"United Nations Vote",
				"Would you like to hold elections for UN Secretary-General?",
				Advisor.Foreign,
				Mood.Happy,
				"Yes.",
				"No.",
				yesAction: () => new MsgCallElection(true).send(),
				noAction: () => new MsgCallElection(false).send()
			) {
				cancelAction = () => new MsgCallElection(false).send(),
			}
		));
	}

	private void EmitVoteRequestSignal(MsgVoteRequest msg) {
		List<ButtonAction> options = msg.candidates
			.Select(c => new ButtonAction() {
				message = $"{c.civilization.leader} of the {c.civilization.noun}",
				action = () => new MsgVote(c.id).send(),
			})
			.Append(new ButtonAction() { message = "We abstain.", action = () => new MsgVote(null).send() })
			.ToList();
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(controller.id, "Vote for the UN Secretary-General...", "Whom do we vote for?", options, Advisor.Foreign, Mood.Happy) {
				cancelAction = () => new MsgVote(null).send(),
			}
		));
	}

	// Play by email: what happened to us while the others played.
	private void EmitNewsWhileAwaySignal(MsgNewsWhileAway msg) {
		List<ButtonAction> options = [new ButtonAction { message = "Very well.", action = () => new MsgNewsRead().send() }];
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(controller.id, "While you were away...", string.Join("\n", msg.news.Select(n => $"• {n}")), options,
				Advisor.Domestic, Mood.Surprised, 520) {
				cancelAction = () => new MsgNewsRead().send(),
			}
		));
	}

	private void EmitElectionResultsSignal(MsgElectionResults msg) {
		string results = string.Join("\n", msg.votes.Select(v => $"{v.voter.civilization.noun}: {(v.candidate == null ? "abstained" : v.candidate.civilization.noun)}"));
		string outcome = msg.winner == null
			? "Election was inconclusive; no majority was found."
			: $"{msg.winner.civilization.leader} of the {msg.winner.civilization.noun} is elected UN Secretary-General!";
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(controller.id, "Voting Results...", $"{results}\n\n{outcome}", Advisor.Foreign,
				msg.winner == controller ? Mood.Happy : Mood.Sad, 400)
		));
	}

	// An AI demands tribute (Civ3's AIDEMANDTRIBUTE and the answers
	// USERWILLGIVETRIBUTE, USERWILLNOTGIVETRIBUTE).
	private void EmitTributeDemandedSignal(MsgTributeDemanded msg) {
		List<string> wanted = msg.demand.techs.Select(t => t.Name).ToList();
		if (msg.demand.gold is int gold) {
			wanted.Add($"{gold} gold");
		}
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Foreign Advisor",
				$"{msg.demander.civilization.leader} of the {msg.demander.civilization.noun} demands {string.Join(" and ", wanted)}:\n" +
					"\"Give us what we want, and we will not crush you. Does that not sound fair?\"",
				Advisor.Foreign,
				Mood.Sad,
				"Very well. We will give you what you want - this time.",
				"Take your empty threats elsewhere.",
				yesAction: () => new MsgTributeAnswer(msg.demander.id, msg.victim.id, msg.demand, pay: true).send(),
				noAction: () => new MsgTributeAnswer(msg.demander.id, msg.victim.id, msg.demand, pay: false).send(),
				hSize: 450
			) {
				cancelAction = () => new MsgTributeAnswer(msg.demander.id, msg.victim.id, msg.demand, pay: false).send(),
			}
		));
	}

	private void EmitForeignAdvisorSignal(string text, Mood mood) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(controller.id, "Foreign Advisor", text, Advisor.Foreign, mood, 400)
		));
	}

	private void EmitTechsFromWonderSignal(MsgTechsFromWonder msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new InformationPopup(
				controller.id,
				"Science Advisor",
				$"{msg.wonder} has given us {string.Join(", ", msg.techs)}!",
				Advisor.Science,
				Mood.Happy,
				350
			)
		));
	}

	private void EmitDescendIntoAnarchySignal(MsgDescendIntoAnarchy msg) {
		EngineStorage.ReadGameData(data => {
			var player = data.players.First(p => p.id == msg.controllerId);
			EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
				new InformationPopup(
					controller.id,
					"Domestic Advisor",
					$"Our people are overthrowing our {msg.government.name}. Our Civilization is descending into Anarchy!",
					Advisor.Domestic,
					Mood.Surprised,
					350
				)
			));
		});
	}

	private void EmitStartARevolutionSignal(MsgConfirmStartRevolution msgConfirm) {
		Player controller = null;
		Government controllerGov = null;
		EngineStorage.ReadGameData(data => {
			controller = data.players.First(p => p.id == msgConfirm.controllerId);
			controllerGov = controller.government;
		});
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Domestic Advisor",
				$"You say you want a Revolution?!",
				Advisor.Domestic,
				Mood.Happy,
				"Yes. You know it's gonna be alright.",
				"No. You can count me out.",
				yesAction: () => {
					new MsgDescendIntoAnarchy(controller.id, controllerGov).send();
				},
				yesCallback: async () => {
					await EngineStorage.WaitForMessageToEngine<MsgUiDisengaged>();
					new StartGovernmentTransitionMsg(controller).send();
				},
				hSize: 350
			)
		));
	}

	private void EmitConfirmAbandonCitySignal(MsgDisplayAbandonCityPopup msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Domestic Advisor",
				$"Are you sure you want to abandon {msg.city.name}?",
				Advisor.Domestic,
				Mood.Angry,
				"Yes, we don't want it anymore.",
				"No. Sorry.",
				yesAction: () => {
					CityInteractions.DestroyCity(msg.city);
				},
				hSize: 350
			)
		));
	}

	private void EmitQuitGameSignal() {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Oh No!",
				$"Do you really want to quit?",
				Advisor.None,
				Mood.None,
				"Yes, immediately.",
				"No, not really",
				yesAction: () => {
					OnQuitTheGame();
				},
				hSize: 350,
				margins: new Margins(top: 100)
			)
		));
	}

	private void EmitNameCitySignal(MsgNameCity msg) {
		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new TextInputPopUp(
				controller.id,
				"Name this town?",
				"Name:",
				msg.nextCityName,
				callback: OnBuildCity,
				Advisor.Culture,
				Mood.Happy,
				hSize: 550
			)
		));
	}

	private void EmitTerrainImprovementReplacementConfirmationSignal(MsgReplaceTerrainImprovementConfirmation msg) {
		var message = $"A previous terrain enhancement ({msg.terrainImprovement.key.Capitalize()}) will be replaced \nby this operation. Do you wish to continue?";

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Domestic Advisor",
				message,
				Advisor.Domestic,
				Mood.Sad,
				"Do as I say!",
				"No, don't let all that hard work go to waste.",
				() => {
					new MsgStartWorkerJob(CurrentlySelectedUnit.id, msg.terraform).send();
				},
				hSize: 500
			)
		));
	}

	private void EmitDisbandUnitConfirmationSignal(MsgDisbandUnitConfirmation msg) {
		var message = $"Disband {msg.mapUnit.name}? Pardon me but these are OUR people.\nDo you really want to disband them?";

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new ConfirmPopUp(
				controller.id,
				"Domestic Advisor",
				message,
				Advisor.Domestic,
				Mood.Surprised,
				"Yes, we need to!",
				"No. Maybe you are right, advisor.",
				() => {
					new ActionToEngineMsg(async () => await CurrentlySelectedUnit.Disband()).send();
				},
				hSize: 500
			)
		));
	}

	private void EmitVictorySignal(MsgVictory msg) {
		var endMsg =
			$"The {msg.winner.civilization.noun} have won a {msg.victory.Header()} victory!\n"
			+ "This game is over: No further score will be entered.\n\n";

		var continueAction = controller.defeated ? "see how it plays out for" : "play";

		var options = new List<ButtonAction> {
			// The game statistics first, then back to the main menu.
			new ButtonAction() {
				message = "Good! I’m Done!",
				action = () => gameViews.ShowStatistics(onClose: OnRetire)
			},
			new ButtonAction() {
				message = $"Wait, lemme just {continueAction} a couple of more turns..."
			}
		};

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(
				controller.id,
				msg.winner.id == controller.id ? "You Win!" : "Game Over",
				endMsg,
				options,
				Advisor.None,
				Mood.None,
				hSize: 600,
				margins: new Margins(top: 100)
			)
		));
	}

	public void EmitMapMenuSignal() {
		var options = new List<ButtonAction>();

		bool gridVisible = mapView != null ? mapView.gridLayer.visible : PreferencesSettings.GetShowGrid();
		var showGridBtn = new ButtonAction() {
			message = $"Show Grid (Ctrl-G): {(gridVisible ? "On" : "Off")}",
			action = () => {
				if (mapView != null) {
					bool newValue = !mapView.gridLayer.visible;
					mapView.gridLayer.visible = newValue;
					PreferencesSettings.SetShowGrid(newValue);
				} else {
					bool newValue = !PreferencesSettings.GetShowGrid();
					PreferencesSettings.SetShowGrid(newValue);
				}
				// Re-open map menu to reflect updated state
				EmitMapMenuSignal();
			},
			pressed = gridVisible,
		};
		options.Add(showGridBtn);

		var backBtn = new ButtonAction() {
			message = "Back",
			action = () => EmitGameMainMenuSignal(),
		};
		options.Add(backBtn);

		EmitSignal(SignalName.InteractivePopUp, new ParameterWrapper<InteractablePopUp>(
			new OptionsPopUp(
				controller.id,
				"Map Options",
				null,
				options,
				Advisor.None,
				Mood.None,
				hSize: 350,
				margins: new Margins(top: 100)
			)
		));
	}
}
