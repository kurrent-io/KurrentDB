// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Api;

public static class Constants {
	public static class Properties {
		public const string SchemaNameKey      = "$schema.name";
		public const string SchemaFormatKey    = "$schema.format";
		public const string SchemaIdKey        = "$schema.id";
		public const string SchemaUrnKey       = "$schema.urn";
		public const string RecordTimestampKey = "$record.timestamp";
	}

	public static class Headers {
		public const string RequiresLeader = "requires-leader";
	}
}
