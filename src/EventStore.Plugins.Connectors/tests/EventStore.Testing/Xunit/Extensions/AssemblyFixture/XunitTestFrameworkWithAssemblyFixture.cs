using System.Reflection;
using Xunit.Sdk;

namespace EventStore.Testing.Xunit.Extensions.AssemblyFixture;

[PublicAPI]
public class XunitTestFrameworkWithAssemblyFixture(IMessageSink messageSink) : XunitTestFramework(messageSink) {
    public const string AssemblyName = "EventStore.Testing";
    public const string TypeName     = $"EventStore.Testing.Xunit.Extensions.AssemblyFixture.{nameof(XunitTestFrameworkWithAssemblyFixture)}";

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new XunitTestFrameworkExecutorWithAssemblyFixture(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}