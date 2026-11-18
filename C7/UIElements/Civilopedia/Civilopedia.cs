using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using C7Engine;
using C7GameData;
using Godot;
using Serilog;
using Resource = C7GameData.Resource;

// Civ3's Civilopedia: the reference to the game's rules. It opens from the
// book in the upper left, with Ctrl-C, and from right-clicking things like
// an advance in the science advisor, and is drawn over the map and the other
// screens, as in Civ3.
//
// Its pages are laid out on the backgrounds in Art/Civilopedia, one for each
// kind of entry, with the entry's text from Text/Civilopedia.txt and its
// statistics from the game's rules. Links in the text, and the icons of the
// advances, units and so on an entry needs or allows, lead to their pages.
public partial class Civilopedia : CenterContainer {
	private static readonly ILogger log = Log.ForContext<Civilopedia>();

	public static Civilopedia Instance { get; private set; }

	private const int Width = 1024;
	private const int Height = 768;

	// Where an entry's text goes, between the panel's edges.
	private const float TextLeft = 222;
	private const float TextWidth = 440;

	// A page of the Civilopedia: the main menu, a section's list of entries,
	// or an entry, showing either its effects or its description.
	private readonly record struct Page(CivilopediaSection Section, string Key, bool Description) {
		public bool IsEntry => Key != null;
		public bool IsMain => Key == null && Section == CivilopediaSection.Main;
	}

	private Control window;
	private TextureRect background;
	private Control pageLayer;
	private TextureRect nameBar;
	private Label nameLabel;
	private TextureRect largeIcon;
	private TextureButton backButton, forwardButton, upButton, previousButton, nextButton, descriptionButton, closeButton;
	private Label descriptionLabel;
	private readonly List<Button> letterButtons = new();

	// What the pages were drawn from, rebuilt when the game changes.
	private CivilopediaCatalog catalog;
	private GameData catalogGameData;
	private static CivilopediaText text;
	private static string textPath;
	private static PediaIcons pediaIcons;
	private static string pediaIconsPath;
	private int playerEra;
	private int playerCultureGroup;
	private Civilization playerCivilization;

	private readonly List<Page> history = new();
	private int historyIndex = -1;
	private Page current = new(CivilopediaSection.Main, null, false);

	// The list on a section's page, so the letters can scroll it.
	private ScrollContainer listScroll;
	private readonly List<(string name, Control row)> listRows = new();

	public Civilopedia() {
		Name = "Civilopedia";
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsPreset(LayoutPreset.FullRect);
		Visible = false;
	}

	public override void _EnterTree() {
		Instance = this;
	}

	public override void _ExitTree() {
		if (Instance == this) {
			Instance = null;
		}
	}

	public override void _Ready() {
		CreateChrome();
	}

	// Opens the Civilopedia at its main menu, or where it was last closed.
	public static void Open() {
		Instance?.OpenAt(null);
	}

	// Opens the Civilopedia at the entry for a tech, unit, building,
	// government, terrain type, resource, worker job or civilization.
	// Returns whether it has one.
	public static bool OpenFor(object subject) {
		if (Instance == null || subject == null) {
			return false;
		}
		Instance.EnsureCatalog();
		CivilopediaEntry entry = Instance.catalog?.For(subject);
		if (entry == null) {
			return false;
		}
		Instance.OpenAt(new Page(entry.Section, entry.Key, false));
		return true;
	}

	// Opens the Civilopedia at the entry with this key, like PRTO_Warrior.
	public static bool OpenKey(string key) {
		if (Instance == null) {
			return false;
		}
		Instance.EnsureCatalog();
		CivilopediaEntry entry = Instance.catalog?.Get(key);
		if (entry == null) {
			return false;
		}
		Instance.OpenAt(new Page(entry.Section, entry.Key, false));
		return true;
	}

	// Opens the Civilopedia at a section's list, or the main menu.
	public static void OpenSection(CivilopediaSection section) {
		Instance?.OpenAt(new Page(section, null, false));
	}

	// Shows the description of the entry on show, if it has one.
	public static void ShowDescription() {
		if (Instance != null && Instance.current.IsEntry && !Instance.current.Description) {
			Instance.ToggleDescription();
		}
	}

	public static bool IsOpen => Instance != null && Instance.Visible;

	public static void Close() {
		Instance?.Hide();
	}

	// Forgets the game the pages were drawn from, e.g. after a LAN snapshot
	// replaced it.
	public void ForgetGameObjects() {
		catalog = null;
		catalogGameData = null;
		history.Clear();
		historyIndex = -1;
		current = new Page(CivilopediaSection.Main, null, false);
		if (Visible) {
			Hide();
		}
	}

	private void OpenAt(Page? page) {
		EnsureCatalog();
		if (catalog == null) {
			return;
		}
		if (page is Page p) {
			Navigate(p);
		} else if (historyIndex < 0) {
			Navigate(new Page(CivilopediaSection.Main, null, false));
		} else {
			DrawPage(current);
		}
		Show();
		GrabFocus();
	}

	public override void _UnhandledInput(InputEvent @event) {
		if (!Visible) {
			return;
		}
		if (@event is InputEventKey { Pressed: true } key) {
			// The advisor and game view keys switch to those screens.
			if (key.Keycode is >= Key.F1 and <= Key.F11) {
				Hide();
				return;
			}
			bool handled = true;
			switch (key.Keycode) {
				case Key.Left when !key.AltPressed:
					GoToNeighbor(-1);
					break;
				case Key.Right when !key.AltPressed:
					GoToNeighbor(1);
					break;
				case Key.Up:
					GoUp();
					break;
				case Key.Backspace:
				case Key.Left when key.AltPressed:
					GoBack();
					break;
				case Key.Right when key.AltPressed:
					GoForward();
					break;
				case Key.Escape:
					// Game closes the Civilopedia on Escape.
					handled = false;
					break;
				default:
					if (key.Keycode is >= Key.A and <= Key.Z && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed) {
						GoToLetter((char)('A' + (key.Keycode - Key.A)));
					} else {
						handled = false;
					}
					break;
			}
			if (handled) {
				GetViewport().SetInputAsHandled();
			}
		} else if (@event is InputEventMouseButton { Pressed: true } button) {
			// The mouse's back and forward buttons go through the history.
			if (button.ButtonIndex == MouseButton.Xbutton1) {
				GoBack();
				GetViewport().SetInputAsHandled();
			} else if (button.ButtonIndex == MouseButton.Xbutton2) {
				GoForward();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	// ---- The game's entries and art ----

	private void EnsureCatalog() {
		GameData gameData = EngineStorage.gameData;
		if (gameData == null) {
			catalog = null;
			return;
		}
		EngineStorage.ReadGameData((GameData gd) => {
			LoadLegacyFiles();
			if (catalog == null || !ReferenceEquals(gd, catalogGameData)) {
				// The scenario's own articles over the game's.
				catalog = CivilopediaCatalog.Build(gd, text?.With(gd.rules?.CivilopediaEntries));
				catalogGameData = gd;
				history.Clear();
				historyIndex = -1;
			}
			Player player = gd.GetUIControllerPlayer();
			playerEra = player?.EraIndex() ?? 0;
			playerCultureGroup = player?.civilization?.cultureGroup?.index ?? 0;
			playerCivilization = player?.civilization;
		});
	}

	// Reads Civilopedia.txt and PediaIcons.txt, again only if the game's
	// scenario, or a switch to or from standalone mode, means others.
	private static void LoadLegacyFiles() {
		string newTextPath = MediaPath("Text/Civilopedia.txt");
		if (text == null || newTextPath != textPath) {
			text = CivilopediaText.Load(newTextPath);
			textPath = newTextPath;
		}
		string newIconsPath = MediaPath("Text/PediaIcons.txt");
		if (newIconsPath != pediaIconsPath) {
			pediaIconsPath = newIconsPath;
			pediaIcons = null;
			if (newIconsPath != null) {
				try {
					pediaIcons = new PediaIcons(newIconsPath);
				} catch (Exception e) {
					log.Warning(e, "Could not read {path}", newIconsPath);
				}
			}
		}
	}

	private static string MediaPath(string relative) {
		return CivilopediaArt.FindFile(relative)?.Path;
	}

	private Texture2D EntryIcon(CivilopediaEntry entry, bool large) {
		return CivilopediaArt.EntryIcon(entry, pediaIcons, catalog, large, playerEra, playerCultureGroup);
	}

	// ---- The parts every page has ----

	private void CreateChrome() {
		window = new Control {
			CustomMinimumSize = new Vector2(Width, Height),
			Size = new Vector2(Width, Height),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		AddChild(window);

		background = new TextureRect {
			Size = new Vector2(Width, Height),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		window.AddChild(background);

		// A plain frame, for when the art is missing.
		if (CivilopediaArt.Load(CivilopediaArt.Folder + "civilopediaBackground.pcx") == null) {
			window.AddChild(new ColorRect { Color = new Color("c8c0a0"), Position = new Vector2(173, 12), Size = new Vector2(676, 734), MouseFilter = MouseFilterEnum.Ignore });
			window.AddChild(new ColorRect { Color = new Color("f2e8c8"), Position = new Vector2(199, 110), Size = new Vector2(470, 602), MouseFilter = MouseFilterEnum.Ignore });
			window.AddChild(new ColorRect { Color = new Color("f6f2e6"), Position = new Vector2(695, 327), Size = new Vector2(129, 327), MouseFilter = MouseFilterEnum.Ignore });
		}

		pageLayer = new Control { Size = new Vector2(Width, Height), MouseFilter = MouseFilterEnum.Ignore };
		window.AddChild(pageLayer);

		Label title = MakeLabel("CIVILOPEDIA", 24, Colors.Black);
		title.Position = new Vector2(198, 26);
		title.Size = new Vector2(192, 34);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		window.AddChild(title);

		backButton = MakeArrowButton(CivilopediaArt.Folder + "fwdBackarrows.pcx", 37, 69, 101, 1, 15, 24, new Vector2(412, 33), "Back");
		backButton.Pressed += GoBack;
		forwardButton = MakeArrowButton(CivilopediaArt.Folder + "fwdBackarrows.pcx", 53, 85, 117, 1, 15, 24, new Vector2(440, 33), "Forward");
		forwardButton.Pressed += GoForward;

		nameBar = new TextureRect { Position = new Vector2(199, 70), MouseFilter = MouseFilterEnum.Ignore };
		window.AddChild(nameBar);
		nameLabel = MakeLabel("", 18, Colors.Black);
		nameLabel.Position = new Vector2(222, 70);
		nameLabel.Size = new Vector2(470, 30);
		nameLabel.ClipText = true;
		window.AddChild(nameLabel);

		largeIcon = new TextureRect {
			Position = new Vector2(695, 59),
			Size = new Vector2(128, 128),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		window.AddChild(largeIcon);

		descriptionButton = new TextureButton {
			Position = new Vector2(707, 233),
			Size = new Vector2(102, 17),
			TooltipText = "",
		};
		descriptionButton.TextureNormal = CivilopediaArt.Crop(CivilopediaArt.Folder + "descbox.pcx", 1, 1, 102, 17);
		descriptionButton.TextureHover = CivilopediaArt.Crop(CivilopediaArt.Folder + "descbox.pcx", 104, 1, 102, 17);
		descriptionButton.TexturePressed = CivilopediaArt.Crop(CivilopediaArt.Folder + "descbox.pcx", 207, 1, 102, 17);
		descriptionButton.IgnoreTextureSize = true;
		descriptionButton.Pressed += ToggleDescription;
		window.AddChild(descriptionButton);
		descriptionLabel = MakeLabel("Description", 12, Colors.Black);
		descriptionLabel.Size = new Vector2(102, 17);
		descriptionLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descriptionLabel.VerticalAlignment = VerticalAlignment.Center;
		descriptionLabel.MouseFilter = MouseFilterEnum.Ignore;
		descriptionButton.AddChild(descriptionLabel);

		string arrows = CivilopediaArt.Folder + "civilo_Up_Next_Back_ARROWS.pcx";
		upButton = MakeArrowButton(arrows, 1, 26, 51, 27, 24, 15, new Vector2(748, 343), "Up");
		upButton.Pressed += GoUp;
		previousButton = MakeArrowButton(arrows, 1, 33, 65, 1, 15, 24, new Vector2(727, 362), "Previous");
		previousButton.Pressed += () => GoToNeighbor(-1);
		nextButton = MakeArrowButton(arrows, 17, 49, 81, 1, 15, 24, new Vector2(777, 362), "Next");
		nextButton.Pressed += () => GoToNeighbor(1);

		for (int i = 0; i < 26; i++) {
			char letter = (char)('A' + i);
			Button button = MakeFlatButton(letter.ToString(), 14);
			button.Position = new Vector2(720 + (i % 4) * 20, 400 + (i / 4) * 32);
			button.Size = new Vector2(20, 22);
			button.Pressed += () => GoToLetter(letter);
			window.AddChild(button);
			letterButtons.Add(button);
		}

		closeButton = new TextureButton {
			Position = new Vector2(728, 684),
			Size = new Vector2(62, 34),
			IgnoreTextureSize = true,
			TooltipText = "Close",
		};
		closeButton.Pressed += Hide;
		window.AddChild(closeButton);
		Label x = MakeLabel("X", 22, new Color("5a6e52"));
		x.Size = closeButton.Size;
		x.HorizontalAlignment = HorizontalAlignment.Center;
		x.VerticalAlignment = VerticalAlignment.Center;
		x.MouseFilter = MouseFilterEnum.Ignore;
		closeButton.AddChild(x);
		closeButton.MouseEntered += () => x.AddThemeColorOverride("font_color", new Color("a85a20"));
		closeButton.MouseExited += () => x.AddThemeColorOverride("font_color", new Color("5a6e52"));
	}

	// A button from a sprite sheet with normal, hover and disabled images
	// side by side; the arrows are blue when they have nowhere to go.
	private TextureButton MakeArrowButton(string sheet, int normalX, int hoverX, int disabledX, int y, int width, int height, Vector2 position, string tooltip) {
		TextureButton button = new() {
			Position = position,
			Size = new Vector2(width, height),
			IgnoreTextureSize = true,
			TooltipText = tooltip,
			TextureNormal = CivilopediaArt.Crop(sheet, normalX, y, width, height),
			TextureHover = CivilopediaArt.Crop(sheet, hoverX, y, width, height),
			TexturePressed = CivilopediaArt.Crop(sheet, hoverX, y, width, height),
			TextureDisabled = CivilopediaArt.Crop(sheet, disabledX, y, width, height),
		};
		if (button.TextureNormal == null) {
			// No art: a text button instead.
			Button text = MakeFlatButton(tooltip switch { "Back" => "<", "Forward" => ">", "Up" => "^", "Previous" => "<", _ => ">" }, 16);
			text.Size = button.Size;
			text.MouseFilter = MouseFilterEnum.Ignore;
			button.AddChild(text);
		}
		window.AddChild(button);
		return button;
	}

	private static Label MakeLabel(string s, int fontSize, Color color, bool bold = false) {
		Label label = new() { Text = s, MouseFilter = MouseFilterEnum.Ignore };
		label.AddThemeFontOverride("font", bold ? CivilopediaRichText.Fonts.Bold : CivilopediaRichText.Fonts.Regular);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private static Button MakeFlatButton(string s, int fontSize) {
		Button button = new() { Text = s, Flat = true, FocusMode = FocusModeEnum.None };
		button.AddThemeFontOverride("font", CivilopediaRichText.Fonts.Regular);
		button.AddThemeFontSizeOverride("font_size", fontSize);
		button.AddThemeColorOverride("font_color", Colors.Black);
		button.AddThemeColorOverride("font_hover_color", new Color("b4641e"));
		button.AddThemeColorOverride("font_pressed_color", new Color("b4641e"));
		button.AddThemeColorOverride("font_focus_color", Colors.Black);
		button.AddThemeColorOverride("font_disabled_color", new Color("8a8a8a"));
		foreach (string style in new[] { "normal", "hover", "pressed", "focus", "disabled" }) {
			button.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
		}
		return button;
	}

	// ---- Navigation ----

	private void Navigate(Page page) {
		if (historyIndex >= 0 && history[historyIndex] == page) {
			DrawPage(page);
			return;
		}
		// Going somewhere new drops the pages ahead in the history.
		if (historyIndex < history.Count - 1) {
			history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
		}
		history.Add(page);
		historyIndex = history.Count - 1;
		DrawPage(page);
	}

	private void GoBack() {
		if (historyIndex > 0) {
			historyIndex--;
			DrawPage(history[historyIndex]);
		}
	}

	private void GoForward() {
		if (historyIndex < history.Count - 1) {
			historyIndex++;
			DrawPage(history[historyIndex]);
		}
	}

	private void GoToEntry(string key) {
		CivilopediaEntry entry = catalog?.Get(key);
		if (entry != null) {
			Navigate(new Page(entry.Section, entry.Key, false));
		}
	}

	private void GoToEntry(CivilopediaEntry entry) {
		if (entry != null) {
			Navigate(new Page(entry.Section, entry.Key, false));
		}
	}

	private void GoToSection(CivilopediaSection section) {
		Navigate(new Page(section, null, false));
	}

	// Up goes from an entry to its section, and from a section to the main
	// menu.
	private void GoUp() {
		if (current.IsEntry) {
			GoToSection(current.Section);
		} else if (!current.IsMain) {
			GoToSection(CivilopediaSection.Main);
		}
	}

	// The entry before or after this one in its section, or the section
	// before or after this one in the main menu, going round at the ends.
	private void GoToNeighbor(int step) {
		if (current.IsMain) {
			return;
		}
		if (current.IsEntry) {
			IReadOnlyList<CivilopediaEntry> entries = catalog.In(current.Section);
			int index = IndexOfKey(entries, current.Key);
			if (index < 0 || entries.Count == 0) {
				return;
			}
			CivilopediaEntry neighbor = entries[(index + step + entries.Count) % entries.Count];
			Navigate(new Page(current.Section, neighbor.Key, false));
		} else {
			CivilopediaSection[] sections = CivilopediaCatalog.MenuSections;
			int index = Array.IndexOf(sections, current.Section);
			GoToSection(sections[(index + step + sections.Length) % sections.Length]);
		}
	}

	// A letter goes to the first entry of the section starting with it; on
	// the main menu, to the index.
	private void GoToLetter(char letter) {
		if (current.IsMain) {
			Navigate(new Page(CivilopediaSection.Index, null, false));
		}
		if (!current.IsEntry) {
			ScrollListTo(letter);
			return;
		}
		CivilopediaEntry first = catalog.In(current.Section).FirstOrDefault(e => StartsWith(e.Name, letter));
		if (first != null) {
			GoToEntry(first);
		}
	}

	private static bool StartsWith(string name, char letter) {
		return !string.IsNullOrEmpty(name) && char.ToUpperInvariant(name.TrimStart()[0]) == letter;
	}

	private static int IndexOfKey(IReadOnlyList<CivilopediaEntry> entries, string key) {
		for (int i = 0; i < entries.Count; i++) {
			if (string.Equals(entries[i].Key, key, StringComparison.OrdinalIgnoreCase)) {
				return i;
			}
		}
		return -1;
	}

	private void ToggleDescription() {
		if (current.IsEntry) {
			Page toggled = current with { Description = !current.Description };
			// Flipping between the two pages of an entry isn't a new step in
			// the history.
			if (historyIndex >= 0) {
				history[historyIndex] = toggled;
			}
			DrawPage(toggled);
		}
	}

	// ---- Drawing pages ----

	private void DrawPage(Page page) {
		current = page;
		ClearPage();

		CivilopediaEntry entry = page.IsEntry ? catalog.Get(page.Key) : null;
		CivilopediaSection section = entry?.Section ?? page.Section;
		CivilopediaArt.BarColor bar = CivilopediaArt.BarColorOf(section);
		nameBar.Texture = CivilopediaArt.Bar(bar);
		nameLabel.AddThemeColorOverride("font_color", CivilopediaArt.BarTextColor(bar));
		nameLabel.Text = entry?.Name ?? CivilopediaCatalog.SectionName(section);
		largeIcon.Texture = entry != null ? EntryIcon(entry, large: true) : CivilopediaArt.SectionIcon(section, large: true);

		backButton.Disabled = historyIndex <= 0;
		forwardButton.Disabled = historyIndex >= history.Count - 1;
		upButton.Disabled = page.IsMain;
		previousButton.Disabled = page.IsMain;
		nextButton.Disabled = page.IsMain;

		bool hasDescription = entry?.Article?.HasDescription ?? false;
		descriptionButton.Visible = hasDescription;
		descriptionLabel.Text = page.Description ? "Effects" : "Description";

		char? letter = entry != null && !string.IsNullOrEmpty(entry.Name) ? char.ToUpperInvariant(entry.Name[0]) : null;
		for (int i = 0; i < letterButtons.Count; i++) {
			bool isCurrent = letter == (char)('A' + i);
			letterButtons[i].AddThemeColorOverride("font_color", isCurrent ? new Color("3a78c8") : Colors.Black);
		}

		if (entry == null) {
			if (page.IsMain) {
				DrawMain();
			} else {
				DrawSection(section);
			}
			return;
		}
		if (page.Description && hasDescription) {
			SetBackground("civilopediaBackground.pcx");
			AddText(entry.Article.DescriptionLines, new Rect2(TextLeft, 134, TextWidth, 568));
			return;
		}

		if (entry.Tech != null) {
			DrawTech(entry);
		} else if (entry.Unit != null) {
			DrawUnit(entry);
		} else if (entry.Building != null) {
			DrawBuilding(entry);
		} else if (entry.Government != null) {
			DrawGovernment(entry);
		} else if (entry.Resource != null) {
			DrawResource(entry);
		} else if (entry.Terrain != null) {
			DrawTerrain(entry);
		} else if (entry.Terraform != null) {
			DrawWorkerAction(entry);
		} else if (entry.Civilization != null) {
			DrawCivilization(entry);
		} else {
			SetBackground("civilopediaBackground.pcx");
			AddEntryText(entry, 118, 702);
		}
	}

	private void ClearPage() {
		foreach (Node child in pageLayer.GetChildren()) {
			pageLayer.RemoveChild(child);
			child.QueueFree();
		}
		listScroll = null;
		listRows.Clear();
	}

	private void SetBackground(string file) {
		Texture2D texture = CivilopediaArt.Load(CivilopediaArt.Folder + file);
		if (texture != null) {
			background.Texture = texture;
		}
	}

	// Covers part of a page that has nothing to show, with one of the
	// pieces of art for that.
	private void Cover(string file) {
		Texture2D texture = CivilopediaArt.Load(CivilopediaArt.Folder + file);
		if (texture != null) {
			pageLayer.AddChild(new TextureRect { Texture = texture, Size = new Vector2(Width, Height), MouseFilter = MouseFilterEnum.Ignore });
		}
	}

	private RichTextLabel AddText(IReadOnlyList<string> lines, Rect2 area, int fontSize = 13) {
		RichTextLabel label = CivilopediaRichText.MakeLabel(fontSize);
		label.Position = area.Position;
		label.Size = area.Size;
		label.Text = CivilopediaRichText.ToBBCode(lines, key => catalog.Get(key) != null);
		label.MetaClicked += meta => GoToEntry(meta.AsString());
		pageLayer.AddChild(label);
		return label;
	}

	// The entry's text from Civilopedia.txt, or what can be said without it.
	private RichTextLabel AddEntryText(CivilopediaEntry entry, float top, float bottom) {
		IReadOnlyList<string> lines = entry.Article?.Lines;
		if (lines == null || lines.Count == 0) {
			lines = new List<string> { "[" + entry.Name + "]" };
		}
		// Civ3's articles start a little way down their panel.
		top += bottom - top > 200 ? 16 : 6;
		return AddText(lines, new Rect2(TextLeft, top, TextWidth, bottom - top));
	}

	// The white tab at the top left of a panel, with its heading.
	private void AddTabLabel(string heading, float x, float y) {
		Label label = MakeLabel(heading, 13, Colors.Black);
		label.Position = new Vector2(x + 16, y - 2);
		pageLayer.AddChild(label);
	}

	private Label AddLabel(string s, Vector2 position, int fontSize = 13, bool bold = false) {
		Label label = MakeLabel(s, fontSize, CivilopediaRichText.TextColor, bold);
		label.Position = position;
		pageLayer.AddChild(label);
		return label;
	}

	// A line of text with links in it, like "Upgrades To: Longbowman".
	private RichTextLabel AddRichLine(string bbcode, Vector2 position, float width, int fontSize = 13, bool center = false) {
		RichTextLabel label = CivilopediaRichText.MakeLabel(fontSize);
		label.ScrollActive = false;
		label.FitContent = true;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.Position = position;
		label.Size = new Vector2(width, 18);
		label.Text = center ? $"[center]{bbcode}[/center]" : bbcode;
		label.MetaClicked += meta => GoToEntry(meta.AsString());
		pageLayer.AddChild(label);
		return label;
	}

	private string Link(CivilopediaEntry entry) {
		if (entry == null) {
			return "";
		}
		return $"[url={entry.Key}][color=#{CivilopediaRichText.LinkColor.ToHtml(false)}]{CivilopediaRichText.Escape(entry.Name)}[/color][/url]";
	}

	private string Links(IEnumerable<object> subjects) {
		return string.Join(", ", subjects.Select(s => catalog.For(s)).Where(e => e != null).Select(Link));
	}

	private TextureRect AddIcon(Texture2D texture, Vector2 position, float size = 0) {
		TextureRect icon = new() {
			Texture = texture,
			Position = position,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		if (size > 0) {
			icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			icon.Size = new Vector2(size, size);
		}
		pageLayer.AddChild(icon);
		return icon;
	}

	private TextureRect AddYieldIcon(string configKey, Vector2 position, float size = 16) {
		try {
			return AddIcon(TextureLoader.Load(configKey), position, size);
		} catch (Exception) {
			return null;
		}
	}

	// An entry's small icon, with its name under it, that leads to its page.
	private Control AddIconLink(CivilopediaEntry entry, Vector2 center, float iconTop, bool showName = true, float nameWidth = 110) {
		if (entry == null) {
			return null;
		}
		Control box = new() {
			Position = new Vector2(center.X - 16, iconTop),
			Size = new Vector2(32, 32),
			MouseFilter = MouseFilterEnum.Stop,
			MouseDefaultCursorShape = CursorShape.PointingHand,
			TooltipText = entry.Name,
		};
		box.AddChild(new ColorRect { Color = new Color("6a6a6a"), Position = new Vector2(-1, -1), Size = new Vector2(34, 34), MouseFilter = MouseFilterEnum.Ignore });
		box.AddChild(new TextureRect {
			Texture = EntryIcon(entry, large: false),
			Size = new Vector2(32, 32),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
		});
		box.GuiInput += e => {
			if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right }) {
				GoToEntry(entry);
				box.AcceptEvent();
			}
		};
		pageLayer.AddChild(box);

		if (showName) {
			Button name = MakeFlatButton(entry.Name, 11);
			name.ClipText = true;
			name.Alignment = HorizontalAlignment.Center;
			name.Position = new Vector2(center.X - nameWidth / 2, iconTop + 33);
			name.Size = new Vector2(nameWidth, 16);
			name.CustomMinimumSize = name.Size;
			name.TooltipText = entry.Name;
			name.Pressed += () => GoToEntry(entry);
			pageLayer.AddChild(name);
		}
		return box;
	}

	// A row of icon links, from left to right, smaller if there are too many
	// to fit with their names.
	private void AddIconRow(IReadOnlyList<CivilopediaEntry> entries, float left, float right, float iconTop) {
		if (entries.Count == 0) {
			return;
		}
		float slot = Math.Min(110, (right - left) / entries.Count);
		bool names = slot >= 54;
		for (int i = 0; i < entries.Count; i++) {
			AddIconLink(entries[i], new Vector2(left + slot * i + slot / 2, 0), iconTop, names, slot);
		}
	}

	private void AddNothing(Vector2 position) {
		AddLabel("Nothing", position);
	}

	// ---- The main menu, the sections and the index ----

	private void DrawMain() {
		SetBackground("civilopediaBackground.pcx");
		CivilopediaSection[] sections = CivilopediaCatalog.MenuSections;
		for (int i = 0; i < sections.Length; i++) {
			CivilopediaSection section = sections[i];
			float y = 124 + i * 38;
			pageLayer.AddChild(new ColorRect { Color = new Color("c4382a"), Position = new Vector2(228, y - 2), Size = new Vector2(36, 36), MouseFilter = MouseFilterEnum.Ignore });
			AddIcon(CivilopediaArt.SectionIcon(section, large: false), new Vector2(230, y), 32);
			Button button = MakeFlatButton(CivilopediaCatalog.SectionName(section), 17);
			button.Alignment = HorizontalAlignment.Left;
			button.Position = new Vector2(272, y + 1);
			button.Size = new Vector2(300, 30);
			button.Pressed += () => GoToSection(section);
			pageLayer.AddChild(button);
		}
	}

	private void DrawSection(CivilopediaSection section) {
		SetBackground("civilopediaBackground.pcx");
		IReadOnlyList<CivilopediaEntry> entries = catalog.In(section);
		bool isIndex = section == CivilopediaSection.Index;

		listScroll = new ScrollContainer {
			Position = new Vector2(212, 118),
			Size = new Vector2(452, 584),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		pageLayer.AddChild(listScroll);
		VBoxContainer column = new() { CustomMinimumSize = new Vector2(440, 0) };
		column.AddThemeConstantOverride("separation", 2);
		listScroll.AddChild(column);

		if (section == CivilopediaSection.Resources) {
			// Civ3 lists resources by kind.
			foreach ((ResourceCategory category, string heading) in new[] {
				(ResourceCategory.BONUS, "Bonus Resources"),
				(ResourceCategory.LUXURY, "Luxury Resources"),
				(ResourceCategory.STRATEGIC, "Strategic Resources"),
			}) {
				List<CivilopediaEntry> ofKind = entries.Where(e => e.Resource?.Category == category).ToList();
				if (ofKind.Count > 0) {
					column.AddChild(MakeLabel(heading, 15, CivilopediaRichText.TextColor, bold: true));
					AddListGrid(column, ofKind, columns: 2, icons: true);
				}
			}
			return;
		}

		AddListGrid(column, entries, columns: isIndex ? 3 : 2, icons: !isIndex);
	}

	private void AddListGrid(Container parent, IReadOnlyList<CivilopediaEntry> entries, int columns, bool icons) {
		GridContainer grid = new() { Columns = columns };
		grid.AddThemeConstantOverride("h_separation", 6);
		grid.AddThemeConstantOverride("v_separation", icons ? 4 : 0);
		parent.AddChild(grid);
		float width = (440 - (columns - 1) * 6) / columns;
		foreach (CivilopediaEntry entry in entries) {
			HBoxContainer row = new() { CustomMinimumSize = new Vector2(width, icons ? 34 : 18) };
			row.AddThemeConstantOverride("separation", 6);
			if (icons) {
				row.AddChild(new TextureRect {
					Texture = EntryIcon(entry, large: false),
					CustomMinimumSize = new Vector2(32, 32),
					ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
					MouseFilter = MouseFilterEnum.Ignore,
				});
			}
			Button name = MakeFlatButton(entry.Name, icons ? 14 : 12);
			name.Alignment = HorizontalAlignment.Left;
			name.ClipText = true;
			name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			name.TooltipText = entry.Name;
			CivilopediaEntry target = entry;
			name.Pressed += () => GoToEntry(target);
			row.AddChild(name);
			grid.AddChild(row);
			listRows.Add((entry.Name, row));
		}
	}

	private void ScrollListTo(char letter) {
		if (listScroll == null) {
			return;
		}
		foreach ((string name, Control row) in listRows) {
			if (StartsWith(name, letter)) {
				// The rows' positions are only known once they're laid out.
				Callable.From(() => {
					if (IsInstanceValid(row) && IsInstanceValid(listScroll)) {
						listScroll.ScrollVertical = (int)(row.GetGlobalRect().Position.Y - listScroll.GetGlobalRect().Position.Y + listScroll.ScrollVertical);
					}
				}).CallDeferred();
				return;
			}
		}
	}

	// ---- Advances ----

	// Background A: the text; what the advance requires on the left and
	// leads to on the right, joined by lines through it in the middle; and
	// what it allows at the bottom.
	private void DrawTech(CivilopediaEntry entry) {
		Tech tech = entry.Tech;
		SetBackground("civilopediaA.pcx");
		AddEntryText(entry, 118, 228);

		AddTabLabel("Requires", 199, 253);
		AddTabLabel("Leads To", 545, 253);

		List<CivilopediaEntry> requires = tech.Prerequisites.Where(t => t != null).Select(t => catalog.For(t)).Where(e => e != null).ToList();
		List<CivilopediaEntry> leadsTo = catalog.LeadsTo(tech).Select(t => catalog.For(t)).Where(e => e != null).ToList();

		const float middle = 383;
		const float centerX = 434;
		TechLines lines = new() { Size = new Vector2(Width, Height), MouseFilter = MouseFilterEnum.Ignore };
		pageLayer.AddChild(lines);

		AddIconLink(entry, new Vector2(centerX, 0), middle - 16, showName: false);
		Label era = AddLabel(EraName(tech.EraCivilopediaName), new Vector2(centerX - 60, middle + 20), 11);
		era.HorizontalAlignment = HorizontalAlignment.Center;
		era.Size = new Vector2(120, 14);

		PlaceTechColumn(requires, 298 + 16, middle, lines, centerX - 17, toTheLeft: true);
		PlaceTechColumn(leadsTo, 539 + 16, middle, lines, centerX + 17, toTheLeft: false);
		if (requires.Count == 0) {
			AddNothing(new Vector2(285, middle - 9));
		}
		if (leadsTo.Count == 0) {
			AddNothing(new Vector2(530, middle - 9));
		}

		// What the advance allows, a row for each kind, at most three.
		List<(string heading, List<CivilopediaEntry> entries)> allows = new();
		List<CivilopediaEntry> allowed = catalog.AllowedBy(tech);
		void Group(string heading, Func<CivilopediaEntry, bool> belongs) {
			List<CivilopediaEntry> group = allowed.Where(belongs).ToList();
			if (group.Count > 0) {
				allows.Add((heading, group));
			}
		}
		Group("Units", e => e.Unit != null);
		Group("City Imp's", e => e.Section == CivilopediaSection.Improvements);
		Group("Gr Wonders", e => e.Section == CivilopediaSection.GreatWonders);
		Group("Sm Wonders", e => e.Section == CivilopediaSection.SmallWonders);
		Group("Govts", e => e.Government != null);
		Group("Actions", e => e.Terraform != null);
		Group("Resources", e => e.Resource != null);
		List<CivilopediaEntry> obsoletes = catalog.ObsoletedBy(tech);
		if (obsoletes.Count > 0) {
			allows.Add(("Obsoletes", obsoletes));
		}
		if (allows.Count > 3) {
			List<CivilopediaEntry> rest = allows.Skip(2).SelectMany(a => a.entries).ToList();
			allows = allows.Take(2).Append(("Other", rest)).ToList();
		}

		AddTabLabel("ALLOWS", 207, 535);
		float[] rowTops = { 563, 612, 662 };
		for (int i = 0; i < 3; i++) {
			if (i < allows.Count) {
				AddTabLabel(allows[i].heading, 203, rowTops[i] + 1);
				AddIconRow(allows[i].entries, 306, 664, rowTops[i] - 6);
			} else {
				// Hide the unused tab under a patch of the parchment beside it.
				AddIcon(CivilopediaArt.Crop(CivilopediaArt.Folder + "civilopediaA.pcx", 430, (int)rowTops[i] - 3, 100, 22), new Vector2(205, rowTops[i] - 3));
			}
		}
		if (allows.Count == 0) {
			AddNothing(new Vector2(306, rowTops[0] - 2));
		}
	}

	private void PlaceTechColumn(List<CivilopediaEntry> techs, float x, float middle, TechLines lines, float centerEdge, bool toTheLeft) {
		if (techs.Count == 0) {
			return;
		}
		// Up to five fit at Civ3's spacing; more are squeezed together.
		float spacing = Math.Min(52, 236f / Math.Max(1, techs.Count - 1));
		float top = middle - spacing * (techs.Count - 1) / 2;
		float elbowX = toTheLeft ? x + 16 + (centerEdge - x - 16) / 2 : x - 16 - (x - 16 - centerEdge) / 2;
		for (int i = 0; i < techs.Count; i++) {
			float y = top + spacing * i;
			AddIconLink(techs[i], new Vector2(x, 0), y - 16, showName: spacing >= 48, nameWidth: 116);
			float boxEdge = toTheLeft ? x + 17 : x - 17;
			lines.Segments.Add((new Vector2(boxEdge, y), new Vector2(elbowX, y)));
			lines.Segments.Add((new Vector2(elbowX, y), new Vector2(elbowX, middle)));
		}
		lines.Segments.Add((new Vector2(elbowX, middle), new Vector2(centerEdge, middle)));
		lines.QueueRedraw();
	}

	private static string EraName(string eraCivilopediaName) => eraCivilopediaName switch {
		EraUtils.ANCIENT_TIMES_CVLPD => EraUtils.ANCIENT_TIMES,
		EraUtils.MIDDLE_AGES_CVLPD => EraUtils.MIDDLE_AGES,
		EraUtils.INDUSTRIAL_AGE_CVLPD => EraUtils.INDUSTRIAL_AGE,
		EraUtils.MODERN_ERA_CVLPD => EraUtils.MODERN_ERA,
		_ => "",
	};

	// The lines joining an advance to the ones it requires and leads to.
	private partial class TechLines : Control {
		public readonly List<(Vector2 from, Vector2 to)> Segments = new();

		public override void _Draw() {
			foreach ((Vector2 from, Vector2 to) in Segments) {
				DrawLine(from, to, new Color("2a2418"), 1.5f);
			}
		}
	}

	// ---- Units ----

	// Background B: the text; the cost and upgrade; the statistics; and the
	// advance and resources the unit requires.
	private void DrawUnit(CivilopediaEntry entry) {
		UnitPrototype unit = entry.Unit;
		SetBackground("civilopediaB.pcx");
		AddEntryText(entry, 118, 364);

		// The cost and what the unit upgrades to.
		Label cost = AddLabel($"Cost: {unit.shieldCost}", new Vector2(250, 384));
		AddYieldIcon("icons.good_shield", new Vector2(250 + cost.GetMinimumSize().X + 6, 385), 16);
		// Another civilization's special unit isn't something this one
		// upgrades to.
		List<UnitPrototype> upgrades = unit.upgradesTo
			.Where(u => u != null && (u.producibleBy.Count != 1 || playerCivilization == null || u.IsProducibleBy(playerCivilization)))
			.ToList();
		if (upgrades.Count > 0) {
			AddRichLine($"Upgrades To: {Links(upgrades)}", new Vector2(380, 384), 280);
		}

		// The statistics, in columns: the basics, then bombardment and
		// carrying, then abilities.
		AddTabLabel("Stats", 205, 436);
		// Three lines to a column, as many columns as it takes: the basics,
		// then bombardment and carrying, then abilities.
		List<string> stats = new() {
			$"Attack: {unit.attack}",
			$"Defense: {unit.defense}",
			$"Movement: {unit.movement}",
		};
		if (unit.bombard > 0 || unit.bombardRange > 0) {
			stats.Add($"Bombard: {unit.bombard}");
			stats.Add($"Range: {unit.bombardRange}");
			stats.Add($"Rate of Fire: {unit.rateOfFire}");
		}
		if (unit.populationCost > 0) {
			stats.Add($"Population Cost: {unit.populationCost}");
		}
		if (unit.hpBonus > 0) {
			stats.Add($"Hit Point Bonus: {unit.hpBonus}");
		}
		if (unit.capacity > 0) {
			stats.Add($"Transport Capacity: {unit.capacity}");
		}
		stats.AddRange(Abilities(unit));
		int columns = (stats.Count + 2) / 3;
		float columnWidth = columns <= 3 ? 150 : 440f / columns;
		int fontSize = columns <= 3 ? 13 : 11;
		for (int c = 0; c < columns; c++) {
			AddColumn(stats.Skip(c * 3).Take(3).ToList(), new Vector2(223 + c * columnWidth, 453), 3, fontSize);
		}

		AddRequirements(catalog.For(unit.requiredTech), unit.requiredResources, 531, 558, 636);
	}

	private void AddColumn(List<string> lines, Vector2 position, int maxLines, int fontSize = 13) {
		float lineHeight = fontSize + 2;
		for (int i = 0; i < Math.Min(lines.Count, maxLines); i++) {
			AddLabel(lines[i], position + new Vector2(0, i * lineHeight), fontSize);
		}
	}

	// What a unit can do beyond fighting and moving, in Civ3's words.
	private static List<string> Abilities(UnitPrototype unit) {
		List<string> abilities = new();
		if (unit.isSettler) abilities.Add("Build City");
		if (unit.isWorker) abilities.Add("Worker Actions");
		if (unit.hasBlitz) abilities.Add("Blitz");
		if (unit.isAmphibious) abilities.Add("Amphibious");
		if (unit.hasZoneOfControl) abilities.Add("Zone of Control");
		if (unit.hasRadar) abilities.Add("Radar");
		if (unit.isLandBombardmentLethal || unit.isSeaBombardmentLethal) abilities.Add("Lethal Bombard");
		if (unit.startsGoldenAge) abilities.Add("Starts Golden Age");
		if (unit.isNuclearWeapon) abilities.Add("Nuclear Weapon");
		if (unit.canCarryAircraft) abilities.Add("Carries Aircraft");
		if (unit.canCarryTacticalMissiles) abilities.Add("Carries Missiles");
		if (unit.isArmy) abilities.Add("Army");
		if (unit.isLeader) abilities.Add("Leader");
		if (unit.sinksInSea) abilities.Add("Sinks in Sea");
		else if (unit.sinksInOcean) abilities.Add("Sinks in Ocean");
		return abilities;
	}

	// The REQUIRES panel shared by units, buildings and worker actions: the
	// advance under one heading and the resources under the other.
	private void AddRequirements(CivilopediaEntry advance, IEnumerable<Resource> resources, float tabY, float advancesY, float resourcesY) {
		AddTabLabel("REQUIRES", 206, tabY);
		AddTabLabel("Advances", 203, advancesY + 1);
		if (advance != null) {
			AddIconLink(advance, new Vector2(432, 0), advancesY - 4);
		} else {
			AddNothing(new Vector2(310, advancesY - 1));
		}
		AddTabLabel("Resources", 203, resourcesY + 1);
		List<CivilopediaEntry> needed = (resources ?? Enumerable.Empty<Resource>()).Select(r => catalog.For(r)).Where(e => e != null).ToList();
		if (needed.Count > 0) {
			AddIconRow(needed, 290, 664, resourcesY + 2);
		}
	}

	// ---- Buildings and wonders ----

	// Background G, as Civ3 draws the Heroic Epic: the text; the cost, upkeep
	// and culture; and what the building requires, covered if nothing.
	private void DrawBuilding(CivilopediaEntry entry) {
		Building building = entry.Building;
		SetBackground("civilopediaG.pcx");
		AddEntryText(entry, 118, 364);

		List<(string text, string icon)> stats = new() {
			($"Cost: {building.shieldCost}", "icons.good_shield"),
		};
		if (!building.IsGreatWonder()) {
			stats.Add(($"Maintenance: {building.maintenanceCost}", "icons.good_gold"));
		}
		stats.Add(($"Culture per turn: {building.culturePerTurn}", null));
		// A scenario's gold-making buildings (see City.BuildingGold).
		if (building.goldPerTurn > 0) {
			stats.Add(($"Gold per turn: {building.goldPerTurn}", "icons.good_gold"));
		}
		if (building.goldPerWorkedCoastTile > 0) {
			stats.Add(($"Gold per worked coast tile: {building.goldPerWorkedCoastTile}", "icons.good_gold"));
		}
		if (building.doublesBuildingGold) {
			stats.Add(("Doubles the gold of the city's buildings", null));
		}
		float y = 388;
		foreach ((string s, string icon) in stats) {
			Label label = AddLabel(s, new Vector2(356, y));
			if (icon != null) {
				AddYieldIcon(icon, new Vector2(356 + label.GetMinimumSize().X + 6, y + 1), 17);
			}
			y += 22;
		}
		if (building.renderedObsoleteBy != null && catalog.For(building.renderedObsoleteBy) is CivilopediaEntry obsolete) {
			AddRichLine($"Made Obsolete By: {Link(obsolete)}", new Vector2(356, y), 300);
			y += 22;
		}
		List<string> needs = new();
		if (building.requiredBuilding != null && catalog.For(building.requiredBuilding) is CivilopediaEntry required) {
			needs.Add(building.requiredBuildingCount > 1 ? $"{building.requiredBuildingCount} × {Link(required)}" : Link(required));
		}
		if (building.requiredGovernment != null && catalog.For(building.requiredGovernment) is CivilopediaEntry government) {
			needs.Add($"Required Government: {Link(government)}");
		}
		if (needs.Count > 0 && y < 500) {
			AddRichLine(string.Join("   ", needs.Select((n, i) => i == 0 && !n.StartsWith("Required") ? "Requires: " + n : n)), new Vector2(356, y), 300);
		}

		CivilopediaEntry advance = catalog.For(building.requiredTech);
		if (advance == null && building.requiredResources.Count == 0) {
			Cover("B-cover.pcx");
			return;
		}
		AddRequirements(advance, building.requiredResources, 531, 558, 636);
	}

	// ---- Governments ----

	// Background F: the text; the advance the government requires; and how
	// it runs, from the rules.
	private void DrawGovernment(CivilopediaEntry entry) {
		Government government = entry.Government;
		SetBackground("civilopediaF.pcx");
		AddEntryText(entry, 118, 482);

		AddTabLabel("REQUIRES", 205, 502);
		AddTabLabel("Advance", 203, 530);
		CivilopediaEntry advance = catalog.For(catalog.TechById(government.prerequisiteTech));
		if (advance != null) {
			AddIconLink(advance, new Vector2(432, 0), 512, showName: false);
			AddRichLine(Link(advance), new Vector2(456, 528), 200);
		} else {
			AddNothing(new Vector2(310, 528));
		}

		// The article lists how the government runs; Civ3 covers the panel
		// under it.
		Cover("F-lower.pcx");
	}

	// ---- Resources ----

	// Background F: the text; the advance that reveals the resource; and
	// what it adds to a tile and where it is found.
	private void DrawResource(CivilopediaEntry entry) {
		Resource resource = entry.Resource;
		SetBackground("civilopediaF.pcx");
		AddEntryText(entry, 118, 482);

		AddTabLabel("REQUIRES", 205, 502);
		AddTabLabel("Advance", 203, 530);
		CivilopediaEntry advance = catalog.For(catalog.TechById(resource.Prerequisite));
		if (advance != null) {
			AddIconLink(advance, new Vector2(432, 0), 512, showName: false);
			AddRichLine(Link(advance), new Vector2(456, 528), 200);
		} else {
			AddNothing(new Vector2(310, 528));
		}

		AddTabLabel("TILE BONUS", 206, 616);
		float x = 335;
		foreach ((int amount, string icon, string name) in new[] {
			(resource.FoodBonus, "icons.food", "Food"),
			(resource.ShieldsBonus, "icons.good_shield", "Shields"),
			(resource.CommerceBonus, "icons.commerce", "Commerce"),
		}) {
			Label label = AddLabel($"{name}: +{amount}", new Vector2(x, 615));
			AddYieldIcon(icon, new Vector2(x + label.GetMinimumSize().X + 4, 616), 16);
			x += 104;
		}
		string kind = resource.Category switch {
			ResourceCategory.BONUS => Link(catalog.Get("GCON_ResourcesB")) is { Length: > 0 } b ? b : "Bonus Resource",
			ResourceCategory.LUXURY => Link(catalog.Get("GCON_ResourcesL")) is { Length: > 0 } l ? l : "Luxury Resource",
			ResourceCategory.STRATEGIC => Link(catalog.Get("GCON_ResourcesS")) is { Length: > 0 } s ? s : "Strategic Resource",
			_ => "",
		};
		AddRichLine($"Type: {kind}", new Vector2(223, 642), 440);
		List<TerrainType> terrains = catalog.TerrainsWith(resource);
		if (terrains.Count > 0) {
			AddRichLine($"Found on: {Links(terrains)}", new Vector2(223, 666), 440);
		}
	}

	// ---- Terrain ----

	// Background D: the text; the tile's yield; how it moves, defends and
	// improves; and the resources found on it.
	private void DrawTerrain(CivilopediaEntry entry) {
		TerrainType terrain = entry.Terrain;
		SetBackground("civilopediaD.pcx");
		AddEntryText(entry, 118, 340);

		AddTabLabel("YIELD", 204, 361);
		AddLabel("Base Tile Yield:", new Vector2(223, 384));
		float x = 340;
		foreach ((int amount, string icon) in new[] {
			(terrain.baseFoodProduction, "icons.food"),
			(terrain.baseShieldProduction, "icons.good_shield"),
			(terrain.baseCommerceProduction, "icons.commerce"),
		}) {
			Label label = AddLabel(amount.ToString(), new Vector2(x, 384));
			AddYieldIcon(icon, new Vector2(x + label.GetMinimumSize().X + 4, 385), 16);
			x += 70;
		}

		AddTabLabel("STATISTICS", 204, 449);
		string movement = terrain.impassable ? "Impassable" : terrain.movementCost.ToString();
		int defense = (int)Math.Round(terrain.defenseBonus.amount * 100);
		AddLabel($"Movement Cost: {movement}", new Vector2(345, 449));
		AddLabel($"Defensive Bonus: +{defense}%", new Vector2(505, 449));
		AddTabLabel("Bonuses", 203, 477);
		List<string> bonuses = new();
		foreach ((string key, Tile.YieldType yield, string name, string unit) in new[] {
			("irrigation", Tile.YieldType.Food, "Irrigation Bonus", "food"),
			("mine", Tile.YieldType.Production, "Mining Bonus", "shield"),
			("road", Tile.YieldType.Commerce, "Road Bonus", "commerce"),
		}) {
			int bonus = catalog.ImprovementBonus(terrain, key, yield);
			if (bonus != 0) {
				bonuses.Add($"{name}: +{bonus} {unit}");
			}
		}
		if (bonuses.Count == 0) {
			bonuses.Add("None");
		}
		AddColumn(bonuses, new Vector2(345, 477), 3, fontSize: 12);
		if (!terrain.allowCities) {
			AddLabel("No cities", new Vector2(505, 477), 12);
		}

		AddTabLabel("Resources", 205, 562);
		List<CivilopediaEntry> resources = catalog.ResourcesOn(terrain).Select(r => catalog.For(r)).Where(e => e != null).ToList();
		if (resources.Count > 0) {
			AddIconRow(resources, 300, 664, 582);
		} else {
			AddNothing(new Vector2(345, 562));
		}
	}

	// ---- Worker actions ----

	// Background E: the text; how long the job takes and who does it; and
	// what it requires.
	private void DrawWorkerAction(CivilopediaEntry entry) {
		Terraform terraform = entry.Terraform;
		SetBackground("civilopediaE.pcx");
		AddEntryText(entry, 118, 392);

		AddLabel($"Base Turns to Complete: {terraform.TurnsToComplete}", new Vector2(223, 404));
		List<UnitPrototype> workers = catalog.UnitsThatCanDo(terraform);
		if (workers.Count > 0) {
			AddRichLine($"Done by: {Links(workers)}", new Vector2(223, 426), 440, fontSize: 12);
		}

		CivilopediaEntry advance = catalog.For(catalog.TechById(terraform.RequiredTech));
		if (advance == null && terraform.RequiredResources.Count == 0) {
			Cover("civilopediaE-coverUp.pcx");
			return;
		}
		AddRequirements(advance, terraform.RequiredResources, 471, 498, 590);
	}

	// ---- Civilizations ----

	// Background D: the text; the leader; the civilization's traits and
	// starting advances; and its special unit.
	private void DrawCivilization(CivilopediaEntry entry) {
		Civilization civ = entry.Civilization;
		SetBackground("civilopediaD.pcx");
		AddEntryText(entry, 118, 340);

		AddTabLabel("Leader", 204, 361);
		AddLabel(civ.leader ?? "", new Vector2(345, 361));
		List<string> governments = new();
		CivilopediaEntry favorite = catalog.All.FirstOrDefault(e => e.Government?.name == civ.favoriteGovernment);
		CivilopediaEntry shunned = catalog.All.FirstOrDefault(e => e.Government?.name == civ.shunnedGovernment);
		if (favorite != null) {
			governments.Add($"Favorite Government: {Link(favorite)}");
		}
		if (shunned != null) {
			governments.Add($"Shunned Government: {Link(shunned)}");
		}
		if (governments.Count > 0) {
			AddRichLine(string.Join("\n", governments), new Vector2(223, 384), 440, fontSize: 12);
		}

		AddTabLabel("Traits", 204, 449);
		CivilopediaEntry strengths = catalog.Get("GCON_Strengths");
		string traits = string.Join(", ", civ.traits.OrderBy(t => t).Select(t => strengths != null
			? $"[url={strengths.Key}][color=#{CivilopediaRichText.LinkColor.ToHtml(false)}]{t}[/color][/url]"
			: t.ToString()));
		AddRichLine(traits, new Vector2(345, 449), 320);
		AddTabLabel("Advances", 203, 477);
		List<CivilopediaEntry> startingTechs = civ.startingTechs.Select(id => catalog.For(catalog.TechById(id))).Where(e => e != null).ToList();
		if (startingTechs.Count > 0) {
			AddIconRow(startingTechs, 300, 664, 480);
		} else {
			AddNothing(new Vector2(345, 477));
		}

		AddTabLabel("Special Unit", 205, 562);
		List<CivilopediaEntry> unique = catalog.UniqueUnitsOf(civ).Select(u => catalog.For(u)).Where(e => e != null).ToList();
		if (unique.Count > 0) {
			AddIconRow(unique, 300, 664, 582);
		} else {
			AddNothing(new Vector2(345, 562));
		}
	}
}
