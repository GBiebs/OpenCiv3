using System.Collections.Generic;
using C7Engine;
using Godot;

// The advisors down an advisor screen's left border, to switch between them
// as in Civ3: each advisor's face and name, the one shown marked.
public partial class AdvisorList : Control {
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	private static readonly Color Marked = new(0.62f, 0.08f, 0.05f);
	private const float Spacing = 86, Width = 60;

	private static readonly List<(AdvisorHead.Advisor advisor, string name, string action, string key)> Entries = [
		(AdvisorHead.Advisor.Domestic, "Domestic", C7Action.ShowDomesticAdvisor, "F1"),
		(AdvisorHead.Advisor.Trade, "Trade", C7Action.ShowTradeAdvisor, "F2"),
		(AdvisorHead.Advisor.Military, "Military", C7Action.ShowMilitaryAdvisor, "F3"),
		(AdvisorHead.Advisor.Foreign, "Foreign", C7Action.ShowForeignAdvisor, "F4"),
		(AdvisorHead.Advisor.Culture, "Cultural", C7Action.ShowCulturalAdvisor, "F5"),
		(AdvisorHead.Advisor.Science, "Science", C7Action.ShowScienceAdvisor, "F6"),
	];

	private readonly AdvisorHead.Advisor shown;
	private readonly List<TextureRect> faces = new();

	public AdvisorList() : this(AdvisorHead.Advisor.None) { }

	public AdvisorList(AdvisorHead.Advisor shown) {
		this.shown = shown;
		MouseFilter = MouseFilterEnum.Pass;
		Position = new Vector2(2, 88);
		Size = new Vector2(Width, Spacing * Entries.Count);
	}

	public override void _Ready() {
		for (int i = 0; i < Entries.Count; i++) {
			(AdvisorHead.Advisor advisor, string name, string action, string key) = Entries[i];
			float y = i * Spacing;
			bool marked = advisor == shown;
			if (marked) {
				AddChild(new ColorRect { Color = new Color(0.36f, 0.25f, 0.12f, 0.25f), Position = new Vector2(0, y - 3), Size = new Vector2(Width, 68), MouseFilter = MouseFilterEnum.Ignore });
			}
			TextureRect face = new() {
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				Position = new Vector2(1, y),
				Size = new Vector2(Width - 2, 44),
				MouseFilter = MouseFilterEnum.Ignore,
			};
			faces.Add(face);
			AddChild(face);
			Label label = new() {
				Text = name,
				HorizontalAlignment = HorizontalAlignment.Center,
				Position = new Vector2(0, y + 45),
				Size = new Vector2(Width, 16),
				MouseFilter = MouseFilterEnum.Ignore,
			};
			label.AddThemeColorOverride("font_color", marked ? Marked : Ink);
			label.AddThemeFontSizeOverride("font_size", 11);
			AddChild(label);
			Button pick = new() {
				Flat = true,
				Position = new Vector2(0, y - 3),
				Size = new Vector2(Width, 68),
				FocusMode = FocusModeEnum.None,
				TooltipText = $"{name} Advisor ({key})",
			};
			pick.Pressed += () => Ancestor()?.ShowAdvisor(action);
			AddChild(pick);
		}
	}

	// The faces for the player's era.
	public void Refresh(int eraIndex) {
		for (int i = 0; i < faces.Count; i++) {
			faces[i].Texture = AdvisorHead.GetPopupImage(Entries[i].advisor, AdvisorHead.Mood.Happy, eraIndex);
		}
	}

	private Advisors Ancestor() {
		Node node = GetParent();
		while (node != null && node is not Advisors) {
			node = node.GetParent();
		}
		return node as Advisors;
	}
}
