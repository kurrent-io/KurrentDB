// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.ServerFeatures;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Routing;
using VersionInfo = EventStore.Common.Utils.VersionInfo;

namespace EventStore.Core.Services.Transport.Grpc;

class ServerFeatures(EndpointDataSource endpointDataSource) : EventStore.Client.ServerFeatures.ServerFeatures.ServerFeaturesBase {
	public const int ApiVersion = 1;
	private readonly Task<SupportedMethods> _supportedMethods = GetSupportedMethods(endpointDataSource);

	public override Task<SupportedMethods> GetSupportedMethods(Empty request, ServerCallContext context) =>
		_supportedMethods;

	private static Task<SupportedMethods> GetSupportedMethods(EndpointDataSource endpointDataSource) {
		var supportedEndpoints = endpointDataSource.Endpoints
			.Select(x => x.Metadata.FirstOrDefault(m => m is GrpcMethodMetadata))
			.OfType<GrpcMethodMetadata>()
			.Where(x => x.Method.ServiceName.Contains("client"))
			.Select(x => {
				var method = new SupportedMethod {
					MethodName = x.Method.Name.ToLower(),
					ServiceName = x.Method.ServiceName.ToLower(),
				};

				if (x.Method.ServiceName.Contains("PersistentSubscriptions")) {
					method.Features.AddRange(["stream", "all"]);
				} else if (x.Method.ServiceName.Contains("Streams") && x.Method.Name.Contains("Read")) {
					method.Features.AddRange(["position", "events"]);
				} else if (x.Method.ServiceName.Contains("Streams") && x.Method.Name.Contains("BatchAppend")) {
					method.Features.Add("deadline_duration");
				} else if (x.Method.ServiceName.Contains("Projections") && x.Method.Name.Contains("Create")) {
					method.Features.Add("track_emitted_streams");
				}

				return method;
			});

		var result = new SupportedMethods {
			EventStoreServerVersion = string.Join('.', VersionInfo.Version.Split('.')[..3]),
			Methods = { supportedEndpoints.Distinct() }
		};
		return Task.FromResult(result);
	}
}
