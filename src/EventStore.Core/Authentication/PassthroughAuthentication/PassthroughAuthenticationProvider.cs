using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Routing;

namespace EventStore.Core.Authentication.PassthroughAuthentication {
	public class PassthroughAuthenticationProvider : IAuthenticationProvider {
		public void Authenticate(AuthenticationRequest authenticationRequest) =>
			authenticationRequest.Authenticated(SystemAccounts.System);
		public string Name => "insecure";
		public IEnumerable<KeyValuePair<string, string>> GetPublicProperties() => null;
		public void ConfigureEndpoints(IEndpointRouteBuilder endpointRouteBuilder) {
			//nothing to do
		}

		public IReadOnlyList<string> GetSupportedAuthenticationSchemes() {
			return new[] {
				"Insecure"
			};
		}

		public Task Initialize() => Task.CompletedTask;
	}
}
