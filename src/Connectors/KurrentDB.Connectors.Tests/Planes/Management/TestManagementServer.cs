// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// using EventStore.Plugins.Authorization;
// using Eventuous;
// using Eventuous.Testing;
// using KurrentDB.Connectors.Tests.Infrastructure.Http;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.DependencyInjection.Extensions;
//
// namespace KurrentDB.Connectors.Tests.Planes.Management;
//
// [PublicAPI]
// public class TestManagementServer(ITestOutputHelper output) : TestServerContext<Program>(output, TestServerStartMode.StartHost) {
//     protected override void ConfigureServices(IServiceCollection services) =>
//         services
//             .AddSingleton<TimeProvider>(new FakeTimeProvider())
//             .AddSingleton<IAuthorizationProvider>(new FakeAuthorizationProvider())
//             .RemoveAll<IEventStore>() // So that we can use InMemoryEventStore.
//             .AddEventStore<InMemoryEventStore>();
// }
