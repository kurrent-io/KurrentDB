// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core;
using KurrentDB.Testing.TUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Tests.Fixtures;

[PublicAPI]
[SuppressMessage("Performance", "CA1822:Mark members as static")]
public sealed partial class ClusterVNodeTestContext {
    [ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
    public required KurrentContext KurrentContext { get; init; }

    /// <summary>
    /// The actual test server instance representing a cluster node.
    /// </summary>
    public ClusterVNodeOptions ServerOptions => KurrentContext.Node.ClusterVNodeOptions;

    /// <summary>
    /// The actual test server instance representing a cluster node.
    /// </summary>
    public IServiceProvider Services => KurrentContext.Node.Services;

    /// <summary>
    /// Pre-configured Faker instance for generating test data.
    /// </summary>
    public ILogger Logger => TestContext.Current.Logger();

    /// <summary>
    /// Pre-configured Faker instance for generating test data.
    /// </summary>
    public ILoggerFactory LoggerFactory => TestContext.Current.LoggerFactory();

    /// <summary>
    /// The time provider used for simulating and controlling time in tests.
    /// </summary>
    public FakeTimeProvider Time => Services.GetRequiredService<FakeTimeProvider>();

    /// <summary>
    /// The client for interacting with the system bus.
    /// </summary>
    public ISystemClient SystemClient => Services.GetRequiredService<ISystemClient>();

    /// <summary>
    /// The gRPC client for the Streams service.
    /// </summary>
    public StreamsServiceClient StreamsClient => KurrentContext.StreamsV2Client;
}
