using Godot;
using System;
using System.Diagnostics;
using C7GameData;
using Serilog;

public partial class ConfirmationPopup : Popup {
	private ILogger log = LogManager.ForContext<ConfirmationPopup>();

	Stopwatch loadTimer = new Stopwatch();
	string message;
	string yesText;
	string noText;
	Action yesAction;
	Action noAction;

	// Whether Escape answers no, for a question that must be answered (like
	// a deal someone is waiting on), rather than just closing the popup.
	public bool escapeMeansNo = false;

	// The advisor asking, and how they look: the Domestic advisor, surprised,
	// unless the question is another's (as script.txt's #advisor gives it).
	public AdvisorHead.Advisor advisor = AdvisorHead.Advisor.Domestic;
	public AdvisorHead.Mood mood = AdvisorHead.Mood.Surprised;

	public ConfirmationPopup(string message, string yesText, string noText, Action yesAction, Action noAction = null) {
		alignment = BoxContainer.AlignmentMode.End;
		margins = new Margins(right: 10);
		this.message = message;
		this.yesText = yesText;
		this.noText = noText;
		this.yesAction = yesAction;
		this.noAction = noAction;
	}

	public override void _EnterTree() {
		loadTimer.Start();
	}

	public override void _Ready() {
		base._Ready();

		TextureRect advisorHead = new TextureRect();
		advisorHead.Texture = AdvisorHead.GetPopupImage(advisor, mood, eraIndex: 0);
		//Appears at 400, 110 in game, but leftmost 25px are transparent with default graphics
		advisorHead.SetPosition(new Vector2(375, 0));
		AddChild(advisorHead);

		Label warningMessage = new();
		warningMessage.Text = message.TrimEnd();
		warningMessage.SetPosition(new Vector2(25, 170));
		AddChild(warningMessage);

		// The buttons go under the message, however many lines it has, and
		// the parchment grows to hold them (owner's report, 2026-10-28: the
		// game-over message's third line was drawn over the first button).
		int lines = Math.Max(1, warningMessage.Text.Split('\n').Length);
		int lineHeight = Math.Max(20, warningMessage.GetLineHeight());
		int yesTop = Math.Max(215, 170 + lines * lineHeight + 16);
		int noTop = yesTop + 30;
		int height = Math.Max(320, noTop + 40);

		// Dimensions in-game are 530x320, more for a long message; the top
		// 110px are for the advisor's head.
		// The size is set as a transparent texture would, so the parent can
		// align the popup.
		AddTexture(530, height);
		AddBackground(530, height - 110, 110);
		// Behind the message, which was added first to measure it.
		MoveChild(GetChild(GetChildCount() - 1), 1);

		AddHeader(advisor == AdvisorHead.Advisor.Culture ? "Cultural Advisor" : $"{advisor} Advisor", 120);

		AddButton(yesText, yesTop, confirmed);
		AddButton(noText, noTop, cancel);

		loadTimer.Stop();
		TimeSpan stopwatchElapsed = loadTimer.Elapsed;
		log.Verbose("Confirmation popup load time: " + Convert.ToInt32(stopwatchElapsed.TotalMilliseconds) + " ms");
	}

	// Hide first, so a popup the action shows isn't the one that gets hidden.
	private void confirmed() {
		GetParent().EmitSignal(PopupOverlay.SignalName.HidePopup);
		yesAction();
	}

	public override bool OnEscape() {
		if (escapeMeansNo) {
			cancel();
			return false;
		}
		return true;
	}

	private void cancel() {
		GetParent().EmitSignal(PopupOverlay.SignalName.HidePopup);
		noAction?.Invoke();
	}
}
