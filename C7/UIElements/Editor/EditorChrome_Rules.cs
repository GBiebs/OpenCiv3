using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using C7Engine.Editor;
using C7GameData.Save;
using Godot;

// Civ3's Edit Rules dialog (screen_citizens.png, screen_general.png): a
// tab per page, each generated from the rule schema (RulePages). A page
// with items has Civ3's selector and its Rename, Add and Delete buttons;
// below, the item's fields in boxes and columns. The dialog changes a copy
// of the scenario; OK hands it to the editor (apply returns why it
// couldn't, or null).
public partial class EditorChrome {
	public Control RulesDialog(SaveGame rules, SaveGame defaults, Func<SaveGame, string> apply, Action close) {
		const int width = 1200, height = 700;
		TextureRect window = Popup.CreateBackground(width, height);
		window.CustomMinimumSize = new Vector2(width, height);
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "margin_left", "margin_right" }) {
			margin.AddThemeConstantOverride(side, 24);
		}
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		window.AddChild(margin);
		VBoxContainer box = new();
		box.AddThemeConstantOverride("separation", 6);
		margin.AddChild(box);
		box.AddChild(SetupStyle.MakeHeading(this, "EDIT RULES", 18));

		Label problem = InkLabel("");
		problem.Name = "Problem";
		problem.AddThemeColorOverride("font_color", SetupStyle.ProblemColor);
		void Report(string why) => problem.Text = why ?? "";

		TabContainer tabs = new() { Name = "Tabs", SizeFlagsVertical = SizeFlags.ExpandFill };
		StyleTabs(tabs);
		box.AddChild(tabs);
		List<Action> refreshes = [];
		foreach (RulePage page in RulePages.All) {
			tabs.AddChild(RulePageTab(page, rules, Report, out Action refresh));
			refreshes.Add(refresh);
		}
		box.AddChild(problem);

		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 8);
		Button Footer(string text, string tooltip, Action action) {
			Button button = TextButton(text, tooltip, action);
			button.Name = text;
			button.CustomMinimumSize = new Vector2(96, 26);
			buttons.AddChild(button);
			return button;
		}
		Footer("Clear Rules", "This page's rules back to the game's standard rules.", () => {
			RuleEditing.ClearPage(rules, defaults, RulePages.All[tabs.CurrentTab]);
			refreshes[tabs.CurrentTab]();
			Report($"{RulePages.All[tabs.CurrentTab].Name}: the standard rules.");
		});
		Footer("Restore Defaults", "Every page back to the game's standard rules.", () => {
			RuleEditing.RestoreDefaults(rules, defaults);
			refreshes.ForEach(r => r());
			Report("Every page: the standard rules.");
		});
		buttons.AddChild(new Control { CustomMinimumSize = new Vector2(40, 0) });
		Footer("OK", null, () => {
			Report(RuleEditing.Problem(rules) ?? apply(rules));
			if (problem.Text == "") {
				close();
			}
		});
		Footer("Cancel", null, close);
		box.AddChild(buttons);
		return Backdrop(window, 0.25f);
	}

	// A boxed group of fields, Civ3's frames with a title.
	private static (Control box, GridContainer grid) RuleGroup(string title) {
		PanelContainer panel = new();
		StyleBoxFlat frame = SetupStyle.Box(new Color(0, 0, 0, 0), new Color(SetupStyle.Brown, 0.6f), 1, 3);
		frame.ContentMarginLeft = frame.ContentMarginRight = 8;
		frame.ContentMarginTop = frame.ContentMarginBottom = 6;
		panel.AddThemeStyleboxOverride("panel", frame);
		VBoxContainer inside = new();
		inside.AddThemeConstantOverride("separation", 4);
		if (title != "") {
			inside.AddChild(InkLabel(title).WithFontSize(15));
		}
		GridContainer grid = new() { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 10);
		grid.AddThemeConstantOverride("v_separation", 3);
		inside.AddChild(grid);
		panel.AddChild(inside);
		return (panel, grid);
	}

	private Control RulePageTab(RulePage page, SaveGame save, Action<string> report, out Action refresh) {
		VBoxContainer tab = new() { Name = page.Name };
		tab.AddThemeConstantOverride("separation", 6);
		int index = 0;
		IList Items() => page.Items(save);
		// The item shown, or null on a page with none.
		object Current() => Items().Count > 0 ? Items()[Math.Min(index, Items().Count - 1)] : null;

		// The selector and its buttons, as Civ3's.
		HBoxContainer top = new() { Visible = !page.IsSingle };
		top.AddThemeConstantOverride("separation", 8);
		tab.AddChild(top);
		top.AddChild(InkLabel(page.ItemLabel ?? ""));
		OptionButton selector = SetupStyle.MakeDropdown();
		selector.Name = "Selector";
		selector.CustomMinimumSize = new Vector2(240, 0);
		selector.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		top.AddChild(selector);
		// Rename: a field in place of the selector's buttons.
		HBoxContainer renaming = new() { Visible = false };
		LineEdit newName = new() { Name = "New Name", CustomMinimumSize = new Vector2(220, 0) };
		StyleField(newName);
		renaming.AddChild(newName);
		// Delete of something in use: asked first.
		HBoxContainer asking = new() { Visible = false };
		Label question = InkLabel("");
		question.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		question.CustomMinimumSize = new Vector2(520, 0);
		asking.AddChild(question);

		HBoxContainer columns = new();
		columns.AddThemeConstantOverride("separation", 14);
		ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		scroll.AddChild(columns);

		void ShowItems() {
			IList items = Items();
			index = Math.Clamp(index, 0, Math.Max(0, items.Count - 1));
			selector.Clear();
			// A page may have none yet (the standard rules have no flavors).
			if (items.Count > 0) {
				SetupStyle.AddItems(selector, items.Cast<object>().Select(i => page.ItemName(save, i)).ToList(), index);
			}
		}
		void ShowFields() {
			foreach (Node child in columns.GetChildren()) {
				columns.RemoveChild(child);
				child.QueueFree();
			}
			IList items = Items();
			if (items.Count == 0) {
				return;
			}
			object item = items[index];
			Dictionary<int, VBoxContainer> byColumn = [];
			Dictionary<(int, string), GridContainer> groups = [];
			foreach (RuleField field in page.Fields) {
				if (!byColumn.TryGetValue(field.Column, out VBoxContainer column)) {
					column = new VBoxContainer();
					column.AddThemeConstantOverride("separation", 8);
					byColumn[field.Column] = column;
				}
				if (!groups.TryGetValue((field.Column, field.Group), out GridContainer grid)) {
					(Control groupBox, grid) = RuleGroup(field.Group);
					column.AddChild(groupBox);
					groups[(field.Column, field.Group)] = grid;
				}
				AddField(grid, page, field, save, item, report);
			}
			foreach (int c in byColumn.Keys.Order()) {
				columns.AddChild(byColumn[c]);
			}
		}
		void Show() {
			ShowItems();
			ShowFields();
			renaming.Visible = asking.Visible = false;
		}
		selector.ItemSelected += i => {
			index = (int)i;
			ShowFields();
		};

		if (page.Rename != null) {
			top.AddChild(TextButton("Rename", "Gives it another name.", () => {
				if (Current() == null) {
					return;
				}
				newName.Text = page.ItemName(save, Current());
				renaming.Visible = true;
				newName.GrabFocus();
			}).WithName("Rename"));
			renaming.AddChild(TextButton("OK", null, () => {
				string why = page.Rename(save, Current(), newName.Text);
				report(why);
				if (why == null) {
					Show();
				}
			}).WithName("Rename OK"));
			renaming.AddChild(TextButton("Cancel", null, () => renaming.Visible = false));
			top.AddChild(renaming);
		}
		if (page.Add != null) {
			top.AddChild(TextButton("Add", "Adds one like the one shown.", () => {
				object added = page.Add(save, Current());
				index = Items().IndexOf(added);
				report(null);
				Show();
			}).WithName("Add"));
		}
		if (page.Delete != null) {
			void DeleteNow() {
				string why = page.Delete(save, Current());
				report(why);
				if (why == null) {
					Show();
				}
			}
			top.AddChild(TextButton("Delete", "Deletes the one shown.", () => {
				if (Current() == null) {
					return;
				}
				List<string> uses = page.UsedBy?.Invoke(save, Current()) ?? [];
				if (uses.Count == 0) {
					DeleteNow();
					return;
				}
				question.Text = $"{page.ItemName(save, Current())} is used for {string.Join(", ", uses)}. Delete it, and clear those?";
				asking.Visible = true;
			}).WithName("Delete"));
			asking.AddChild(TextButton("Delete", null, DeleteNow).WithName("Delete Anyway"));
			asking.AddChild(TextButton("Cancel", null, () => asking.Visible = false));
		}
		tab.AddChild(asking);
		tab.AddChild(scroll);
		if (page.Name == "Civilization Advances") {
			AddScienceTree(tab, top, scroll, save, tech => {
				index = Items().IndexOf(tech);
				ShowItems();
				ShowFields();
			}, report);
		}
		Show();
		refresh = Show;
		return tab;
	}

	// Civ3's science advisor, an era at a time, in place of the fields:
	// drag an advance to place it (its X and Y); the one moved is shown.
	private void AddScienceTree(VBoxContainer tab, HBoxContainer top, Control fields, SaveGame save, Action<SaveTech> shown, Action<string> report) {
		VBoxContainer tree = new() { Visible = false, SizeFlagsVertical = SizeFlags.ExpandFill };
		OptionButton era = SetupStyle.MakeDropdown("The era shown");
		era.Name = "Tree Era";
		era.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		era.CustomMinimumSize = new Vector2(200, 0);
		string[] eras = [C7GameData.EraUtils.ANCIENT_TIMES_CVLPD, C7GameData.EraUtils.MIDDLE_AGES_CVLPD,
			C7GameData.EraUtils.INDUSTRIAL_AGE_CVLPD, C7GameData.EraUtils.MODERN_ERA_CVLPD];
		SetupStyle.AddItems(era, eras.Select(k => RulePages.EraName(save, k)).ToList(), 0);
		ScienceTreeView view = new(save, eras[0], (tech, x, y) => {
			shown(tech);
			report($"{tech.Name} at {x}, {y}.");
		});
		era.ItemSelected += i => {
			view.Era = eras[i];
			view.QueueRedraw();
		};
		tree.AddChild(era);
		tree.AddChild(view);
		tab.AddChild(tree);
		Button toggle = TextButton("Science Tree", "Lay the advances out by dragging, an era at a time (Civ3's science advisor).", () => {
			tree.Visible = !tree.Visible;
			fields.Visible = !tree.Visible;
			view.QueueRedraw();
		});
		toggle.Name = "Science Tree";
		top.AddChild(toggle);
	}

	// A node name from a label: Godot leaves out : / . @ % and quotes.
	public static string NodeName(string label) => new(label.Where(c => !":/.@%\"".Contains(c)).ToArray());

	// A field's label and control, which sets the value as it changes (or
	// shows why it can't).
	private void AddField(GridContainer grid, RulePage page, RuleField field, SaveGame save, object item, Action<string> report) {
		string tooltip = field.Help + (field.NotUsed ? "\n" + NotUsedYet : "");
		void Set(object value) => report(RuleEditing.SetValue(save, field, item, value));
		object value = field.Get(save, item);
		Label label = InkLabel(field.Kind == RuleFieldKind.Check ? "" : field.Label);
		label.TooltipText = tooltip;
		label.MouseFilter = MouseFilterEnum.Stop;
		if (field.NotUsed) {
			label.AddThemeColorOverride("font_color", SetupStyle.Faded);
		}
		Control control;
		switch (field.Kind) {
			case RuleFieldKind.Number: {
					SpinBox spin = Spin((int)value, field.Min, field.Max);
					spin.ValueChanged += v => Set((int)v);
					control = spin;
					break;
				}
			case RuleFieldKind.Text: {
					LineEdit text = new() { Text = (string)value, Editable = !field.ReadOnly, CustomMinimumSize = new Vector2(200, 0) };
					StyleField(text);
					text.TextChanged += t => Set(t);
					control = text;
					break;
				}
			case RuleFieldKind.Check: {
					CheckBox check = Check(field.Label, (bool)value);
					check.Toggled += on => Set(on);
					control = check;
					break;
				}
			case RuleFieldKind.Choice: {
					OptionButton choice = SetupStyle.MakeDropdown();
					choice.CustomMinimumSize = new Vector2(160, 0);
					List<string> names = field.Choices(save);
					List<string> shown = field.AllowNone ? ["None", .. names] : names;
					int at = value == null ? 0 : Math.Max(0, shown.IndexOf((string)value));
					SetupStyle.AddItems(choice, shown, at);
					choice.ItemSelected += i => Set(field.AllowNone && i == 0 ? null : shown[(int)i]);
					control = choice;
					break;
				}
			case RuleFieldKind.Choices: {
					ScrollContainer scroll = new() { CustomMinimumSize = new Vector2(220, 130) };
					VBoxContainer list = new();
					List<string> chosen = [.. (List<string>)value];
					foreach (string name in field.Choices(save)) {
						CheckBox box = Check(name, chosen.Contains(name));
						box.Toggled += on => {
							List<string> next = on ? [.. chosen, name] : chosen.Where(c => c != name).ToList();
							string why = RuleEditing.SetValue(save, field, item, next);
							report(why);
							if (why == null) {
								chosen = next;
							} else {
								box.SetPressedNoSignal(!on);
							}
						};
						list.AddChild(box);
					}
					scroll.AddChild(list);
					control = scroll;
					break;
				}
			case RuleFieldKind.Lines: {
					TextEdit lines = new() { Text = string.Join("\n", (List<string>)value), CustomMinimumSize = new Vector2(150, 110) };
					StyleField(lines);
					lines.AddThemeFontSizeOverride("font_size", 14);
					lines.TextChanged += () => Set(lines.Text.Split('\n').Select(l => l.Trim()).Reverse().SkipWhile(l => l == "").Reverse().ToList());
					control = lines;
					break;
				}
			case RuleFieldKind.Numbers when field.NumberLabels != null: {
					// A number for each name (a flavor's relationships, a
					// government's modifiers).
					List<int> numbers = [.. (List<int>)value];
					List<string> names = field.NumberLabels(save, item);
					GridContainer named = new() { Columns = 2 };
					named.AddThemeConstantOverride("v_separation", 2);
					for (int i = 0; i < names.Count && i < numbers.Count; ++i) {
						int at = i;
						named.AddChild(InkLabel(names[i]));
						SpinBox spin = Spin(numbers[i], field.Min, field.Max);
						spin.ValueChanged += v => {
							numbers[at] = (int)v;
							Set(new List<int>(numbers));
						};
						named.AddChild(spin);
					}
					control = named;
					break;
				}
			case RuleFieldKind.Numbers: {
					// Civ3's spaceship parts: how many kinds, then how many of
					// the one chosen.
					List<int> numbers = [.. (List<int>)value];
					HBoxContainer row = new();
					SpinBox kinds = Spin(numbers.Count, 0, 30);
					OptionButton which = SetupStyle.MakeDropdown();
					SpinBox needed = Spin(numbers.FirstOrDefault(), field.Min, field.Max);
					void Parts() {
						which.Clear();
						SetupStyle.AddItems(which, Enumerable.Range(1, numbers.Count).Select(n => $"Part {n}").ToList(), 0);
						needed.SetValueNoSignal(numbers.FirstOrDefault());
						needed.Editable = numbers.Count > 0;
					}
					kinds.ValueChanged += v => {
						while (numbers.Count < (int)v) {
							numbers.Add(1);
						}
						numbers.RemoveRange((int)v, numbers.Count - (int)v);
						Set(new List<int>(numbers));
						Parts();
					};
					which.ItemSelected += i => needed.SetValueNoSignal(numbers[(int)i]);
					needed.ValueChanged += v => {
						if (which.Selected >= 0 && which.Selected < numbers.Count) {
							numbers[which.Selected] = (int)v;
							Set(new List<int>(numbers));
						}
					};
					Parts();
					row.AddChild(kinds);
					row.AddChild(InkLabel("kinds; of"));
					row.AddChild(which);
					row.AddChild(needed);
					control = row;
					break;
				}
			default:
				throw new ArgumentException(field.Kind.ToString());
		}
		control.Name = NodeName(field.Label);
		control.TooltipText = tooltip;
		grid.AddChild(label);
		grid.AddChild(control);
	}
}
