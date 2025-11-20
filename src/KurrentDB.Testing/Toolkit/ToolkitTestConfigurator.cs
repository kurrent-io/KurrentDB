// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Testing.OpenTelemetry;
using TUnit.Core.Interfaces;

namespace KurrentDB.Testing;

[AttributeUsage(AttributeTargets.Assembly)]
public class ToolkitTestConfigurator : Attribute, ITestDiscoveryEventReceiver {
    public ValueTask OnTestDiscovered(DiscoveredTestContext context) {
        context.TestContext.ConfigureOtel(new(context.TestContext.Metadata.TestDetails.ClassType.Name) {
            ServiceInstanceId = context.TestContext.Id,
            ServiceNamespace  = context.TestContext.Metadata.TestDetails.ClassType.Namespace
        });

        return ValueTask.CompletedTask;
    }
}
