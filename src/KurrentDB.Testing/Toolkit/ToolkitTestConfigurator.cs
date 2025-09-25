using KurrentDB.Testing.OpenTelemetry;
using KurrentDB.Testing.TUnit;
using TUnit.Core.Interfaces;

namespace KurrentDB.Testing;

[AttributeUsage(AttributeTargets.Assembly)]
public class ToolkitTestConfigurator : Attribute, ITestDiscoveryEventReceiver {
    public ValueTask OnTestDiscovered(DiscoveredTestContext context) {
        var testUid = context.TestContext.AssignTestUid();

        context.TestContext.ConfigureOtel(new(context.TestContext.TestDetails.ClassType.Name) {
            ServiceInstanceId = testUid,
            ServiceNamespace  = context.TestContext.TestDetails.ClassType.Namespace
        });

        return ValueTask.CompletedTask;
    }
}
