// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Net;
using System.Threading;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Internal;
using EventStore.ClientAPI.SystemData;
using EventStore.Core.Tests.Helpers;

namespace EventStore.Core.Tests.ClientAPI.Helpers;

public static class TestConnection {
	private static int _nextConnId = -1;

	public static IEventStoreConnection Create(IPEndPoint endPoint, TcpType tcpType = TcpType.Ssl,
		UserCredentials userCredentials = null) {

		return EventStoreConnection.Create(Settings(tcpType, userCredentials),
			endPoint.ToESTcpUri(),
			$"ESC-{Interlocked.Increment(ref _nextConnId)}");
	}

	public static IEventStoreConnection To(MiniNode miniNode, TcpType tcpType,
		UserCredentials userCredentials = null) {
		return EventStoreConnection.Create(Settings(tcpType, userCredentials),
			miniNode.TcpEndPoint.ToESTcpUri(),
			$"ESC-{Interlocked.Increment(ref _nextConnId)}");
	}

	private static ConnectionSettingsBuilder Settings(TcpType tcpType, UserCredentials userCredentials) {
		var settings = ConnectionSettings.Create()
			.SetDefaultUserCredentials(userCredentials)
			.UseCustomLogger(ClientApiLoggerBridge.Default)
			.EnableVerboseLogging()
			.LimitReconnectionsTo(10)
			.LimitAttemptsForOperationTo(1)
			.SetTimeoutCheckPeriodTo(TimeSpan.FromMilliseconds(100))
			.SetReconnectionDelayTo(TimeSpan.Zero)
			.FailOnNoServerResponse()
			//.SetOperationTimeoutTo(TimeSpan.FromDays(1))
			;
		if (tcpType == TcpType.Ssl) {
			settings.DisableServerCertificateValidation();
		} else {
			settings.DisableTls();
		}

		return settings;
	}
}
