// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.LoadTesting.Generators;

public interface IMessageGenerator {
	IAsyncEnumerable<MessageBatch> GenerateBatches(LoadTestPartitionConfig config);
}

public class MessageGenerator : IMessageGenerator {
	public async IAsyncEnumerable<MessageBatch> GenerateBatches(LoadTestPartitionConfig config) {
		var eventTypesByCategory = GenerateCategories(config);
		var eventsLeft = config.TotalMessagesCount;

		do {
			var batchSize = Math.Min(Random.Shared.Next(1, config.MaxBatchSize + 1), eventsLeft);

			yield return GenerateBatch(config, eventTypesByCategory, batchSize); // <- Pass batchSize

			eventsLeft -= batchSize;

			if (eventsLeft % 10 == 0) await Task.Yield();
		} while (eventsLeft > 0);
	}

	private static MessageBatch GenerateBatch(LoadTestPartitionConfig config, Dictionary<string, string[]> eventTypesByCategory, int batchSize) {
		var category = eventTypesByCategory.Keys.RandomElement();
		var streamName = $"{category}-${config.PartitionId}_${Random.Shared.Next(0, config.MaxStreamsPerCategory)}";

		var messages = new MessageData[batchSize]; // <- Use batchSize instead of MaxBatchSize

		for (int i = 0; i < messages.Length; i++) {
			var eventType = eventTypesByCategory[category].RandomElement();
			messages[i] = new MessageData(eventType, Enumerable.Repeat((byte)0x20, config.MessageSize).ToArray());
		}

		return new MessageBatch(category, streamName, messages);
	}

	private static Dictionary<string, string[]> GenerateCategories(LoadTestPartitionConfig loadTestPartitionConfig) {
		var categories = Enumerable.Range(loadTestPartitionConfig.StartCategoryIndex, loadTestPartitionConfig.CategoriesCount)
			.Select(index => $"c{index}")
			.ToArray();

		return categories.ToDictionary(
			category => category,
			category => Enumerable.Range(0, loadTestPartitionConfig.MessageTypesCount).Select(index => $"{category}-et{index}").ToArray()
		);
	}
}

public readonly record struct MessageData(string EventType, byte[] Data);

public readonly record struct MessageBatch(string CategoryName, string StreamName, MessageData[] Messages);

public static class CollectionExtension {
	public static T RandomElement<T>(this ICollection<T> collection) =>
		collection.ElementAt(Random.Shared.Next(0, collection.Count));
}
