// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

//qq consider the names here
public static class CustomIndexEvents {
	public class Created {
		public string EventFilter { get; set; } = "";

		public string PartitionKeySelector { get; set; } = "";

		public KeyType PartitionKeyType { get; set; }
	}

	public class Started {
	}

	public class Stopped {
	}

	public class Deleted {
	}
}
