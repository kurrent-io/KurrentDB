using System.Reflection;
using Xunit.Sdk;

namespace EventStore.Toolkit.Testing.Xunit.Extensions.AssemblyFixture;

[PublicAPI]
public class XunitTestFrameworkWithAssemblyFixture(IMessageSink messageSink) : XunitTestFramework(messageSink) {
    public const string AssemblyName = "EventStore.Toolkit.Testing";
    public const string TypeName     = $"EventStore.Toolkit.Testing.Xunit.Extensions.AssemblyFixture.{nameof(XunitTestFrameworkWithAssemblyFixture)}";

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new XunitTestFrameworkExecutorWithAssemblyFixture(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}