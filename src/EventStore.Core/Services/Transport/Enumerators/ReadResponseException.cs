// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Common;

namespace EventStore.Core.Services.Transport.Enumerators;

public abstract class ReadResponseException : Exception {
	public class StreamNotFound(string streamName) : ReadResponseException {
		public string StreamName { get; } = streamName;
	}

	public class WrongExpectedRevision(string stream, long expectedRevision, long actualRevision) : ReadResponseException {
		public string Stream { get; } = stream;

		public StreamRevision ExpectedStreamRevision { get; } = StreamRevision.FromInt64(expectedRevision);

		public StreamRevision ActualStreamRevision { get; } = StreamRevision.FromInt64(actualRevision);
	}

	public class StreamDeleted(string streamName) : ReadResponseException {
		public readonly string StreamName = streamName;
	}

	public class AccessDenied : ReadResponseException;

	public class InvalidPosition : ReadResponseException;

	public class Timeout(string errorMessage) : ReadResponseException {
		public readonly string ErrorMessage = errorMessage;
	}

	public class UnknownMessage(Type unknownMessageType, Type expectedMessageType) : ReadResponseException {
		public readonly Type UnknownMessageType = unknownMessageType;
		public readonly Type ExpectedMessageType = expectedMessageType;

		public static UnknownMessage Create<T>(Message message) where T : Message => new(message.GetType(), typeof(T));
	}

	public class UnknownError(Type resultType, object result) : ReadResponseException {
		public readonly Type ResultType = resultType;
		public readonly object Result = result;

		public static UnknownError Create<T>(T result) => new(typeof(T), result);
	}

	public abstract class NotHandled {
		public class ServerNotReady : ReadResponseException;

		public class ServerBusy : ReadResponseException;

		public class LeaderInfo(string host, int port) : ReadResponseException {
			public string Host { get; } = host;
			public int Port { get; } = port;
		}

		public class NoLeaderInfo : ReadResponseException;
	}
}
