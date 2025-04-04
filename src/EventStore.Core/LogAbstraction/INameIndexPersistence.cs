// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.LogAbstraction;

public interface INameIndexPersistence<TValue> : IValueLookup<TValue>, IDisposable
	where TValue : struct {
	TValue LastValueAdded { get; }
	ValueTask Init(INameLookup<TValue> source, CancellationToken token);
	bool TryGetValue(string name, out TValue value);
	void Add(string name, TValue value);
}
