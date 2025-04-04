using System.Net.Http.Headers;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Http;

namespace EventStore.Core.Services.Transport.Http.Authentication {
	public class BearerHttpAuthenticationProvider : IHttpAuthenticationProvider {
		private readonly IAuthenticationProvider _internalAuthenticationProvider;

		public BearerHttpAuthenticationProvider(IAuthenticationProvider internalAuthenticationProvider) {
			_internalAuthenticationProvider = internalAuthenticationProvider;
		}

		public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
			if (!context.Request.Headers.TryGetValue("authorization", out var values) || values.Count != 1 ||
				!AuthenticationHeaderValue.TryParse(
					values[0], out var authenticationHeader) || authenticationHeader.Scheme != "Bearer") {
				request = null;
				return false;
			}

			request = new HttpAuthenticationRequest(context, authenticationHeader.Parameter);
			_internalAuthenticationProvider.Authenticate(request);
			return true;
		}
	}
}
