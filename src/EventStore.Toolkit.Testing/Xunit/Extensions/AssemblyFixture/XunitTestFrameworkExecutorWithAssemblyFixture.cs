using System.Reflection;
using Xunit.Sdk;

namespace EventStore.Toolkit.Testing.Xunit.Extensions.AssemblyFixture;

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