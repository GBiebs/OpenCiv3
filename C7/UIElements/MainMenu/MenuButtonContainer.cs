using Godot;
using System;

[Tool]
public partial class MenuButtonContainer : VBoxContainer {
	public Civ3MenuButton NewGame { get; private set; }
	public Civ3MenuButton QuickStart { get; private set; }
	public Civ3MenuButton WatchAi { get; private set; }
	public Civ3MenuButton Tutorial { get; private set; }
	public Civ3MenuButton LoadGame { get; private set; }
	public Civ3MenuButton LoadScenario { get; private set; }
	public Civ3MenuButton ScenarioEditor { get; private set; }
	public Civ3MenuButton HostLan { get; private set; }
	public Civ3MenuButton JoinLan { get; private set; }
	public Civ3MenuButton BrowseOnline { get; private set; }
	public Civ3MenuButton HallOfFame { get; private set; }
	public Civ3MenuButton Settings { get; private set; }
	public Civ3MenuButton Preferences { get; private set; }
	public Civ3MenuButton AudioPreferences { get; private set; }
	public Civ3MenuButton Credits { get; private set; }
	public Civ3MenuButton Exit { get; private set; }

	public void CreateButtons() {
		foreach (Node child in GetChildren()) {
			RemoveChild(child);
			child.QueueFree();
		}

		NewGame = null;
		QuickStart = null;
		WatchAi = null;
		Tutorial = null;
		LoadGame = null;
		LoadScenario = null;
		ScenarioEditor = null;
		HostLan = null;
		JoinLan = null;
		BrowseOnline = null;
		HallOfFame = null;
		Settings = null;
		Preferences = null;
		AudioPreferences = null;
		Credits = null;
		Exit = null;

		NewGame = new Civ3MenuButton() { Text = "New Game" };
		AddChild(NewGame);

		QuickStart = new Civ3MenuButton() { Text = "Quick Start" };
		AddChild(QuickStart);

		WatchAi = new Civ3MenuButton() { Text = "Watch AI Game" };
		AddChild(WatchAi);

		Tutorial = new Civ3MenuButton() { Text = "Tutorial" };
		AddChild(Tutorial);

		LoadGame = new Civ3MenuButton() { Text = "Load Game" };
		AddChild(LoadGame);

		LoadScenario = new Civ3MenuButton() { Text = "Load Scenario" };
		AddChild(LoadScenario);

		ScenarioEditor = new Civ3MenuButton() { Text = "Scenario Editor" };
		AddChild(ScenarioEditor);

		HostLan = new Civ3MenuButton() { Text = "Host LAN Game" };
		AddChild(HostLan);

		JoinLan = new Civ3MenuButton() { Text = "Join LAN Game" };
		AddChild(JoinLan);

		BrowseOnline = new Civ3MenuButton() { Text = "Browse Online Games" };
		AddChild(BrowseOnline);

		HallOfFame = new Civ3MenuButton() { Text = "Hall of Fame" };
		AddChild(HallOfFame);

		Settings = new Civ3MenuButton() { Text = "Settings" };
		AddChild(Settings);

		Preferences = new Civ3MenuButton() { Text = "Preferences" };
		AddChild(Preferences);

		AudioPreferences = new Civ3MenuButton() { Text = "Audio Preferences" };
		AddChild(AudioPreferences);

		Credits = new Civ3MenuButton() { Text = "Credits" };
		AddChild(Credits);

		Exit = new Civ3MenuButton() { Text = "Exit" };
		AddChild(Exit);
	}
}
