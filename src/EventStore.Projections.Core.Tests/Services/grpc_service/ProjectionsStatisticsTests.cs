// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Projections;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Tests.ClientAPI.projectionsManager;
using Grpc.Core;
using Grpc.Net.Client;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.grpc_service;

[TestFixture]
public class ProjectionsStatisticsTests: SpecificationWithNodeAndProjectionsManager<LogFormat.V2, string> {
	private Client.Projections.Projections.ProjectionsClient _client;
	private GrpcChannel _channel;
	private StatisticsResp _invalidProjectionStatistics;
	private StatisticsResp _validProjectionStatistics;

	public override async Task Given() {
		_channel = GrpcChannel.ForAddress(
			new Uri($"https://{_node.HttpEndPoint}"),
			new GrpcChannelOptions {
				HttpHandler = _node.HttpMessageHandler,
			});

		_client = new Client.Projections.Projections.ProjectionsClient(_channel);
		await _client.CreateAsync(new CreateReq {
			Options = new CreateReq.Types.Options {
				Continuous = new CreateReq.Types.Options.Types.Continuous {
					Name = "invalid-projection",
					EmitEnabled = false,
					TrackEmittedStreams = false,
				},
				Query = "something invalid"
			},
		}, GetCallOptions());
		await _client.CreateAsync(new CreateReq {
			Options = new CreateReq.Types.Options {
				Continuous = new CreateReq.Types.Options.Types.Continuous {
					Name = "valid-projection",
					EmitEnabled = false,
					TrackEmittedStreams = false,
				},
				Query = "fromAll().when({})"
			},
		}, GetCallOptions());
	}

	public override async Task When() {
		var statuses = _client.Statistics(new StatisticsReq {
			Options = new StatisticsReq.Types.Options {
				All = new Empty()
			}
		}, GetCallOptions());
		while (await statuses.ResponseStream.MoveNext(CancellationToken.None)) {
			var status = statuses.ResponseStream.Current;
			if (status.Details.Name == "invalid-projection") {
				_invalidProjectionStatistics = status;
			}
			if (status.Details.Name == "valid-projection") {
				_validProjectionStatistics = status;
			}
		}
	}
	[Test]
	public void invalid_projection_should_have_default_values() {
		Assert.AreEqual("invalid-projection", _invalidProjectionStatistics.Details.EffectiveName);
		Assert.AreEqual("Faulted", _invalidProjectionStatistics.Details.Status);
		Assert.AreEqual(string.Empty, _invalidProjectionStatistics.Details.CheckpointStatus);
		Assert.AreEqual(string.Empty, _invalidProjectionStatistics.Details.Position);
		Assert.AreEqual(string.Empty, _invalidProjectionStatistics.Details.LastCheckpoint);
	}

	[Test]
	public void valid_projection_should_have_correct_values() {
		Assert.AreEqual("valid-projection", _validProjectionStatistics.Details.EffectiveName);
		Assert.AreEqual("Running", _validProjectionStatistics.Details.Status);
		Assert.AreEqual(string.Empty, _validProjectionStatistics.Details.CheckpointStatus);
		Assert.AreEqual("C:0/P:-1", _validProjectionStatistics.Details.Position);
		Assert.AreEqual("C:0/P:-1", _validProjectionStatistics.Details.LastCheckpoint);
	}

	public override Task TestFixtureTearDown() {
		_channel.Dispose();
		return base.TestFixtureTearDown();
	}

	protected CallOptions GetCallOptions() {
		var credentials = CallCredentials.FromInterceptor((_, metadata) => {
				metadata.Add(new Metadata.Entry("authorization",
					$"Basic {Convert.ToBase64String("admin:changeit"u8.ToArray())}"));
				return Task.CompletedTask;
			});
		return new(credentials: credentials,
			deadline: Debugger.IsAttached
				? DateTime.UtcNow.AddDays(1)
				: new DateTime?());
	}
}
