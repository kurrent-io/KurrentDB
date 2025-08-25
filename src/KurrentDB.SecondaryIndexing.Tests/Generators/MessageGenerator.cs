// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;

namespace KurrentDB.SecondaryIndexing.Tests.Generators;

public interface IMessageGenerator {
	IAsyncEnumerable<TestMessageBatch> GenerateBatches(LoadTestPartitionConfig config);
}

public record LoadTestPartitionConfig(
	int PartitionId,
	int StartCategoryIndex,
	int CategoriesCount,
	int MaxStreamsPerCategory,
	int MessageTypesCount,
	int MessageSize,
	int MaxBatchSize,
	int TotalMessagesCount
);

public class MessageGenerator : IMessageGenerator {
	public async IAsyncEnumerable<TestMessageBatch> GenerateBatches(LoadTestPartitionConfig config) {
		var eventTypesByCategory = GenerateCategories(config);
		var streams = new Dictionary<string, int>();
		var eventsLeft = config.TotalMessagesCount;
		long logPosition = 0;

		do {
			//var batchSize = Math.Min(Random.Shared.Next(1, config.MaxBatchSize + 1), eventsLeft);
			var batchSize = Math.Min(config.MaxBatchSize, eventsLeft);

			yield return GenerateBatch(config, eventTypesByCategory, streams, batchSize, logPosition);

			eventsLeft -= batchSize;
			logPosition += batchSize;

			if (eventsLeft % 10 == 0) await Task.Yield();
		} while (eventsLeft > 0);
	}

	private TestMessageBatch GenerateBatch(LoadTestPartitionConfig config,
		Dictionary<string, string[]> eventTypesByCategory,
		Dictionary<string, int> streams,
		int batchSize,
		long logPosition
	) {
		var category = eventTypesByCategory.Keys.RandomElement();
		var streamName = $"{category}-{config.PartitionId}_{Random.Shared.Next(0, config.MaxStreamsPerCategory)}";

		streams.TryAdd(streamName, -1);

		var messages = new TestMessageData[batchSize];

		for (int i = 0; i < messages.Length; i++) {
			var eventType = eventTypesByCategory[category].RandomElement();
			streams[streamName] += 1;
			var streamPosition = streams[streamName];

			messages[i] = new TestMessageData(
				streamPosition,
				logPosition + i,
				eventType,
				Enumerable.Repeat((byte)0x20, config.MessageSize).ToArray(),
				Enumerable.Repeat((byte)0x30, config.MessageSize).ToArray()
			);
		}

		return new TestMessageBatch(category, streamName, messages);
	}

	private static Dictionary<string, string[]> GenerateCategories(LoadTestPartitionConfig loadTestPartitionConfig) {
		var categories = Enumerable
			.Range(loadTestPartitionConfig.StartCategoryIndex, loadTestPartitionConfig.CategoriesCount)
			.Select(index => $"c{index}")
			.ToArray();

		return categories.ToDictionary(
			category => category,
			category => Enumerable.Range(0, loadTestPartitionConfig.MessageTypesCount)
				.Select(index => $"{category}-et{index}").ToArray()
		);
	}
}

public readonly record struct TestMessageData(
	int StreamPosition,
	long LogSequence,
	string EventType,
	byte[] Data,
	byte[] MetaData
) {
	public readonly Guid EventId = Guid.NewGuid();
	public readonly Guid CorrelationId = Guid.NewGuid();

	public ResolvedEvent ToResolvedEvent(string streamName, int batchIndex) {
		var recordFactory = LogFormatHelper<LogFormat.V2, string>.RecordFactory;

		var prepare = LogRecord.Prepare(
			recordFactory,
			LogSequence,
			CorrelationId,
			EventId,
			LogSequence,
			batchIndex,
			streamName,
			StreamPosition,
			PrepareFlags.None,
			EventType,
			Data,
			MetaData
		);

		var record = new EventRecord(
			StreamPosition,
			prepare,
			streamName,
			EventType
		);

		return ResolvedEvent.ForUnresolvedEvent(record, 0);
	}

	public ResolvedEvent ToIndexResolvedEvent(string originalStreamName, string indexStreamName, int indexSequence,
		int batchIndex) {
		var recordFactory = LogFormatHelper<LogFormat.V2, string>.RecordFactory;

		var prepare = LogRecord.Prepare(
			recordFactory,
			LogSequence,
			CorrelationId,
			EventId,
			LogSequence,
			batchIndex,
			originalStreamName,
			StreamPosition - 1,
			PrepareFlags.None,
			EventType,
			Data,
			MetaData
		);

		return ResolvedEvent.ForUnresolvedEvent(
			new(
				prepare.ExpectedVersion + 1,
				prepare,
				prepare.EventStreamId,
				prepare.EventType
			));
	}

	public Event ToEventData() => new(EventId, EventType, false, Data, false, MetaData);
}

public readonly record struct TestMessageBatch(string CategoryName, string StreamName, TestMessageData[] Messages) {
	public ResolvedEvent[] ToResolvedEvents() {
		var streamName = StreamName;
		return Messages.Select((m, i) => m.ToResolvedEvent(streamName, i)).ToArray();
	}

	public ResolvedEvent[] ToIndexResolvedEvents(
		string indexStreamName,
		int indexPosition,
		Func<TestMessageData, bool>? predicate = null
	) {
		var streamName = StreamName;
		predicate ??= _ => true;

		return Messages
			.Where(predicate)
			.Select((m, i) => m.ToIndexResolvedEvent(streamName, indexStreamName, indexPosition + i, i))
			.ToArray();
	}
}

public static class TestMessageBatchExtensions {
	public static ResolvedEvent[] ToDefaultIndexResolvedEvents(this List<TestMessageBatch> batches) {
		var result = new List<ResolvedEvent>();

		var currentIndex = 0;

		foreach (var batch in batches) {
			var resolvedEvents = batch.ToIndexResolvedEvents(SystemStreams.DefaultSecondaryIndex, currentIndex);
			currentIndex += batch.Messages.Length;
			result.AddRange(resolvedEvents);
		}

		return result.ToArray();
	}

	public static ResolvedEvent[] ToCategoryIndexResolvedEvents(
		this List<TestMessageBatch> batches,
		string category
	) {
		var categoryStreamName = CategoryIndex.Name(category);

		var result = new List<ResolvedEvent>();

		var currentIndex = 0;

		foreach (var batch in batches.Where(b => b.StreamName.StartsWith(category + "-"))) {
			var resolvedEvents = batch.ToIndexResolvedEvents(
				categoryStreamName,
				currentIndex
			);
			currentIndex += batch.Messages.Length;
			result.AddRange(resolvedEvents);
		}

		return result.ToArray();
	}

	public static ResolvedEvent[] ToEventTypeIndexResolvedEvents(
		this List<TestMessageBatch> batches,
		string eventType
	) {
		var eventTypeStreamName = EventTypeIndex.Name(eventType);

		var result = new List<ResolvedEvent>();

		var currentIndex = 0;

		foreach (var batch in batches) {
			var resolvedEvents = batch.ToIndexResolvedEvents(
				eventTypeStreamName,
				currentIndex,
				m => m.EventType == eventType
			);
			currentIndex += resolvedEvents.Length;
			result.AddRange(resolvedEvents);
		}

		return result.ToArray();
	}
}

public static class CollectionExtension {
	public static T RandomElement<T>(this ICollection<T> collection) =>
		collection.ElementAt(Random.Shared.Next(0, collection.Count));
}
