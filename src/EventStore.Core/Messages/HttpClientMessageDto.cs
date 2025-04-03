// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using System.Xml.Serialization;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Messages;

public static class HttpClientMessageDto {
	public class ClientEventDynamic {
		public Guid eventId { get; set; }
		public string eventType { get; set; }

		public object data { get; set; }
		public object metadata { get; set; }
	}

	public class WriteEventsDynamic {
		public ClientEventDynamic[] events { get; set; }

		public WriteEventsDynamic() {
		}

		public WriteEventsDynamic(ClientEventDynamic[] events) {
			this.events = events;
		}
	}

	[XmlType(TypeName = "event")]
	public class ClientEventText {
		public Guid eventId { get; set; }
		public string eventType { get; set; }

		public string data { get; set; }
		public string metadata { get; set; }

		public ClientEventText() {
		}

		public ClientEventText(Guid eventId, string eventType, string data, string metadata) {
			Ensure.NotEmptyGuid(eventId, "eventId");
			Ensure.NotNull(data, "data");

			this.eventId = eventId;
			this.eventType = eventType;

			this.data = data;
			this.metadata = metadata;
		}

		public ClientEventText(Guid eventId, string eventType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata) {
			Ensure.NotEmptyGuid(eventId, "eventId");

			this.eventId = eventId;
			this.eventType = eventType;

			this.data = Helper.UTF8NoBom.GetString(data.Span);
			this.metadata = Helper.UTF8NoBom.GetString(metadata.Span);
		}
	}

	[XmlRoot(ElementName = "event")]
	public class ReadEventCompletedText {
		public string eventStreamId { get; set; }
		public long eventNumber { get; set; }
		public string eventType { get; set; }
		public string eventId { get; set; }
		public object data { get; set; }
		public object metadata { get; set; }

		public ReadEventCompletedText() {
		}

		public ReadEventCompletedText(ResolvedEvent evnt) {
			if (evnt.Event != null) {
				eventStreamId = evnt.Event.EventStreamId;
				eventNumber = evnt.Event.EventNumber;
				eventType = evnt.Event.EventType;
				eventId = evnt.Event.EventId.ToString();
				data = Helper.UTF8NoBom.GetString(evnt.Event.Data.Span);
				metadata = Helper.UTF8NoBom.GetString(evnt.Event.Metadata.Span);
			} else {
				eventStreamId = null;
				eventNumber = EventNumber.Invalid;
				eventType = null;
				data = null;
				metadata = null;
			}
		}

		public override string ToString() {
			return string.Format(
				"id: {5} eventStreamId: {0}, eventNumber: {1}, eventType: {2}, data: {3}, metadata: {4}",
				eventStreamId,
				eventNumber,
				eventType,
				data,
				metadata,
				eventId);
		}
	}
}
