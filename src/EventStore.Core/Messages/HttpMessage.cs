// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Http;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Messages;

public enum DenialReason {
	ServerTooBusy
}

public static partial class HttpMessage {
	[DerivedMessage]
	public abstract partial class HttpSendMessage : Message, IQueueAffineMessage {
		public int QueueId {
			get { return HttpEntityManager.GetHashCode(); }
		}

		public readonly IEnvelope Envelope;
		public readonly Guid CorrelationId;
		public readonly HttpEntityManager HttpEntityManager;

		/// <param name="correlationId"></param>
		/// <param name="envelope">non-null envelope requests HttpCompleted messages in response</param>
		/// <param name="httpEntityManager"></param>
		protected HttpSendMessage(Guid correlationId, IEnvelope envelope, HttpEntityManager httpEntityManager) {
			CorrelationId = correlationId;
			Envelope = envelope;
			HttpEntityManager = httpEntityManager;
		}
	}

	[DerivedMessage(CoreMessage.Http)]
	public partial class HttpSend : HttpSendMessage {
		public readonly object Data;
		public readonly ResponseConfiguration Configuration;
		public readonly Message Message;

		public HttpSend(
			HttpEntityManager httpEntityManager, ResponseConfiguration configuration, object data, Message message)
			: base(Guid.Empty, null, httpEntityManager) {
			Data = data;
			Configuration = configuration;
			Message = message;
		}
	}

	[DerivedMessage(CoreMessage.Http)]
	public partial class DeniedToHandle : Message {
		public readonly DenialReason Reason;
		public readonly string Details;

		public DeniedToHandle(DenialReason reason, string details) {
			Reason = reason;
			Details = details;
		}
	}

	[DerivedMessage(CoreMessage.Http)]
	public partial class PurgeTimedOutRequests : Message {
	}

	[DerivedMessage(CoreMessage.Http)]
	public partial class TextMessage : Message {
		public string Text { get; set; }

		public TextMessage() {
		}

		public TextMessage(string text) {
			Text = text;
		}

		public override string ToString() {
			return string.Format("Text: {0}", Text);
		}
	}
}
