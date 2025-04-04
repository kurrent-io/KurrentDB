using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Routing;
using Serilog;

namespace EventStore.Core.Tests.Common.ClusterNodeOptionsTests {
	public class TestAuthenticationProviderFactory : IAuthenticationProviderFactory {
		public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts, ILogger logger) {
			return new TestAuthenticationProvider();
		}
	}

	public class TestAuthenticationProvider : IAuthenticationProvider {
		public Task Initialize() {
			return Task.FromResult(true);
		}

		public void Authenticate(AuthenticationRequest authenticationRequest) {
			authenticationRequest.Authenticated(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, authenticationRequest.Name), })));
		}
		public string Name => "test";
		public IEnumerable<KeyValuePair<string, string>> GetPublicProperties() => null;
		public void ConfigureEndpoints(IEndpointRouteBuilder endpointRouteBuilder) {
			//nothing to do
		}
		public IReadOnlyList<string> GetSupportedAuthenticationSchemes() => new[]{
			"Basic"
		};
	}
}
