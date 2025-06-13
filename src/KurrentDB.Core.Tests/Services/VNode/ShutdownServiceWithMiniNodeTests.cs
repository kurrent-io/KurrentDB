// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Subsystems;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.Tests.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.VNode;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class ShutdownServiceWithMiniNodeTests<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private readonly CancellationTokenSource _cts = new();
	private readonly TaskCompletionSource _tcs = TaskCompletionSourceFactory.CreateDefault();
	private MiniNode<TLogFormat, TStreamId> _node;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName, subsystems: [new FakePlugin(_tcs)]);
		await _node.Start();
	}

	[Test]
	public async Task should_graceful_shutdown_with_mininode() {
		await using var _ = _cts.Token.Register(() => _tcs.TrySetCanceled(_cts.Token));
		_cts.CancelAfter(TimeSpan.FromSeconds(15));
		await _node.Shutdown();
		await _tcs.Task;
	}

	private class FakePlugin(TaskCompletionSource source) : ISubsystem {
		private IPublisher _publisher;
		public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

		public void ConfigureApplication(IApplicationBuilder builder, IConfiguration configuration) {
			_publisher = builder.ApplicationServices.GetRequiredService<IPublisher>();
			_publisher.Publish(new SystemMessage.RegisterForGracefulTermination("foobar", () => {
				var envelope = new CallbackEnvelope(msg => {
					if (msg is not ClientMessage.ReadStreamEventsForwardCompleted resp)
						return;

					if (resp.Result != ReadStreamResult.NoStream)
						return;

					source.TrySetResult();
					_publisher.Publish(new SystemMessage.ComponentTerminated("foobar"));
				});

				_publisher.Publish(
					new ClientMessage.ReadStreamEventsForward(
						Guid.NewGuid(),
						Guid.NewGuid(),
						envelope,
						"foobar",
						0,
						1,
						false,
						false,
						null,
						SystemAccounts.System,
						true));
			}));
		}

		public string Name => "foobar";
		public string DiagnosticsName => "foobar";
		public KeyValuePair<string, object>[] DiagnosticsTags { get; }
		public string Version => "version";
		public bool Enabled => true;
		public string LicensePublicKey { get; }
		public Task Start() => Task.CompletedTask;

		public Task Stop() => Task.CompletedTask;
	}
}
