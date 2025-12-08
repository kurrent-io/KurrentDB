// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public enum PartitionKeyType {
	None,
	String,
	Double,
	Int16,
	Int32,
	Int64,
	UInt32,
	UInt64,
}

//qq consider the names here
public static class CustomIndexEvents {
	public class Created {
		public string EventFilter { get; set; } = "";

		public string PartitionKeySelector { get; set; } = "";

		public PartitionKeyType PartitionKeyType { get; set; }
	}

	public class Enabled {
	}

	public class Disabled {
	}

	public class Deleted {
	}
}
