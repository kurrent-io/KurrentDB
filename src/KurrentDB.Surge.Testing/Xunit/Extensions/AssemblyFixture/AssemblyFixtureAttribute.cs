// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Surge.Testing.Xunit.Extensions.AssemblyFixture;

// Required for test parallelization and assembly-scoped fixtures that collection fixtures cannot provide.
// This will be available in XUnit 3.0 which is not yet released, so it is recommended to do this for now.
// TODO: Remove this when XUnit 3.0 is released.
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class AssemblyFixtureAttribute(Type fixtureType) : Attribute {
    public Type FixtureType { get; private set; } = fixtureType;
}
