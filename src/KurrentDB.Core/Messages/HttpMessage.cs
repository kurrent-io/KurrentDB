// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Http;
using KurrentDB.Transport.Http.EntityManagement;

namespace KurrentDB.Core.Messages;

public enum DenialReason {
	ServerTooBusy
}

public static partial class HttpMessage {
	[DerivedMessage]
	public abstract partial class HttpSendMessage : Message {
		public sealed override HttpEntityManager Affinity => HttpEntityManager;

		public readonly HttpEntityManager HttpEntityManager;

		/// <param name="httpEntityManager"></param>
		protected HttpSendMessage(HttpEntityManager httpEntityManager) {
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
			: base(httpEntityManager) {
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
