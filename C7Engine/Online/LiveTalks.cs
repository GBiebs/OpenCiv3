using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using C7GameData;

namespace C7Engine.Online {
	// Live diplomacy between humans online (Civ3's multiplayer diplomacy):
	// one sends an envoy, which the other hears (or refuses, or asks to be
	// contacted later); then both sit at the table, each putting up what
	// they give (the other's goods aren't shown), every change shown to the
	// other at once, with a face (pleased, neutral or angry) and a private
	// chat; each may accept the table as it stands, and take that back while
	// the other hasn't; a change to the table takes both acceptances back.
	// Once both have accepted the same table, every game makes the trade
	// (the host sequences it, MsgHumansTrade). The host keeps the talks;
	// the games only hear of them. A player is in one talk at a time.
	public sealed class LiveTalks {
		public const int MaxTerms = 16 * 1024;

		private sealed class Side {
			public string terms = "";
			public bool accepted;
			public int mood = Neutral;
		}

		private sealed class Talk {
			// Who sent the envoy, and to whom.
			public ID from;
			public ID to;
			public bool open;
			public readonly Dictionary<ID, Side> sides = new();
		}

		public const int Pleased = 0;
		public const int Neutral = 1;
		public const int Angry = 2;

		private readonly List<Talk> talks = new();
		private readonly Action<ID, OnlineMessage> send;
		private readonly Func<ID, ID, string> whyNot;
		private readonly Action<ID, string, ID, string> trade;

		// `send` a message to a civ's player; `whyNot` a civ may talk to
		// another (null if it may); `trade` the table both accepted.
		public LiveTalks(Action<ID, OnlineMessage> send, Func<ID, ID, string> whyNot, Action<ID, string, ID, string> trade) {
			this.send = send;
			this.whyNot = whyNot;
			this.trade = trade;
		}

		private Talk Of(ID civ) => talks.FirstOrDefault(t => t.from == civ || t.to == civ);

		private static ID Other(Talk talk, ID civ) => talk.from == civ ? talk.to : talk.from;

		private static ID Parse(string id) {
			try {
				return string.IsNullOrEmpty(id) ? null : ID.FromString(id);
			} catch (Exception) {
				return null;
			}
		}

		// What `civ`'s player says about it.
		public void Handle(ID civ, OnlineMessage message) {
			switch (message) {
				case TalkEnvoy envoy:
					Envoy(civ, Parse(envoy.with));
					break;
				case TalkAnswer answer:
					Answer(civ, Parse(answer.with), answer.answer);
					break;
				case TalkTable table:
					Table(civ, Parse(table.with), table);
					break;
				case TalkSay say when Of(civ) is Talk talk && talk.open && Other(talk, civ) == Parse(say.with):
					send(Other(talk, civ), new TalkSay { with = civ.ToString(), text = say.text.Trim() });
					break;
				case TalkEnd end when Of(civ) is Talk talk && Other(talk, civ) == Parse(end.with):
					talks.Remove(talk);
					send(Other(talk, civ), new TalkEnd { with = civ.ToString(), reason = talk.open || talk.from == civ ? TalkEnd.Cancelled : TalkEnd.Refused });
					break;
			}
		}

		// `civ`'s player is gone: their talk ends.
		public void Left(ID civ) {
			if (Of(civ) is Talk talk) {
				talks.Remove(talk);
				send(Other(talk, civ), new TalkEnd { with = civ.ToString(), reason = TalkEnd.Left });
			}
		}

		private void Envoy(ID from, ID to) {
			if (to == null) {
				return;
			}
			if (whyNot(from, to) is string problem) {
				send(from, new TalkEnd { with = to.ToString(), reason = TalkEnd.Unable, text = problem });
				return;
			}
			if (Of(from) != null || Of(to) != null) {
				send(from, new TalkEnd { with = to.ToString(), reason = TalkEnd.Busy });
				return;
			}
			Talk talk = new() { from = from, to = to };
			talk.sides[from] = new Side();
			talk.sides[to] = new Side();
			talks.Add(talk);
			send(to, new TalkEnvoy { with = from.ToString() });
		}

		private void Answer(ID civ, ID with, string answer) {
			if (Of(civ) is not Talk talk || talk.open || talk.to != civ || talk.from != with) {
				return;
			}
			if (answer == TalkAnswer.Hear) {
				talk.open = true;
				Tell(talk);
				return;
			}
			talks.Remove(talk);
			send(with, new TalkEnd { with = civ.ToString(), reason = answer == TalkAnswer.Later ? TalkEnd.Later : TalkEnd.Refused });
		}

		private void Table(ID civ, ID with, TalkTable table) {
			if (Of(civ) is not Talk talk || !talk.open || Other(talk, civ) != with) {
				return;
			}
			Side ours = talk.sides[civ];
			Side theirs = talk.sides[with];
			string terms = table.terms ?? "";
			if (terms != ours.terms) {
				ours.terms = terms;
				ours.accepted = false;
				theirs.accepted = false;
			}
			ours.accepted = table.accepted;
			ours.mood = Math.Clamp(table.mood, Pleased, Angry);
			if (ours.accepted && theirs.accepted) {
				talks.Remove(talk);
				trade(talk.from, talk.sides[talk.from].terms, talk.to, talk.sides[talk.to].terms);
				send(talk.from, new TalkEnd { with = talk.to.ToString(), reason = TalkEnd.Done });
				send(talk.to, new TalkEnd { with = talk.from.ToString(), reason = TalkEnd.Done });
				return;
			}
			Tell(talk);
		}

		// Each side: the other's half of the table, and who has accepted it.
		private void Tell(Talk talk) {
			foreach (ID civ in new[] { talk.from, talk.to }) {
				ID other = Other(talk, civ);
				Side ours = talk.sides[civ];
				Side theirs = talk.sides[other];
				send(civ, new TalkState { with = other.ToString(), theirTerms = theirs.terms, theyAccepted = theirs.accepted, weAccepted = ours.accepted, theirMood = theirs.mood, envoyFromUs = talk.from == civ });
			}
		}

		// A half of the table as it goes over the network, and back (what
		// isn't in this game left out).
		private static readonly JsonSerializerOptions plain = new() { IncludeFields = true, MaxDepth = 8, Converters = { new IDJsonConverter() } };

		public static string Write(TradeOffer offer) => offer == null || offer.IsEmpty() ? "" : JsonSerializer.Serialize(DealTerms.Of(offer), plain);

		public static TradeOffer Read(string terms, GameData gameData) {
			if (string.IsNullOrEmpty(terms) || terms.Length > MaxTerms) {
				return new TradeOffer();
			}
			try {
				return JsonSerializer.Deserialize<DealTerms>(terms, plain)?.ToOffer(gameData) ?? new TradeOffer();
			} catch (Exception) {
				return new TradeOffer();
			}
		}
	}
}
