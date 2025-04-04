using System.Threading.Tasks;
using EventStore.Client.Users;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc {
	internal partial class Users {
		private static readonly Operation DisableOperation = new Operation(Plugins.Authorization.Operations.Users.Disable);
		public override async Task<DisableResp> Disable(DisableReq request, ServerCallContext context) {
			var options = request.Options;

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, DisableOperation, context.CancellationToken).ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}
			var disableSource = new TaskCompletionSource<bool>();

			var envelope = new CallbackEnvelope(OnMessage);

			_publisher.Publish(new UserManagementMessage.Disable(envelope, user, options.LoginName));

			await disableSource.Task.ConfigureAwait(false);

			return new DisableResp();

			void OnMessage(Message message) {
				if (HandleErrors(options.LoginName, message, disableSource))
					return;

				disableSource.TrySetResult(true);
			}
		}
	}
}
