using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;

namespace EventStore.Projections.Core.Services.Grpc {
	internal partial class ProjectionManagement {
		private static readonly Operation DisableOperation = new Operation(Operations.Projections.Disable);
		public override async Task<DisableResp> Disable(DisableReq request, ServerCallContext context) {
			var disableSource = new TaskCompletionSource<bool>();

			var options = request.Options;

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, DisableOperation, context.CancellationToken)
				.ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}
			var name = options.Name;
			var runAs = new ProjectionManagementMessage.RunAs(user);

			var envelope = new CallbackEnvelope(OnMessage);

			_queue.Publish(options.WriteCheckpoint
				? new ProjectionManagementMessage.Command.Disable(envelope, name, runAs)
				: (Message)new ProjectionManagementMessage.Command.Abort(envelope, name, runAs));

			await disableSource.Task.ConfigureAwait(false);

			return new DisableResp();

			void OnMessage(Message message) {
				if (!(message is ProjectionManagementMessage.Updated)) {
					disableSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					return;
				}

				disableSource.TrySetResult(true);
			}
		}
	}
}
