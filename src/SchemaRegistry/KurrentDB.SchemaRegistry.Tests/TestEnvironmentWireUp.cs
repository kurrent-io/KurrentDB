// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Testing;
using TUnit.Core.Executors;

[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]

namespace KurrentDB.SchemaRegistry.Tests;

public class TestEnvironmentWireUp {
    [Before(Assembly)]
    public static ValueTask BeforeAssembly(AssemblyHookContext context) => ToolkitTestEnvironment.Initialize(context.Assembly);

    [After(Assembly)]
    public static ValueTask AfterAssembly(AssemblyHookContext context) => ToolkitTestEnvironment.Reset(context.Assembly);
}
