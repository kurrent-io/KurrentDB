// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.LogAbstraction;

namespace KurrentDB.Core.Tests;

public class LogFormat {
	public class V2 { }
}

public static class LogFormatHelper<TLogFormat, TStreamId> {
	public static bool IsV2 => typeof(TLogFormat) == typeof(LogFormat.V2);

	readonly static LogFormatAbstractor<string> _v2 = new LogV2FormatAbstractorFactory().Create(new() {
		InMemory = true,
	});

	public static T Choose<T>(object v2) {
		if (typeof(TLogFormat) == typeof(LogFormat.V2)) {
			if (typeof(TStreamId) != typeof(string))
				throw new InvalidOperationException();
			return (T)v2;
		}
		throw new InvalidOperationException();
	}

	private static LogFormatAbstractor<TStreamId> _staticLogFormat =
		Choose<LogFormatAbstractor<TStreamId>>(_v2);

	public static ILogFormatAbstractorFactory<TStreamId> LogFormatFactory { get; } =
		Choose<ILogFormatAbstractorFactory<TStreamId>>(new LogV2FormatAbstractorFactory());

	// safe because stateless
	public static IRecordFactory<TStreamId> RecordFactory { get; } = _staticLogFormat.RecordFactory;

	// safe because stateless
	public static bool SupportsExplicitTransactions { get; } = _staticLogFormat.SupportsExplicitTransactions;

	// safe because stateless
	public static TStreamId EmptyStreamId { get; } = _staticLogFormat.EmptyStreamId;

	/// just a valid stream id
	public static TStreamId StreamId { get; } = Choose<TStreamId>("stream");
	public static TStreamId StreamId2 { get; } = Choose<TStreamId>("stream2");

	public static TStreamId EventTypeId { get; } = Choose<TStreamId>("eventType");
	public static TStreamId EventTypeId2 { get; } = Choose<TStreamId>("eventType2");
	public static TStreamId EmptyEventTypeId { get; } = _staticLogFormat.EmptyEventTypeId;
}
