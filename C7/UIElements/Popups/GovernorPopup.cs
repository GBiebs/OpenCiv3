using System;
using System.Collections.Generic;
using C7Engine;
using C7GameData;
using Godot;

// "Contact Governor": the city's governor settings, each a toggle, applied
// to this city or to all our cities (and those we found or take later).
public partial class GovernorPopup : Popup {
	private readonly City city;
	private readonly CityGovernor settings;

	public GovernorPopup(City city) {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins(top: 150);
		this.city = city;
		settings = city.governor.Copy();
	}

	private List<(string label, Func<bool> get, Action<bool> set)> Toggles() {
		return [
			("Manage moods (keep the city out of disorder)", () => settings.manageMoods, v => settings.manageMoods = v),
			("Emphasize food", () => settings.emphasizeFood, v => settings.emphasizeFood = v),
			("Emphasize production", () => settings.emphasizeShields, v => settings.emphasizeShields = v),
			("Emphasize commerce", () => settings.emphasizeCommerce, v => settings.emphasizeCommerce = v),
			("Manage production (choose what to build)", () => settings.manageProduction, v => settings.manageProduction = v),
			("   Build great wonders", () => settings.buildGreatWonders, v => settings.buildGreatWonders = v),
			("   Build small wonders", () => settings.buildSmallWonders, v => settings.buildSmallWonders = v),
		];
	}

	private static string Text(string label, bool on) => $"[{(on ? "X" : "  ")}]  {label}";

	public override void _Ready() {
		base._Ready();

		List<(string label, Func<bool> get, Action<bool> set)> toggles = Toggles();
		int width = 470;
		int height = 125 + 25 * (toggles.Count + 3);
		AddTexture(width, height);
		AddBackground(width, height);
		AddHeader($"Governor of {city.name}", 10);

		int vOffset = 60;
		foreach ((string label, Func<bool> get, Action<bool> set) in toggles) {
			Civ3MenuButton button = new() { Text = Text(label, get()), FontSize = 14 };
			button.SetPosition(new Vector2(30, vOffset));
			AddChild(button);
			button.Pressed += () => {
				set(!get());
				button.Text = Text(label, get());
			};
			vOffset += 25;
		}

		vOffset += 15;
		AddButton("Done: just this city", vOffset, () => Apply(allCities: false));
		AddButton("Done: all our cities", vOffset + 25, () => Apply(allCities: true));
		AddButton("Cancel", vOffset + 50, Close);
	}

	private void Apply(bool allCities) {
		new MsgSetGovernor(city.id, settings, allCities).send();
		Close();
	}

	private void Close() {
		GetParent().EmitSignal(PopupOverlay.SignalName.HidePopup);
	}
}
