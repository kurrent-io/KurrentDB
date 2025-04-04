using System;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using static EventStore.Core.Messages.UserManagementMessage;

namespace EventStore.Core.Services.Transport.Grpc {
	internal partial class Users : EventStore.Client.Users.Users.UsersBase {
		private readonly IPublisher _publisher;
		private IAuthorizationProvider _authorizationProvider;

		public Users(IPublisher publisher, IAuthorizationProvider authorizationProvider) {
			if (publisher == null)
				throw new ArgumentNullException(nameof(publisher));
			if (authorizationProvider == null)
				throw new ArgumentNullException(nameof(authorizationProvider));
			_publisher = publisher;
			_authorizationProvider = authorizationProvider;
		}

		private static bool HandleErrors<T>(string loginName, Message message, TaskCompletionSource<T> source) {
			if (!(message is ResponseMessage response)) {
				source.TrySetException(
					RpcExceptions.UnknownMessage<ResponseMessage>(message));
				return true;
			}

			if (response.Success)
				return false;
			source.TrySetException(response.Error switch {
				Error.Unauthorized => RpcExceptions.AccessDenied(),
				Error.NotFound => RpcExceptions.LoginNotFound(loginName),
				Error.Conflict => RpcExceptions.LoginConflict(loginName),
				Error.TryAgain => RpcExceptions.LoginTryAgain(loginName),
				_ => RpcExceptions.UnknownError(response.Error)
			});
			return true;
		}
	}
}
