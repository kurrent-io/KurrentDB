// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EventStore.Client.Projections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Tests.ClientAPI.projectionsManager;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.grpc_service;

[TestFixture]
public class ProjectionMetadataTests : SpecificationWithNodeAndProjectionsManager<LogFormat.V2, string> {
	private const string WithProperties = "projection-with-properties";
	private const string WithoutProperties = "projection-without-properties";

	private EventStore.Client.Projections.Projections.ProjectionsClient _client;
	private GrpcChannel _channel;

	public override async Task Given() {
		_channel = GrpcChannel.ForAddress(
			new Uri($"https://{_node.HttpEndPoint}"),
			new GrpcChannelOptions {
				HttpHandler = _node.HttpMessageHandler,
			});
		_client = new EventStore.Client.Projections.Projections.ProjectionsClient(_channel);

		await _client.CreateAsync(new CreateReq {
			Options = new CreateReq.Types.Options {
				Continuous = new CreateReq.Types.Options.Types.Continuous {
					Name = WithProperties,
					EmitEnabled = false,
					TrackEmittedStreams = false,
				},
				Query = "fromAll().when({})",
				Properties = {
					{ "deploy", Value.ForString("abc123") },
					{ "tool", Value.ForString("gaffer") },
				},
			},
		}, GetCallOptions());

		await _client.CreateAsync(new CreateReq {
			Options = new CreateReq.Types.Options {
				Continuous = new CreateReq.Types.Options.Types.Continuous {
					Name = WithoutProperties,
					EmitEnabled = false,
					TrackEmittedStreams = false,
				},
				Query = "fromAll().when({})",
			},
		}, GetCallOptions());
	}

	public override Task When() => Task.CompletedTask;

	// The definition write that carries the user properties is the first $ProjectionUpdated event;
	// later lifecycle writes only contain synthetic properties, so read forward and take the earliest.
	private async Task<byte[]> ReadDefinitionMetadata(string projectionName) {
		var stream = ProjectionNamesBuilder.ProjectionsStreamPrefix + projectionName;
		var slice = await _connection.ReadStreamEventsForwardAsync(stream, 0, 100, false, _credentials);
		return slice.Events
			.Select(resolved => resolved.Event)
			.First(e => e.EventType == ProjectionEventTypes.ProjectionUpdated)
			.Metadata;
	}

	[Test]
	public async Task properties_round_trip_into_the_definition_event_metadata() {
		var metadata = await ReadDefinitionMetadata(WithProperties);
		// Property metadata is synthesized back to JSON on read; the caller's keys round-trip at the top level.
		using var json = JsonDocument.Parse(metadata);
		Assert.AreEqual("abc123", json.RootElement.GetProperty("deploy").GetString());
		Assert.AreEqual("gaffer", json.RootElement.GetProperty("tool").GetString());
	}

	[Test]
	public async Task no_properties_writes_only_synthetic_metadata() {
		var metadata = await ReadDefinitionMetadata(WithoutProperties);
		if (metadata.Length == 0) return;

		using var json = JsonDocument.Parse(metadata);
		foreach (var prop in json.RootElement.EnumerateObject())
			Assert.That(prop.Name.StartsWith('$'), $"Unexpected user property: {prop.Name}");
	}

	public override Task TestFixtureTearDown() {
		_channel.Dispose();
		return base.TestFixtureTearDown();
	}

	private static CallOptions GetCallOptions() {
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
