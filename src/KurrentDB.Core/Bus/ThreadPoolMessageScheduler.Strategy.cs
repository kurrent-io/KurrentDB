// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotNext.Patterns;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Bus;

partial class ThreadPoolMessageScheduler {
	/// <summary>
	/// Returns a strategy that executes all the messages with <see cref="Message.UnknownAffinity"/> in order.
	/// </summary>
	/// <returns>The strategy instance.</returns>
	public static MessageProcessingStrategy SynchronizeMessagesWithUnknownAffinity()
		=> new SynchronizeMessagesWithUnknownAffinityStrategy();

	/// <summary>
	/// Returns a strategy that executes all the messages with <see cref="Message.UnknownAffinity"/> in parallel,
	/// i.e. without the synchronization.
	/// </summary>
	/// <returns>The strategy instance.</returns>
	public static MessageProcessingStrategy TreatUnknownAffinityAsNoAffinity()
		=> new TreatUnknownAffinityAsNoAffinityStrategy();

	/// <summary>
	/// Returns a strategy that executes all the messages with <see cref="Message.UnknownAffinity"/> in parallel,
	/// but the parallelism is restricted by the concurrency limit.
	/// </summary>
	/// <param name="concurrencyLimit">The number of messages that can be processed in parallel.</param>
	/// <returns>The strategy instance.</returns>
	public static MessageProcessingStrategy UseRateLimitForUnknownAffinity(long concurrencyLimit) {
		ArgumentOutOfRangeException.ThrowIfZero(concurrencyLimit);

		return new RateLimitedStrategy(concurrencyLimit);
	}

	/// <summary>
	/// Describes how the scheduler have to process the messages.
	/// </summary>
	public abstract class MessageProcessingStrategy {
		private protected MessageProcessingStrategy() {
		}

		internal abstract ISynchronizationGroup GetSynchronizationGroup(object affinity);
	}

	private abstract class SimpleMessageProcessingStrategy : MessageProcessingStrategy {
		// ConditionalWeakTable does not keep the keys alive, they are removed from the table when
		// they are garbage collected. It is thread safe.
		// We use it to associate AsyncExclusiveLocks with Affinity objects.
		private readonly ConditionalWeakTable<object, SimpleSynchronizer> _syncGroups = new();

		internal override ISynchronizationGroup GetSynchronizationGroup(object affinity) {
			Debug.Assert(affinity is not null);

			SimpleSynchronizer synchronizer;
			while (!_syncGroups.TryGetValue(affinity, out synchronizer)) {
				synchronizer = new();
				if (_syncGroups.TryAdd(affinity, synchronizer))
					break;

				synchronizer.Dispose();
			}

			return synchronizer;
		}
	}

	private sealed class TreatUnknownAffinityAsNoAffinityStrategy : SimpleMessageProcessingStrategy {
		internal override ISynchronizationGroup GetSynchronizationGroup(object affinity)
			=> affinity is null || ReferenceEquals(affinity, Message.UnknownAffinity)
				? null
				: base.GetSynchronizationGroup(affinity);
	}

	private sealed class SynchronizeMessagesWithUnknownAffinityStrategy : SimpleMessageProcessingStrategy {
		private readonly SimpleSynchronizer _defaultSynchronizer = new();

		internal override ISynchronizationGroup GetSynchronizationGroup(object affinity) {
			if (affinity is null)
				return null;

			if (ReferenceEquals(affinity, Message.UnknownAffinity))
				return _defaultSynchronizer;

			return base.GetSynchronizationGroup(affinity);
		}
	}

	private sealed class RateLimitedStrategy(long concurrencyLimit) : SimpleMessageProcessingStrategy {
		// Perf: HasConcurrencyLimit set to false means that the capacity of the internal IValueTaskSource pool is limited to avoid
		// inflation of the pool in the case of high (but rare) workloads, and let GC collect the sources that cannot be returned
		// to the pool
		private readonly RateLimitingSynchronizer _defaultSynchronizer = new(concurrencyLimit) {
			ConcurrencyLevel = concurrencyLimit,
			HasConcurrencyLimit = false,
		};

		internal override ISynchronizationGroup GetSynchronizationGroup(object affinity) {
			if (affinity is null)
				return null;

			if (ReferenceEquals(affinity, Message.UnknownAffinity))
				return _defaultSynchronizer;

			return base.GetSynchronizationGroup(affinity);
		}
	}
}
