using System;
using C7.UIElements;
using Godot;

/**
 * User preferences: settings that stay stable across games and scenarios, so
 * they are stored in C7.ini rather than in the save file.
 *
 * Deliberately not a PopupOverlay child. Preferences is a main menu style view,
 * not a lightweight in-game modal, so it is a full scene shown and hidden in
 * place, like the advisors.
 */
[GlobalClass]
public partial class Preferences : Control {
	[Export] Civ3Checkbox promptForResearch;
	[Export] Civ3Checkbox fullscreen;
	[Export] Civ3Checkbox animateFriendly;
	[Export] Civ3Checkbox animateEnemy;

	// TODO: Make an audio volume control scene

	[Export] Civ3HSlider masterVolume;
	[Export] Civ3HSlider musicVolume;
	[Export] Civ3HSlider sfxVolume;
	[Export] Civ3HSlider uiVolume;
	[Export] Civ3HSlider ambienceVolume;
	[Export] Label masterVolumeLabel;
	[Export] Label musicVolumeLabel;
	[Export] Label sfxVolumeLabel;
	[Export] Label uiVolumeLabel;
	[Export] Label ambienceVolumeLabel;
	[Export] Control masterVolumeRow;
	[Export] Control musicVolumeRow;
	[Export] Control sfxVolumeRow;
	[Export] Control uiVolumeRow;
	[Export] Control ambienceVolumeRow;
	[Export] CheckBox autosaveToggle;
	[Export] LineEdit autosaveCount;
	[Export] Label autosaveLabel;
	[Export] Control autosaveRow;
	[Export] Civ3TextureButton close;
	[Export] TextureRect background;

	private AudioManager _audioManager;

	public override void _Ready() {
		_audioManager = GlobalAudio.manager;

		// Match the advisor screens: the background texture is set on the
		// TextureRect via textureConfigKey, and the title is drawn with the
		// shared helper so the font and spacing match the other full-screen views.
		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "PREFERENCES");

		promptForResearch.ButtonPressed = PreferencesSettings.GetPromptForResearch();
		promptForResearch.Toggled += enabled => {
			PreferencesSettings.SetPromptForResearch(enabled);
		};

		fullscreen.ButtonPressed = PreferencesSettings.GetFullscreen();
		fullscreen.Toggled += PreferencesSettings.SetFullscreen;
		animateFriendly.ButtonPressed = PreferencesSettings.GetAnimateFriendlyUnits();
		animateFriendly.Toggled += PreferencesSettings.SetAnimateFriendlyUnits;
		animateEnemy.ButtonPressed = PreferencesSettings.GetAnimateEnemyUnits();
		animateEnemy.Toggled += PreferencesSettings.SetAnimateEnemyUnits;
		if (autosaveCount != null) {
			int autosaveTurns = PreferencesSettings.GetAutosaveTurns();
			if (autosaveTurns < 0) autosaveTurns = 0;
			if (autosaveTurns > 99) autosaveTurns = 99;
			autosaveCount.Text = autosaveTurns.ToString();
			if (autosaveToggle != null) {
				autosaveToggle.ButtonPressed = autosaveTurns > 0;
			}
			if (autosaveToggle != null) {
				autosaveToggle.Toggled += enabled => {
					if (enabled) {
						if (int.TryParse(autosaveCount.Text, out int t) && t > 0) {
							PreferencesSettings.SetAutosaveTurns(t);
						} else {
							PreferencesSettings.SetAutosaveTurns(5);
							autosaveCount.Text = "5";
						}
					} else {
						PreferencesSettings.SetAutosaveTurns(0);
					}
				};
			}
			autosaveCount.TextChanged += text => {
				if (int.TryParse(text, out int val)) {
					if (val < 0) val = 0;
					if (val > 99) val = 99;
					PreferencesSettings.SetAutosaveTurns(val);
					if (autosaveToggle != null) {
						autosaveToggle.ButtonPressed = val > 0;
					}
				}
			};
		} else if (autosaveRow != null) {
			autosaveRow.Visible = false;
		}

		ConnectVolumeSlider(masterVolumeRow, masterVolume, masterVolumeLabel, AudioManager.MasterBus,
			() => { }); // music is always playing (through master)

		ConnectVolumeSlider(musicVolumeRow, musicVolume, musicVolumeLabel, AudioManager.MusicBus,
			() => { }); // music is always playing

		ConnectVolumeSlider(sfxVolumeRow, sfxVolume, sfxVolumeLabel, AudioManager.SfxAudioBus,
			() => _audioManager.PlaySfxAudio("extra.warrior.victory"));

		ConnectVolumeSlider(uiVolumeRow, uiVolume, uiVolumeLabel, AudioManager.UIAudioBus,
			() => _audioManager.PlayUIAudio("buttons.button_1"));

		ConnectVolumeSlider(ambienceVolumeRow, ambienceVolume, ambienceVolumeLabel, AudioManager.AmbienceAudioBus,
			() => _audioManager.PlayAmbienceAudio("ambience.oriole"));

		TextureLoader.SetButtonTextures(close, "ui.exit");
		close.Pressed += HidePreferences;
		AddCiv3Options();
	}

	// Raised when one of Civ3's preferences changes (the game applies it).
	public static event Action<Civ3Preferences.Option> Changed;

	// Civ3's preferences (Civ3Preferences): the game's and the units' two to
	// a row in their grids, the sound switches with the volumes, and the map
	// scroll speed. Those the game can't act on yet are greyed, saying why.
	private void AddCiv3Options() {
		GridContainer gameGrid = GetNode<GridContainer>("Background/Rows/GameSection/GameGrid");
		GridContainer unitsGrid = GetNode<GridContainer>("Background/Rows/UnitsSection/UnitsGrid");
		VBoxContainer gameSection = GetNode<VBoxContainer>("Background/Rows/GameSection");
		VBoxContainer audio = GetNode<VBoxContainer>("Background/Rows/AudioSection");
		gameGrid.Columns = 2;
		unitsGrid.Columns = 2;
		// (The four animation settings below replace these two.)
		animateFriendly.Visible = false;
		animateEnemy.Visible = false;

		HBoxContainer soundRow = new();
		audio.AddChild(soundRow);
		audio.MoveChild(soundRow, 1);
		foreach (Civ3Preferences.Option option in Civ3Preferences.All) {
			Civ3Checkbox box = new() { Text = option.label, CustomMinimumSize = new Vector2(option.section == Civ3Preferences.Sound ? 200 : 420, 18) };
			box.SetPressed(Civ3Preferences.Get(option.key));
			if (option.unavailable != null) {
				box.Disabled = true;
				box.TooltipText = option.unavailable;
			}
			box.Toggled += on => {
				Civ3Preferences.Set(option.key, on);
				if (option.section == Civ3Preferences.Sound) {
					ApplySound();
				}
				Changed?.Invoke(option);
			};
			(option.section == Civ3Preferences.Game ? gameGrid : option.section == Civ3Preferences.Units ? unitsGrid : (Container)soundRow).AddChild(box);
		}

		HBoxContainer scroll = new();
		Label scrollLabel = new() { Text = "Map Scroll Speed", CustomMinimumSize = new Vector2(140, 0) };
		scrollLabel.AddThemeFontSizeOverride("font_size", 13);
		scroll.AddChild(scrollLabel);
		HSlider speed = new() { MinValue = 25, MaxValue = 200, Step = 5, Value = Civ3Preferences.GetMapScrollSpeed(), CustomMinimumSize = new Vector2(240, 20) };
		Label speedValue = new() { Text = $"{Civ3Preferences.GetMapScrollSpeed()}%", CustomMinimumSize = new Vector2(52, 0) };
		speedValue.AddThemeFontSizeOverride("font_size", 13);
		speed.ValueChanged += value => {
			Civ3Preferences.SetMapScrollSpeed((int)value);
			speedValue.Text = $"{(int)value}%";
		};
		scroll.AddChild(speed);
		scroll.AddChild(speedValue);
		gameSection.AddChild(scroll);
	}

	// Civ3's sound switches: everything, the music, the effects (muting the
	// buses; the volumes stay as they are). Also when the game starts.
	public static void ApplySound() {
		void Mute(string bus, bool mute) {
			int index = AudioServer.GetBusIndex(bus);
			if (index >= 0 && mute) {
				AudioServer.SetBusMute(index, true);
			} else if (index >= 0) {
				AudioServer.SetBusMute(index, AudioServer.GetBusVolumeDb(index) <= -79f);
			}
		}
		Mute(AudioManager.MasterBus, !Civ3Preferences.Get(Civ3Preferences.AllSoundsOn));
		Mute(AudioManager.MusicBus, !Civ3Preferences.Get(Civ3Preferences.MusicOn));
		Mute(AudioManager.SfxAudioBus, !Civ3Preferences.Get(Civ3Preferences.SoundFxOn));
	}

	private void ConnectVolumeSlider(Control row, Civ3HSlider slider, Label valueLabel, string bus,
		Action callback) {
		int volume = _audioManager.GetBusVolume(bus);
		if (volume < 0) {
			row.Visible = false;
			return;
		}

		slider.Value = volume;
		UpdateVolumeLabel(valueLabel, volume);

		slider.ValueChanged += value => {
			UpdateVolumeLabel(valueLabel, (int)value);
			_audioManager.SetBusVolumePercent(bus, (int)value);
		};
		slider.DragEnded += changed => {
			_audioManager.SetBusVolume(bus, (int)slider.Value);
			ApplySound();
			callback?.Invoke();
		};
	}

	private static void UpdateVolumeLabel(Label label, int volume) {
		label.Text = volume == 0 ? "Off" : $"{volume}%";
	}

	public void ShowPreferences() {
		Show();
	}

	public void HidePreferences() {
		Hide();
	}
}
