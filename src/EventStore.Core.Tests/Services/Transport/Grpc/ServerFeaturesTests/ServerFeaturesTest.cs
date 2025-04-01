// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.ServerFeatures;
using EventStore.Core.Tests.Integration;
using Google.Protobuf.Reflection;
using Grpc.Net.Client;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc.ServerFeaturesTests;

public class ServerFeaturesTest {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_getting_supported_methods<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {

		private List<SupportedMethod> _supportedEndPoints = new ();
		private List<SupportedMethod> _expectedEndPoints = new ();
		private string _expectedServerVersion;
		private string _serverVersion;

		protected override async Task Given() {
			var streamEndPoints = GetEndPoints(Client.Streams.Streams.Descriptor);
			foreach (var ep in streamEndPoints) {
				if (ep.MethodName.Contains("read")) ep.Features.AddRange(new[] {"position", "events"});
				else if (ep.MethodName.Contains("batchappend")) ep.Features.Add("deadline_duration");
			}

			var psubEndPoints = GetEndPoints(Client.PersistentSubscriptions.PersistentSubscriptions.Descriptor);
			foreach (var ep in psubEndPoints) {
				ep.Features.AddRange(new[] {"stream", "all"});
			}

			_expectedEndPoints.AddRange(streamEndPoints);
			_expectedEndPoints.AddRange(psubEndPoints);
			_expectedEndPoints.AddRange(GetEndPoints(Client.Operations.Operations.Descriptor));
			_expectedEndPoints.AddRange(GetEndPoints(Client.Users.Users.Descriptor));
			_expectedEndPoints.AddRange(GetEndPoints(Client.Gossip.Gossip.Descriptor));
			_expectedEndPoints.AddRange(GetEndPoints(Client.Monitoring.Monitoring.Descriptor));
			_expectedEndPoints.AddRange(GetEndPoints(Kurrent.Client.Redaction.Redaction.Descriptor));
			_expectedEndPoints.AddRange(GetEndPoints(ServerFeatures.Descriptor));

			var versionParts = EventStore.Common.Utils.VersionInfo.Version.Split('.');
			_expectedServerVersion = string.Join('.', versionParts.Take(3));

			var node = GetLeader();
			await Task.WhenAll(node.AdminUserCreated, node.Started);

			using var channel = GrpcChannel.ForAddress(new Uri($"https://{node.HttpEndPoint}"),
				new GrpcChannelOptions {
					HttpClient = new HttpClient(new SocketsHttpHandler {
						SslOptions = {
							RemoteCertificateValidationCallback = delegate { return true; }
						}
					}, true)
				});
			var client = new ServerFeatures.ServerFeaturesClient(channel);

			var resp = await client.GetSupportedMethodsAsync(new Empty());
			_supportedEndPoints = resp.Methods.ToList();
			_serverVersion = resp.EventStoreServerVersion;

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

		[Test]
		public void should_receive_the_correct_eventstore_version() {
			Assert.AreEqual(_expectedServerVersion, _serverVersion);
		}
	}
}

