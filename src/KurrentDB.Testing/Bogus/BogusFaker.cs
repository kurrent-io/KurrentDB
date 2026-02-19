// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using TUnit.Core.Interfaces;

namespace KurrentDB.Testing.Bogus;

/// <summary>
/// A parameterless version of Bogus.Faker for use with TUnit's ClassDataSource.
/// This allows TUnit to instantiate and inject a Faker instance into test classes.
/// </summary>
public class BogusFaker : Faker, IAsyncInitializer {
	public Task InitializeAsync() {
		// Initialize the randomizer up front. If the tests do it
		// on demand we can get collection has been modified errors
		_ = Random;
		return Task.CompletedTask;
	}
}
