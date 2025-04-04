using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Services.UserManagement;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace EventStore.Core.Services.Transport.Http.Authentication {
	public class ClientCertificateAuthenticationProvider : IHttpAuthenticationProvider {
		private readonly Func<string> _getCertificateReservedNodeCommonName;

		public ClientCertificateAuthenticationProvider(Func<string> getCertificateReservedNodeCommonName) {
			_getCertificateReservedNodeCommonName = getCertificateReservedNodeCommonName;
		}

		public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
			return AuthenticateCached(context, out request);
		}

		private bool AuthenticateCached(HttpContext context, out HttpAuthenticationRequest request) {
			// we cache the authentication result as the same TLS connection may be used for multiple HTTP requests.
			// performance aside, the cache also ensures that authentication requests that were successful in the past
			// will succeed in the future for the same TLS connection even if certificates are rotated.

			request = null;

			// if the connection doesn't have a client certificate, take a shortcut
			var clientCertificate = context.Connection.ClientCertificate;
			if (clientCertificate is null)
				return false;

			bool authenticated;

			var connectionItems = context.Features.Get<IConnectionItemsFeature>()?.Items;
			const string connectionItemsKey = "ClientCertificateAuthenticationStatus";
			if (TryGetDictionaryValue(connectionItems, connectionItemsKey, out var wasAuthenticated)) {
				authenticated = (bool)wasAuthenticated;
			} else {
				authenticated = AuthenticateUncached(context, clientCertificate);
				TrySetDictionaryValue(connectionItems, connectionItemsKey, authenticated);
			}

			if (!authenticated)
				return false;

			request = new HttpAuthenticationRequest(context, "system", "");
			request.Authenticated(SystemAccounts.System);

			return true;
		}

		private static bool TryGetDictionaryValue(IDictionary<object, object> dictionary, string key, out object value) {
			if (dictionary == null) {
				value = null;
				return false;
			}

			lock (dictionary) {
				return dictionary.TryGetValue(key, out value);
			}
		}

		private static bool TrySetDictionaryValue(IDictionary<object, object> dictionary, string key, object value) {
			if (dictionary == null)
				return false;

			lock (dictionary) {
				return dictionary.TryAdd(key, value);
			}
		}

		private bool AuthenticateUncached(HttpContext context, X509Certificate2 clientCertificate) {
			var reservedNodeCN = _getCertificateReservedNodeCommonName();
			bool hasReservedNodeCN;
			try {
				hasReservedNodeCN = clientCertificate.ClientCertificateMatchesName(reservedNodeCN);
			} catch (CryptographicException) {
				return false;
			} catch (NullReferenceException) {
				return false;
			}

			if (!hasReservedNodeCN) {
				var clientCertificateCN = clientCertificate.GetCommonName();
				var ip = context.Connection.RemoteIpAddress?.ToString() ?? "<unknown>";
				Log.Error(
					"Connection from node: {ip} was denied because its CN: {clientCertificateCN} does not match with the reserved node CN: {reservedNodeCN}",
					ip, clientCertificateCN, reservedNodeCN);
				return false;
			}

			bool hasIpOrDnsSan = clientCertificate.GetSubjectAlternativeNames()
				.Where(x => x.type is CertificateNameType.DnsName or CertificateNameType.IpAddress)
				.IsNotEmpty();

			if (!hasIpOrDnsSan) {
				var ip = context.Connection.RemoteIpAddress?.ToString() ?? "<unknown>";
				Log.Error("Connection from node: {ip} was denied because its certificate does not have any IP or DNS Subject Alternative Names (SAN).", ip);
				return false;
			}

			return true;
		}
	}
}
