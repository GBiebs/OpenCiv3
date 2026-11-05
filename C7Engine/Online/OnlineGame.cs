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

		// From the sequence `seq` on (a game begun from the host's copy goes
		// on from the order it has).
		public void Begin(long seq = 0) {
			EngineStorage.ResetSequence(seq);
			EngineStorage.sendOrder = Submit;
			EngineStorage.onEndTurnDone = seq => Checked(seq, GameChecksum.Of(EngineStorage.gameData));
		}

		public virtual void Dispose() {
			EngineStorage.sendOrder = null;
			EngineStorage.onEndTurnDone = null;
			EngineStorage.ResetSequence();
		}

		protected abstract void Submit(MessageToEngine order);

		// Once a frame: what came over the network.
		public abstract void Poll(GameData gameData);

		protected abstract void Checked(long seq, string hash);

		// Said to everyone (the in-game chat); it comes back as a Notice.
		public abstract void Say(string text);

		protected void Tell(string text) => Notice?.Invoke(text);

		protected void WentOutOfSync() => OutOfSync?.Invoke();

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

		// The game to send each player, from the save this game was begun
		// from (the host begins from the same bytes).
		public void SendGame(string packedSave) {
			foreach ((int connection, ID player) in players) {
				host.Send(connection, new GameStart { save = packedSave, you = player.ToString(), seq = seq });
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
		private static bool Allowed(ID player) => player == EngineStorage.uiControllerID;

		private void Sequence(ID from, MessageToEngine order) {
			seq++;
			int seed = RandomNumberGenerator.GetInt32(int.MaxValue);
			host.SendToAll(new SequencedOrder { seq = seq, seed = seed, from = from.ToString(), orderType = OrderCodec.Kind(order), order = OrderCodec.Write(order) });
			EngineStorage.EnqueueSequenced(seq, seed, from, order);
		}

		public override void Poll(GameData gameData) {
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
					case MessageFrom { message: Chat chat } from:
						host.SendToAll(new ChatLine { text = $"{from.player.name}: {chat.text.Trim()}" });
						Tell($"{from.player.name}: {chat.text.Trim()}");
						break;
					case PlayerJoined joined:
						host.Drop(joined.player.id, "The game has already begun.");
						break;
					case PlayerLeft left when players.TryGetValue(left.player.id, out ID gone):
						players.Remove(left.player.id);
						Tell($"{left.player.name} has left the game.");
						host.SendToAll(new ChatLine { text = $"{left.player.name} has left the game." });
						// Every game takes their civilization out, at the same point.
						Sequence(localPlayer, new MsgPlayerLeftGame(gone));
						break;
				}
			}
		}

		protected override void Checked(long seq, string hash) {
			hashes[seq] = hash;
			Compare(seq);
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

		public override void Say(string text) {
			string name = host.hostName;
			host.SendToAll(new ChatLine { text = $"{name}: {text}" });
			Tell($"{name}: {text}");
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
					case Goodbye goodbye:
						Ended?.Invoke(goodbye.reason);
						break;
				}
			}
		}

		protected override void Checked(long seq, string hash) {
			client.Send(new Checksum { seq = seq, hash = hash });
		}

		public override void Say(string text) => client.Send(new Chat { text = text });

		public override void Dispose() {
			base.Dispose();
			client.Dispose();
		}
	}
}
