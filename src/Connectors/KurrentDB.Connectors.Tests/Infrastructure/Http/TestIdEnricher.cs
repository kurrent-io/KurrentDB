// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Serilog.Core;
using Serilog.Events;

namespace KurrentDB.Connectors.Tests.Infrastructure.Http;

public class TestIdEnricher : ILogEventEnricher {
    string? _testRunId;

    public void UpdateId(string testRunId) => _testRunId = testRunId;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("TestRunId", _testRunId));
}