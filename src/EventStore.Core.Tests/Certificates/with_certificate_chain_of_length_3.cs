// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography.X509Certificates;

namespace EventStore.Core.Tests.Certificates;

public class with_certificate_chain_of_length_3 : with_certificates {
	protected readonly X509Certificate2 _root, _intermediate, _leaf;

	public with_certificate_chain_of_length_3(
		bool leafExpired = false,
		bool intermediateExpired = false,
		bool rootExpired = false) {
		var root = CreateCertificate(issuer: true, expired: rootExpired);
		var intermediate = CreateCertificate(issuer: true, parent: root, expired: intermediateExpired);
		var leaf = CreateCertificate(issuer: false, parent: intermediate, expired: leafExpired);

		//get rid of private keys except for the leaf
		_root = new X509Certificate2(root.Export(X509ContentType.Cert));
		_intermediate = new X509Certificate2(intermediate.Export(X509ContentType.Cert));
		_leaf = leaf;
	}
}
