// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.User.Management;

public abstract class UserIndexException : Exception;

public class UserIndexDomainException(string indexName) : UserIndexException {
	public string IndexName => indexName;
}

public class UserIndexNotFoundException(string indexName) : UserIndexDomainException(indexName);

public class UserIndexAlreadyExistsException(string indexName) : UserIndexDomainException(indexName);

public class UserIndexesNotReadyException(long currentPosition, long targetPosition) : UserIndexException {
	public long CurrentPosition { get; } = currentPosition;
	public long TargetPosition { get; } = targetPosition;
};
