// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace EventStore.Core.Services.Transport.Http.Authentication;

public class NodeCertificateAuthenticationProvider(Func<string> getCertificateReservedNodeCommonName) : IHttpAuthenticationProvider {
	public string Name => "node-certificate";

	static readonly ILogger Log = Serilog.Log.ForContext<NodeCertificateAuthenticationProvider>();

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) => AuthenticateCached(context, out request);

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
		const string connectionItemsKey = "NodeCertificateAuthenticationStatus";
		if (TryGetDictionaryValue(connectionItems, connectionItemsKey, out var wasAuthenticated)) {
			authenticated = (bool) wasAuthenticated;
		} else {
			authenticated = AuthenticateUncached(context, clientCertificate);
			TrySetDictionaryValue(connectionItems, connectionItemsKey, authenticated);
		}

		if (!authenticated)
			return false;

		request = new(context, "system", "");
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
		var ip = context.Connection.RemoteIpAddress?.ToString() ?? "<unknown>";
		var isServerCertificate = clientCertificate.IsServerCertificate(out var serverCertReason);

		var reservedNodeCN = getCertificateReservedNodeCommonName();
		bool hasReservedNodeCN;
		try {
			hasReservedNodeCN = clientCertificate.ClientCertificateMatchesName(reservedNodeCN);
		} catch (CryptographicException) {
			return false;
		} catch (NullReferenceException) {
			return false;
		}

		bool hasIpOrDnsSan = clientCertificate.GetSubjectAlternativeNames()
			.Where(x => x.type is CertificateNameType.DnsName or CertificateNameType.IpAddress)
			.IsNotEmpty();

		if (!isServerCertificate && !hasReservedNodeCN && !hasIpOrDnsSan) {
			// We are sure that this is not a misconfigured node certificate with incorrect EKUs, missing SANs, etc. It could be a user certificate.
			return false;
		}
		if (!hasReservedNodeCN) {
			var clientCertificateCN = clientCertificate.GetCommonName();
			Log.Error(
				"Connection from node: {ip} was denied because its CN: {clientCertificateCN} does not match with the reserved node CN: {reservedNodeCN}",
				ip, clientCertificateCN, reservedNodeCN);
		}
		if (!hasIpOrDnsSan) {
			Log.Error("Connection from node: {ip} was denied because its certificate does not have any IP or DNS Subject Alternative Names (SAN).", ip);
		}
		if (!isServerCertificate) {
			Log.Error("Connection from node: {ip} was denied because it is not configured as a server certificate: {failReason}", ip, serverCertReason);
		}

		return hasReservedNodeCN && hasIpOrDnsSan && isServerCertificate;
	}
}
