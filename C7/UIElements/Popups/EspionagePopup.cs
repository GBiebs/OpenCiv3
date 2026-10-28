using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// Civ3's espionage screen, for one of another civ's cities: the missions we
// can try there (through our embassy, or our spy), each with its cost and
// chance, and how carefully to go about the risky ones.
public partial class EspionagePopup : Popup {
	private readonly City city;
	private readonly Player us;
	private Espionage.Risk risk = Espionage.Risk.Immediate;
	private readonly List<(EspionageMission mission, Civ3MenuButton button)> missionButtons = new();
	private readonly List<(Espionage.Risk risk, Civ3MenuButton button)> riskButtons = new();

	public EspionagePopup(City city, Player us) {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins(top: 150);
		this.city = city;
		this.us = us;
	}

	private List<EspionageMission> Missions() {
		List<EspionageMission> result = new();
		EngineStorage.ReadGameData((GameData gameData) => {
			result = gameData.rules.EspionageMissions.Where(m => Espionage.CanAttempt(us, city.owner, m, out _)).ToList();
		});
		return result;
	}

	private string MissionText(EspionageMission mission) {
		string text = mission.name;
		EngineStorage.ReadGameData((GameData gameData) => {
			Espionage.CanAttempt(us, city.owner, mission, out bool bySpy);
			int cost = Espionage.Cost(gameData, us, city.owner, mission, city, risk);
			int chance = Espionage.SuccessPercent(gameData, us, mission.key, bySpy, risk);
			text = $"{mission.name} ({cost} gold{(mission.key == EspionageMission.Propaganda ? "" : $", {chance}%")}{(bySpy ? ", spy" : "")})";
		});
		return text;
	}

	public override void _Ready() {
		base._Ready();

		List<EspionageMission> missions = Missions();
		int width = 470;
		int height = 160 + 25 * (missions.Count + 1);
		AddTexture(width, height);
		AddBackground(width, height);
		AddHeader($"Espionage: {city.name} ({city.owner.civilization.noun})", 10);

		int hOffset = 30;
		foreach ((Espionage.Risk option, string label) in new[] {
			(Espionage.Risk.Immediate, "Immediately"), (Espionage.Risk.Carefully, "Carefully"), (Espionage.Risk.Safely, "Safely") }) {
			Civ3MenuButton button = new() { FontSize = 14 };
			button.SetPosition(new Vector2(hOffset, 55));
			AddChild(button);
			button.Pressed += () => {
				risk = option;
				Refresh();
			};
			riskButtons.Add((option, button));
			hOffset += 140;
		}

		int vOffset = 90;
		foreach (EspionageMission mission in missions) {
			Civ3MenuButton button = new() { FontSize = 14 };
			button.SetPosition(new Vector2(30, vOffset));
			AddChild(button);
			button.Pressed += () => {
				new MsgEspionageMission(us.id, city.owner.id, city.id, mission.key, risk).send();
				Close();
			};
			missionButtons.Add((mission, button));
			vOffset += 25;
		}
		Refresh();

		AddButton("Cancel", vOffset + 15, Close);
	}

	private void Refresh() {
		foreach ((Espionage.Risk option, Civ3MenuButton button) in riskButtons) {
			button.Text = $"[{(option == risk ? "X" : "  ")}] {option}";
		}
		foreach ((EspionageMission mission, Civ3MenuButton button) in missionButtons) {
			button.Text = MissionText(mission);
		}
	}

	private void Close() {
		GetParent().EmitSignal(PopupOverlay.SignalName.HidePopup);
	}
}
