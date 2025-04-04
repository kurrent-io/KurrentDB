using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;

namespace EventStore.Projections.Core.Services.Grpc {
	internal partial class ProjectionManagement {
		private static readonly Operation DeleteOperation = new Operation(Operations.Projections.Delete);
		public override async Task<DeleteResp> Delete(DeleteReq request, ServerCallContext context) {
			var deletedSource = new TaskCompletionSource<bool>();
			var options = request.Options;

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, DeleteOperation, context.CancellationToken)
				.ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}
			var name = options.Name;
			var deleteCheckpointStream = options.DeleteCheckpointStream;
			var deleteStateStream = options.DeleteStateStream;
			var deleteEmittedStreams = options.DeleteEmittedStreams;
			var runAs = new ProjectionManagementMessage.RunAs(user);

			var envelope = new CallbackEnvelope(OnMessage);

			_queue.Publish(new ProjectionManagementMessage.Command.Delete(envelope, name, runAs,
				deleteCheckpointStream, deleteStateStream, deleteEmittedStreams));

			await deletedSource.Task.ConfigureAwait(false);

			return new DeleteResp();

			void OnMessage(Message message) {
				if (!(message is ProjectionManagementMessage.Updated)) {
					deletedSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					return;
				}

				deletedSource.TrySetResult(true);
			}
		}
	}
}
