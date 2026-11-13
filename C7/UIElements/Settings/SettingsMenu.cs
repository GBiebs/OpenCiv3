using System;
using System.Globalization;
using C7.UIElements;
using C7Engine;
using C7Engine.Network;
using Godot;
using Serilog;

// The settings page, opened from the main menu. Settings are saved to C7.ini
// as they're changed.
public partial class SettingsMenu : Control {
	private static readonly ILogger log = Log.ForContext<SettingsMenu>();

	private const string MainMenuScene = "res://UIElements/MainMenu/main_menu.tscn";

	private static readonly Color HeadingColor = new(0.55f, 0.08f, 0f);
	private static readonly Color TextColor = new(0f, 0f, 0f);
	private static readonly Color NoteColor = new(0.22f, 0.2f, 0.16f);
	private static readonly Color DisabledModulate = new(1, 1, 1, 0.5f);

	// The room left above and below the page.
	private const float WindowMargin = 16;

	private AudioManager audioManager;
	private DisplayScale displayScale;

	public override void _Ready() {
		audioManager = GetNodeOrNull<AudioManager>("/root/GlobalAudioManager");
		displayScale = GetNodeOrNull<DisplayScale>("/root/DisplayScale");
		try {
			Build();
		} catch (Exception ex) {
			log.Error(ex, "Could not set up the settings page");
			ShowBuildError(ex);
		}
	}

	// Replaces a page that couldn't be built with what went wrong and a way
	// back, rather than leaving it half built.
	private void ShowBuildError(Exception ex) {
		foreach (Node child in GetChildren()) {
			RemoveChild(child);
			child.QueueFree();
		}
		ColorRect black = new() { Color = Colors.Black };
		black.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(black);

		VBoxContainer box = new();
		box.SetAnchorsPreset(LayoutPreset.Center);
		AddChild(box);
		box.AddChild(new Label { Text = $"The settings couldn't be shown:\n{ex.Message}" });
		Button back = new() { Text = "Back to the menu" };
		back.Pressed += ReturnToMenu;
		box.AddChild(back);
	}

	public override void _UnhandledInput(InputEvent @event) {
		if (@event.IsActionPressed("ui_cancel")) {
			ReturnToMenu();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Build() {
		SetAnchorsPreset(LayoutPreset.FullRect);

		// The same backdrop as the main menu, letterboxed in black.
		ColorRect black = new() { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
		black.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(black);
		TextureRect background = new() {
			Texture = GD.Load<Texture2D>("res://title-screen.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		background.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(background);

		CenterContainer center = new();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		// A parchment panel, so the text reads clearly over the painting.
		PanelContainer panel = new();
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
			BgColor = new Color(0.93f, 0.88f, 0.76f, 0.97f),
			BorderColor = new Color(0.42f, 0.27f, 0.12f),
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ShadowColor = new Color(0, 0, 0, 0.5f),
			ShadowSize = 8,
		});
		center.AddChild(panel);

		// The page scrolls when it's taller than the window.
		ScrollContainer scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		panel.AddChild(scroll);

		MarginContainer margin = new();
		foreach (string side in new[] { "left", "right", "top", "bottom" }) {
			margin.AddThemeConstantOverride($"margin_{side}", 24);
		}
		scroll.AddChild(margin);
		void FitToWindow() {
			Vector2 content = margin.GetCombinedMinimumSize();
			float room = GetViewportRect().Size.Y - 2 * WindowMargin;
			float scrollBar = content.Y > room ? scroll.GetVScrollBar().GetCombinedMinimumSize().X : 0;
			scroll.CustomMinimumSize = new Vector2(content.X + scrollBar, Mathf.Min(content.Y, room));
		}
		Resized += FitToWindow;
		Callable.From(FitToWindow).CallDeferred();

		VBoxContainer column = new() { CustomMinimumSize = new Vector2(560, 0) };
		column.AddThemeConstantOverride("separation", 10);
		margin.AddChild(column);

		column.AddChild(MakeLabel("Settings", 28, HeadingColor, HorizontalAlignment.Center));

		AddGameSettings(column);
		AddGraphicsSettings(column);
		AddWindowSettings(column);
		AddAudioSettings(column);
		AddOnlineSettings(column);
		AddMapGenerationSettings(column);
		AddAdvancedAiSettings(column);
		AddDeveloperSettings(column);

		column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
		Civ3MenuButton back = new() { Text = "Back to Main Menu", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		back.Pressed += ReturnToMenu;
		column.AddChild(back);
	}

	// Standalone mode: playing with the game's own art and rules, without
	// Civ3's files.
	private void AddGameSettings(VBoxContainer column) {
		GlobalSingleton global = GetNodeOrNull<GlobalSingleton>("/root/GlobalSingleton");
		if (global == null) {
			return;
		}
		column.AddChild(MakeHeading("Game"));

		bool civ3Available = GamePaths.Civ3Available();
		Civ3Checkbox standalone = new() {
			Text = "Play without Civ3 (standalone)",
			FontSize = 16,
			ToggleMode = true,
			ButtonPressed = C7Settings.UseStandaloneMode(),
			// Without Civ3's files there's nothing else to play with.
			Disabled = !civ3Available,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
		};
		if (!civ3Available) {
			standalone.Modulate = DisabledModulate;
		}
		standalone.Toggled += (bool on) => {
			PlayClick();
			// The same switch the game makes for a save of the other mode.
			if (on != C7Settings.UseStandaloneMode()) {
				global.ToggleStandaloneMode();
			}
			log.Information("Standalone mode turned {State}", on ? "on" : "off");
		};
		column.AddChild(standalone);
		column.AddChild(MakeNote(civ3Available
			? "Plays with the game's own art and rules. Off, it uses the art, sounds and rules of your " +
				"Civilization III install instead. Takes effect for the next game you start; a saved game loads " +
				"in the mode it was saved in, which switches this too."
			: "No Civilization III install was found, so the game plays with its own art and rules. " +
				"To play with Civ3's instead, install Civilization III."));

		Civ3Checkbox intro = new() {
			Text = "Play the intro movie on launch",
			FontSize = 16,
			ToggleMode = true,
			ButtonPressed = IntroVideo.Enabled,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
		};
		intro.Toggled += (bool on) => {
			PlayClick();
			IntroVideo.SetEnabled(on);
			log.Information("Intro movie turned {State}", on ? "on" : "off");
		};
		column.AddChild(intro);
		column.AddChild(MakeNote(IntroVideo.FindFile() != null
			? "Plays the intro movie when the game starts. Any key or click skips it."
			: "Plays the intro movie when the game starts, once there is an Intro/" + IntroVideo.FileName + " in your OpenCiv3 " +
				"folder. Any key or click skips it."));
	}

	private void AddGraphicsSettings(VBoxContainer column) {
		column.AddChild(MakeHeading("Graphics"));

		Civ3Checkbox modernGraphics = new() {
			Text = "Modern graphics (Graphics Overhaul)",
			FontSize = 16,
			ToggleMode = true,
			ButtonPressed = ModernGraphics.Enabled,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
		};
		modernGraphics.Toggled += (bool on) => {
			PlayClick();
			ModernGraphics.SetEnabled(on);
			log.Information("Modern graphics turned {State}", on ? "on" : "off");
		};
		column.AddChild(modernGraphics);
		column.AddChild(MakeNote(
			"Uses the remade, higher resolution map art: terrain, rivers, roads, mines, cities, resources " +
			"and units, redrawn with a modern look in the spirit of the originals. Art that hasn't been " +
			"remade, and the interface, keep the original look. Takes effect for the next game you start or load."));

		if (displayScale != null) {
			Window root = GetTree().Root;
			Label value = MakeLabel(FormatScale(root.ContentScaleFactor), 14, TextColor, HorizontalAlignment.Right);
			value.CustomMinimumSize = new Vector2(60, 0);
			Civ3HSlider slider = MakeSlider(DisplayScale.Min, DisplayScale.Max, DisplayScale.Step, root.ContentScaleFactor);

			// Rescaling the window while the slider is dragged would move the
			// slider out from under the mouse, so the scale is applied once it's
			// let go of.
			bool dragging = false;
			slider.DragStarted += () => dragging = true;
			slider.DragEnded += (bool changed) => {
				dragging = false;
				displayScale.SetScale((float)slider.Value);
			};
			slider.ValueChanged += (double v) => {
				value.Text = FormatScale((float)v);
				if (!dragging) {
					displayScale.SetScale((float)v);
				}
			};
			column.AddChild(MakeRow("Interface scale", slider, value));
			column.AddChild(MakeNote("Ctrl + and Ctrl - also change the scale, and Ctrl 0 fits it to the screen."));
		}
	}

	// How the game's window shows, and the map's scrolling at its edges.
	private void AddWindowSettings(VBoxContainer column) {
		column.AddChild(MakeHeading("Window"));

		if (displayScale != null) {
			HBoxContainer modes = new();
			modes.AddThemeConstantOverride("separation", 24);
			ButtonGroup group = new();
			foreach ((string name, bool fullscreen) in new[] { ("Windowed", false), ("Fullscreen", true) }) {
				Civ3Checkbox mode = new() {
					Text = name,
					FontSize = 16,
					ToggleMode = true,
					ButtonGroup = group,
					ButtonPressed = DisplayScale.Fullscreen == fullscreen,
					// Its label is drawn over it rather than sized into it, so
					// it's given the room for the text.
					CustomMinimumSize = new Vector2(130, 0),
					SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
				};
				mode.Toggled += (bool on) => {
					if (!on || DisplayScale.Fullscreen == fullscreen) {
						return;
					}
					PlayClick();
					displayScale.SetFullscreen(fullscreen);
					log.Information("Display mode set to {Mode}", name);
				};
				modes.AddChild(mode);
			}
			column.AddChild(MakeRow("Display mode", modes, new Control()));
			column.AddChild(MakeNote("Fullscreen fills the screen, with no window frame or taskbar. Windowed keeps the " +
				"window maximized on the desktop."));
		}

		Civ3Checkbox edgeScrolling = new() {
			Text = "Edge scrolling",
			FontSize = 16,
			ToggleMode = true,
			ButtonPressed = EdgeScrolling.Enabled,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
		};
		edgeScrolling.Toggled += (bool on) => {
			PlayClick();
			EdgeScrolling.SetEnabled(on);
			log.Information("Edge scrolling turned {State}", on ? "on" : "off");
		};
		column.AddChild(edgeScrolling);
		column.AddChild(MakeNote("Scrolls the map while the mouse rests at an edge of the window. Dragging the map " +
			"works either way."));
	}

	private void AddAudioSettings(VBoxContainer column) {
		if (audioManager == null) {
			return;
		}
		column.AddChild(MakeHeading("Audio"));

		foreach ((string key, string bus, string name) in AudioManager.VolumeSettings) {
			int volume = AudioManager.GetVolume(key);
			Label value = MakeLabel($"{volume}%", 14, TextColor, HorizontalAlignment.Right);
			value.CustomMinimumSize = new Vector2(60, 0);
			Civ3HSlider slider = MakeSlider(0, 100, 5, volume);
			slider.ValueChanged += (double v) => {
				value.Text = $"{(int)v}%";
				audioManager.SetVolume(key, bus, (int)v);
			};
			// Let the player hear the new interface volume.
			if (bus == AudioManager.UIAudioBus) {
				slider.DragEnded += (bool changed) => PlayClick();
			}
			column.AddChild(MakeRow(name, slider, value));
		}
	}

	private static string FormatScale(float scale) {
		return scale.ToString("0.00", CultureInfo.InvariantCulture) + "x";
	}

	// The relay server that games hosted and joined online go through.
	private void AddOnlineSettings(VBoxContainer column) {
		column.AddChild(MakeHeading("Online"));

		LineEdit relay = new() {
			Text = OnlineRelay.SameRelay(OnlineRelay.Url, OnlineRelay.DefaultUrl) ? "" : OnlineRelay.Url,
			PlaceholderText = "relay.example.org",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		Label result = MakeLabel("", 14, TextColor, HorizontalAlignment.Left);
		result.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		result.CustomMinimumSize = new Vector2(540, 0);

		void Save() {
			if (!string.IsNullOrWhiteSpace(relay.Text) && OnlineRelay.Problem(relay.Text) is string problem) {
				result.Text = problem;
				return;
			}
			OnlineRelay.SetUrl(relay.Text);
			log.Information("Online relay set to {Url}", OnlineRelay.Url);
			result.Text = string.IsNullOrWhiteSpace(relay.Text) ? "Using the default relay server." : "Saved.";
		}
		relay.TextSubmitted += _ => Save();
		relay.FocusExited += Save;

		Civ3MenuButton test = new() { Text = "Test", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		test.Pressed += async () => {
			// An async void handler's exceptions would otherwise go unseen.
			try {
				PlayClick();
				Save();
				string url = OnlineRelay.Url;
				result.Text = "Checking...";
				string problem = await OnlineRelay.CheckAsync(url);
				if (IsInstanceValid(result)) {
					result.Text = problem ?? "The relay server answered. Games can be hosted and joined online through it.";
				}
			} catch (Exception ex) {
				log.Warning(ex, "Couldn't check the relay server");
				if (IsInstanceValid(result)) {
					result.Text = $"Couldn't check the relay server: {ex.Message}";
				}
			}
		};
		column.AddChild(MakeRow("Relay server", relay, test));
		column.AddChild(result);
		column.AddChild(MakeNote(
			"Games hosted online go through this server, which gives each a join code. A host can also choose " +
			"one when hosting; players joining that host's game are told it along with the code. Leave it blank " +
			"for the default."));
	}

	private void AddMapGenerationSettings(VBoxContainer column) {
		column.AddChild(MakeHeading("Map Generation"));

		Civ3Checkbox saveInvalidMaps = new() {
			Text = "Save invalid maps",
			FontSize = 16,
			ToggleMode = true,
			ButtonPressed = InvalidMaps.Enabled,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
		};
		saveInvalidMaps.Toggled += (bool on) => {
			PlayClick();
			InvalidMaps.SetEnabled(on);
			log.Information("Saving invalid maps turned {State}", on ? "on" : "off");
		};
		column.AddChild(saveInvalidMaps);
		column.AddChild(MakeNote(
			"When a new map has no room for every civilization to start far enough apart, another is generated. " +
			"With this on, each map turned down is saved to the Invalid Maps folder, with a note saying why, " +
			"so it can be loaded and looked at."));
	}

	// The Advanced AI's model planner: an Anthropic API key and the model.
	private void AddAdvancedAiSettings(VBoxContainer column) {
		column.AddChild(MakeHeading("Advanced AI"));

		Civ3Checkbox review = new() {
			Text = "Model review (experimental)",
			FontSize = 16,
			ToggleMode = true,
			ButtonPressed = AdvancedAiSettings.ModelReview,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
		};
		review.Toggled += (bool on) => {
			PlayClick();
			AdvancedAiSettings.ModelReview = on;
			log.Information("Advanced AI model review turned {State}", on ? "on" : "off");
		};
		column.AddChild(review);

		LineEdit key = new() {
			Text = AdvancedAiSettings.ApiKey,
			Secret = true,
			PlaceholderText = "Anthropic API key (or set ANTHROPIC_API_KEY)",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		void SaveKey() {
			if (key.Text.Trim() != AdvancedAiSettings.ApiKey) {
				AdvancedAiSettings.ApiKey = key.Text;
				log.Information("Advanced AI API key {State}", string.IsNullOrWhiteSpace(key.Text) ? "cleared" : "set");
			}
		}
		key.TextSubmitted += _ => SaveKey();
		key.FocusExited += SaveKey;
		column.AddChild(MakeRow("API key", key, new Control()));

		LineEdit model = new() {
			Text = AdvancedAiSettings.Model,
			PlaceholderText = C7Engine.AI.Advanced.ModelPlanner.DefaultModel,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		void SaveModel() {
			if (model.Text.Trim() != AdvancedAiSettings.Model) {
				AdvancedAiSettings.Model = model.Text;
				model.Text = AdvancedAiSettings.Model;
			}
		}
		model.TextSubmitted += _ => SaveModel();
		model.FocusExited += SaveModel;
		column.AddChild(MakeRow("Model", model, new Control()));

		column.AddChild(MakeNote(
			"Off by default: trials haven't shown that Claude's advice helps the Advanced AI, so a key alone does nothing. " +
			"With Model review on, every 50 turns each computer player set to Advanced AI asks Claude to review how it " +
			"plays, using this key, and keeps playing while it waits. What the reviews learn is kept in AdvancedAiPlaybook.jsonl for later " +
			"games. Each call is billed to the key (about 8 a game per Advanced player). With it off or no key, Advanced players " +
			"play by their built-in rules. The key is saved in C7.ini as plain text. Takes effect for the next game you " +
			"start or load."));
	}

	// Options for testing the game.
	private void AddDeveloperSettings(VBoxContainer column) {
		column.AddChild(MakeHeading("Developer"));

		Civ3Checkbox aiFogOfWar = new() {
			Text = "AI Fog of War",
			FontSize = 16,
			ToggleMode = true,
			ButtonPressed = DeveloperSettings.AIFogOfWar,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
		};
		aiFogOfWar.Toggled += (bool on) => {
			PlayClick();
			DeveloperSettings.AIFogOfWar = on;
			log.Information("AI fog of war turned {State}", on ? "on" : "off");
		};
		column.AddChild(aiFogOfWar);
		column.AddChild(MakeNote(
			"For testing the AI. Off, the AI sees the whole map. On, it plans only with what it has explored " +
			"and the units it can see now, as you must. Takes effect for the next game you start or load; " +
			"in a network game, the host's setting counts."));

		Civ3Checkbox recordPlayLog = new() {
			Text = "Record play log",
			FontSize = 16,
			ToggleMode = true,
			ButtonPressed = DeveloperSettings.RecordPlayLog,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
		};
		recordPlayLog.Toggled += (bool on) => {
			PlayClick();
			DeveloperSettings.RecordPlayLog = on;
			log.Information("Play log recording turned {State}", on ? "on" : "off");
		};
		column.AddChild(recordPlayLog);
		column.AddChild(MakeNote(
			"For tuning the AI. On, each game you start or load writes a log of what you do each turn, " +
			"with what the Advanced AI would have done instead, to " + DeveloperSettings.PlayLogDirectory + ". " +
			"Summarize it with tools/play-log/summarize.py. Takes effect for the next game you start or load; " +
			"in a network game, the host records every human's play."));
	}

	private static Civ3HSlider MakeSlider(double min, double max, double step, double value) {
		Civ3HSlider slider = new() {
			MinValue = min,
			MaxValue = max,
			Step = step,
			Value = value,
			CustomMinimumSize = new Vector2(260, 24),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
		};
		return slider;
	}

	private static HBoxContainer MakeRow(string name, Control control, Control value) {
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 12);
		Label label = MakeLabel(name, 16, TextColor, HorizontalAlignment.Left);
		label.CustomMinimumSize = new Vector2(170, 0);
		row.AddChild(label);
		row.AddChild(control);
		row.AddChild(value);
		return row;
	}

	private static Label MakeHeading(string text) {
		Label heading = MakeLabel(text, 20, HeadingColor, HorizontalAlignment.Left);
		heading.CustomMinimumSize = new Vector2(0, 34);
		heading.VerticalAlignment = VerticalAlignment.Bottom;
		return heading;
	}

	private static Label MakeNote(string text) {
		Label note = MakeLabel(text, 12, NoteColor, HorizontalAlignment.Left);
		note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		note.CustomMinimumSize = new Vector2(540, 0);
		return note;
	}

	private static Label MakeLabel(string text, int size, Color color, HorizontalAlignment alignment) {
		Label label = new() {
			Text = text,
			HorizontalAlignment = alignment,
			VerticalAlignment = VerticalAlignment.Center,
		};
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private void PlayClick() {
		audioManager?.PlayUIAudio("buttons.button_1");
	}

	private void ReturnToMenu() {
		PlayClick();
		GetTree().ChangeSceneToFile(MainMenuScene);
	}
}
