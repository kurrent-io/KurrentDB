using System;
using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;
using static EventStore.Client.Projections.UpdateReq.Types.Options;

namespace EventStore.Projections.Core.Services.Grpc {
	internal partial class ProjectionManagement {
		private static readonly Operation UpdateOperation = new Operation(Operations.Projections.Update);
		public override async Task<UpdateResp> Update(UpdateReq request, ServerCallContext context) {
			var updatedSource = new TaskCompletionSource<bool>();
			var options = request.Options;

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, UpdateOperation, context.CancellationToken)
				.ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}

			var name = options.Name;
			var query = options.Query;
			bool? emitEnabled = (options.EmitOptionCase, options.EmitEnabled) switch {
				(EmitOptionOneofCase.EmitEnabled, true) => true,
				(EmitOptionOneofCase.EmitEnabled, false) => false,
				(EmitOptionOneofCase.NoEmitOptions, _) => default,
				_ => throw new InvalidOperationException()
			};
			var runAs = new ProjectionManagementMessage.RunAs(user);

			var envelope = new CallbackEnvelope(OnMessage);
			_queue.Publish(
				new ProjectionManagementMessage.Command.UpdateQuery(envelope, name, runAs, query,
					emitEnabled));

			await updatedSource.Task.ConfigureAwait(false);

			return new UpdateResp();

			void OnMessage(Message message) {
				if (!(message is ProjectionManagementMessage.Updated)) {
					updatedSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					return;
				}

				updatedSource.TrySetResult(true);
			}
		}
	}
}
