// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace KurrentDB.Surge.Testing.TUnit;

public abstract class TestCaseGenerator<T> : DataSourceGeneratorAttribute<T> {
    protected Faker Faker => TestingToolkitAutoWireUp.Faker;

    protected override IEnumerable<Func<T>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata) =>
        Data().Select(x => new Func<T>(() => x));

    protected abstract IEnumerable<T> Data();
}

public abstract class TestCaseGenerator<T1, T2> : DataSourceGeneratorAttribute<T1, T2> {
    protected Faker Faker => TestingToolkitAutoWireUp.Faker;

    protected override IEnumerable<Func<(T1, T2)>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata) =>
        Data().Select(x => new Func<(T1, T2)>(() => x));

    protected abstract IEnumerable<(T1, T2)> Data();
}

public abstract class TestCaseGenerator<T1, T2, T3> : DataSourceGeneratorAttribute<T1, T2, T3> {
    protected Faker Faker => TestingToolkitAutoWireUp.Faker;

    protected override IEnumerable<Func<(T1, T2, T3)>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata) =>
        Data().Select(x => new Func<(T1, T2, T3)>(() => x));

    protected abstract IEnumerable<(T1, T2, T3)> Data();
}

public abstract class TestCaseGenerator<T1, T2, T3, T4> : DataSourceGeneratorAttribute<T1, T2, T3, T4> {
    protected Faker Faker => TestingToolkitAutoWireUp.Faker;

    protected override IEnumerable<Func<(T1, T2, T3, T4)>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata) =>
        Data().Select(x => new Func<(T1, T2, T3, T4)>(() => x));

    protected abstract IEnumerable<(T1, T2, T3, T4)> Data();
}

public abstract class TestCaseGenerator<T1, T2, T3, T4, T5> : DataSourceGeneratorAttribute<T1, T2, T3, T4, T5> {
    protected Faker Faker => TestingToolkitAutoWireUp.Faker;

    protected override IEnumerable<Func<(T1, T2, T3, T4, T5)>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata) =>
        Data().Select(x => new Func<(T1, T2, T3, T4, T5)>(() => x));

    protected abstract IEnumerable<(T1, T2, T3, T4, T5)> Data();
}
