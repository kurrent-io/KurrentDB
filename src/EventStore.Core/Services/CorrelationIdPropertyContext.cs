namespace EventStore.Core.Services {
	public static class CorrelationIdPropertyContext {
		public static string CorrelationIdProperty { get; set; } = "$correlationId";
	}
}