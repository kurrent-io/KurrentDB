// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager;
using NUnit.Framework;

// ReSharper disable ObjectCreationAsStatement

namespace KurrentDB.Core.Tests.Services.RequestManagement;

[TestFixture]
public class when_creating_stream_request_manager {
	protected static readonly IPublisher Publisher = new SynchronousScheduler();
	protected static readonly TimeSpan CommitTimeout = TimeSpan.FromMinutes(5);
	protected static readonly IEnvelope Envelope = new NoopEnvelope();
	protected static readonly Guid InternalCorrId = Guid.NewGuid();
	protected static readonly Guid ClientCorrId = Guid.NewGuid();



	[Test]
	public void null_publisher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() =>
			new FakeRequestManager(
				null,
				CommitTimeout,
				Envelope,
				InternalCorrId,
				ClientCorrId,
				new CommitSource()));
	}

	[Test]
	public void null_envelope_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() =>
			new FakeRequestManager(
				Publisher,
				CommitTimeout,
				null,
				InternalCorrId,
				ClientCorrId,
				new CommitSource()));
	}
	[Test]
	public void internal_corrId_empty_guid_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() =>
			new FakeRequestManager(
				Publisher,
				CommitTimeout,
				Envelope,
				Guid.Empty,
				ClientCorrId,
				new CommitSource()));
	}
	[Test]
	public void client_corrId_empty_guid_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() =>
			new FakeRequestManager(
				Publisher,
				CommitTimeout,
				Envelope,
				InternalCorrId,
				Guid.Empty,
				new CommitSource()));
	}

	[Test]
	public void empty_commit_source_throws_null_argument_exception() {
		Assert.Throws<ArgumentNullException>(() =>
			new FakeRequestManager(
				Publisher,
				CommitTimeout,
				Envelope,
				InternalCorrId,
				ClientCorrId,
				null));
	}
}
