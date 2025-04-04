using System.Linq;
using System.Threading.Tasks;
using EventStore.Client.Users;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc {
	internal partial class Users {
		private static readonly Operation CreateOperation = new Operation(Plugins.Authorization.Operations.Users.Create);
		public override async Task<CreateResp> Create(CreateReq request, ServerCallContext context) {
			var options = request.Options;

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, CreateOperation, context.CancellationToken).ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}
			var createSource = new TaskCompletionSource<bool>();

			var envelope = new CallbackEnvelope(OnMessage);

			_publisher.Publish(new UserManagementMessage.Create(envelope, user, options.LoginName, options.FullName,
				options.Groups.ToArray(),
				options.Password));

			await createSource.Task.ConfigureAwait(false);

			return new CreateResp();

			void OnMessage(Message message) {
				if (HandleErrors(options.LoginName, message, createSource))
					return;

				createSource.TrySetResult(true);
			}
		}
	}
}
