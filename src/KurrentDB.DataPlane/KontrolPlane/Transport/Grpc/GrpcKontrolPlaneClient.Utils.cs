// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Net;
using DotNext;

namespace KurrentDB.KontrolPlane.Transport.Grpc;

partial class GrpcKontrolPlaneClient {
	private readonly ConcurrentDictionary<EndPoint, ClientCacheEntry> _clients = new();

	// The client maintains connections to the multiple KPlane nodes. We don't know a fixed list of these nodes.
	// So, for bootstrapping we have a seed. Then, with every announcement KPlane send us an updated list
	// of nodes. With gRPC, we can't accumulate channels for every new node, we need to dispose the channels
	// that are not in use. In this case, we dispose the channel if the remote service doesn't respond, then
	// we can try the next address from the list (which is seed or recently obtained list from the KPlane).
	private volatile IReadOnlyList<EndPoint> _kontrollerNodes = [];
	private volatile EndPoint? _current;

	private EndPoint CurrentAddress => _current ?? _kontrollerNodes[0];

	private EndPoint MarkAsUnavailable(EndPoint currentAddress, EndPoint? newAddress) {
		var result = newAddress is null ? EraseAddress(currentAddress) : ReplaceAddress(currentAddress, newAddress);

		// address is switched, dispose the associated channel
		if (!Equals(result, currentAddress) && _clients.TryRemove(currentAddress, out var entry)) {
			entry.Release();
		}

		return result;
	}

	private EndPoint ReplaceAddress(EndPoint oldAddress, EndPoint newAddress)
		=> Interlocked.CompareExchange(ref _current, newAddress, oldAddress) ?? newAddress;

	private EndPoint EraseAddress(EndPoint currentAddress) {
		var kontrollerNodes = _kontrollerNodes;
		var index = IndexOf(kontrollerNodes, currentAddress);

		// take the next address of the list
		index = index >= 0
			? (index + 1) % kontrollerNodes.Count
			: 0;

		return Interlocked.CompareExchange(ref _current, kontrollerNodes[index], currentAddress) ?? kontrollerNodes[index];

		static int IndexOf(IReadOnlyList<EndPoint> seed, EndPoint address) {
			for (var i = 0; i < seed.Count; i++) {
				if (Equals(seed[i], address))
					return i;
			}

			return -1;
		}
	}

	private ClientCacheEntry GetOrCreateClient(EndPoint address) {
		ClientCacheEntry? entry;
		do {
			if (!_clients.TryGetValue(address, out entry)) {
				var channel = CreateChannel(address, out var invoker);
				var newEntry = new ClientCacheEntry(new(invoker), channel);
				entry = _clients.GetOrAdd(address, newEntry);
				if (!ReferenceEquals(entry, newEntry)) {
					newEntry.Dispose();
				}
			}
		} while (!entry.TryAcquire());

		return entry;
	}

	private void DestroyChannels() {
		foreach (var entry in _clients.Values) {
			entry.Release();
		}
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			DestroyChannels();
		}

		base.Dispose(disposing);
	}

	private sealed class ClientCacheEntry(Kontroller.KontrollerClient client, IDisposable channel) : Disposable {
		private uint _referenceCounter = 1U;

		public bool TryAcquire() {
			for (uint current = _referenceCounter, tmp;; current = tmp) {
				if (current is 0U)
					return false;

				tmp = Interlocked.CompareExchange(ref _referenceCounter, current + 1U, current);
				if (current == tmp)
					break;
			}

			return true;
		}

		public void Release() {
			for (uint current = _referenceCounter, tmp;; current = tmp) {
				if (current is 0U)
					return;

				tmp = Interlocked.CompareExchange(ref _referenceCounter, current - 1U, current);
				if (tmp == current)
					break;
			}

			Dispose();
		}

		internal Kontroller.KontrollerClient Client => client;

		protected override void Dispose(bool disposing) {
			if (disposing) {
				channel.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
