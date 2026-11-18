using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine.Editor;
using C7GameData;
using Godot;

// The lists Scenario Properties chooses from: the rules' civilizations,
// difficulty levels, governments, advances and unit types, and a
// civilization's own leader, gender, colour and free advances.
public sealed record ScenarioLists(List<string> Civilizations, List<string> Difficulties, List<string> Governments,
	List<string> Techs, List<string> UnitTypes, Func<string, (string leader, Gender gender, int color, List<string> techs)> CivDefaults);

// Scenario Properties' Players, Locked Alliances and Disasters tabs
// (Civ3's Player Properties, screen_playerproperties.png, and the
// Locked Alliances and Disasters pages).
public partial class EditorChrome {
	private const string AnyLabel = "Any", RandomLabel = "Random";

	// A list on the parchment: a light fill, a brown frame, ink, the
	// selected row darker.
	private static ItemList ParchmentList(string name, float height) {
		ItemList list = new() { Name = name, CustomMinimumSize = new Vector2(0, height), SelectMode = ItemList.SelectModeEnum.Single };
		list.AddThemeStyleboxOverride("panel", SetupStyle.Box(LightColor, new Color(SetupStyle.Brown, 0.8f), 1, 3));
		list.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		list.AddThemeStyleboxOverride("selected", SetupStyle.Box(PressedColor, SetupStyle.Brown, 1, 2));
		list.AddThemeStyleboxOverride("selected_focus", SetupStyle.Box(PressedColor, SetupStyle.Brown, 1, 2));
		list.AddThemeStyleboxOverride("hovered", SetupStyle.Box(HoverColor, new Color(SetupStyle.Brown, 0), 0, 2));
		foreach (string color in new[] { "font_color", "font_selected_color", "font_hovered_color" }) {
			list.AddThemeColorOverride(color, SetupStyle.Ink);
		}
		list.AddThemeFontSizeOverride("font_size", 14);
		return list;
	}

	private static string PlayerLabel(PlayerSettings p, int number) => p.Choice switch {
		CivChoice.Any => $"Player {number}: Any",
		CivChoice.Random => $"Player {number}: Random",
		_ => $"Player {number}: {p.Civilization}",
	};

	// Civ3's Player Properties, a player at a time: Civilization (a
	// civilization, Any or Random), Human Player, Civilization Defaults
	// (else leader name, gender, team colour, free advances), Difficulty,
	// Government, Starting Era, Treasury, Starting Units, Skip 1st Turn,
	// Start with Embassies.
	private Control PlayersTab(ScenarioSettings s, ScenarioLists lists) {
		HBoxContainer tab = new() { Name = "Players" };
		tab.AddThemeConstantOverride("separation", 16);
		if (s.Players.Count == 0) {
			VBoxContainer empty = new();
			empty.AddChild(InkLabel("The scenario has no players yet: add them with Set Active Player."));
			return empty.WithName("Players");
		}
		ItemList players = ParchmentList("PlayerList", 0);
		players.CustomMinimumSize = new Vector2(220, 0);
		players.SizeFlagsVertical = SizeFlags.ExpandFill;
		for (int i = 0; i < s.Players.Count; ++i) {
			players.AddItem(PlayerLabel(s.Players[i], i + 1));
		}
		tab.AddChild(players);
		ScrollContainer scroll = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		VBoxContainer form = new() { Name = "PlayerForm", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		form.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(form);
		tab.AddChild(scroll);

		void Show(int index) {
			foreach (Node child in form.GetChildren()) {
				form.RemoveChild(child);
				child.QueueFree();
			}
			PlayerSettings p = s.Players[index];
			HBoxContainer columns = new();
			columns.AddThemeConstantOverride("separation", 20);
			form.AddChild(columns);
			GridContainer grid = new() { Columns = 2 };
			grid.AddThemeConstantOverride("h_separation", 12);
			grid.AddThemeConstantOverride("v_separation", 5);
			columns.AddChild(grid);

			grid.AddChild(InkLabel("Civilization:"));
			OptionButton civ = SetupStyle.MakeDropdown("Any: the human picks one at setup (a computer gets one at random). Random: drawn at setup.");
			civ.Name = "Civilization";
			List<string> civItems = [AnyLabel, RandomLabel, .. lists.Civilizations];
			int civIndex = p.Choice == CivChoice.Any ? 0 : p.Choice == CivChoice.Random ? 1 : Math.Max(2, civItems.IndexOf(p.Civilization));
			SetupStyle.AddItems(civ, civItems, civIndex);
			grid.AddChild(civ);

			CheckBox human = Check("Human Player", p.Human);
			human.Name = "Human Player";
			human.TooltipText = "A human may play this player. With none ticked, the human may pick any.";
			human.Toggled += on => p.Human = on;
			grid.AddChild(human);
			CheckBox defaults = Check("Civilization Defaults", p.CivDefaults);
			defaults.Name = "Civilization Defaults";
			defaults.TooltipText = "The civilization's own leader name, gender, team colour and free advances.";
			grid.AddChild(defaults);

			grid.AddChild(InkLabel("Leader Name:"));
			LineEdit leader = new() { Name = "Leader Name", Text = p.LeaderName, CustomMinimumSize = new Vector2(200, 0) };
			StyleField(leader);
			leader.TextChanged += t => p.LeaderName = t;
			grid.AddChild(leader);
			grid.AddChild(InkLabel("Gender:"));
			OptionButton gender = SetupStyle.MakeDropdown();
			SetupStyle.AddItems(gender, ["Male", "Female"], p.LeaderGender == Gender.Female ? 1 : 0);
			gender.ItemSelected += i => p.LeaderGender = i == 1 ? Gender.Female : Gender.Male;
			grid.AddChild(gender);
			grid.AddChild(InkLabel("Team Color:"));
			HBoxContainer colorRow = new();
			SpinBox color = Spin(p.Color, 0, 31);
			ColorRect swatch = new() { CustomMinimumSize = new Vector2(26, 22), Color = TextureLoader.LoadColor(p.Color) };
			color.ValueChanged += v => {
				p.Color = (int)v;
				swatch.Color = TextureLoader.LoadColor(p.Color);
			};
			colorRow.AddChild(color);
			colorRow.AddChild(swatch);
			grid.AddChild(colorRow);

			grid.AddChild(InkLabel("Difficulty:"));
			OptionButton difficulty = SetupStyle.MakeDropdown("The human's is the game's (Any: the player's choice). A computer player plays with its own level's bonuses (Any: the game's).");
			difficulty.Name = "Difficulty";
			SetupStyle.AddItems(difficulty, [AnyLabel, .. lists.Difficulties], p.Difficulty == null ? 0 : Math.Max(0, lists.Difficulties.IndexOf(p.Difficulty) + 1));
			difficulty.ItemSelected += i => p.Difficulty = i == 0 ? null : lists.Difficulties[(int)i - 1];
			grid.AddChild(difficulty);
			grid.AddChild(InkLabel("Government:"));
			OptionButton government = SetupStyle.MakeDropdown();
			SetupStyle.AddItems(government, lists.Governments, Math.Max(0, lists.Governments.IndexOf(p.Government)));
			government.ItemSelected += i => p.Government = lists.Governments[(int)i];
			grid.AddChild(government);
			grid.AddChild(InkLabel("Starting Era:"));
			OptionButton era = SetupStyle.MakeDropdown();
			SetupStyle.AddItems(era, [.. ScenarioSettings.EraNames], Math.Clamp(p.Era, 0, 3));
			era.ItemSelected += i => p.Era = (int)i;
			grid.AddChild(era);
			grid.AddChild(InkLabel("Treasury:"));
			SpinBox treasury = Spin(p.Treasury, 0, 999999);
			treasury.Name = "Treasury";
			treasury.ValueChanged += v => p.Treasury = (int)v;
			grid.AddChild(treasury);
			CheckBox skip = Check("Skip 1st Turn", p.SkipFirstTurn);
			skip.TooltipText = "Its units do nothing on the first turn.";
			skip.Toggled += on => p.SkipFirstTurn = on;
			grid.AddChild(skip);
			CheckBox embassies = Check("Start with Embassies", p.StartWithEmbassies);
			embassies.Name = "Start with Embassies";
			embassies.TooltipText = "An embassy with every other player that has this ticked (so they've met).";
			embassies.Toggled += on => p.StartWithEmbassies = on;
			grid.AddChild(embassies);

			// Starting units and free advances.
			VBoxContainer right = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			right.AddThemeConstantOverride("separation", 4);
			columns.AddChild(right);
			right.AddChild(InkLabel("Starting Units (none: the rules' two):"));
			VBoxContainer units = new() { Name = "StartingUnits" };
			right.AddChild(units);
			void ShowUnits() {
				foreach (Node child in units.GetChildren()) {
					units.RemoveChild(child);
					child.QueueFree();
				}
				foreach (StartUnit unit in p.StartUnits) {
					HBoxContainer row = new();
					OptionButton type = SetupStyle.MakeDropdown();
					SetupStyle.AddItems(type, lists.UnitTypes, Math.Max(0, lists.UnitTypes.IndexOf(unit.type)));
					type.ItemSelected += i => unit.type = lists.UnitTypes[(int)i];
					row.AddChild(type);
					SpinBox count = Spin(unit.count, 1, 99);
					count.ValueChanged += v => unit.count = (int)v;
					row.AddChild(count);
					row.AddChild(TextButton("Remove", null, () => {
						p.StartUnits.Remove(unit);
						ShowUnits();
					}));
					units.AddChild(row);
				}
				Button add = TextButton("Add Unit", null, () => {
					p.StartUnits.Add(new StartUnit { type = lists.UnitTypes.FirstOrDefault(), count = 1 });
					ShowUnits();
				});
				add.Name = "Add Unit";
				units.AddChild(add);
			}
			ShowUnits();
			right.AddChild(Rule());
			right.AddChild(InkLabel("Free Techs:"));
			ScrollContainer techScroll = new() { CustomMinimumSize = new Vector2(0, 200), SizeFlagsVertical = SizeFlags.ExpandFill };
			VBoxContainer techs = new() { Name = "FreeTechs" };
			List<CheckBox> techBoxes = [];
			foreach (string tech in lists.Techs) {
				CheckBox box = Check(tech, p.FreeTechs.Contains(tech));
				box.Toggled += on => {
					p.FreeTechs.Remove(tech);
					if (on) {
						p.FreeTechs.Add(tech);
					}
				};
				techs.AddChild(box);
				techBoxes.Add(box);
			}
			techScroll.AddChild(techs);
			right.AddChild(techScroll);

			// Civilization Defaults greys out what the civilization sets,
			// showing its values.
			void Refresh() {
				bool own = !p.CivDefaults;
				if (p.CivDefaults && p.Choice == CivChoice.Fixed) {
					(string name, Gender g, int c, List<string> known) = lists.CivDefaults(p.Civilization);
					leader.Text = p.LeaderName = name;
					gender.Selected = g == Gender.Female ? 1 : 0;
					p.LeaderGender = g;
					color.Value = c;
					p.FreeTechs = [.. known];
					foreach (CheckBox box in techBoxes) {
						box.SetPressedNoSignal(known.Contains(box.Text));
					}
				}
				leader.Editable = own;
				gender.Disabled = !own;
				color.Editable = own;
				// The civilization's own free advances are always known (the
				// game adds them), so they're ticked for good.
				List<string> always = p.Choice == CivChoice.Fixed ? lists.CivDefaults(p.Civilization).techs : [];
				foreach (CheckBox box in techBoxes) {
					bool civs = always.Contains(box.Text);
					if (civs && !box.ButtonPressed) {
						box.ButtonPressed = true;
					}
					box.Disabled = !own || civs;
					box.TooltipText = civs ? "The civilization's own: always known." : null;
				}
			}
			defaults.Toggled += on => {
				p.CivDefaults = on;
				Refresh();
			};
			civ.ItemSelected += i => {
				p.Choice = i == 0 ? CivChoice.Any : i == 1 ? CivChoice.Random : CivChoice.Fixed;
				if (i >= 2) {
					p.Civilization = civItems[(int)i];
				}
				players.SetItemText(index, PlayerLabel(p, index + 1));
				Refresh();
			};
			Refresh();
		}
		players.ItemSelected += i => Show((int)i);
		players.Select(0);
		Show(0);
		return tab;
	}

	// Civ3's Locked Alliances page: up to four named alliances (a player
	// moves from "No Alliance" into one with its arrow, and back), which
	// alliances start at war, and the victory type.
	private Control AlliancesTab(ScenarioSettings s, out Action refresh) {
		VBoxContainer tab = new() { Name = "Locked Alliances" };
		tab.AddThemeConstantOverride("separation", 8);
		HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 12);
		tab.AddChild(columns);

		List<PlayerSettings> players = s.Players;
		string PlayerName(PlayerSettings p) => PlayerLabel(p, players.IndexOf(p) + 1);
		VBoxContainer noneColumn = new() { CustomMinimumSize = new Vector2(200, 0) };
		noneColumn.AddChild(InkLabel("No Alliance:"));
		ItemList none = ParchmentList("No Alliance", 260);
		noneColumn.AddChild(none);
		columns.AddChild(noneColumn);
		ItemList[] members = new ItemList[4];

		void Fill() {
			none.Clear();
			foreach (PlayerSettings p in players.Where(p => !s.AllianceOf.ContainsKey(p.Id))) {
				none.AddItem(PlayerName(p));
				none.SetItemMetadata(none.ItemCount - 1, p.Id);
			}
			for (int a = 0; a < 4; ++a) {
				members[a].Clear();
				foreach (PlayerSettings p in players.Where(p => s.AllianceOf.TryGetValue(p.Id, out int x) && x == a + 1)) {
					members[a].AddItem(PlayerName(p));
					members[a].SetItemMetadata(members[a].ItemCount - 1, p.Id);
				}
			}
		}
		static string Selected(ItemList list) => list.GetSelectedItems() is { Length: > 0 } picked ? (string)list.GetItemMetadata(picked[0]) : null;

		for (int a = 0; a < 4; ++a) {
			int alliance = a + 1;
			VBoxContainer column = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			column.AddThemeConstantOverride("separation", 4);
			LineEdit name = new() { Name = $"Alliance {alliance} Name", Text = s.AllianceNames[a] };
			StyleField(name);
			name.TextChanged += t => s.AllianceNames[alliance - 1] = t;
			column.AddChild(name);
			HBoxContainer arrows = new();
			Button join = TextButton("▶ Join", "Moves the player chosen under No Alliance into this alliance.", () => {
				if (Selected(none) is string id) {
					s.AllianceOf[id] = alliance;
					Fill();
				}
			});
			join.Name = $"Join {alliance}";
			arrows.AddChild(join);
			arrows.AddChild(TextButton("◀ Leave", "Moves the player chosen here back to No Alliance.", () => {
				if (Selected(members[alliance - 1]) is string id) {
					s.AllianceOf.Remove(id);
					Fill();
				}
			}));
			column.AddChild(arrows);
			members[a] = ParchmentList($"Alliance {alliance}", 180);
			column.AddChild(members[a]);
			column.AddChild(InkLabel("At War With..."));
			for (int b = 1; b <= 4; ++b) {
				if (b == alliance) {
					continue;
				}
				(int, int) pair = (Math.Min(alliance, b), Math.Max(alliance, b));
				int other = b;
				CheckBox war = Check($"Alliance {other}", s.AllianceWars.Contains(pair));
				war.Name = $"War {alliance}-{other}";
				war.Toggled += on => {
					s.AllianceWars.Remove(pair);
					if (on) {
						s.AllianceWars.Add(pair);
					}
					// The same war shows under both alliances.
					if (tab.FindChild($"War {other}-{alliance}", true, false) is CheckBox mirror) {
						mirror.SetPressedNoSignal(on);
					}
				};
				column.AddChild(war);
			}
			columns.AddChild(column);
		}
		Fill();
		// The Players tab may have changed a player's civilization.
		refresh = Fill;

		tab.AddChild(Rule());
		HBoxContainer victory = new();
		victory.AddThemeConstantOverride("separation", 10);
		victory.AddChild(InkLabel("Victory Type:"));
		OptionButton type = SetupStyle.MakeDropdown("Coalition: allies win together (a victory one wins is theirs too). Individual: each plays for itself.");
		type.Name = "Victory Type";
		type.CustomMinimumSize = new Vector2(200, 0);
		type.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		SetupStyle.AddItems(type, ["Individual", "Coalition"], s.CoalitionVictory ? 1 : 0);
		type.ItemSelected += i => s.CoalitionVictory = i == 1;
		victory.AddChild(type);
		tab.AddChild(victory);
		tab.AddChild(Note("Allies start at peace with a mutual protection pact and can't go to war with each other; alliances at war can't make peace."));
		return tab;
	}

	// Civ3's Disasters page: the plague and the volcano eruption period.
	private Control DisastersTab(ScenarioSettings s) {
		VBoxContainer tab = new() { Name = "Disasters" };
		tab.AddThemeConstantOverride("separation", 8);
		tab.AddChild(InkLabel("Plague").WithFontSize(15));
		CheckBox permit = Check("Permit Plagues", s.PermitPlagues);
		permit.Name = "Permit Plagues";
		permit.Toggled += on => s.PermitPlagues = on;
		tab.AddChild(permit);
		GridContainer grid = new() { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 20);
		grid.AddThemeConstantOverride("v_separation", 4);
		grid.AddChild(InkLabel("Name:"));
		LineEdit name = new() { Name = "Plague Name", Text = s.PlagueName, CustomMinimumSize = new Vector2(200, 0) };
		StyleField(name);
		name.TextChanged += t => s.PlagueName = t;
		grid.AddChild(name);
		void Number(string label, int value, int min, int max, Action<int> set, string tooltip) {
			grid.AddChild(InkLabel(label));
			SpinBox spin = Spin(value, min, max);
			spin.Name = label;
			spin.TooltipText = tooltip;
			spin.ValueChanged += v => set((int)v);
			grid.AddChild(spin);
		}
		Number("Earliest Start (year):", s.PlagueEarliestStart, -99999, 99999, v => s.PlagueEarliestStart = v, "The first year a plague can break out (below 0: BC).");
		Number("Variance (% of the grace period):", s.PlagueVariance, 0, 1000, v => s.PlagueVariance = v, "How far past the earliest start the first outbreak may come.");
		Number("Duration (years):", s.PlagueDuration, 0, 99999, v => s.PlagueDuration = v, "How long an outbreak lasts.");
		Number("Strength (0-100):", s.PlagueStrength, 0, 100, v => s.PlagueStrength = v, "How deadly it is.");
		Number("Grace Period (years):", s.PlagueGracePeriod, 0, 99999, v => s.PlagueGracePeriod = v, "The years between outbreaks.");
		Number("Maximum Occurrences:", s.PlagueMaxOccurrences, 0, 999, v => s.PlagueMaxOccurrences = v, "How many times it breaks out at most.");
		tab.AddChild(grid);
		tab.AddChild(Rule());
		tab.AddChild(InkLabel("Volcanoes").WithFontSize(15));
		HBoxContainer volcano = new();
		volcano.AddThemeConstantOverride("separation", 10);
		volcano.AddChild(InkLabel("Eruption Period (years):"));
		SpinBox period = Spin(s.VolcanoEruptionPeriod, 0, 999999);
		period.Name = "Eruption Period";
		period.TooltipText = "Each volcano erupts at least once in this many years (0: never).";
		period.ValueChanged += v => s.VolcanoEruptionPeriod = (int)v;
		volcano.AddChild(period);
		tab.AddChild(volcano);
		return tab;
	}
}

internal static class NodeNames {
	public static T WithName<T>(this T node, string name) where T : Node {
		node.Name = name;
		return node;
	}
}
