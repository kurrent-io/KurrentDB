// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class CustomIndexDomainException(string customIndexName) : Exception {
	public string CustomIndexName => customIndexName;
}

public class CustomIndexNotFoundException(string customIndexName) : CustomIndexDomainException(customIndexName);

public class CustomIndexAlreadyExistsException(string customIndexName) : CustomIndexDomainException(customIndexName);
