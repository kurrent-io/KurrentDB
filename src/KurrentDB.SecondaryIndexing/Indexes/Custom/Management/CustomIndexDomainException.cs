// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class UserIndexException : Exception;

public class UserIndexDomainException(string customIndexName) : UserIndexException {
	public string CustomIndexName => customIndexName;
}

public class UserIndexNotFoundException(string customIndexName) : UserIndexDomainException(customIndexName);

public class UserIndexAlreadyExistsException(string customIndexName) : UserIndexDomainException(customIndexName);

public class UserIndexesNotReadyException(long currentPosition, long targetPosition) : UserIndexException {
	public long CurrentPosition { get; } = currentPosition;
	public long TargetPosition { get; } = targetPosition;
};
