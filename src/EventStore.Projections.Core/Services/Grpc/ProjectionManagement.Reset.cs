using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;

namespace EventStore.Projections.Core.Services.Grpc {
	internal partial class ProjectionManagement {
		private static readonly Operation ResetOperation = new Operation(Operations.Projections.Create);
		public override async Task<ResetResp> Reset(ResetReq request, ServerCallContext context) {
			var resetSource = new TaskCompletionSource<bool>();

			var options = request.Options;

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, ResetOperation, context.CancellationToken)
				.ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}
			var name = options.Name;
			var runAs = new ProjectionManagementMessage.RunAs(user);

			var envelope = new CallbackEnvelope(OnMessage);

			_queue.Publish(new ProjectionManagementMessage.Command.Reset(envelope, name, runAs));

			await resetSource.Task.ConfigureAwait(false);

			return new ResetResp();

			void OnMessage(Message message) {
				if (!(message is ProjectionManagementMessage.Updated)) {
					resetSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					return;
				}

				resetSource.TrySetResult(true);
			}
		}
	}
}
