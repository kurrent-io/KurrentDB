// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using KurrentDB.SecondaryIndexing.Tests.Generators;

namespace KurrentDB.SecondaryIndexing.Tests.Observability;

public interface IMessagesBatchObserver {
	void On(TestMessageBatch batch);

	public ConcurrentDictionary<string, long> Categories { get; }
	public ConcurrentDictionary<string, long> EventTypes { get; }
	public long TotalCount { get; }

	public IndexingSummary Summary { get; }
}

public record IndexingSummary(
	IDictionary<string, long> Categories,
	IDictionary<string, long> EventTypes,
	long TotalCount
);

public class StreamsMessagesBatchObserver : IMessagesBatchObserver {
	private long _totalCount;

	public void On(TestMessageBatch batch) {
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

	public long TotalCount => Interlocked.Read(ref _totalCount);

	public IndexingSummary Summary => new(
		Categories.ToDictionary(ks => ks.Key, vs => vs.Value),
		EventTypes.ToDictionary(ks => ks.Key, vs => vs.Value),
		TotalCount
	);
}

public class SimpleMessagesBatchObserver : IMessagesBatchObserver {
	private long _totalCount;

	public void On(TestMessageBatch batch) {
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

	public long TotalCount => Interlocked.Read(ref _totalCount);

	public IndexingSummary Summary => new(
		Categories.ToDictionary(ks => ks.Key, vs => vs.Value),
		EventTypes.ToDictionary(ks => ks.Key, vs => vs.Value),
		TotalCount
	);
}
