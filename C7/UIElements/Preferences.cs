using System;
using System.Linq;
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
		LayOutPage();
		AddCiv3Options();
	}

	// Every option the same size, two to a row; the page scrolls inside the
	// parchment's border when it doesn't fit.
	private static readonly Vector2 OptionSize = new(428, 24);
	private const int OptionFontSize = 14;
	private ScrollContainer scroller;
	// The page's sections (moved into the scroller, so found from here).
	private VBoxContainer rows;

	private void LayOutPage() {
		rows = GetNode<VBoxContainer>("Background/Rows");
		scroller = new() {
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			Position = new Vector2(72, 70),
			Size = new Vector2(884, 644),
		};
		background.AddChild(scroller);
		// (Under the close button, which must keep its clicks.)
		background.MoveChild(scroller, rows.GetIndex());
		rows.Reparent(scroller, keepGlobalTransform: false);
		rows.Position = Vector2.Zero;
		rows.CustomMinimumSize = new Vector2(866, 0);
		rows.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		rows.AddThemeConstantOverride("separation", 8);
		foreach (Control section in rows.GetChildren().OfType<Control>()) {
			section.SizeFlagsVertical = SizeFlags.Fill;
		}
		// Rows one height apart, the autosaves' box included.
		foreach (GridContainer grid in new[] { rows.GetNode<GridContainer>("GameSection/GameGrid"), rows.GetNode<GridContainer>("UnitsSection/UnitsGrid") }) {
			grid.AddThemeConstantOverride("v_separation", 0);
		}

		VScrollBar bar = scroller.GetVScrollBar();
		bar.CustomMinimumSize = new Vector2(10, 0);
		bar.AddThemeStyleboxOverride("scroll", new StyleBoxFlat { BgColor = new Color(0.35f, 0.25f, 0.12f, 0.12f) });
		foreach (string grabber in new[] { "grabber", "grabber_highlight", "grabber_pressed" }) {
			bar.AddThemeStyleboxOverride(grabber, new StyleBoxFlat {
				BgColor = new Color(0.35f, 0.25f, 0.12f, grabber == "grabber" ? 0.55f : 0.8f),
				CornerRadiusTopLeft = 4,
				CornerRadiusTopRight = 4,
				CornerRadiusBottomLeft = 4,
				CornerRadiusBottomRight = 4,
			});
		}

		// The research question, full screen and the autosaves: cells of the
		// game's grid like the others (they shared a column of their own).
		GridContainer gameGrid = rows.GetNode<GridContainer>("GameSection/GameGrid");
		Node firstColumn = gameGrid.GetNode("GameFirstCol");
		promptForResearch.Reparent(gameGrid, keepGlobalTransform: false);
		gameGrid.MoveChild(promptForResearch, 0);
		if (autosaveRow != null) {
			autosaveRow.Reparent(gameGrid, keepGlobalTransform: false);
			gameGrid.MoveChild(fullscreen, 1);
			gameGrid.MoveChild(autosaveRow, 2);
			autosaveRow.CustomMinimumSize = OptionSize;
			autosaveRow.AddThemeConstantOverride("separation", 8);
			if (autosaveToggle is Civ3Checkbox toggle) {
				// Its own label, as every other option has.
				toggle.Text = "Number of autosaves";
				toggle.FontSize = OptionFontSize;
				toggle.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
				autosaveLabel.Visible = false;
			}
			// A small box on the parchment, no taller than a checkbox.
			autosaveCount.CustomMinimumSize = new Vector2(34, 18);
			autosaveCount.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			autosaveCount.AddThemeFontSizeOverride("font_size", 12);
			autosaveCount.AddThemeColorOverride("font_color", new Color(0.129412f, 0.0980392f, 0.054902f));
			foreach (string state in new[] { "normal", "focus" }) {
				autosaveCount.AddThemeStyleboxOverride(state, new StyleBoxFlat {
					BgColor = new Color(1, 0.97f, 0.88f, 0.7f),
					BorderColor = new Color(0.35f, 0.25f, 0.12f, state == "focus" ? 0.9f : 0.5f),
					BorderWidthLeft = 1,
					BorderWidthTop = 1,
					BorderWidthRight = 1,
					BorderWidthBottom = 1,
					ContentMarginLeft = 4,
					ContentMarginRight = 4,
					ContentMarginTop = 0,
					ContentMarginBottom = 0,
				});
			}
		}
		gameGrid.RemoveChild(firstColumn);
		firstColumn.QueueFree();
		foreach (Civ3Checkbox box in new[] { promptForResearch, fullscreen }) {
			box.CustomMinimumSize = OptionSize;
			box.FontSize = OptionFontSize;
		}
	}

	// Raised when one of Civ3's preferences changes (the game applies it).
	public static event Action<Civ3Preferences.Option> Changed;

	// Civ3's preferences (Civ3Preferences): the game's and the units' two to
	// a row in their grids, the sound switches with the volumes, and the map
	// scroll speed. Those the game can't act on yet (the Civilopedia cursor,
	// the tutorial, color blind help, fewer multiplayer popups) aren't shown
	// for now.
	private void AddCiv3Options() {
		GridContainer gameGrid = rows.GetNode<GridContainer>("GameSection/GameGrid");
		GridContainer unitsGrid = rows.GetNode<GridContainer>("UnitsSection/UnitsGrid");
		VBoxContainer gameSection = rows.GetNode<VBoxContainer>("GameSection");
		VBoxContainer audio = rows.GetNode<VBoxContainer>("AudioSection");
		gameGrid.Columns = 2;
		unitsGrid.Columns = 2;
		// (The four animation settings below replace these two.)
		animateFriendly.Visible = false;
		animateEnemy.Visible = false;

		HBoxContainer soundRow = new();
		audio.AddChild(soundRow);
		audio.MoveChild(soundRow, 1);
		foreach (Civ3Preferences.Option option in Civ3Preferences.All.Where(o => o.unavailable == null)) {
			Civ3Checkbox box = new() { Text = option.label, CustomMinimumSize = option.section == Civ3Preferences.Sound ? new Vector2(200, OptionSize.Y) : OptionSize };
			box.SetPressed(Civ3Preferences.Get(option.key));
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
		scroll.AddThemeConstantOverride("separation", 12);
		Label scrollLabel = new() { Text = "Map Scroll Speed", CustomMinimumSize = new Vector2(140, 0) };
		scrollLabel.AddThemeFontSizeOverride("font_size", 13);
		scrollLabel.AddThemeColorOverride("font_color", new Color(0.129412f, 0.0980392f, 0.054902f));
		scroll.AddChild(scrollLabel);
		HSlider speed = new() { MinValue = 25, MaxValue = 200, Step = 5, Value = Civ3Preferences.GetMapScrollSpeed(), CustomMinimumSize = new Vector2(240, 20) };
		Label speedValue = new() { Text = $"{Civ3Preferences.GetMapScrollSpeed()}%", CustomMinimumSize = new Vector2(52, 0) };
		speedValue.AddThemeFontSizeOverride("font_size", 13);
		speedValue.AddThemeColorOverride("font_color", new Color(0.129412f, 0.0980392f, 0.054902f));
		speed.ValueChanged += value => {
			Civ3Preferences.SetMapScrollSpeed((int)value);
			speedValue.Text = $"{(int)value}%";
		};
		scroll.AddChild(speed);
		scroll.AddChild(speedValue);
		gameSection.AddChild(scroll);

		// Each option in a cell of one size, so the rows are evenly spaced
		// (a Civ3Checkbox sizes itself to its contents, the autosaves' row
		// is a little taller).
		foreach (GridContainer grid in new[] { gameGrid, unitsGrid }) {
			foreach (Control content in grid.GetChildren().OfType<Control>().Where(c => c.Visible).ToList()) {
				int index = content.GetIndex();
				HBoxContainer cell = new() { CustomMinimumSize = OptionSize };
				grid.AddChild(cell);
				grid.MoveChild(cell, index);
				content.Reparent(cell, keepGlobalTransform: false);
				content.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			}
		}
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
		FitToWindow();
		if (scroller != null) {
			scroller.ScrollVertical = 0;
		}
	}

	// A window smaller than the page (1024 by 768) shows it scaled down to
	// fit, title and close button included. (The whole screen is scaled
	// about its middle: a container would undo a scale on the page.)
	private void FitToWindow() {
		Vector2 window = GetViewportRect().Size;
		Vector2 page = background.Texture.GetSize();
		float scale = Mathf.Min(1, Mathf.Min(window.X / page.X, window.Y / page.Y));
		PivotOffset = Size / 2;
		Scale = new Vector2(scale, scale);
	}

	public override void _Notification(int what) {
		if (what == NotificationResized && IsVisibleInTree()) {
			FitToWindow();
		}
	}

	public void HidePreferences() {
		Hide();
	}
}
