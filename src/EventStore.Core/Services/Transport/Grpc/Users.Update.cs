using System.Linq;
using System.Threading.Tasks;
using EventStore.Client.Users;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc {
	internal partial class Users {
		private static readonly Operation UpdateOperation = new Operation(Plugins.Authorization.Operations.Users.Update);

		public override async Task<UpdateResp> Update(UpdateReq request, ServerCallContext context) {
			var options = request.Options;

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, UpdateOperation, context.CancellationToken).ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}
			var updateSource = new TaskCompletionSource<bool>();

			var envelope = new CallbackEnvelope(OnMessage);

			_publisher.Publish(new UserManagementMessage.Update(envelope, user, options.LoginName, options.FullName,
				options.Groups.ToArray()));

			await updateSource.Task.ConfigureAwait(false);

			return new UpdateResp();

			void OnMessage(Message message) {
				if (HandleErrors(options.LoginName, message, updateSource))
					return;

				updateSource.TrySetResult(true);
			}
		}
	}
}
