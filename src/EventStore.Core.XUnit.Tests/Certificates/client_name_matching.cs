// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Certificates;

public class client_name_matching {
	private static X509Certificate2 GenSut(string subjectName) {
		using (RSA rsa = RSA.Create()) {
			var certReq = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			return certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}
	}

	[Theory]
	[InlineData("CN=10.123.45.6", "10.123.45.6", "client_cn_matches_ip_address")]
	[InlineData("CN=abc.test.com", "abc.test.com", "client_cn_matches_dns_name")]
	[InlineData("CN=abc.test.com", "abC.tEst.cOm", "client_cn_matches_dns_name_case_insensitive")]
	[InlineData("CN=eventstoredb-node", "eventstoredb-node", "client_cn_matches_non_fqdn_dns_name")]
	[InlineData("CN=eventstoredb-node", "EventstoreDB-Node", "client_cn_matches_non_fqdn_dns_name_case_insensitive")]
	[InlineData("CN=abc.test.com", "*.test.com", "client_cn_matches_wildcard_dns_name")]
	[InlineData("CN=abc.test.com", "*.tEst.cOm", "client_cn_matches_wildcard_dns_name_case_insensitive")]
	[InlineData("CN=*.test.com", "*.test.com", "client_wildcard_cn_matches_wildcard_dns_name")]
	[InlineData("CN=*.test.com", "*.teSt.cOm", "client_wildcard_cn_matches_wildcard_dns_name_case_insensitive")]
	public void does_match(string clientCN, string pattern, string testCase) {
		var sut = GenSut(clientCN);
		Assert.True(sut.ClientCertificateMatchesName(pattern), testCase);
	}


	[Theory]
	[InlineData("CN=abc.test.com", "*.com", "client_cn_does_not_match_wildcard_on_main_domain_name")]
	[InlineData("CN=abc.test.com", "cde.test.com", "client_cn_does_not_match_different_host_name")]
	[InlineData("CN=abc.test.com", "abc.test1.com", "client_cn_does_not_match_different_domain_name")]
	[InlineData("CN=abc.def.test.com", "abc.ghi.test.com", "client_cn_does_not_match_different_subdomain_name")]
	[InlineData("CN=*.test.com", "*.test1.com", "client_wildcard_cn_does_not_match_different_wildcard_dns_name")]
	[InlineData("CN=*.test.com", "abc.test.com", "client_wildcard_cn_does_not_match_non_wildcard_dns_name")]
	[InlineData("CN=*.test.com", "abc.d.test.com", "client_wildcard_cn_does_not_match_non_wildcard_subdomain_name")]
	[InlineData("CN=*.*.test.com", "*.*.test.com", "client_invalid_wildcard_cn_does_not_match_invalid_wildcard_dns_name")]
	[InlineData("CN=*", "*", "client_single_star_cn_does_not_match_single_star_dns_name")]
	public void does_not_match(string clientCN, string pattern, string testCase) {
		var sut = GenSut(clientCN);
		Assert.False(sut.ClientCertificateMatchesName(pattern), testCase);
	}
}
