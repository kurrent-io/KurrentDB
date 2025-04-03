// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc.PersistentSubscriptionTests;

internal class RestartSubsystemTests {

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_restarting_the_persistent_subscription_subsystem<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;
		private Exception _exception;

		protected override Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);
			return Task.CompletedTask;
		}

		protected override async Task When() {
			try {
				await _persistentSubscriptionsClient.RestartSubsystemAsync(new Empty(), GetCallOptions(AdminCredentials));
			} catch (Exception e) {
				_exception = e;
			}
		}

		[Test]
		public void should_not_throw_an_exception() {
			Assert.IsNull(_exception);
		}
	}

}
