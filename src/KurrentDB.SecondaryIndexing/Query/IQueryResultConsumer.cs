// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

/// <summary>
/// Represents query result consumer.
/// </summary>
public interface IQueryResultConsumer {
	ValueTask ConsumeAsync(IQueryResultReader resultReader, CancellationToken token);

	void Bind<TBinder>(scoped TBinder binder) where TBinder : IPreparedQueryBinder, allows ref struct;

	bool UseStreaming => true;
}
