using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Authentication.DelegatedAuthentication;
using EventStore.Core.Authentication.PassthroughAuthentication;
using EventStore.Core.Services.Transport.Http.Authentication;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;

namespace EventStore.Core.Tests.Authentication {
	[TestFixture]
	public class PassthroughHttpAuthenticationProviderTests {
		[Test]
		public void WrongProviderThrows() =>
			Assert.Throws<ArgumentException>(() =>
				new PassthroughHttpAuthenticationProvider(new TestAuthenticationProvider()));

		[TestCaseSource(nameof(TestCases))]
		public void CorrectProviderDoesNotThrow(IAuthenticationProvider provider) =>
			Assert.DoesNotThrow(() => new PassthroughHttpAuthenticationProvider(provider));

		public static IEnumerable<object[]> TestCases() {
			yield return new object[] { new DelegatedAuthenticationProvider(new PassthroughAuthenticationProvider()) };
			yield return new object[] { new PassthroughAuthenticationProvider() };
		}

		private class TestAuthenticationProvider : IAuthenticationProvider {
			public Task Initialize() {
				throw new System.NotImplementedException();
			}

			public void Authenticate(AuthenticationRequest authenticationRequest) {
				throw new System.NotImplementedException();
			}

			public IEnumerable<KeyValuePair<string, string>> GetPublicProperties() {
				throw new System.NotImplementedException();
			}

			public void ConfigureEndpoints(IEndpointRouteBuilder endpointRouteBuilder) {
				throw new System.NotImplementedException();
			}

			public IReadOnlyList<string> GetSupportedAuthenticationSchemes() {
				throw new System.NotImplementedException();
			}

			public string Name { get; }
		}
	}
}
