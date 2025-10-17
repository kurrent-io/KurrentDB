// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Testing.TUnit;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KurrentDB.Api.Tests;

public class TestEnvironmentWireUpTests {
    [Test]
    [Repeat(10)]
    public void logging_configured() {
        TestContext.Current.Logger()
            .LogInformation("Logger() is effective. TestContext TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        TestContext.Current.CreateLogger<TestEnvironmentWireUpTests>()
            .LogInformation("CreateLogger<T>() is effective. TestContext TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        TestContext.Current.CreateLogger("TestEnvironmentWireUpTestsFromString")
            .LogInformation("CreateLogger(string categoryName) is effective. TestContext TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        Log.Information("Logging from Serilog Static Log. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);
    }

    [Test]
    [Repeat(10)]
    public async ValueTask logs_are_scoped_to_test() {
        TestContext.Current.Logger()
            .LogInformation("Logger() is effective. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        TestContext.Current.CreateLogger<TestEnvironmentWireUpTests>()
            .LogInformation("CreateLogger<T>() is effective. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        TestContext.Current.CreateLogger("TestEnvironmentWireUpTestsFromString")
            .LogInformation("CreateLogger(string categoryName) is effective. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        Log.Information("Logging from Serilog Static Log. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        await Task.Run(async () => {
            TestContext.Current.Logger()
                .LogInformation("Logger() is effective in another thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

            TestContext.Current.CreateLogger<TestEnvironmentWireUpTests>()
                .LogInformation("CreateLogger<T>() is effective in another thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

            TestContext.Current.CreateLogger("TestEnvironmentWireUpTestsFromString")
                .LogInformation("CreateLogger(string categoryName) is effective in another thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

            Log.Information("Logging from Serilog Static Log in another thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

            await Task.Run(() => {
                TestContext.Current.Logger()
                    .LogInformation("Logger() is effective in sub thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

                TestContext.Current.CreateLogger<TestEnvironmentWireUpTests>()
                    .LogInformation("CreateLogger<T>() is effective in sub thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

                TestContext.Current.CreateLogger("TestEnvironmentWireUpTestsFromString")
                    .LogInformation("CreateLogger(string categoryName) is effective in sub thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

                Log.Information("Logging from Serilog Static Log in sub thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);
            });
        });
    }
}
