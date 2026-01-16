// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Indexes.User.Management;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

public static class UserIndexConstants {
	public const string Category = $"${nameof(UserIndex)}-";
	public const string StreamPrefix = $"{SystemStreams.IndexStreamPrefix}user-";
	public const string ManagementAllStream = $"$all-indexes";
	public const char FieldDelimiter = ':';
}
