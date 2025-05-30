// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Core.DataStructures.ProbabilisticFilter;

public interface IPersistenceStrategy : IDisposable {
	BloomFilterAccessor DataAccessor { get; }
	bool Create { get; }
	void Init();
	void Flush();
	Header ReadHeader();
	void WriteHeader(Header header);
}
