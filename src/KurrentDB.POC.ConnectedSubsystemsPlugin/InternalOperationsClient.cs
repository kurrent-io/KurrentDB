// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.POC.IO.Core;

namespace KurrentDB.POC.ConnectedSubsystemsPlugin;

public class InternalOperationsClient : IOperationsClient {
	readonly IPublisher _publisher;

	public InternalOperationsClient(IPublisher publisher) {
		_publisher = publisher;
	}

	public void Resign() {
		_publisher.Publish(new ClientMessage.ResignNode());
	}

	public async Task<Guid?> StartScavengeAsync(CancellationToken token) {
		var envelope = new TcsEnvelope<Message>();
		_publisher.Publish(new ClientMessage.ScavengeDatabase(
			envelope,
			correlationId: Guid.NewGuid(),
			user: SystemAccounts.System,
			startFromChunk: 0,
			threads: 1,
			threshold: null,
			throttlePercent: null,
			syncOnly: false));

		return await envelope.Task.WaitAsync(token) switch {
			ClientMessage.ScavengeDatabaseStartedResponse started => Guid.Parse(started.ScavengeId),
			ClientMessage.ScavengeDatabaseInProgressResponse => null,
			var response => throw UnexpectedResponse(response),
		};
	}

	public async Task<LastScavenge> GetLastScavengeAsync(CancellationToken token) {
		var envelope = new TcsEnvelope<Message>();
		_publisher.Publish(new ClientMessage.GetLastDatabaseScavenge(envelope, Guid.NewGuid(), SystemAccounts.System));

		var response = await envelope.Task.WaitAsync(token);
		if (response is not ClientMessage.ScavengeDatabaseGetLastResponse last)
			throw UnexpectedResponse(response);

		var result = last.Result switch {
			ClientMessage.ScavengeDatabaseGetLastResponse.ScavengeResult.InProgress => LastScavengeResult.InProgress,
			ClientMessage.ScavengeDatabaseGetLastResponse.ScavengeResult.Success => LastScavengeResult.Success,
			ClientMessage.ScavengeDatabaseGetLastResponse.ScavengeResult.Stopped => LastScavengeResult.Stopped,
			ClientMessage.ScavengeDatabaseGetLastResponse.ScavengeResult.Errored => LastScavengeResult.Errored,
			_ => LastScavengeResult.Unknown,
		};

		return new LastScavenge(Guid.TryParse(last.ScavengeId, out var scavengeId) ? scavengeId : null, result);
	}

	public async Task<bool> StopScavengeAsync(Guid? scavengeId, CancellationToken token) {
		var envelope = new TcsEnvelope<Message>();
		_publisher.Publish(new ClientMessage.StopDatabaseScavenge(
			envelope, Guid.NewGuid(), SystemAccounts.System, scavengeId?.ToString() ?? "current"));

		return await envelope.Task.WaitAsync(token) switch {
			ClientMessage.ScavengeDatabaseStoppedResponse => true,
			ClientMessage.ScavengeDatabaseNotFoundResponse => false,
			var response => throw UnexpectedResponse(response),
		};
	}

	static InvalidOperationException UnexpectedResponse(Message response) =>
		new($"Unexpected response to a scavenge request: {response.GetType().Name} {response}");
}
