// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using EventStore.Common.Log;
using Serilog.Events;

namespace KurrentDB.Tools;

class LogObserver : IObserver<LogEvent>, IDisposable {
	const int MaxCount = 200;
	public readonly ObservableCollection<LogEvent> Items = [];
	readonly IDisposable _sub;

	public LogObserver() => _sub = ObservableSerilogSink.Instance.Subscribe(this);

	public event PropertyChangedEventHandler PropertyChanged;

	public void OnCompleted() {
	}

	public void OnError(Exception error) {
	}

	public void OnNext(LogEvent value) {
		Items.Add(value);
		if (Items.Count > MaxCount) {
			Items.RemoveAt(0);
		}

		PropertyChanged?.Invoke(this, new(nameof(Items)));
	}

	public void Dispose() {
		_sub?.Dispose();
	}
}

public delegate void LogEntryAdded(LogEvent entry);
