// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace EventStore.Auth.UserCertificates;

public class UserCertificateAuthenticationProvider(
	IAuthenticationProvider authenticationProvider,
	Func<(X509Certificate2 Node, X509Certificate2Collection Intermediates, X509Certificate2Collection Roots)> getCertificates,
	IConfiguration configuration) :
	IHttpAuthenticationProvider {

	private static readonly ILogger Logger = Log.ForContext<UserCertificateAuthenticationProvider>();
	private readonly bool _logFailedAuthenticationAttempts = configuration.GetValue<bool>(
		$"{UserCertificatesPlugin.KurrentConfigurationPrefix}:LogFailedAuthenticationAttempts");

	public string Name => "user-certificate";

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
		string userId;

		var connectionItems = context.Features.Get<IConnectionItemsFeature>()?.Items;
		const string connectionItemsKey = "UserCertificateAuthenticationStatus";
		if (TryGetDictionaryValue(connectionItems, connectionItemsKey, out var cached)) {
			(authenticated, userId) = ((bool, string)) cached;
		} else {
			authenticated = AuthenticateUncached(context, clientCertificate, out userId);
			TrySetDictionaryValue(connectionItems, connectionItemsKey, (authenticated, userId));
		}

		// todo: consider not falling through if we have a cert but fail to authenticate it
		if (!authenticated)
			return false;

		if (!IsTimeValid(clientCertificate, userId))
			return false;

		request = HttpAuthenticationRequest.CreateWithValidCertificate(context, userId, clientCertificate);
		authenticationProvider.Authenticate(request);

		return true;
	}

	private bool IsTimeValid(X509Certificate2 clientCertificate, string userId) {
		var now = DateTime.Now;

		if (now < clientCertificate.NotBefore) {
			if (_logFailedAuthenticationAttempts)
				Logger.Warning("Connection from user: {user} was denied because its certificate's effective start time hasn't been reached yet.", userId);
			return false;
		}

		if (now > clientCertificate.NotAfter) {
			if (_logFailedAuthenticationAttempts)
				Logger.Warning("Connection from user: {user} was denied because its certificate has expired.", userId);
			return false;
		}

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

	private bool AuthenticateUncached(HttpContext context, X509Certificate2 clientCertificate, out string userId) {
		userId = null;

		if (!clientCertificate.IsClientCertificate(out _))
			return false;

		if (clientCertificate.IsServerCertificate(out _))
			return false;

		userId = clientCertificate.GetCommonName();

		// currently, we restrict user certificates to have the same root CA as the node certificate
		// however, this may not suit all use cases and can be expanded later based on feedback from users.
		if (!HasSameRootAsNodeCertificate(clientCertificate)) {
			if (_logFailedAuthenticationAttempts)
				Logger.Warning("Connection from user: {user} was denied because its certificate's root CA does not match with the node's certificate's root CA", userId);
			return false;
		}

		return true;
	}

	private bool HasSameRootAsNodeCertificate(X509Certificate2 user) {
		var (node, intermediates, roots) = getCertificates();
		var shareARoot = CertificateUtils.ShareARoot(
			certificate1: user,
			certificate2: node,
			intermediates: intermediates,
			roots: roots,
			out var userRoots,
			out var nodeRoots);

		Logger.Verbose("User certificate's root CA thumbprint(s): {roots}", string.Join(",", userRoots));
		Logger.Verbose("Node certificate's root CA thumbprint(s): {roots}", string.Join(",", nodeRoots));

		return shareARoot;
	}
}
