// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public enum CustomIndexValueType { 
	None,
	String,
	Double,
	Int16,
	Int32,
	Int64,
	UInt32,
	UInt64,
}

public static class CustomIndexEvents {
	public class Created {
		public string Filter { get; set; } = "";

		public string ValueSelector { get; set; } = "";

		public CustomIndexValueType ValueType { get; set; }
	}

	public class Started {
	}

	public class Stopped {
	}

	public class Deleted {
	}
}
