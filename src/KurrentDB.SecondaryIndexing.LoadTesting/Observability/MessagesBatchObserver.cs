// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using KurrentDB.SecondaryIndexing.LoadTesting.Generators;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Observability;

public interface IMessagesBatchObserver {
	void On(MessageBatch batch);

	public ConcurrentDictionary<string, long> Categories { get; }
	public ConcurrentDictionary<string, long> EventTypes { get; }
	public long TotalCount { get; }
}

public class SimpleMessagesBatchObserver : IMessagesBatchObserver {
	private long _totalCount;

	public void On(MessageBatch batch) {
		long messagesCount = batch.Messages.Length;

		Categories.AddOrUpdate(batch.CategoryName, messagesCount, (_, current) => current + messagesCount);
		foreach (var messagesByType in batch.Messages.GroupBy(e => e.EventType)) {
			var eventTypeCount = (long)messagesByType.Count();
			EventTypes.AddOrUpdate(messagesByType.Key, eventTypeCount, (_, current) => current + eventTypeCount);
		}

		Interlocked.Add(ref _totalCount, messagesCount);
	}

	public ConcurrentDictionary<string, long> Categories { get; } = new();
	public ConcurrentDictionary<string, long> EventTypes { get; } = new();

	public long TotalCount => _totalCount;
}
