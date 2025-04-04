using System;
using System.Threading.Tasks;
using EventStore.Client.Redaction;
using EventStore.Core.Data.Redaction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc {
	internal partial class Redaction {
		private static readonly Operation SwitchChunkOperation = new(EventStore.Plugins.Authorization.Operations.Node.Redaction.SwitchChunk);

		public override async Task SwitchChunks(
			IAsyncStreamReader<SwitchChunkReq> requestStream,
			IServerStreamWriter<SwitchChunkResp> responseStream,
			ServerCallContext context) {

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, SwitchChunkOperation, context.CancellationToken).ConfigureAwait(false))
				throw RpcExceptions.AccessDenied();

			var acquisitionId = Guid.Empty;
			try {
				acquisitionId = await AcquireChunksLock(context).ConfigureAwait(false);
				await SwitchChunks(requestStream, responseStream, acquisitionId).ConfigureAwait(false);
			} finally {
				await ReleaseChunksLock(acquisitionId).ConfigureAwait(false);
			}
		}

		private async Task<Guid> AcquireChunksLock(ServerCallContext context) {
			var tcsEnvelope = new TcsEnvelope<RedactionMessage.AcquireChunksLockCompleted>();
			_bus.Publish(new RedactionMessage.AcquireChunksLock(tcsEnvelope));

			var completionMsg = await tcsEnvelope.Task.ConfigureAwait(false);
			if (completionMsg.Result != AcquireChunksLockResult.Success)
				throw RpcExceptions.RedactionLockFailed();

			// we've managed to acquire the lock. we need to write the headers here since the client's waiting for them.
			// (they're written automatically when throwing an RpcException e.g. when acquiring the lock failed above)
			await context.WriteResponseHeadersAsync(new Metadata()).ConfigureAwait(false);

			return completionMsg.AcquisitionId;
		}

		private async Task SwitchChunks(
			IAsyncStreamReader<SwitchChunkReq> requestStream,
			IServerStreamWriter<SwitchChunkResp> responseStream,
			Guid acquisitionId) {

			await foreach (var request in requestStream.ReadAllAsync().ConfigureAwait(false)) {
				var tcsEnvelope = new TcsEnvelope<RedactionMessage.SwitchChunkCompleted>();
				_bus.Publish(new RedactionMessage.SwitchChunk(tcsEnvelope, acquisitionId, request.TargetChunkFile, request.NewChunkFile));

				var completionMsg = await tcsEnvelope.Task.ConfigureAwait(false);
				var result = completionMsg.Result;
				if (result != SwitchChunkResult.Success)
					throw RpcExceptions.RedactionSwitchChunkFailed(result.GetErrorMessage());

				await responseStream.WriteAsync(new SwitchChunkResp()).ConfigureAwait(false);
			}
		}

		private async Task ReleaseChunksLock(Guid acquisitionId) {
			if (acquisitionId == Guid.Empty)
				return;

			var tcsEnvelope = new TcsEnvelope<RedactionMessage.ReleaseChunksLockCompleted>();
			_bus.Publish(new RedactionMessage.ReleaseChunksLock(tcsEnvelope, acquisitionId));

			var completionMsg = await tcsEnvelope.Task.ConfigureAwait(false);
			var result = completionMsg.Result;
			if (result != ReleaseChunksLockResult.Success)
				throw RpcExceptions.RedactionSwitchChunkFailed(result.GetErrorMessage());
		}
	}
}
