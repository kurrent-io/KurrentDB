using EventStore.Core.Messaging;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http.Messages {
	[DerivedMessage(CoreMessage.Http)]
	partial class AuthenticatedHttpRequestMessage : Message {
		public readonly HttpEntityManager Manager;
		public readonly UriToActionMatch Match;

		public AuthenticatedHttpRequestMessage(HttpEntityManager manager, UriToActionMatch match) {
			Manager = manager;
			Match = match;
		}
	}
}
