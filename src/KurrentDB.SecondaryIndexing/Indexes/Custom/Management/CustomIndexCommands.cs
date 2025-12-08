// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public static class CustomIndexCommands {
	public class Create {
		public string Name { get; set; } = "";

		public string EventFilter { get; set; } = "";

		public string PartitionKeySelector { get; set; } = "";

		public PartitionKeyType PartitionKeyType { get; set; }

		public bool Start { get; set; }

		public bool Force { get; set; }
	}

	public class Start {
		public string Name { get; set; } = "";
	}

	public class Stop {
		public string Name { get; set; } = "";
	}

	public class Delete {
		public string Name { get; set; } = "";
	}
}
