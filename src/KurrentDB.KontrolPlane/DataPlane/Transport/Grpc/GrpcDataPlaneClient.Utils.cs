// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Net;
using DotNext;

namespace KurrentDB.DataPlane.Transport.Grpc;

partial class GrpcDataPlaneClient : Disposable {
	private readonly ConcurrentDictionary<EndPoint, ClientCacheEntry> _clients = new();

	private DataPlaneNode.DataPlaneNodeClient GetClient(EndPoint address) {
		if (!_clients.TryGetValue(address, out var entry)) {
			var channel = CreateChannel(address, out var invoker);
			var existingEntry = _clients
				.GetOrAdd(address, new ClientCacheEntry {
					Client = new(invoker),
					Channel = channel,
				});

			if (!ReferenceEquals(channel, existingEntry.Channel)) {
				channel.Dispose();
				entry = existingEntry;
			}
		}

		return entry.Client;
	}

	public ValueTask ReclaimConnectionsAsync(IReadOnlySet<EndPoint> activeConnections, CancellationToken token) {
		var task = ValueTask.CompletedTask;
		try {
			ReclaimConnections(activeConnections, token);
		} catch (Exception e) {
			task = ValueTask.FromException(e);
		}

		return task;
	}

	private void ReclaimConnections(IReadOnlySet<EndPoint> activeConnections, CancellationToken token) {
		var removedConnections = new HashSet<EndPoint>();
		foreach (var address in _clients.Keys) {
			if (!activeConnections.Contains(address))
				removedConnections.Add(address);

			token.ThrowIfCancellationRequested();
		}

		foreach (var removedConnection in removedConnections) {
			if (_clients.TryRemove(removedConnection, out var entry)) {
				entry.Channel.Dispose();
			}

			token.ThrowIfCancellationRequested();
		}

		removedConnections.Clear(); // help GC
	}

	private void DestroyChannels() {
		foreach (var entry in _clients.Values) {
			entry.Channel.Dispose();
		}

		_clients.Clear();
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			DestroyChannels();
		}

		base.Dispose(disposing);
	}

	public new ValueTask DisposeAsync() => base.DisposeAsync();

	private readonly struct ClientCacheEntry {
		public required DataPlaneNode.DataPlaneNodeClient Client { get; init; }
		public required IDisposable Channel { get; init; }
	}
}
