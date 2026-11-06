using System;
using System.Linq;
using Godot;
using Serilog;

public partial class GameMenu : Popup {
	public GameMenu() {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins(top: 100);
	}

	public override void _Ready() {
		base._Ready();

		AddTexture(370, 300);
		AddBackground(370, 300);

		AddHeader("Main Menu", 10);

		// Note: Enable buttons as the features are implemented

		// (With Civ3's keys.)
		Tuple<string, Action>[] buttons = [
			new("Map", ShowMap),
			new("Load Game (Ctrl-L)", Load),
			new("New Game (Ctrl-Shift-Q)", NewGame),
			new("Preferences (Ctrl-P)", OpenPreferences),
			new("Retire (Shift-Q)", Retire),
			new("Save Game (Ctrl-S)", Save),
			new("Quit Game (ESC)", Quit)
		];

		int verticalOffset = 60;
		int rowHeight = 25;

		var indexedButtons = buttons.Select((x, i) =>
			new { Label = x.Item1, Func = x.Item2, Idx = i });

		foreach (var button in indexedButtons) {
			AddButton(button.Label, verticalOffset + button.Idx * rowHeight, button.Func);
		}
	}

	private void Load() {
		GetParent().EmitSignal(PopupOverlay.SignalName.LoadGame);
	}

	private void Save() {
		GetParent().EmitSignal(PopupOverlay.SignalName.SaveGame);
	}


	private void ShowMap() { // i.e., Cancel: return from menu to game
		GetParent().EmitSignal(PopupOverlay.SignalName.HidePopup);
	}

	private void Retire() {
		GetParent().EmitSignal(PopupOverlay.SignalName.Retire);
	}

	private void Quit() {
		GetParent().EmitSignal(PopupOverlay.SignalName.Quit);
	}

	private void NewGame() {
		// (The menu closes first: the question is a popup of the newer kind.)
		GetParent().EmitSignal(PopupOverlay.SignalName.HidePopup);
		GetParent().EmitSignal(PopupOverlay.SignalName.NewGame);
	}

	private void OpenPreferences() {
		GetParent().EmitSignal(PopupOverlay.SignalName.OpenPreferences);
	}
}
