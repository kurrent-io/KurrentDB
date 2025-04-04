// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography.X509Certificates;

namespace EventStore.Core.Tests.Certificates;

public class with_certificate_chain_of_length_2 : with_certificates {
	protected readonly X509Certificate2 _root, _leaf;

	public with_certificate_chain_of_length_2() {
		var root = CreateCertificate(issuer: true);
		var leaf = CreateCertificate(issuer: false, parent: root);

		//get rid of private keys except for the leaf
		_root = new X509Certificate2(root.Export(X509ContentType.Cert));
		_leaf = leaf;
	}
}
