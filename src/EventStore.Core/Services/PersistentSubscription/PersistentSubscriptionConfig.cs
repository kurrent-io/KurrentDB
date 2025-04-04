// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Services.PersistentSubscription;

public class PersistentSubscriptionConfig {
	public string Version;
	public DateTime Updated;
	public string UpdatedBy;
	public List<PersistentSubscriptionEntry> Entries = new List<PersistentSubscriptionEntry>();

	public byte[] GetSerializedForm() {
		return this.ToJsonBytes();
	}

	public static PersistentSubscriptionConfig FromSerializedForm(ReadOnlyMemory<byte> data) {
		try {
			var ret = data.ParseJson<PersistentSubscriptionConfig>();
			if (ret.Version == null)
				throw new BadConfigDataException("Deserialized but no version present, invalid configuration data.",
					null);

			UpdateIfRequired(ret);

			return ret;
		} catch (Exception ex) {
			throw new BadConfigDataException("The config data appears to be invalid", ex);
		}
	}

	private static void UpdateIfRequired(PersistentSubscriptionConfig ret) {
		if (ret.Version == "1") {
			if (ret.Entries != null) {
				foreach (var persistentSubscriptionEntry in ret.Entries) {
					if (string.IsNullOrEmpty(persistentSubscriptionEntry.NamedConsumerStrategy)) {
						persistentSubscriptionEntry.NamedConsumerStrategy =
							persistentSubscriptionEntry.PreferRoundRobin
								? SystemConsumerStrategies.RoundRobin
								: SystemConsumerStrategies.DispatchToSingle;
					}
				}
			}

			ret.Version = "2";
		}
	}
}

public class BadConfigDataException : Exception {
	public BadConfigDataException(string message, Exception inner) : base(message, inner) {
	}
}

public class PersistentSubscriptionEntry {
	public string Stream;
	public string Group;
	public EventFilter.EventFilterDto Filter;
	public bool ResolveLinkTos;
	public bool ExtraStatistics;
	public int MessageTimeout;
	[Obsolete] public long StartFrom;
	public string StartPosition;
	public int LiveBufferSize;
	public int HistoryBufferSize;
	public int MaxRetryCount;
	public int ReadBatchSize;
	public bool PreferRoundRobin;
	public int CheckPointAfter { get; set; }
	public int MinCheckPointCount { get; set; }
	public int MaxCheckPointCount { get; set; }
	public int MaxSubscriberCount { get; set; }
	public string NamedConsumerStrategy { get; set; }
}
