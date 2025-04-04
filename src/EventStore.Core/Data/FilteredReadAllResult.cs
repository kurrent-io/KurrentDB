namespace EventStore.Core.Data {
	public enum FilteredReadAllResult {
		Success = 0,
		NotModified = 1,
		Error = 2,
		AccessDenied = 3,
		Expired = 4,
	}
}
