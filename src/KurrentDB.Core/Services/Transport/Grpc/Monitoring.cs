// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Client.Monitoring;
using Grpc.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.Transport.Grpc;

internal class Monitoring(IPublisher publisher) : EventStore.Client.Monitoring.Monitoring.MonitoringBase {
	public override async Task Stats(StatsReq request, IServerStreamWriter<StatsResp> responseStream, ServerCallContext context) {
		var channel = Channel.CreateBounded<StatsResp>(new BoundedChannelOptions(1) {
			SingleReader = true,
			SingleWriter = true
		});

		_ = Receive();

		await foreach (var statsResponse in channel.Reader.ReadAllAsync(context.CancellationToken)) {
			await responseStream.WriteAsync(statsResponse, context.CancellationToken);
		}

		async Task Receive() {
			var delay = TimeSpan.FromMilliseconds(request.RefreshTimePeriodInMs);
			var envelope = new CallbackEnvelope(message => {
				if (message is not MonitoringMessage.GetFreshStatsCompleted completed) {
					channel.Writer.TryComplete(UnknownMessage<MonitoringMessage.GetFreshStatsCompleted>(message));
					return;
				}

				var response = new StatsResp();

				foreach (var (key, value) in completed.Stats.Where(stat => stat.Value is not null)) {
					response.Stats.Add(key, value.ToString());
				}

				channel.Writer.TryWrite(response);
			});
			while (!context.CancellationToken.IsCancellationRequested) {
				publisher.Publish(new MonitoringMessage.GetFreshStats(envelope, x => x, request.UseMetadata, false));

				await Task.Delay(delay, context.CancellationToken);
			}
		}
	}

	private static RpcException UnknownMessage<T>(Message message) where T : Message =>
		new(new(StatusCode.Unknown, $"Envelope callback expected {typeof(T).Name}, received {message.GetType().Name} instead"));
}
