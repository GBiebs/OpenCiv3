using System;
using System.Collections.Generic;
using C7Engine;
using C7GameData;
using Godot;

// The scenario editor's controls in one look (see EditorLook): its menus,
// tools, status line and dialogs. The ScenarioEditor keeps what they show
// and does what they ask.
public abstract partial class EditorChrome : Control {
	protected readonly ScenarioEditor editor;

	protected EditorChrome(ScenarioEditor editor) {
		this.editor = editor;
		Name = "Chrome";
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
	}

	// Builds the controls, with the menus (File, Edit, View, Map, Scenario,
	// Rules) wherever this look has them.
	public abstract void Build(List<PopupMenu> menus);

	public abstract void ShowTitle(string title);

	// What's on the tile under the mouse.
	public abstract void ShowTile(string text);

	public abstract void ShowTool(EditorTool tool, int size, bool grid);

	public abstract void ShowMessage(string text, bool problem);

	// Gives a menu this look.
	public abstract void StyleMenu(PopupMenu menu);

	// Civ3's Tile Properties dialog for a tile, which close closes. Shows
	// what's there; changing it comes with painting (Phase 1).
	public abstract Control TileProperties(Tile tile, Action close);

	protected static GameData Data => EngineStorage.gameData;

	protected void Run(ScenarioEditor.MenuItem item) => editor.RunMenuItem(item);

	// A dialog's backdrop: the map darkened, catching the clicks meant for
	// the map while the dialog is open.
	protected static Control Backdrop(Control dialog, float dim = 0.35f) {
		ColorRect backdrop = new() {
			Color = new Color(0, 0, 0, dim),
			MouseFilter = MouseFilterEnum.Stop,
		};
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		CenterContainer center = new() { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		center.AddChild(dialog);
		backdrop.AddChild(center);
		return backdrop;
	}

	// The status line's text: the brush and its size.
	protected static string ToolText(EditorTool tool, int size) {
		string name = EditorTools.Of(tool).Name;
		return tool == EditorTool.FogOfWar ? name : $"{name} brush, {size} x {size}";
	}
}
