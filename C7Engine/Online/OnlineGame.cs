using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using C7GameData;
using C7GameData.Save;
using Serilog;

namespace C7Engine.Online {
	// Online play, once the game has begun (the user's choice: Civ3's way).
	// Every game plays out the whole game: the host puts every game's orders
	// in one sequence and gives each its dice (a seed), and every game, the
	// host's too, plays them in that order (EngineStorage.sendOrder,
	// EnqueueSequenced), so all play out the same game. When an End Turn's
	// work is done (the turn's AIs), each game's fingerprint of the game
	// goes to the host; a game that has gone its own way is begun again from
	// the host's copy, as all of them are (Civ3's "resynchronizing"), so
	// their AIs' plans, which saves don't keep, are the same again.
	public abstract class OnlineGame : IDisposable {
		protected static readonly ILogger log = Log.ForContext<OnlineGame>();

		// The player this game plays.
		public ID localPlayer { get; protected set; }

		// Something to tell the player (who left, why an order was turned
		// down), and the games having gone their own ways.
		public event Action<string> Notice;
		public event Action OutOfSync;
		// The turn timer started: whose turn, and its seconds.
		public event Action<ID, int> TimerStarted;

		// From the sequence `seq` on (a game begun from the host's copy goes
		// on from the order it has).
		public void Begin(long seq = 0) {
			EngineStorage.ResetSequence(seq);
			EngineStorage.sendOrder = Submit;
			EngineStorage.onEndTurnDone = seq => Checked(seq, GameChecksum.Of(EngineStorage.gameData));
			Begun();
		}

		protected virtual void Begun() { }

		public virtual void Dispose() {
			EngineStorage.sendOrder = null;
			EngineStorage.onEndTurnDone = null;
			EngineStorage.ResetSequence();
		}

		protected abstract void Submit(MessageToEngine order);

		// Once a frame: what came over the network.
		public abstract void Poll(GameData gameData);

		protected abstract void Checked(long seq, string hash);

		// Said in the in-game chat, to the players of the civs `to` (none
		// for everyone); it comes back as a Notice.
		public abstract void Say(string text, IReadOnlyCollection<ID> to = null);

		// Who plays each human civ (nicknames), and who has lost their
		// connection: for the player list.
		private readonly Dictionary<ID, string> names = new();
		private readonly HashSet<ID> awayCivs = new();
		public IReadOnlyDictionary<ID, string> Names => names;
		public IReadOnlyCollection<ID> AwayCivs => awayCivs;
		public event Action PlayersChanged;

		protected void SetPlayers(IEnumerable<(ID civ, string name, bool away)> players) {
			names.Clear();
			awayCivs.Clear();
			foreach ((ID civ, string name, bool away) in players) {
				names[civ] = name;
				if (away) {
					awayCivs.Add(civ);
				}
			}
			PlayersChanged?.Invoke();
		}

		protected void Tell(string text) => Notice?.Invoke(text);

		protected void WentOutOfSync() => OutOfSync?.Invoke();

		protected void Timed(ID player, int seconds) => TimerStarted?.Invoke(player, seconds);

		// The save as it goes over the network, and back (no bigger than
		// `limit` once unpacked; the host's folders for its scenario don't
		// apply here).
		public static string Pack(SaveGame save) {
			using MemoryStream packed = new();
			using (GZipStream zip = new(packed, CompressionLevel.Optimal)) {
				zip.Write(save.ToJson());
			}
			return Convert.ToBase64String(packed.ToArray());
		}

		public static SaveGame Unpack(string text, int limit = 256 * 1024 * 1024) {
			using GZipStream zip = new(new MemoryStream(Convert.FromBase64String(text)), CompressionMode.Decompress);
			using MemoryStream json = new();
			byte[] buffer = new byte[81920];
			int read;
			while ((read = zip.Read(buffer)) > 0) {
				json.Write(buffer, 0, read);
				if (json.Length > limit) {
					throw new InvalidDataException("The game is too big");
				}
			}
			SaveGame save = SaveGame.FromJson(json.ToArray());
			save.ScenarioSearchPath = null;
			return save;
		}
	}

	// The game's fingerprint: its save's SHA-256.
	public static class GameChecksum {
		public static string Of(GameData gameData) => Convert.ToHexString(SHA256.HashData(SaveGame.FromGameData(gameData).ToJson()));
	}

	// The host's side: its own orders and the players' go in sequence.
	public sealed class HostGame : OnlineGame {
		private readonly OnlineHost host;
		// The player each connection plays.
		private readonly Dictionary<int, ID> players;
		private long seq;
		private readonly Dictionary<long, string> hashes = new();
		private readonly Dictionary<long, Dictionary<int, string>> theirHashes = new();

		public HostGame(OnlineHost host, ID localPlayer, Dictionary<int, ID> players, long seq = 0) {
			this.host = host;
			this.localPlayer = localPlayer;
			this.players = players;
			this.seq = seq;
		}

		public long Seq => seq;

		// Players who lost their connection (Civ3's "Waiting on Player"):
		// their civ and the nickname they played with, until they rejoin
		// with it or the host drops them; and those who rejoined, until
		// every game begins again from this one's copy (they get it then).
		private readonly Dictionary<ID, string> away = new();
		private readonly HashSet<ID> rejoining = new();
		public IReadOnlyDictionary<ID, string> Away => away;
		public event Action AwayChanged;
		// Someone rejoined: every game begins again from this copy.
		public event Action Rejoined;

		// The turn timer: whose turn it times (a human's, until they've
		// played), and when it's up.
		private ID timed;
		private int timedTurn;
		private DateTime timeUp;
		private bool timeUpSent;

		// The game to send each player, from the save this game was begun
		// from (the host begins from the same bytes).
		public void SendGame(string packedSave, bool resumed = true) {
			rejoining.Clear();
			foreach ((int connection, ID player) in players) {
				host.Send(connection, new GameStart { save = packedSave, you = player.ToString(), seq = seq, resumed = resumed });
			}
		}

		protected override void Submit(MessageToEngine order) {
			if (Allowed(localPlayer)) {
				Sequence(localPlayer, order);
			}
		}

		// Orders come from the player whose turn it is, or who the engine is
		// asking something (TurnHandling.HandControlTo makes them the one in
		// control); every game checks again when it plays one.
		private static bool Allowed(ID player) => TurnHandling.MayGiveOrders(EngineStorage.gameData, player);

		private void Sequence(ID from, MessageToEngine order) {
			seq++;
			int seed = RandomNumberGenerator.GetInt32(int.MaxValue);
			host.SendToAll(new SequencedOrder { seq = seq, seed = seed, from = from.ToString(), orderType = OrderCodec.Kind(order), order = OrderCodec.Write(order) });
			EngineStorage.EnqueueSequenced(seq, seed, from, order);
		}

		public override void Poll(GameData gameData) {
			WatchTheClock(gameData, DateTime.UtcNow);
			AnswerForTheAway();
			while (host.TryNextEvent(out HostEvent hostEvent)) {
				switch (hostEvent) {
					case MessageFrom { message: OrderRequest request } from when players.TryGetValue(from.player.id, out ID player): {
							MessageToEngine order = OrderCodec.Read(request.orderType, request.order, gameData);
							if (order == null) {
								log.Warning("Couldn't read {Kind} from {Name}", request.orderType, from.player.name);
							} else if (order.HostOnly || !Allowed(player)) {
								log.Information("{Kind} from {Name}, not in control: turned down", request.orderType, from.player.name);
							} else {
								Sequence(player, order);
							}
							break;
						}
					case MessageFrom { message: Checksum checksum } from:
						if (!theirHashes.TryGetValue(checksum.seq, out Dictionary<int, string> them)) {
							theirHashes[checksum.seq] = them = new();
						}
						them[from.player.id] = checksum.hash;
						Compare(checksum.seq);
						break;
					case MessageFrom { message: Chat chat } from when players.TryGetValue(from.player.id, out ID sender):
						Deliver(from.player.name, sender, chat.text.Trim(), chat.to);
						break;
					case PlayerJoined joined:
						Rejoin(joined.player);
						break;
					case PlayerLeft left when players.TryGetValue(left.player.id, out ID gone):
						players.Remove(left.player.id);
						away[gone] = left.player.name;
						Tell($"{left.player.name} has left the game.");
						host.SendToAll(new ChatLine { text = $"{left.player.name} has left the game. The game waits for them to reconnect, unless the host drops them." });
						AnnouncePlayers();
						AwayChanged?.Invoke();
						break;
				}
			}
		}

		protected override void Checked(long seq, string hash) {
			hashes[seq] = hash;
			Compare(seq);
		}

		// One who left joins again, with the nickname they played with: their
		// civ is theirs again (each game begins again from this copy, so
		// theirs is the same). Anyone else is turned away.
		private void Rejoin(RemotePlayer player) {
			ID civ = away.FirstOrDefault(a => a.Value == player.name).Key;
			if (civ == null) {
				host.Drop(player.id, away.Count > 0 ? "The game has already begun. To take your seat again, join with the nickname you played with."
					: "The game has already begun.");
				return;
			}
			away.Remove(civ);
			rejoining.Add(civ);
			players[player.id] = civ;
			Tell($"{player.name} has rejoined the game.");
			host.SendToAll(new ChatLine { text = $"{player.name} has rejoined the game." });
			host.Send(player.id, new LobbyNotice { text = "Rejoining the game..." });
			AnnouncePlayers();
			AwayChanged?.Invoke();
			Rejoined?.Invoke();
		}

		// The host gives up waiting for one who left: every game takes their
		// civilization out, at the same point.
		public void Drop(ID civ) {
			if (!away.Remove(civ, out string name)) {
				return;
			}
			Tell($"{name} has been dropped from the game.");
			host.SendToAll(new ChatLine { text = $"{name} has been dropped from the game." });
			Sequence(localPlayer, new MsgPlayerLeftGame(civ));
			AnnouncePlayers();
			AwayChanged?.Invoke();
		}

		// What the engine asks a player who's away (or hasn't the game back
		// yet) is answered for them as closing its popup would, so the game
		// doesn't wait on them for it.
		private void AnswerForTheAway() {
			if (EngineStorage.OrdersWaiting > 0) {
				return;
			}
			foreach (ID asked in away.Keys.Concat(rejoining).ToList()) {
				if (LeavingPlayers.AnswerFor(EngineStorage.WaitedForFrom(asked)) is MessageToEngine answer) {
					log.Information("Answering {Kind} for {Player}, who's away", answer.GetType().Name, asked);
					Sequence(asked, answer);
					return;
				}
			}
		}

		// Simultaneous moves: the turn timer times the round, as long as the
		// biggest empire's turn, and once it's up ends every turn still going.
		private void WatchTheRound(GameData gameData, DateTime now) {
			List<Player> playing = gameData.turnTimer == TurnTimer.None ? []
				: gameData.players.Where(p => TurnHandling.PlayingAtOnce(gameData, p.id)).ToList();
			if (playing.Count == 0) {
				timed = null;
				return;
			}
			if (timed == null || timedTurn != gameData.turn) {
				timed = playing[0].id;
				timedTurn = gameData.turn;
				timeUpSent = false;
				int seconds = playing.Max(p => TurnTimes.Seconds(gameData, p));
				timeUp = now.AddSeconds(seconds);
				host.SendToAll(new TurnTimerStarted { round = true, seconds = seconds });
				Timed(null, seconds);
				return;
			}
			if (!timeUpSent && now >= timeUp && EngineStorage.OrdersDone() && EngineStorage.OrdersWaiting == 0) {
				timeUpSent = true;
				log.Information("Time's up for the round");
				foreach (Player player in playing) {
					Sequence(localPlayer, new MsgTurnTimeUp(player.id, gameData.turn));
				}
			}
		}

		// (Begun again from this copy: the turn timer goes on, told again.)
		protected override void Begun() {
			AnnouncePlayers();
			if (timed != null) {
				int seconds = Math.Max(0, (int)Math.Ceiling((timeUp - DateTime.UtcNow).TotalSeconds));
				bool round = EngineStorage.gameData?.simultaneousMoves == true;
				host.SendToAll(new TurnTimerStarted { player = round ? null : timed.ToString(), round = round, seconds = seconds });
				Timed(round ? null : timed, seconds);
			}
		}

		// The turn timer: started when a human's turn begins (everyone is
		// told), and once it's up and the game is settled, every game ends
		// that turn.
		internal void WatchTheClock(GameData gameData, DateTime now) {
			if (gameData.simultaneousMoves) {
				WatchTheRound(gameData, now);
				return;
			}
			Player acting = gameData.turnTimer == TurnTimer.None ? null : gameData.GetPlayer(EngineStorage.uiControllerID);
			if (acting == null || !acting.isHuman || acting.hasPlayedThisTurn || acting.defeated) {
				timed = null;
				return;
			}
			if (timed != acting.id || timedTurn != gameData.turn) {
				timed = acting.id;
				timedTurn = gameData.turn;
				timeUpSent = false;
				int seconds = TurnTimes.Seconds(gameData, acting);
				timeUp = now.AddSeconds(seconds);
				host.SendToAll(new TurnTimerStarted { player = acting.id.ToString(), seconds = seconds });
				Timed(acting.id, seconds);
				return;
			}
			if (!timeUpSent && now >= timeUp && EngineStorage.OrdersDone() && EngineStorage.OrdersWaiting == 0) {
				timeUpSent = true;
				log.Information("Time's up for {Player}", acting);
				Sequence(localPlayer, new MsgTurnTimeUp(acting.id, gameData.turn));
			}
		}

		private void Compare(long seq) {
			if (!hashes.TryGetValue(seq, out string ours) || !theirHashes.TryGetValue(seq, out Dictionary<int, string> them)) {
				return;
			}
			if (them.Values.Any(h => h != ours)) {
				log.Warning("A game went out of sync after order {Seq}", seq);
				theirHashes.Remove(seq);
				WentOutOfSync();
			}
		}

		public override void Say(string text, IReadOnlyCollection<ID> to = null) {
			Deliver(host.hostName, localPlayer, text.Trim(), to?.Select(c => c.ToString()).ToList());
		}

		// A line said in the game: to everyone, or (Civ3's chat balloons) to
		// the players of the civs `to` only, and its sayer; the line says to
		// whom.
		private void Deliver(string name, ID sender, string text, List<string> to) {
			List<ID> recipients = to == null ? Names.Keys.ToList() : Names.Keys.Where(c => to.Contains(c.ToString())).ToList();
			string line = to == null ? $"{name}: {text}"
				: $"{name} (to {(recipients.Count == 0 ? "nobody" : string.Join(", ", recipients.Select(c => Names[c])))}): {text}";
			foreach ((int connection, ID civ) in players) {
				if (civ == sender || recipients.Contains(civ)) {
					host.Send(connection, new ChatLine { text = line });
				}
			}
			if (sender == localPlayer || recipients.Contains(localPlayer)) {
				Tell(line);
			}
		}

		// Everyone's player list: who plays each human civ, and who's away.
		private void AnnouncePlayers() {
			List<(ID civ, string name, bool away)> playing = [(localPlayer, host.hostName, false)];
			foreach ((int connection, ID civ) in players) {
				playing.Add((civ, host.Players.FirstOrDefault(p => p.id == connection)?.name ?? "?", false));
			}
			foreach ((ID civ, string name) in away) {
				playing.Add((civ, name, true));
			}
			SetPlayers(playing);
			host.SendToAll(new PlayersInGame { players = playing.Select(p => new PlayerInGame { civ = p.civ.ToString(), name = p.name, away = p.away }).ToList() });
		}

		public override void Dispose() {
			base.Dispose();
			host.Dispose();
		}
	}

	// A player's side: orders go to the host, and come back in sequence.
	public sealed class ClientGame : OnlineGame {
		private readonly OnlineClient client;

		// The game begun again from the host's copy (GameStart), for the
		// screens to load; and the host gone.
		public event Action<GameStart> Restart;
		public event Action<string> Ended;

		public ClientGame(OnlineClient client, ID localPlayer) {
			this.client = client;
			this.localPlayer = localPlayer;
		}

		protected override void Submit(MessageToEngine order) {
			client.Send(new OrderRequest { orderType = OrderCodec.Kind(order), order = OrderCodec.Write(order) });
		}

		public override void Poll(GameData gameData) {
			while (client.TryNextMessage(out OnlineMessage message)) {
				switch (message) {
					case SequencedOrder sequenced: {
							MessageToEngine order = OrderCodec.Read(sequenced.orderType, sequenced.order, gameData);
							if (order == null) {
								// (Not this game's copy any more: begin again.)
								log.Warning("Couldn't read the host's order {Seq} ({Kind})", sequenced.seq, sequenced.orderType);
								WentOutOfSync();
							} else {
								EngineStorage.EnqueueSequenced(sequenced.seq, sequenced.seed, ID.FromString(sequenced.from), order);
							}
							break;
						}
					case GameStart start:
						Restart?.Invoke(start);
						break;
					case ChatLine line:
						Tell(line.text);
						break;
					case TurnTimerStarted started:
						Timed(started.round ? null : ID.FromString(started.player), started.seconds);
						break;
					case PlayersInGame playing:
						SetPlayers(playing.players.Select(p => (ID.FromString(p.civ), p.name, p.away)));
						break;
					case Goodbye goodbye:
						Ended?.Invoke(goodbye.reason);
						break;
				}
			}
		}

		protected override void Checked(long seq, string hash) {
			client.Send(new Checksum { seq = seq, hash = hash });
		}

		public override void Say(string text, IReadOnlyCollection<ID> to = null) => client.Send(new Chat { text = text, to = to?.Select(c => c.ToString()).ToList() });

		public override void Dispose() {
			base.Dispose();
			client.Dispose();
		}
	}
}
