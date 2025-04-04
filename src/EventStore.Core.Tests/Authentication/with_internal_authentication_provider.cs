using System;
using System.Security.Claims;
using EventStore.Core.Authentication.InternalAuthentication;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Helpers;
using EventStore.Plugins.Authentication;

namespace EventStore.Core.Tests.Authentication {
	public abstract class with_internal_authentication_provider<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		protected new IODispatcher _ioDispatcher;
		protected InternalAuthenticationProvider _internalAuthenticationProvider;

		protected void SetUpProvider() {
			_ioDispatcher = new IODispatcher(_bus, new PublishEnvelope(_bus));
			_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_ioDispatcher.BackwardReader);
			_bus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher.BackwardReader);
			_bus.Subscribe(_ioDispatcher.ForwardReader);
			_bus.Subscribe(_ioDispatcher.Writer);
			_bus.Subscribe(_ioDispatcher.StreamDeleter);
			_bus.Subscribe(_ioDispatcher.Awaker);
			_bus.Subscribe<IODispatcherDelayedMessage>(_ioDispatcher);
			_bus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher);

			PasswordHashAlgorithm passwordHashAlgorithm = new StubPasswordHashAlgorithm();
			_internalAuthenticationProvider =
				new InternalAuthenticationProvider(_bus, _ioDispatcher, passwordHashAlgorithm, 1000, false, DefaultData.DefaultUserOptions);
			_bus.Subscribe(_internalAuthenticationProvider);
		}
	}

	class TestAuthenticationRequest : AuthenticationRequest {
		private readonly Action _unauthorized;
		private readonly Action<ClaimsPrincipal> _authenticated;
		private readonly Action _error;
		private readonly Action _notReady;

		public TestAuthenticationRequest(string name, string suppliedPassword, Action unauthorized,
			Action<ClaimsPrincipal> authenticated, Action error, Action notReady)
			: base("test", name, suppliedPassword) {
			_unauthorized = unauthorized;
			_authenticated = authenticated;
			_error = error;
			_notReady = notReady;
		}

		public override void Unauthorized() {
			_unauthorized();
		}

		public override void Authenticated(ClaimsPrincipal principal) {
			_authenticated(principal);
		}

		public override void Error() {
			_error();
		}

		public override void NotReady() {
			_notReady();
		}
	}
}
