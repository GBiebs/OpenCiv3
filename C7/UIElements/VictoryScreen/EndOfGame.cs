using System;
using System.Collections.Generic;
using C7Engine;
using C7GameData;
using Godot;

// What Civ3 shows when a game ends (Civ3 FUN_004f9fb0): the announcement
// ("You Win!!" or "We are disgraced!", script.txt's END_GAME_WIN,
// END_GAME_WIN2 and END_GAME_LOSS), then the victory screen, then the
// replay of the game, and in the end the Hall of Fame. Civ3 also shows the
// Demographics (DEMO_WIN, FUN_00506a00), the Histograph (FUN_005509c0) and,
// in a single player game, the Historical Ranking (FUN_00596ab0) between
// the replay and the Hall of Fame; those aren't part of this yet. After a
// win or a loss, Civ3 asks whether to keep playing (script.txt's
// GAMEOVERMAN, FUN_004fad70), which ShowGameOver does.
//
// All the words come from the Civ3 install; without it (standalone mode),
// they're OpenCiv3's own (OwnEndGameWording), and the screen is drawn in our
// own art (see VictoryScreen). Each page goes on to the next with a click,
// Enter or Space.
public static class EndOfGame {
	// The end-of-game replay (Civ3's REPLAY_WIN, FUN_00599be0), shown after
	// the victory screen. Whoever provides it sets this: it's given the
	// node to show the replay under and what to do once the replay is
	// closed, which it must call. While it's unset, there's no replay.
	public static Action<Node, Action> ShowReplay;

	// Above the hotseat curtain, below the LAN reconnecting one.
	private const int ScreenLayer = 105;

	// Whether the end of the game is being shown, from the announcement
	// until the Hall of Fame closes, so the game underneath takes no keys
	// (Enter would end the turn, Escape open the menu).
	public static bool InProgress { get; private set; }

	// The end of the game's hold on the screen and the game (see
	// ScreenClaim), from the announcement until the player answers whether
	// to play on: nothing else the game has to show comes up in between,
	// and the engine waits. Playing on (ShowGameOver, PlayOn) releases it,
	// and what the game held back then comes up in order; leaving for the
	// main menu drops it all with the game.
	public static ScreenClaim Claim { get; private set; }

	// Shows the end of the game for the player playing here: the
	// announcement, the victory screen, the replay and (if the game went
	// into it) the Hall of Fame, then calls finished. Returns false, having
	// shown nothing, if there's no human player here to show it to or the
	// screen can't be built. A null reason is a loss with no winner, as
	// when every human player has been destroyed.
	public static bool Show(Node host, PopupOverlay overlay, GameEndReason? reason, Player winner, bool showHallOfFame,
			Action finished) {
		Civ3EndGameText text = LoadText() ?? Civ3EndGameText.OwnWording();
		VictoryScreen screen = null;
		(string caption, string message) announcement = (null, null);
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = ViewingPlayer(gameData, winner, reason);
			if (player == null) {
				return;
			}
			// A team wins together (see GameSetup.Teams.cs), so a
			// teammate's victory is shown as the player's own.
			Player shownWinner = gameData.AreTeammates(player, winner) ? player : winner;
			// UNVERIFIED: that Civ3 shows its defeat screen when every human
			// player has been destroyed.
			screen = VictoryScreen.TryCreate(gameData, player, shownWinner, reason ?? GameEndReason.Conquest, text);
			announcement = Announcement(text, reason, player, shownWinner);
		});
		if (screen == null || announcement.message == null) {
			screen?.Free();
			return false;
		}

		// Anything already up waits behind the announcement, and anything
		// the game has yet to show waits until the player plays on.
		Claim = overlay.Claims.Claim("end of game", holdsGame: true);
		ScreenClaim claim = Claim;
		InProgress = true;
		void Finish() {
			InProgress = false;
			finished();
		}

		void ShowHallOfFame() {
			if (!showHallOfFame) {
				Finish();
				return;
			}
			ShowOnTop(host, new HallOfFameScreen { EndOfGame = true }, s => s.Closed += Finish);
		}

		void ShowReplayThenHallOfFame() {
			if (ShowReplay != null) {
				ShowReplay(host, ShowHallOfFame);
			} else {
				ShowHallOfFame();
			}
		}

		overlay.ShowPopup(new EndOfGamePopup(announcement.caption, announcement.message, () => {
			ShowOnTop(host, screen, s => s.Closed += ShowReplayThenHallOfFame);
		}), PopupOverlay.PopupCategory.Advisor, claim);
		return true;
	}

	// The player plays on after the end of the game: what the game held
	// back comes up, in order, and the game goes on.
	public static void PlayOn() {
		ScreenClaim claim = Claim;
		Claim = null;
		claim?.Release();
	}

	// labels.txt and script.txt from the Civ3 install (or a scenario's), or
	// null if they aren't there.
	private static Civ3EndGameText LoadText() {
		if (C7Settings.UseStandaloneMode() || !Util.HasCiv3Media("Text/labels.txt") || !Util.HasCiv3Media("Text/script.txt")) {
			return null;
		}
		return Civ3EndGameText.Load(Util.Civ3MediaPath("Text/labels.txt"), Util.Civ3MediaPath("Text/script.txt"));
	}

	// Shows a full screen above the game, taking it away again when it
	// closes.
	private static void ShowOnTop<T>(Node host, T screen, Action<T> whenShown) where T : Control {
		CanvasLayer layer = new() { Layer = ScreenLayer };
		layer.AddChild(screen);
		host.AddChild(layer);
		screen.TreeExiting += () => layer.QueueFree();
		whenShown(screen);
	}

	// Who the end of the game is shown to: the player playing here, but in
	// a hotseat game the winner, if they're human (Civ3 FUN_004f9fb0).
	private static Player ViewingPlayer(GameData gameData, Player winner, GameEndReason? reason) {
		Player player = gameData.GetUIControllerPlayer();
		if (reason != GameEndReason.Retirement && winner != null && winner.isHuman && !LanSession.IsActive) {
			return winner;
		}
		return player != null && player.isHuman ? player : null;
	}

	// The announcement's caption and text, like "You Win!!" and
	// "Congratulations Caesar!  You have achieved a Domination Victory.",
	// or nulls if script.txt doesn't have it.
	private static (string caption, string message) Announcement(Civ3EndGameText text, GameEndReason? reason,
			Player player, Player winner) {
		bool humiliating = Random.Shared.Next(2) == 1;
		(string key, int? typeLabel) = VictoryScreenLayout.Announcement(reason, player == winner, humiliating);
		Civ3EndGameText.ScriptPopup popup = text.Popup(key);
		if (popup == null) {
			return (null, null);
		}
		// $WHO1 is the player's leader (Civ3 FUN_00566180).
		string message = popup.Text.Replace("$WHO1", player.civilization?.leader ?? "");
		if (typeLabel is int label) {
			message = message.Replace("$TYPE0", text.Label(label) ?? "");
		}
		return (popup.Caption, message.Trim());
	}

	// Civ3's question once the game is over (script.txt's GAMEOVERMAN):
	// "So, this game is, like, over. No further score will be entered.",
	// answered "Good! I'm done!" (done) or "Wait, lemme just play 'couple
	// more turns..." (keepPlaying, also what Escape picks). Without the
	// Civ3 text, OpenCiv3's own wording.
	// It's the last page of the end of the game, so playing on lets the game
	// go on (see Claim); done keeps it held on the way to the main menu.
	public static void ShowGameOver(PopupOverlay overlay, Action done, Action keepPlaying = null) {
		ScreenClaim claim = Claim;
		void PlayOnThen() {
			PlayOn();
			keepPlaying?.Invoke();
		}
		Civ3EndGameText.ScriptPopup popup = LoadText()?.Popup("GAMEOVERMAN");
		if (popup == null || popup.Choices.Count < 2) {
			overlay.ShowPopup(new ConfirmationPopup(
					"This game is over: No further score will be entered.\n\n",
					"Good! I’m Done!",
					"Wait, lemme just play a couple of more turns...",
					done, PlayOnThen),
				PopupOverlay.PopupCategory.Advisor, claim);
			return;
		}
		overlay.ShowPopup(new ChoicePopup(popup.Caption, popup.Text.Trim(), [
				new ChoicePopup.Choice(popup.Choices[0], done),
				new ChoicePopup.Choice(popup.Choices[1], PlayOnThen),
			], cancellable: false),
			PopupOverlay.PopupCategory.Advisor, claim);
	}
}

// Civ3's announcement of how the game ended: its caption, its text and a
// button to go on.
public partial class EndOfGamePopup : Popup {
	private readonly string caption;
	private readonly string message;
	private readonly Action next;
	private bool answered;

	public EndOfGamePopup(string caption, string message, Action next) {
		alignment = BoxContainer.AlignmentMode.Center;
		margins = new Margins(top: 100);
		this.caption = caption;
		this.message = message;
		this.next = next;
	}

	// The game goes on to the victory screen however the popup is closed:
	// Escape, Enter, Space or the button.
	public override bool OnEscape() {
		Answer();
		return false;
	}

	public override void _UnhandledInput(InputEvent @event) {
		if (@event is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Space }) {
			GetViewport().SetInputAsHandled();
			Answer();
		}
	}

	private void Answer() {
		if (answered) {
			return;
		}
		answered = true;
		GetParent().EmitSignal(PopupOverlay.SignalName.HidePopup);
		next();
	}

	public override void _Ready() {
		base._Ready();

		const int width = 500;
		Label messageLabel = new() {
			Text = message,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center,
			Position = new Vector2(25, 60),
			Size = new Vector2(width - 50, 0),
		};
		AddChild(messageLabel);
		int height = 120 + Math.Max(1, messageLabel.GetLineCount()) * messageLabel.GetLineHeight();

		AddTexture(width, height);
		AddBackground(width, height);
		AddHeader(caption, 10);
		MoveChild(messageLabel, -1);
		AddConfirmButton(new Vector2(width - 40, height - 40), Answer);
	}
}
