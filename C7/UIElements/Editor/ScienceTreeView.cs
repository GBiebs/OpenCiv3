using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData.Save;
using Godot;

// The rules editor's science tree (Civ3's science advisor, one era at a
// time): each advance a box at its X and Y, lines from its prerequisites.
// Dragging a box moves the advance (moved reports its new X and Y).
public partial class ScienceTreeView : Control {
	// The science advisor's page in Civ3's coordinates.
	private const float PageWidth = 1000, PageHeight = 720;
	private static readonly Vector2 BoxSize = new(118, 24);

	private readonly SaveGame save;
	private readonly Action<SaveTech, int, int> moved;
	public string Era;
	private SaveTech dragging;
	private Vector2 grab;

	public ScienceTreeView(SaveGame save, string era, Action<SaveTech, int, int> moved) {
		this.save = save;
		this.moved = moved;
		Era = era;
		Name = "ScienceTree";
		CustomMinimumSize = new Vector2(PageWidth * 0.9f, PageHeight * 0.62f);
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.ExpandFill;
		MouseFilter = MouseFilterEnum.Stop;
	}

	private float Scale => Mathf.Min(Size.X / PageWidth, Size.Y / PageHeight);

	private IEnumerable<SaveTech> Shown => save.Techs.Where(t => (t.EraCivilopediaName ?? "") == Era);

	// The advance's box on screen.
	public Rect2 BoxOf(SaveTech t) => new(new Vector2(t.X, t.Y) * Scale, BoxSize * Mathf.Max(Scale, 0.7f));

	public override void _Draw() {
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.97f, 0.93f, 0.82f));
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(SetupStyle.Brown, 0.7f), false, 1);
		Font font = GetThemeDefaultFont();
		List<SaveTech> shown = Shown.ToList();
		Dictionary<string, SaveTech> byId = save.Techs.Where(t => t.id != null).ToDictionary(t => t.id.ToString());
		foreach (SaveTech t in shown) {
			Rect2 box = BoxOf(t);
			foreach (var p in t.Prerequisites) {
				if (byId.TryGetValue(p.ToString(), out SaveTech pre) && (pre.EraCivilopediaName ?? "") == Era) {
					Rect2 from = BoxOf(pre);
					DrawLine(from.Position + new Vector2(from.Size.X, from.Size.Y / 2), box.Position + new Vector2(0, box.Size.Y / 2),
						new Color(SetupStyle.Brown, 0.8f), 1.5f);
				}
			}
		}
		foreach (SaveTech t in shown) {
			Rect2 box = BoxOf(t);
			DrawRect(box, t == dragging ? new Color(0.85f, 0.75f, 0.55f) : new Color(0.93f, 0.86f, 0.7f));
			DrawRect(box, SetupStyle.Ink, false, 1);
			DrawString(font, box.Position + new Vector2(4, box.Size.Y * 0.7f), t.Name, HorizontalAlignment.Left, box.Size.X - 6, 12, SetupStyle.Ink);
		}
	}

	public override void _GuiInput(InputEvent e) {
		if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) {
			if (button.Pressed) {
				dragging = Shown.LastOrDefault(t => BoxOf(t).HasPoint(button.Position));
				if (dragging != null) {
					grab = button.Position - BoxOf(dragging).Position;
				}
			} else if (dragging != null) {
				SaveTech done = dragging;
				dragging = null;
				moved(done, done.X, done.Y);
			}
			QueueRedraw();
			AcceptEvent();
		} else if (e is InputEventMouseMotion motion && dragging != null) {
			Vector2 at = (motion.Position - grab) / Scale;
			dragging.X = Math.Clamp((int)Mathf.Round(at.X), 0, (int)PageWidth);
			dragging.Y = Math.Clamp((int)Mathf.Round(at.Y), 0, (int)PageHeight);
			QueueRedraw();
			AcceptEvent();
		}
	}
}
