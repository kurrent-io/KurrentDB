// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace EventStore.Auth.UserCertificates;

public static class CertificateUtils {
	// returns true iff there exists a root in `roots` that begins a valid chain of
	// trust to both `certificate1` and `certificate2`.
	public static bool ShareARoot(
		X509Certificate2 certificate1,
		X509Certificate2 certificate2,
		X509Certificate2Collection intermediates,
		X509Certificate2Collection roots,
		out string[] certificate1Roots,
		out string[] certificate2Roots) {

		certificate1Roots = FindRoots(certificate1, intermediates, roots).Select(x => x.Thumbprint).ToArray();
		certificate2Roots = FindRoots(certificate2, intermediates, roots).Select(x => x.Thumbprint).ToArray();

		return certificate1Roots.Intersect(certificate2Roots).Any();
	}

	// finds every root in `roots` that begins a valid chain of trust to `leaf`
	public static X509Certificate2[] FindRoots(
		X509Certificate2 leaf,
		X509Certificate2Collection intermediates,
		X509Certificate2Collection roots) {

		var candidateRoots = new HashSet<X509Certificate2>();
		FindCandidateRoots(leaf, intermediates, roots, visited: [], candidateRoots);

		return candidateRoots
			.Where(root => IsValidRoot(leaf, intermediates, root))
			.ToArray();
	}

	// returns true iff there is a valid chain from the root to the leaf
	private static bool IsValidRoot(X509Certificate2 leaf, X509Certificate2Collection intermediates, X509Certificate2 root) {
		var chain = new X509Chain {
			ChainPolicy = {
				TrustMode = X509ChainTrustMode.CustomRootTrust,
				RevocationMode = X509RevocationMode.NoCheck,
				DisableCertificateDownloads = true
			}
		};

		if (intermediates != null)
			foreach (var intermediate in intermediates)
				chain.ChainPolicy.ExtraStore.Add(intermediate);

		chain.ChainPolicy.CustomTrustStore.Add(root);

		return chain.Build(leaf);
	}

	// DFS. finds every root in `roots` with a issuer/subject chain down to `certificate`
	private static void FindCandidateRoots(
		X509Certificate2 certificate,
		X509Certificate2Collection intermediates,
		X509Certificate2Collection roots,
		HashSet<string> visited,
		HashSet<X509Certificate2> results) {

		if (!visited.Add(certificate.Thumbprint))
			return;

		if (certificate.Issuer == certificate.Subject) {
			// we've reached a root certificate - check if it's a trusted root
			if (roots.Contains(certificate))
				results.Add(certificate);
		}

		if (intermediates != null)
			foreach (var intermediate in intermediates)
				if (certificate.Issuer == intermediate.Subject)
					FindCandidateRoots(intermediate, intermediates, roots, visited, results);

		if (roots != null)
			foreach (var root in roots)
				if (certificate.Issuer == root.Subject)
					FindCandidateRoots(root, intermediates, roots, visited, results);
	}
}
