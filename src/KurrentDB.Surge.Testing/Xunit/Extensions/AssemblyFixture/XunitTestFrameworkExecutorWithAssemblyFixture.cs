// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using Xunit.Sdk;

namespace KurrentDB.Surge.Testing.Xunit.Extensions.AssemblyFixture;

public class XunitTestFrameworkExecutorWithAssemblyFixture(
    AssemblyName assemblyName,
    ISourceInformationProvider sourceInformationProvider,
    IMessageSink diagnosticMessageSink
) : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink) {
    protected override async void RunTestCases(
        IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions
    ) {
        using var assemblyRunner = new XunitTestAssemblyRunnerWithAssemblyFixture(
            TestAssembly,
            testCases,
            DiagnosticMessageSink,
            executionMessageSink,
            executionOptions
        );

        await assemblyRunner.RunAsync();
    }
}
