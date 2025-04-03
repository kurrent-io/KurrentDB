// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.LogAbstraction;
using LogV3StreamId = System.UInt32;

namespace EventStore.Core.Tests;

public class LogFormat {
	public class V2{}
	public class V3{}
}

public static class LogFormatHelper<TLogFormat, TStreamId> {
	public static bool IsV2 => typeof(TLogFormat) == typeof(LogFormat.V2);
	public static bool IsV3 => typeof(TLogFormat) == typeof(LogFormat.V3);

	// static v3 but private so that we can be sure we only hand out stateless parts of it
	readonly static LogFormatAbstractor<LogV3StreamId> _v3 = new LogV3FormatAbstractorFactory().Create(new() {
		InMemory = true,
	});

	readonly static LogFormatAbstractor<string> _v2 = new LogV2FormatAbstractorFactory().Create(new() {
		InMemory = true,
	});		

	public static T Choose<T>(object v2, object v3) {
		if (typeof(TLogFormat) == typeof(LogFormat.V2)) {
			if (typeof(TStreamId) != typeof(string)) throw new InvalidOperationException();
			return (T)v2;
		}
		if(typeof(TLogFormat) == typeof(LogFormat.V3)) {
			if (typeof(TStreamId) != typeof(LogV3StreamId)) throw new InvalidOperationException($"TStreamId was {typeof(TStreamId)} but expected {typeof(LogV3StreamId)}");
			return (T)v3;
		}
		throw new InvalidOperationException();
	}

	private static LogFormatAbstractor<TStreamId> _staticLogFormat =
		Choose<LogFormatAbstractor<TStreamId>>(_v2, _v3);

	public static ILogFormatAbstractorFactory<TStreamId> LogFormatFactory { get; } =
		Choose<ILogFormatAbstractorFactory<TStreamId>>(new LogV2FormatAbstractorFactory(), new LogV3FormatAbstractorFactory());

	// safe because stateless
	public static IRecordFactory<TStreamId> RecordFactory { get; } = _staticLogFormat.RecordFactory;

	// safe because stateless
	public static bool SupportsExplicitTransactions { get; } = _staticLogFormat.SupportsExplicitTransactions;

	// safe because stateless
	public static TStreamId EmptyStreamId { get; } = _staticLogFormat.EmptyStreamId;

	/// just a valid stream id
	public static TStreamId StreamId { get; } =	Choose<TStreamId>("stream", 1024U);
	public static TStreamId StreamId2 { get; } = Choose<TStreamId>("stream2", 1026U);

	public static TStreamId EventTypeId { get; } =	Choose<TStreamId>("eventType", 1024U);
	public static TStreamId EventTypeId2 { get; } =	Choose<TStreamId>("eventType2", 1025U);
	public static TStreamId EmptyEventTypeId { get; } = _staticLogFormat.EmptyEventTypeId;
	
	public static void CheckIfExplicitTransactionsSupported() {
		if (typeof(TLogFormat) == typeof(LogFormat.V3)) {
			throw new InvalidOperationException("Explicit transactions are not supported yet by Log V3");
		}
	}

	public static void EnsureV0PrepareSupported() {
		if (typeof(TLogFormat) == typeof(LogFormat.V3)) {
			throw new InvalidOperationException("No such thing as a V0 prepare in LogV3");
		}
	}
}
