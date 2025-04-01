// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Kurrent.Client.Redaction;
using EventStore.Core.Data.Redaction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Common;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Redaction {
	private static readonly Operation ReadOperation = new(Plugins.Authorization.Operations.Streams.Read);

	public override async Task GetEventPositions(
		IAsyncStreamReader<GetEventPositionReq> requestStream,
		IServerStreamWriter<GetEventPositionResp> responseStream,
		ServerCallContext context) {

		var user = context.GetHttpContext().User;

		await foreach (var request in requestStream.ReadAllAsync()) {
			var streamId = request.StreamIdentifier.StreamName.ToStringUtf8();
			var streamRevision = new StreamRevision(request.StreamRevision);

			var op = ReadOperation.WithParameter(
				Plugins.Authorization.Operations.Streams.Parameters.StreamId(streamId));

			if (!await _authorizationProvider.CheckAccessAsync(user, op, context.CancellationToken))
				throw RpcExceptions.AccessDenied();

			var tcsEnvelope = new TcsEnvelope<RedactionMessage.GetEventPositionCompleted>();
			_bus.Publish(new RedactionMessage.GetEventPosition(tcsEnvelope, streamId, streamRevision.ToInt64()));

			var completionMsg = await tcsEnvelope.Task;
			var result = completionMsg.Result;
			if (result != GetEventPositionResult.Success)
				throw RpcExceptions.RedactionGetEventPositionFailed(result.GetErrorMessage());

			var eventPositions = completionMsg.EventPositions;

			var response = new GetEventPositionResp();
			foreach (var eventPosition in eventPositions) {
				var pos = Position.FromInt64(eventPosition.LogPosition, eventPosition.LogPosition);
				response.EventPositions.Add(new Kurrent.Client.Redaction.EventPosition {
					LogPosition = pos.PreparePosition,
					ChunkInfo = new Kurrent.Client.Redaction.ChunkInfo {
						FileName = eventPosition.ChunkInfo.FileName,
						Version = eventPosition.ChunkInfo.Version,
						IsComplete = eventPosition.ChunkInfo.IsComplete,
						EventOffset = eventPosition.ChunkInfo.EventOffset
					}
				});
			}

			await responseStream.WriteAsync(response);
		}
	}
}
