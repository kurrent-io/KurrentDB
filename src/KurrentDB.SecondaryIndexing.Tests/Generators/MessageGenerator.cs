// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;

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
				Enumerable.Repeat((byte)0x20, config.MessageSize).ToArray()
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

public readonly record struct TestMessageData(int StreamPosition, long LogSequence, string EventType, byte[] Data) {
	public ResolvedEvent ToResolvedEvent(string streamName) {
		var recordFactory = LogFormatHelper<LogFormat.V2, string>.RecordFactory;
		var streamIdIgnored = LogFormatHelper<LogFormat.V2, string>.StreamId;
		var eventTypeIdIgnored = LogFormatHelper<LogFormat.V2, string>.EventTypeId;

		var record = new EventRecord(
			StreamPosition,
			LogRecord.Prepare(recordFactory, LogSequence, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
				streamIdIgnored, StreamPosition, PrepareFlags.None, eventTypeIdIgnored, Data,
				Encoding.UTF8.GetBytes("")
			),
			streamName,
			EventType
		);

		return ResolvedEvent.ForUnresolvedEvent(record, 0);
	}

	public Event ToEventData() =>
		new(Guid.NewGuid(), EventType, false, Data, null, null);
}

public readonly record struct TestMessageBatch(string CategoryName, string StreamName, TestMessageData[] Messages);

public static class CollectionExtension {
	public static T RandomElement<T>(this ICollection<T> collection) =>
		collection.ElementAt(Random.Shared.Next(0, collection.Count));
}
