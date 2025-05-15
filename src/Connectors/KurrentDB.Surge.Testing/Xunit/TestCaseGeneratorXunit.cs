// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;

namespace KurrentDB.Surge.Testing.Xunit;

public abstract class TestCaseGeneratorXunit<T> : ClassDataAttribute, IEnumerable<object[]> {
	protected TestCaseGeneratorXunit() : base(typeof(T)) {
		Faker = new();

		// ReSharper disable once VirtualMemberCallInConstructor
		Generated.AddRange(Data());

		if (Generated.Count == 0)
			throw new InvalidOperationException($"TestDataGeneratorXunit<{typeof(T).Name}> must provide at least one test case.");
	}

	protected Faker Faker { get; }

	List<object[]> Generated { get; } = [];

	public IEnumerator<object[]> GetEnumerator() => Generated.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	protected abstract IEnumerable<object[]> Data();
}
