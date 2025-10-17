// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Tests.Generators;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Appenders;

public interface IMessageBatchAppender : IAsyncDisposable {
	ValueTask Append(TestMessageBatch batch);
}
