using C7GameData;
using Godot;

public static class AdvisorUtils {
	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);

	public static TextureButton CreateExitButton(Control parent, Vector2? position = null) {
		Vector2 buttonPosition = position ?? new Vector2(952, 720);

		TextureButton btn = new();
		TextureLoader.SetButtonTextures(btn, "ui.exit");
		btn.SetPosition(buttonPosition);
		parent.AddChild(btn);
		return btn;
	}

	public static TextureRect CreateAdvisorHead(Control parent, AdvisorHead.Advisor advisor, Vector2? position = null) {
		Vector2 headPosition = position ?? new Vector2(851, 0);

		TextureRect advisorHead = new();
		advisorHead.Texture = AdvisorHead.GetPopupImage(advisor, AdvisorHead.Mood.Happy, eraIndex: 0);
		advisorHead.SetPosition(headPosition);
		parent.AddChild(advisorHead);

		return advisorHead;
	}

	public static (TextureButton box, Label label) CreateAdvisorDialogBox(Control parent, Vector2? position = null) {
		Vector2 boxPosition = position ?? new Vector2(806, 110);
		Vector2 labelPosition = boxPosition + new Vector2(9, 9);

		ImageTexture dialogBoxTexture = TextureLoader.Load("advisors.dialog_box");
		TextureButton dialogBox = new TextureButton();
		dialogBox.TextureNormal = dialogBoxTexture;
		dialogBox.SetPosition(boxPosition);
		parent.AddChild(dialogBox);

		// The advice wraps inside the box (set before the size, so it wraps
		// at it rather than growing).
		Label dialogBoxLabel = new() {
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		dialogBoxLabel.AddThemeColorOverride("font_color", Ink);
		dialogBoxLabel.AddThemeFontSizeOverride("font_size", 14);
		dialogBoxLabel.SetPosition(labelPosition);
		dialogBoxLabel.Size = dialogBoxTexture.GetSize() - new Vector2(18, 18);
		parent.AddChild(dialogBoxLabel);

		return (dialogBox, dialogBoxLabel);
	}

	// What the advisor says, with the face to match.
	public static void ShowAdvice(TextureRect head, Label label, AdvisorHead.Advisor advisor, AdvisorAdvice.Advice advice, int eraIndex) {
		AdvisorHead.Mood mood = advice.mood switch {
			AdvisorAdvice.Mood.Sad => AdvisorHead.Mood.Sad,
			AdvisorAdvice.Mood.Angry => AdvisorHead.Mood.Angry,
			AdvisorAdvice.Mood.Surprised => AdvisorHead.Mood.Surprised,
			_ => AdvisorHead.Mood.Happy,
		};
		head.Texture = AdvisorHead.GetPopupImage(advisor, mood, eraIndex);
		label.Text = advice.text;
	}

	public static Label CreateAdvisorTitle(Control parent, float containerWidth, string advisorTitleString) {
		int bigFontSize = 26;
		// int middleFontSize = 20;
		int bigFontGlyphSpacing = 14;
		int bigFontGlyphSpaceSpacing = 22;

		FontFile regularFont = ResourceLoader.Load<FontFile>("res://Fonts/NotoSans-Regular.ttf");
		Theme regularBigFontTheme = new();
		regularBigFontTheme.DefaultFont = regularFont;
		regularBigFontTheme.SetFontSize("font_size", "Label", bigFontSize);

		FontVariation fontVariation = new FontVariation
		{
			BaseFont = regularFont,
			SpacingGlyph = bigFontGlyphSpacing,
			SpacingSpace = bigFontGlyphSpaceSpacing,
		};

		Theme regularThemeWithCustomSpacing = new Theme();
		regularThemeWithCustomSpacing.SetFont("font", "Label", fontVariation);
		regularThemeWithCustomSpacing.SetFontSize("font_size", "Label", bigFontSize);

		float advisorTitleStringWidth = GetStringSizeWithCustomSpacing(regularFont, advisorTitleString, bigFontSize,
			bigFontGlyphSpacing, bigFontGlyphSpaceSpacing).X;

		float advisorTitleOffsetLeft = (containerWidth / 2.0f) - (advisorTitleStringWidth) / 2.0f;

		Label advisorTitle = new() {
			Text = advisorTitleString,
			OffsetLeft = advisorTitleOffsetLeft,
			OffsetTop = 15,
			Theme = regularThemeWithCustomSpacing,
		};
		parent.AddChild(advisorTitle);

		return advisorTitle;
	}

	private static Vector2 GetStringSizeWithCustomSpacing(Font font, string input, int fontSize = 16, int glyphSpacing = 0, int glyphSpaceSpacing = 0) {

		float extraSpacing = 0.0f;
		for (int i = 0; i < input.Length; i++) {
			if (i < input.Length - 1) {
				if (char.IsWhiteSpace(input[i]) && glyphSpaceSpacing > 0) {
					extraSpacing += glyphSpaceSpacing;
				} else {
					extraSpacing += glyphSpacing;
				}
			}
		}

		Vector2 originalSize = font.GetStringSize(input, fontSize: fontSize);
		return new Vector2(originalSize.X + extraSpacing, originalSize.Y);
	}
}
