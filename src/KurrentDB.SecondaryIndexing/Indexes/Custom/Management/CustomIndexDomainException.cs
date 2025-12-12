// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class CustomIndexException : Exception;

public class CustomIndexDomainException(string customIndexName) : CustomIndexException {
	public string CustomIndexName => customIndexName;
}

public class CustomIndexNotFoundException(string customIndexName) : CustomIndexDomainException(customIndexName);

public class CustomIndexAlreadyExistsException(string customIndexName) : CustomIndexDomainException(customIndexName);

public class CustomIndexesNotReadyException(long currentPosition, long targetPosition) : CustomIndexException {
	public long CurrentPosition { get; } = currentPosition;
	public long TargetPosition { get; } = targetPosition;
};
