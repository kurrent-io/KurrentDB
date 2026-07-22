// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Hosting;

namespace KurrentDB.Core.TUnit.Tests.Hosting;

/// <summary>
/// Pins the contract <see cref="NodeLifetimeService"/> relies on: the token handed to a
/// waiter must be the same token the producer can revoke later, which is how leadership
/// revocation reaches work that is already running.
/// </summary>
public class TokenCompletionSourceTests {
	[Test]
	public async ValueTask complete_hands_out_a_token_that_is_not_cancelled() {
		// Arrange
		using var sut = new TokenCompletionSource();

		// Act
		sut.Complete();
		var token = await sut.Task;

		// Assert
		await Assert.That(token.IsCancellationRequested).IsFalse();
	}

	[Test]
	public async ValueTask cancel_revokes_the_token_already_handed_out() {
		// Arrange
		using var sut = new TokenCompletionSource();
		sut.Complete();
		var token = await sut.Task;

		// Act
		sut.Cancel();

		// Assert
		await Assert.That(token.IsCancellationRequested).IsTrue();
	}

	[Test]
	public async ValueTask cancel_releases_a_waiter_that_never_received_the_signal() {
		// Arrange — nothing completes this source, so the waiter is parked
		using var sut = new TokenCompletionSource();
		var waiter = sut.Task;

		// Act — this is the dispose path: revoke without ever having signalled
		sut.Cancel();

		// Assert — the waiter is released rather than hanging, and told to stop
		var token = await waiter;
		await Assert.That(token.IsCancellationRequested).IsTrue();
	}

	[Test]
	public async ValueTask complete_after_cancel_does_not_resurrect_the_token() {
		// Arrange
		using var sut = new TokenCompletionSource();
		sut.Cancel();

		// Act
		sut.Complete();
		var token = await sut.Task;

		// Assert
		await Assert.That(token.IsCancellationRequested).IsTrue();
	}
}
