// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Storage;

internal struct IndexRecord {
	public int event_number { get; set; }
	public long log_position { get; set; }
}

internal struct CategoryRecord {
	public int category_seq { get; set; }
	public long log_position { get; set; }
	public int event_number { get; set; }
}

internal struct EventTypeRecord {
	public int event_type_seq { get; set; }
	public long log_position { get; set; }
	public int event_number { get; set; }
}

internal struct AllRecord {
	public long seq { get; set; }
	public long log_position { get; set; }
	public int event_number { get; set; }
}

public struct ReferenceRecord {
	public long id { get; set; }
	public required string name { get; set; }
}
