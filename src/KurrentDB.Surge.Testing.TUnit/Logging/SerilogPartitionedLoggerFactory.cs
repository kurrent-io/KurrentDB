// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Kurrent.Surge.Testing.TUnit.Logging;

public interface IPartitionedLoggerFactory : ILoggerFactory, IAsyncDisposable;

sealed class SerilogPartitionedLoggerFactory : IPartitionedLoggerFactory {
    public SerilogPartitionedLoggerFactory(string partitionName, Guid partitionId, Logger logger, Subject<LogEvent> onNext, Predicate<(Guid PartitionId, LogEvent LogEvent)> filter) {
        Logger   = logger;
        Provider = new SerilogLoggerProvider();

        var partitionProp = new LogEventProperty(partitionName, new ScalarValue(partitionId));

        Subscription = onNext
            .Where(logEvent => filter((partitionId, logEvent)))
            .Subscribe(logEvent => {
                logEvent.AddPropertyIfAbsent(partitionProp);
                Logger.Write(logEvent);
            });
    }

    SerilogLoggerProvider Provider     { get; }
    Logger                Logger       { get; }
    IDisposable           Subscription { get; }

    public ILogger CreateLogger(string categoryName) =>
        Provider.CreateLogger(categoryName);

    public void AddProvider(ILoggerProvider provider) =>
        throw new NotImplementedException();

    public async ValueTask DisposeAsync() {
        Subscription.Dispose();
        await Logger.DisposeAsync();
        await Provider.DisposeAsync();
    }

    // Failsafe
    public void Dispose() {
        Logger
            .ForContext<SerilogPartitionedLoggerFactory>()
            .Warning("Dispose() method called directly, use DisposeAsync() instead!");

        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
