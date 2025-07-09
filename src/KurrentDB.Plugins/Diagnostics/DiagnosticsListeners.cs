// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace EventStore.Plugins.Diagnostics;

public delegate void OnSourceEvent(string source, object data);

/// <summary>
/// Generic listener that can subscribe to multiple sources, ignores the default diagnostics model and always returns just the value and only if its not null.
/// </summary>
public class MultiSourceDiagnosticsListener : IDisposable {
	public MultiSourceDiagnosticsListener(string[] sources, int capacity = 10, OnSourceEvent? onEvent = null) {
		foreach (var source in sources)
			Listeners.TryAdd(source, new(source, capacity, data => onEvent?.Invoke(source, data)));
	}

	Dictionary<string, SingleSourceDiagnosticsListener> Listeners { get; } = new();

	public IEnumerable<object> CollectedEvents(string source) =>
		Listeners.TryGetValue(source, out var listener) ? (IEnumerable<object>)listener : [];

	public bool HasCollectedEvents(string source) =>
		Listeners.TryGetValue(source, out var listener) && listener.HasCollectedEvents;

	public void ClearCollectedEvents(string source) {
		if (Listeners.TryGetValue(source, out var listener))
			listener.ClearCollectedEvents();
	}

	public void ClearAllCollectedEvents() {
		foreach (var listener in Listeners.Values)
			listener.ClearCollectedEvents();
	}

	public void Dispose() {
		foreach (var listener in Listeners.Values)
			listener.Dispose();

		Listeners.Clear();
	}

	public static MultiSourceDiagnosticsListener Start(OnSourceEvent onEvent, params string[] sources) =>
			new(sources, 10, onEvent);

	public static MultiSourceDiagnosticsListener Start(OnSourceEvent onEvent, int capacity, params string[] sources) =>
		new(sources, capacity, onEvent);

	public static MultiSourceDiagnosticsListener Start(params string[] sources) =>
		new(sources);

	public static MultiSourceDiagnosticsListener Start(int capacity, params string[] sources) =>
		new(sources, capacity);
}

/// <summary>
/// Generic listener that ignores the default diagnostics model and always returns just the value and only if its not null.
/// </summary>
public class SingleSourceDiagnosticsListener : IEnumerable<object>, IDisposable {
	public SingleSourceDiagnosticsListener(string source, int capacity = 10, Action<object>? onEvent = null) {
		Listener = new(source, capacity, data => {
			if (data.Value is not null)
				onEvent?.Invoke(data.Value);
		});
	}

	GenericDiagnosticsListener Listener { get; }

	List<object> ValidEvents => Listener.CollectedEvents
		.Where(x => x.Value is not null)
		.Select(x => x.Value!)
		.ToList();

	public string Source => Listener.Source;
	public int Capacity => Listener.Capacity;

	public IReadOnlyList<object> CollectedEvents => ValidEvents;

	public bool HasCollectedEvents => Listener.HasCollectedEvents;

	public void ClearCollectedEvents() => Listener.ClearCollectedEvents();

	public IEnumerator<object> GetEnumerator() => ValidEvents.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public void Dispose() => Listener.Dispose();

	public static SingleSourceDiagnosticsListener Start(string source, int capacity) =>
		new(source, capacity);

	public static SingleSourceDiagnosticsListener Start(string source) =>
		new(source);

	public static SingleSourceDiagnosticsListener Start(Action<object> onEvent, string source) =>
		new(source, 10, onEvent);

	public static SingleSourceDiagnosticsListener Start(Action<object> onEvent, int capacity, string source) =>
		new(source, capacity, onEvent);
}

/// <summary>
/// Generic listener that also collects the last N events and can be used to subscribe to a single source.
/// </summary>
class GenericDiagnosticsListener : IDisposable {
	static readonly object Locker = new();

	public GenericDiagnosticsListener(string source, int capacity = 10, Action<KeyValuePair<string, object?>>? onEvent = null) {
		if (string.IsNullOrWhiteSpace(source))
			throw new ArgumentException("Source cannot be null or whitespace.", nameof(source));

		ArgumentOutOfRangeException.ThrowIfNegative(capacity);

		Source = source;
		Capacity = capacity;
		Queue  = new(capacity);

		var observer = new GenericObserver<KeyValuePair<string, object?>>(data => {
			if (capacity > 0)
				Queue.Enqueue(data);

			try {
				onEvent?.Invoke(data);
			}
			catch {
				// stay on target
			}
		});

		ListenerSubscription = DiagnosticListener.AllListeners
			.Subscribe(new GenericObserver<DiagnosticListener>(OnNewListener));

		return;

		void OnNewListener(DiagnosticListener listener) {
			if (listener.Name != source) return;

			lock (Locker) {
				NetworkSubscription?.Dispose();
				NetworkSubscription = listener.Subscribe(observer);
			}
		}
	}


	FixedSizedConcurrentQueue<KeyValuePair<string, object?>> Queue { get; }
	IDisposable? ListenerSubscription { get; }
	IDisposable? NetworkSubscription { get; set; }

	public string Source { get; }
	public int Capacity { get; }

	public IReadOnlyList<KeyValuePair<string, object?>> CollectedEvents => Queue.ToArray();

	public bool HasCollectedEvents => Queue.TryPeek(out _);

	public void ClearCollectedEvents() => Queue.Clear();

	public void Dispose() {
		NetworkSubscription?.Dispose();
		ListenerSubscription?.Dispose();
		ClearCollectedEvents();
	}

	class GenericObserver<T>(Action<T>? onNext, Action? onCompleted = null) : IObserver<T> {
		public void OnNext(T value) => _onNext(value);
		public void OnCompleted() => _onCompleted();

		public void OnError(Exception error) { }

		readonly Action<T> _onNext      = onNext      ?? (_ => { });
		readonly Action    _onCompleted = onCompleted ?? (() => { });
	}

	class FixedSizedConcurrentQueue<T>(int maxSize) : ConcurrentQueue<T> {
		readonly object _locker = new();

		public new void Enqueue(T item) {
			lock (_locker) {
				base.Enqueue(item);
				if (Count > maxSize)
					TryDequeue(out _); // Throw away
			}
		}
	}

	public static GenericDiagnosticsListener Start(string source, int capacity = 10, Action<KeyValuePair<string, object?>>? onEvent = null) =>
		new(source, capacity, onEvent);

	public static GenericDiagnosticsListener Start(string source, Action<KeyValuePair<string, object?>>? onEvent = null) =>
		new(source, 10, onEvent);
}
