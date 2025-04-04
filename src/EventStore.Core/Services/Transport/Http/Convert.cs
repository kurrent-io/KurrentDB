// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Http.Controllers;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Atom;
using EventStore.Transport.Http.Codecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EventStore.Core.Services.Transport.Http;

public static class Convert {
	private static readonly string AllEscaped = Uri.EscapeDataString("$all");
	private static readonly string AllFilteredEscaped = "%24all/filtered";

	public static FeedElement ToStreamEventForwardFeed(ClientMessage.ReadStreamEventsForwardCompleted msg,
		Uri requestedUrl, EmbedLevel embedContent) {
		Ensure.NotNull(msg, "msg");

		string escapedStreamId = Uri.EscapeDataString(msg.EventStreamId);
		var self = HostName.Combine(requestedUrl, "/streams/{0}", escapedStreamId);
		var feed = new FeedElement();
		feed.SetTitle(string.Format("Event stream '{0}'", msg.EventStreamId));
		feed.StreamId = msg.EventStreamId;
		feed.SetId(self);
		feed.SetUpdated(msg.Events.Length > 0 && msg.Events[0].Event != null
			? msg.Events[0].Event.TimeStamp
			: DateTime.MinValue.ToUniversalTime());
		feed.SetAuthor(AtomSpecs.Author);
		feed.SetHeadOfStream(msg.IsEndOfStream);

		var prevEventNumber = Math.Min(msg.FromEventNumber + msg.MaxCount - 1, msg.LastEventNumber) + 1;
		var nextEventNumber = msg.FromEventNumber - 1;

		feed.AddLink("self", self);
		feed.AddLink("first",
			HostName.Combine(requestedUrl, "/streams/{0}/head/backward/{1}", escapedStreamId, msg.MaxCount));
		if (nextEventNumber >= 0) {
			feed.AddLink("last",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", escapedStreamId, 0, msg.MaxCount));
			feed.AddLink("next",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/backward/{2}", escapedStreamId, nextEventNumber,
					msg.MaxCount));
		}

		if (!msg.IsEndOfStream || msg.Events.Length > 0)
			feed.AddLink("previous",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", escapedStreamId, prevEventNumber,
					msg.MaxCount));
		if (!escapedStreamId.StartsWith("$$"))
			feed.AddLink("metadata", HostName.Combine(requestedUrl, "/streams/{0}/metadata", escapedStreamId));
		for (int i = msg.Events.Length - 1; i >= 0; --i) {
			feed.AddEntry(ToEntry(msg.Events[i], requestedUrl, embedContent));
		}

		return feed;
	}

	public static FeedElement ToStreamEventBackwardFeed(ClientMessage.ReadStreamEventsBackwardCompleted msg,
		Uri requestedUrl, EmbedLevel embedContent, bool headOfStream) {
		Ensure.NotNull(msg, "msg");

		string escapedStreamId = Uri.EscapeDataString(msg.EventStreamId);
		var self = HostName.Combine(requestedUrl, "/streams/{0}", escapedStreamId);
		var feed = new FeedElement();
		feed.SetTitle(string.Format("Event stream '{0}'", msg.EventStreamId));
		feed.StreamId = msg.EventStreamId;
		feed.SetId(self);
		feed.SetUpdated(msg.Events.Length > 0 && msg.Events[0].Event != null
			? msg.Events[0].Event.TimeStamp
			: DateTime.MinValue.ToUniversalTime());
		feed.SetAuthor(AtomSpecs.Author);
		feed.SetHeadOfStream(headOfStream); //TODO AN: remove this ?
		feed.SetSelfUrl(self);
		//TODO AN: remove this ?
		if (headOfStream) //NOTE: etag workaround - to be fixed with better http handling model
			feed.SetETag(Configure.GetPositionETag(msg.LastEventNumber, ContentType.AtomJson));

		var prevEventNumber = Math.Min(msg.FromEventNumber, msg.LastEventNumber) + 1;
		var nextEventNumber = msg.FromEventNumber - msg.MaxCount;

		feed.AddLink("self", self);
		feed.AddLink("first",
			HostName.Combine(requestedUrl, "/streams/{0}/head/backward/{1}", escapedStreamId, msg.MaxCount));
		if (!msg.IsEndOfStream) {
			if (nextEventNumber < 0)
				throw new Exception(string.Format("nextEventNumber is negative: {0} while IsEndOfStream",
					nextEventNumber));
			feed.AddLink("last",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", escapedStreamId, 0, msg.MaxCount));
			feed.AddLink("next",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/backward/{2}", escapedStreamId, nextEventNumber,
					msg.MaxCount));
		}

		feed.AddLink("previous",
			HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", escapedStreamId, prevEventNumber,
				msg.MaxCount));
		feed.AddLink("metadata", HostName.Combine(requestedUrl, "/streams/{0}/metadata", escapedStreamId));
		for (int i = 0; i < msg.Events.Length; ++i) {
			feed.AddEntry(ToEntry(msg.Events[i], requestedUrl, embedContent));
		}

		return feed;
	}

	public static FeedElement ToAllEventsForwardFeed(ClientMessage.ReadAllEventsForwardCompleted msg,
		Uri requestedUrl, EmbedLevel embedContent) {
		var self = HostName.Combine(requestedUrl, "/streams/{0}", AllEscaped);
		var feed = new FeedElement();
		feed.SetTitle("All events");
		feed.SetId(self);
		feed.SetUpdated(msg.Events.Length > 0 && msg.Events[0].Event != null
			? msg.Events[msg.Events.Length - 1].Event.TimeStamp
			: DateTime.MinValue.ToUniversalTime());
		feed.SetAuthor(AtomSpecs.Author);

		feed.AddLink("self", self);
		feed.AddLink("first",
			HostName.Combine(requestedUrl, "/streams/{0}/head/backward/{1}", AllEscaped, msg.MaxCount));
		if (msg.CurrentPos.CommitPosition != 0) {
			feed.AddLink("last",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", AllEscaped,
					new TFPos(0, 0).AsString(), msg.MaxCount));
			feed.AddLink("next",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/backward/{2}", AllEscaped, msg.PrevPos.AsString(),
					msg.MaxCount));
		}

		if (!msg.IsEndOfStream || msg.Events.Length > 0)
			feed.AddLink("previous",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", AllEscaped, msg.NextPos.AsString(),
					msg.MaxCount));
		feed.AddLink("metadata", HostName.Combine(requestedUrl, "/streams/{0}/metadata", AllEscaped));
		for (int i = msg.Events.Length - 1; i >= 0; --i) {
			feed.AddEntry(ToEntry(msg.Events[i].WithoutPosition(), requestedUrl, embedContent));
		}

		return feed;
	}

	public static FeedElement ToAllEventsForwardFilteredFeed(ClientMessage.FilteredReadAllEventsForwardCompleted msg,
		Uri requestedUrl, EmbedLevel embedContent) {
		var self = HostName.Combine(requestedUrl, "/streams/{0}", AllFilteredEscaped);
		var feed = new FeedElement();
		feed.SetTitle("All events");
		feed.SetId(self);
		feed.SetUpdated(msg.Events.Length > 0 && msg.Events[0].Event != null
			? msg.Events[msg.Events.Length - 1].Event.TimeStamp
			: DateTime.MinValue.ToUniversalTime());
		feed.SetAuthor(AtomSpecs.Author);

		feed.AddLink("self", self);
		feed.AddLink("first",
			HostName.Combine(requestedUrl, "/streams/{0}/head/backward/{1}", AllFilteredEscaped, msg.MaxCount));
		if (msg.CurrentPos.CommitPosition != 0) {
			feed.AddLink("last",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", AllFilteredEscaped,
					new TFPos(0, 0).AsString(), msg.MaxCount));
			feed.AddLink("next",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/backward/{2}", AllFilteredEscaped, msg.PrevPos.AsString(),
					msg.MaxCount));
		}

		if (!msg.IsEndOfStream || msg.Events.Length > 0)
			feed.AddLink("previous",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", AllFilteredEscaped, msg.NextPos.AsString(),
					msg.MaxCount));
		for (int i = msg.Events.Length - 1; i >= 0; --i) {
			feed.AddEntry(ToEntry(msg.Events[i].WithoutPosition(), requestedUrl, embedContent));
		}

		return feed;
	}

	public static FeedElement ToAllEventsBackwardFeed(ClientMessage.ReadAllEventsBackwardCompleted msg,
		Uri requestedUrl, EmbedLevel embedContent) {
		var self = HostName.Combine(requestedUrl, "/streams/{0}", AllEscaped);
		var feed = new FeedElement();
		feed.SetTitle(string.Format("All events"));
		feed.SetId(self);
		feed.SetUpdated(msg.Events.Length > 0 && msg.Events[0].Event != null
			? msg.Events[0].Event.TimeStamp
			: DateTime.MinValue.ToUniversalTime());
		feed.SetAuthor(AtomSpecs.Author);

		feed.AddLink("self", self);
		feed.AddLink("first",
			HostName.Combine(requestedUrl, "/streams/{0}/head/backward/{1}", AllEscaped, msg.MaxCount));
		if (!msg.IsEndOfStream) {
			feed.AddLink("last",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", AllEscaped,
					new TFPos(0, 0).AsString(), msg.MaxCount));
			feed.AddLink("next",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/backward/{2}", AllEscaped, msg.NextPos.AsString(),
					msg.MaxCount));
		}

		feed.AddLink("previous",
			HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", AllEscaped, msg.PrevPos.AsString(),
				msg.MaxCount));
		feed.AddLink("metadata", HostName.Combine(requestedUrl, "/streams/{0}/metadata", AllEscaped));
		for (int i = 0; i < msg.Events.Length; ++i) {
			feed.AddEntry(ToEntry(msg.Events[i].WithoutPosition(), requestedUrl, embedContent));
		}

		return feed;
	}

	public static FeedElement ToFilteredAllEventsBackwardFeed(ClientMessage.FilteredReadAllEventsBackwardCompleted msg,
		Uri requestedUrl, EmbedLevel embedContent) {
		var self = HostName.Combine(requestedUrl, "/streams/{0}", AllFilteredEscaped);
		var feed = new FeedElement();
		feed.SetTitle(string.Format("All events"));
		feed.SetId(self);
		feed.SetUpdated(msg.Events.Length > 0 && msg.Events[0].Event != null
			? msg.Events[0].Event.TimeStamp
			: DateTime.MinValue.ToUniversalTime());
		feed.SetAuthor(AtomSpecs.Author);

		feed.AddLink("self", self);
		feed.AddLink("first",
			HostName.Combine(requestedUrl, "/streams/{0}/head/backward/{1}", AllFilteredEscaped, msg.MaxCount));
		if (!msg.IsEndOfStream) {
			feed.AddLink("last",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", AllFilteredEscaped,
					new TFPos(0, 0).AsString(), msg.MaxCount));
			feed.AddLink("next",
				HostName.Combine(requestedUrl, "/streams/{0}/{1}/backward/{2}", AllFilteredEscaped, msg.NextPos.AsString(),
					msg.MaxCount));
		}

		feed.AddLink("previous",
			HostName.Combine(requestedUrl, "/streams/{0}/{1}/forward/{2}", AllFilteredEscaped, msg.PrevPos.AsString(),
				msg.MaxCount));
		for (int i = 0; i < msg.Events.Length; ++i) {
			feed.AddEntry(ToEntry(msg.Events[i].WithoutPosition(), requestedUrl, embedContent));
		}

		return feed;
	}

	public static FeedElement ToNextNPersistentMessagesFeed(ClientMessage.ReadNextNPersistentMessagesCompleted msg,
		Uri requestedUrl, string streamId, string groupName, int count, EmbedLevel embedContent) {
		string escapedStreamId = Uri.EscapeDataString(streamId);
		string escapedGroupName = Uri.EscapeDataString(groupName);
		var self = HostName.Combine(requestedUrl, "/subscriptions/{0}/{1}", escapedStreamId, escapedGroupName);
		var feed = new FeedElement();
		feed.SetTitle(string.Format("Messages for '{0}/{1}'", streamId, groupName));
		feed.SetId(self);
		feed.SetUpdated(msg.Events.Length > 0 && msg.Events[0].ResolvedEvent.Event != null
			? msg.Events[msg.Events.Length - 1].ResolvedEvent.Event.TimeStamp
			: DateTime.MinValue.ToUniversalTime());
		feed.SetAuthor(AtomSpecs.Author);

		if (msg.Events != null && msg.Events.Length > 0) {
			var ackAllQueryString = String.Format("?ids={0}",
				String.Join(",", msg.Events.Select(x => x.ResolvedEvent.OriginalEvent.EventId)));
			var ackAll =
				HostName.Combine(requestedUrl, "/subscriptions/{0}/{1}/ack", escapedStreamId, escapedGroupName) +
				ackAllQueryString;
			feed.AddLink("ackAll", ackAll);

			var nackAllQueryString = String.Format("?ids={0}",
				String.Join(",", msg.Events.Select(x => x.ResolvedEvent.OriginalEvent.EventId)));
			var nackAll =
				HostName.Combine(requestedUrl, "/subscriptions/{0}/{1}/nack", escapedStreamId, escapedGroupName) +
				nackAllQueryString;
			feed.AddLink("nackAll", nackAll);
		}

		var prev = HostName.Combine(requestedUrl, "/subscriptions/{0}/{1}/{2}", escapedStreamId, escapedGroupName,
			count);
		feed.AddLink("previous", prev);

		feed.AddLink("self", self);
		for (int i = msg.Events.Length - 1; i >= 0; --i) {
			var entry = ToEntry(msg.Events[i].ResolvedEvent.WithoutPosition(), requestedUrl, embedContent);
			var ack = HostName.Combine(requestedUrl, "/subscriptions/{0}/{1}/ack/{2}", escapedStreamId,
				escapedGroupName, msg.Events[i].ResolvedEvent.OriginalEvent.EventId);
			var nack = HostName.Combine(requestedUrl, "/subscriptions/{0}/{1}/nack/{2}", escapedStreamId,
				escapedGroupName, msg.Events[i].ResolvedEvent.OriginalEvent.EventId);
			entry.AddLink("ack", ack);
			entry.AddLink("nack", nack);
			entry.AddRetryCount(msg.Events[i].RetryCount);
			feed.AddEntry(entry);
		}

		return feed;
	}

	public static DescriptionDocument ToDescriptionDocument(Uri requestedUrl, string streamId,
		string[] subscriptions) {
		string escapedStreamId = Uri.EscapeDataString(streamId);
		var descriptionDocument = new DescriptionDocument();
		descriptionDocument.SetTitle(string.Format("Description document for '{0}'", streamId));
		descriptionDocument.SetDescription(
			@"The description document will be presented when no accept header is present or it was requested");

		descriptionDocument.SetSelf("/streams/" + escapedStreamId,
			Codec.DescriptionJson.ContentType);

		descriptionDocument.SetStream("/streams/" + escapedStreamId,
			Codec.EventStoreXmlCodec.ContentType,
			Codec.EventStoreJsonCodec.ContentType);

		if (subscriptions != null) {
			foreach (var group in subscriptions) {
				descriptionDocument.AddStreamSubscription(
					String.Format("/subscriptions/{0}/{1}", escapedStreamId, group),
					Codec.CompetingXml.ContentType,
					Codec.CompetingJson.ContentType);
			}
		}

		return descriptionDocument;
	}

	public static EntryElement ToEntry(ResolvedEvent eventLinkPair, Uri requestedUrl, EmbedLevel embedContent,
		bool singleEntry = false) {
		if (requestedUrl == null)
			return null;

		var evnt = eventLinkPair.Event;
		var link = eventLinkPair.Link;
		EntryElement entry;
		if (embedContent > EmbedLevel.Content && evnt != null) {
			var richEntry = new RichEntryElement();
			entry = richEntry;

			richEntry.EventId = evnt.EventId;
			richEntry.EventType = evnt.EventType;
			richEntry.EventNumber = evnt.EventNumber;
			richEntry.StreamId = evnt.EventStreamId;
			richEntry.PositionEventNumber = eventLinkPair.OriginalEvent.EventNumber;
			richEntry.PositionStreamId = eventLinkPair.OriginalEvent.EventStreamId;
			richEntry.IsJson = (evnt.Flags & PrepareFlags.IsJson) != 0;
			richEntry.IsRedacted = (evnt.Flags & PrepareFlags.IsRedacted) != 0;

			var data = evnt.Data.Span;

			if (embedContent >= EmbedLevel.Body && eventLinkPair.Event != null) {
				if (richEntry.IsJson) {
					if (embedContent >= EmbedLevel.PrettyBody) {
						try {
							richEntry.Data = Helper.UTF8NoBom.GetString(data);
							// next step may fail, so we have already assigned body
							richEntry.Data = FormatJson(Helper.UTF8NoBom.GetString(data));
						} catch {
							// ignore - we tried
						}
					} else
						richEntry.Data = Helper.UTF8NoBom.GetString(data);
				} else if (embedContent >= EmbedLevel.TryHarder) {
					try {
						richEntry.Data = Helper.UTF8NoBom.GetString(data);
						// next step may fail, so we have already assigned body
						richEntry.Data = FormatJson(richEntry.Data);
						// it is json if successed
						richEntry.IsJson = true;
					} catch {
						// ignore - we tried
					}
				}

				// metadata
				if (embedContent >= EmbedLevel.Body) {

					try {
						richEntry.MetaData = Helper.UTF8NoBom.GetString(evnt.Metadata.Span);
						richEntry.IsMetaData = richEntry.MetaData.IsNotEmptyString();
						// next step may fail, so we have already assigned body
						if (embedContent >= EmbedLevel.PrettyBody) {
							richEntry.MetaData = FormatJson(richEntry.MetaData);
						}

						if (string.IsNullOrEmpty(richEntry.MetaData)) {
							richEntry.MetaData = null;
						}
					} catch {
						// ignore - we tried
					}

					var lnk = eventLinkPair.Link;
					if (lnk != null) {
						try {
							richEntry.LinkMetaData = Helper.UTF8NoBom.GetString(lnk.Metadata.Span);
							richEntry.IsLinkMetaData = richEntry.LinkMetaData.IsNotEmptyString();
							// next step may fail, so we have already assigned body
							if (embedContent >= EmbedLevel.PrettyBody) {
								richEntry.LinkMetaData = FormatJson(richEntry.LinkMetaData);
							}
						} catch {
							// ignore - we tried
						}
					}
				}
			}
		} else {
			entry = new EntryElement();
		}

		if (evnt != null && link == null) {
			SetEntryProperties(evnt.EventStreamId, evnt.EventNumber, evnt.TimeStamp, requestedUrl, entry);
			entry.SetSummary(evnt.EventType);
			if ((singleEntry || embedContent == EmbedLevel.Content) && ((evnt.Flags & PrepareFlags.IsJson) != 0))
				entry.SetContent(AutoEventConverter.CreateDataDto(eventLinkPair));
		} else if (link != null) {
			var eventLoc = GetLinkData(Encoding.UTF8.GetString(link.Data.Span));
			SetEntryProperties(eventLoc.Item1, eventLoc.Item2, link.TimeStamp, requestedUrl, entry);
			entry.SetSummary("$>");
		}

		return entry;
	}

	private static Tuple<string, long> GetLinkData(string link) {
		Ensure.NotNull(link, "link data cannot be null");
		var loc = link.IndexOf("@", StringComparison.Ordinal);
		if (loc == -1)
			throw new Exception(String.Format("Unable to parse link {0}", link));
		var position = long.Parse(link.Substring(0, loc));
		var stream = link.Substring(loc + 1, link.Length - loc - 1);
		return new Tuple<string, long>(stream, position);
	}

	private static void SetEntryProperties(string stream, long eventNumber, DateTime timestamp, Uri requestedUrl,
		EntryElement entry) {
		var escapedStreamId = Uri.EscapeDataString(stream);
		entry.SetTitle(eventNumber + "@" + stream);
		entry.SetId(HostName.Combine(requestedUrl, "/streams/{0}/{1}", escapedStreamId, eventNumber));
		entry.SetUpdated(timestamp);
		entry.SetAuthor(AtomSpecs.Author);
		entry.AddLink("edit",
			HostName.Combine(requestedUrl, "/streams/{0}/{1}", escapedStreamId, eventNumber));
		entry.AddLink("alternate",
			HostName.Combine(requestedUrl, "/streams/{0}/{1}", escapedStreamId, eventNumber));
	}

	private static string FormatJson(string unformattedjson) {
		if (string.IsNullOrEmpty(unformattedjson))
			return unformattedjson;
		JsonReader reader = new JsonTextReader(new System.IO.StringReader(unformattedjson));
		reader.DateParseHandling = DateParseHandling.None;
		var jo = JObject.Load(reader);
		var json = JsonConvert.SerializeObject(jo, Formatting.Indented);
		return json;
	}
}

public class Links {
	public Link Self;
	public Link Stream;
	public List<Link> StreamSubscription;
}

public class Link {
	public string Href;
	public string[] SupportedContentTypes;

	public Link() {
	}

	public Link(string href, string[] supportedContentTypes) {
		Href = href;
		SupportedContentTypes = supportedContentTypes;
	}
}

public class DescriptionDocument {
	public string Title;
	public string Description;
	[JsonProperty("_links")] public Links Links = new Links();

	public void SetTitle(string title) {
		Title = title;
	}

	public void SetDescription(string description) {
		Description = description;
	}

	public void SetSelf(string href, params string[] supportedContentTypes) {
		Links.Self = new Link(href, supportedContentTypes);
	}

	public void AddStreamSubscription(string href, params string[] supportedContentTypes) {
		if (Links.StreamSubscription == null)
			Links.StreamSubscription = new List<Link>();

		Links.StreamSubscription.Add(new Link(href, supportedContentTypes));
	}

	public void SetStream(string href, params string[] supportedContentTypes) {
		Links.Stream = new Link(href, supportedContentTypes);
	}
}
