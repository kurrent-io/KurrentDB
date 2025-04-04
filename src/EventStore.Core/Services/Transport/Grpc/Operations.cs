using System;
using EventStore.Core.Bus;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Services.Transport.Grpc {
	internal partial class Operations
		: EventStore.Client.Operations.Operations.OperationsBase {
		private readonly IPublisher _publisher;
		private readonly IAuthorizationProvider _authorizationProvider;

		public Operations(IPublisher publisher, IAuthorizationProvider authorizationProvider) {
			if (publisher == null)
				throw new ArgumentNullException(nameof(publisher));
			if (authorizationProvider == null)
				throw new ArgumentNullException(nameof(authorizationProvider));
			_publisher = publisher;
			_authorizationProvider = authorizationProvider;
		}
	}
}
