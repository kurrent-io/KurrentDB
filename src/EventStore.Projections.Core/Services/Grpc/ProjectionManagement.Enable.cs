using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;

namespace EventStore.Projections.Core.Services.Grpc {
	internal partial class ProjectionManagement {
		private static readonly Operation EnableOperation = new Operation(Operations.Projections.Enable);
		public override async Task<EnableResp> Enable(EnableReq request, ServerCallContext context) {
			var enableSource = new TaskCompletionSource<bool>();

			var options = request.Options;

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, EnableOperation, context.CancellationToken)
				.ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}
			var name = options.Name;
			var runAs = new ProjectionManagementMessage.RunAs(user);

			var envelope = new CallbackEnvelope(OnMessage);

			_queue.Publish(new ProjectionManagementMessage.Command.Enable(envelope, name, runAs));

			await enableSource.Task.ConfigureAwait(false);

			return new EnableResp();

			void OnMessage(Message message) {
				if (!(message is ProjectionManagementMessage.Updated)) {
					enableSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					return;
				}

				enableSource.TrySetResult(true);
			}
		}
	}
}
