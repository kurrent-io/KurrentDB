// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.ServerFeatures;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Tests.ClientAPI.projectionsManager;
using Google.Protobuf.Reflection;
using Grpc.Net.Client;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.grpc_service;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class ServerFeaturesTests<TLogFormat, TStreamId>: SpecificationWithNodeAndProjectionsManager<TLogFormat, TStreamId> {
	private List<SupportedMethod> _supportedEndPoints = new ();
	private List<SupportedMethod> _expectedEndPoints = new ();

	public override Task Given() {
		_expectedEndPoints.AddRange(GetEndPoints(Client.Projections.Projections.Descriptor));
		var createEndPoint = _expectedEndPoints.FirstOrDefault(ep => ep.MethodName.Contains("create"));
		createEndPoint?.Features.Add("track_emitted_streams");

		return Task.CompletedTask;
	}

	public override async Task When() {

		using var channel = GrpcChannel.ForAddress(
			new Uri($"https://{_node.HttpEndPoint}"),
			new GrpcChannelOptions {
				HttpHandler = _node.HttpMessageHandler
			});
		var client = new ServerFeatures.ServerFeaturesClient(channel);

		var resp = await client.GetSupportedMethodsAsync(new Empty());
		_supportedEndPoints = resp.Methods.Where(x => x.ServiceName.Contains("projections")).ToList();
	}

	private SupportedMethod[] GetEndPoints(ServiceDescriptor desc) =>
		desc.Methods.Select(x => new SupportedMethod {
			MethodName = x.Name.ToLower(),
			ServiceName = x.Service.FullName.ToLower()
		}).ToArray();

	[Test]
	public void should_receive_expected_endpoints() {
		CollectionAssert.AreEquivalent(_expectedEndPoints, _supportedEndPoints);
	}
}
