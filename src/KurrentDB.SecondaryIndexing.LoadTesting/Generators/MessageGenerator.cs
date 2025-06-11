// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Generators;

public interface IMessageGenerator {
	IAsyncEnumerable<MessageBatch> GenerateBatches(LoadTestPartitionConfig config);
}

public class MessageGenerator : IMessageGenerator {
	public async IAsyncEnumerable<MessageBatch> GenerateBatches(LoadTestPartitionConfig config) {
		var eventTypesByCategory = GenerateCategories(config);
		var eventsLeft = config.TotalMessagesCount;

		do {
			//var batchSize = Math.Min(Random.Shared.Next(1, config.MaxBatchSize + 1), eventsLeft);
			var batchSize = Math.Min(config.MaxBatchSize, eventsLeft);

			yield return GenerateBatch(config, eventTypesByCategory, batchSize);

			eventsLeft -= batchSize;

			if (eventsLeft % 10 == 0) await Task.Yield();
		} while (eventsLeft > 0);
	}

	private static MessageBatch GenerateBatch(LoadTestPartitionConfig config,
		Dictionary<string, string[]> eventTypesByCategory, int batchSize) {
		var category = eventTypesByCategory.Keys.RandomElement();
		var streamName = $"{category}-${config.PartitionId}_${Random.Shared.Next(0, config.MaxStreamsPerCategory)}";

		var messages = new MessageData[batchSize];

		for (int i = 0; i < messages.Length; i++) {
			var eventType = eventTypesByCategory[category].RandomElement();
			messages[i] = new MessageData(
				Random.Shared.Next(0, 20), // TODO: make it a real, base on cache
				eventType,
				Enumerable.Repeat((byte)0x20, config.MessageSize).ToArray()
			);
		}

		return new MessageBatch(category, streamName, messages);
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

public readonly record struct MessageData(int StreamPosition, string EventType, byte[] Data) {
	public ResolvedEvent ToResolvedEvent(string streamName) {
		var recordFactory = LogFormatHelper<LogFormat.V2, string>.RecordFactory;
		var streamIdIgnored = LogFormatHelper<LogFormat.V2, string>.StreamId;
		var eventTypeIdIgnored = LogFormatHelper<LogFormat.V2, string>.EventTypeId;

		var record = new EventRecord(
			StreamPosition,
			LogRecord.Prepare(recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
				streamIdIgnored, StreamPosition, PrepareFlags.None, eventTypeIdIgnored, Data,
				Encoding.UTF8.GetBytes("")
			),
			streamName,
			EventType
		);

		return ResolvedEvent.ForUnresolvedEvent(record, 0);
	}
}

public readonly record struct MessageBatch(string CategoryName, string StreamName, MessageData[] Messages);

public static class CollectionExtension {
	public static T RandomElement<T>(this ICollection<T> collection) =>
		collection.ElementAt(Random.Shared.Next(0, collection.Count));
}
