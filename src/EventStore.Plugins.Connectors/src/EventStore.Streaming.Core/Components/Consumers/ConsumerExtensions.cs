namespace EventStore.Streaming.Consumers;

public static class ConsumerExtensions {
	public static async Task<List<EventStoreRecord>> ReadRecords(this IConsumer consumer, int count, TimeSpan timeSpan) {
		using var cancellator = new CancellationTokenSource(timeSpan);
		return await consumer.Records(cancellator.Token)
			.Take(count)
			.ToListAsync(CancellationToken.None);
	}
}