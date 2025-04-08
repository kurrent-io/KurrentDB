// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Reactive.Subjects;
using Serilog.Core;
using Serilog.Events;

// ReSharper disable once CheckNamespace
namespace EventStore.Common.Log;

public class ObservableSerilogSink : ILogEventSink, IObservable<LogEvent> {
	public static readonly ObservableSerilogSink Instance = new();

	readonly Subject<LogEvent> _subject = new();

	public void Emit(LogEvent logEvent) => _subject.OnNext(logEvent);

	public IDisposable Subscribe(IObserver<LogEvent> observer) => _subject.Subscribe(observer);
}
