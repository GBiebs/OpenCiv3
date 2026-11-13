using Godot;

// Civ3's Preferences (Ctrl-P, or Preferences on the game menu), with its
// wording. Each choice is saved as it's made and takes effect at once. The
// ones the game has nothing for yet are shown greyed out.
public partial class PreferencesPopup : Popup {
	private const int Width = 920;
	private const int Height = 600;
	private const int FontSize = 13;

	private static readonly Color DisabledModulate = new(1, 1, 1, 0.45f);
	private const string NotAvailable = "Not available yet.";

	public PreferencesPopup() {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins(top: 40);
	}

	public override void _Ready() {
		base._Ready();

		AddTexture(Width, Height);
		AddBackground(Width, Height);
		AddHeader("PREFERENCES", 10);

		HBoxContainer columns = new() { Position = new Vector2(36, 52) };
		columns.AddThemeConstantOverride("separation", 24);
		AddChild(columns);

		VBoxContainer game = AddColumn(columns, 340);
		AddHeading(game, "GAME");
		AddUnavailable(game, "Map Scroll Speed", "Not available yet: the map is moved by dragging it.");
		AddOption(game, "Auto-Save", Preferences.Option.AutoSave,
			"Saves the game as each of your turns begins, in the Auto folder of the saves. The last 10 are kept.");
		AddOption(game, "Capital Governor is Default for New Cities", Preferences.Option.CapitalGovernorIsDefaultForNewCities,
			"The cities you found start with the options of your capital's governor (Contact Governor on a city).");
		AddOption(game, "Always Wait at End of Turn", Preferences.Option.AlwaysWaitAtEndOfTurn,
			"Off, your turn ends by itself once none of your units needs orders and nothing is waiting for an answer.");
		AddUnavailable(game, "Show Civilopedia Book Cursor");
		AddTutorialMode(game);
		AddUnavailable(game, "Show Wonder Initiation Pop-Up");
		AddUnavailable(game, "Show Civil Disorder Pop-Up");
		AddUnavailable(game, "Show Food and Shields on Map");
		AddUnavailable(game, "Show City Pop. Dropshadow");
		AddOption(game, "Ask for Build Orders after Unit Construction", Preferences.Option.AskForBuildOrdersAfterUnitConstruction,
			"Off, a city that finishes a unit goes on building the next without asking.");
		AddUnavailable(game, "Color Blind Help");
		AddUnavailable(game, "Disable Population Limit Warnings");
		AddUnavailable(game, "Always Renegotiate Deals");
		AddOption(game, "Show Advanced Unit Action Buttons", Preferences.Option.ShowAdvancedUnitActionButtons,
			"Shows buttons for the workers' other kinds of automation: Road to, Rail to, Build Trade and the rest.");
		AddUnavailable(game, "Show Fewer Multiplayer Popups");
		AddUnavailable(game, "Do not auto-select units in Multiplayer");

		VBoxContainer units = AddColumn(columns, 300);
		AddHeading(units, "UNITS");
		const string show = "Moves the map to show {0} as they move.";
		const string animate = "Animates {0} as they move, where you can see them.";
		AddOption(units, "Show our Manual Moves", Preferences.Option.ShowOurManualMoves, string.Format(show, "the units you move by hand"));
		AddOption(units, "Show our Automatic Moves", Preferences.Option.ShowOurAutomaticMoves,
			string.Format(show, "your units following orders (going somewhere, automated or exploring)"));
		AddOption(units, "Show Friend Moves", Preferences.Option.ShowFriendMoves, string.Format(show, "the units of civilizations you're not at war with"));
		AddOption(units, "Show Enemy Moves", Preferences.Option.ShowEnemyMoves, string.Format(show, "the units of civilizations you're at war with, and barbarians"));
		AddUnavailable(units, "Show Units over Cities");
		AddUnavailable(units, "Show Team Color Disc");
		AddOption(units, "Animate Battles", Preferences.Option.AnimateBattles, "Animates attacks, bombardment, deaths and victories.");
		AddOption(units, "Animate our Manual Moves", Preferences.Option.AnimateOurManualMoves, string.Format(animate, "the units you move by hand"));
		AddOption(units, "Animate our Automatic Moves", Preferences.Option.AnimateOurAutomaticMoves, string.Format(animate, "your units following orders"));
		AddOption(units, "Animate Friend Moves", Preferences.Option.AnimateFriendMoves, string.Format(animate, "the units of civilizations you're not at war with"));
		AddOption(units, "Animate Enemy Moves", Preferences.Option.AnimateEnemyMoves, string.Format(animate, "the units of civilizations you're at war with, and barbarians"));
		AddUnavailable(units, "Cancel Orders for Friendly Combat Unit");
		AddUnavailable(units, "Cancel Orders for Enemy Combat Unit");
		// The game always does this for now.
		AddUnavailable(units, "Always Build Previously Built Unit", "A city that finishes a unit always goes on building it for now.", pressed: true);

		VBoxContainer audio = AddColumn(columns, 140);
		AddHeading(audio, "AUDIO");
		AddOption(audio, "All Sounds On", Preferences.Option.AllSoundsOn, "Off, the game makes no sound at all.");
		AddOption(audio, "Music On", Preferences.Option.MusicOn, "Plays music. Its volume is on the Settings page of the main menu.");
		AddOption(audio, "Sound FX On", Preferences.Option.SoundFxOn, "Plays sound effects, interface sounds and ambience.");

		Label note = new() {
			Text = "Greyed-out preferences aren't available yet. Caps Lock switches every animation on or off for now; "
				+ "holding Shift does until it's let go of.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(36, Height - 70),
			Size = new Vector2(Width - 120, 40),
		};
		note.AddThemeFontSizeOverride("font_size", 12);
		AddChild(note);

		AddConfirmButton(new Vector2(Width - 66, Height - 66), () => GetParent<PopupOverlay>().OnHidePopup());
	}

	private static VBoxContainer AddColumn(HBoxContainer columns, int width) {
		VBoxContainer column = new() { CustomMinimumSize = new Vector2(width, 0) };
		column.AddThemeConstantOverride("separation", 1);
		columns.AddChild(column);
		return column;
	}

	private static void AddHeading(VBoxContainer column, string text) {
		Label heading = new() { Text = text, CustomMinimumSize = new Vector2(0, 26), VerticalAlignment = VerticalAlignment.Bottom };
		heading.AddThemeFontSizeOverride("font_size", 16);
		column.AddChild(heading);
	}

	private static Civ3Checkbox MakeCheckbox(string text, bool pressed, string tooltip) {
		return new Civ3Checkbox {
			Text = text,
			FontSize = FontSize,
			ToggleMode = true,
			ButtonPressed = pressed,
			TooltipText = tooltip,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
			FocusMode = FocusModeEnum.None,
		};
	}

	private void AddOption(VBoxContainer column, string text, Preferences.Option option, string tooltip) {
		Civ3Checkbox checkbox = MakeCheckbox(text, Preferences.Get(option), tooltip);
		checkbox.Toggled += (bool on) => Preferences.Set(option, on);
		column.AddChild(checkbox);
	}

	// Tutorial Mode belongs to the game being played rather than to this
	// machine: Civ3 saves it with the game and turns it off on going back to
	// the main menu (see C7GameData.TutorialState), and greys it out in a
	// multiplayer game (FUN_00595aa0).
	private static void AddTutorialMode(VBoxContainer column) {
		C7GameData.GameData gameData = C7Engine.EngineStorage.gameData;
		string unavailable = TutorialGuide.Unavailable(gameData);
		if (unavailable != null) {
			AddUnavailable(column, "Tutorial Mode", unavailable);
			return;
		}
		Civ3Checkbox checkbox = MakeCheckbox("Tutorial Mode", gameData.tutorial.enabled,
			"The domestic advisor explains the game as things first happen in it. Each tip is given once a game.");
		checkbox.Toggled += (bool on) => C7Engine.EngineStorage.ReadGameData(data => data.tutorial.enabled = on);
		column.AddChild(checkbox);
	}

	private static void AddUnavailable(VBoxContainer column, string text, string tooltip = NotAvailable, bool pressed = false) {
		Civ3Checkbox checkbox = MakeCheckbox(text, pressed, tooltip);
		checkbox.Disabled = true;
		checkbox.Modulate = DisabledModulate;
		column.AddChild(checkbox);
	}
}
