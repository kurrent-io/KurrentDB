// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Core.Tests.Certificates;

public class name_matching {
	private X509Certificate2 GenSut(string subjectName, (string name, string type)[] sans)  {
		using (RSA rsa = RSA.Create())
		{
			var certReq = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			var sanBuilder = new SubjectAlternativeNameBuilder();
			foreach (var (name, type) in sans) {
				switch (type) {
					case CertificateNameType.IpAddress:
						sanBuilder.AddIpAddress(IPAddress.Parse(name));
						break;
					case CertificateNameType.DnsName:
						sanBuilder.AddDnsName(name);
						break;
				}
			}

			certReq.CertificateExtensions.Add(sanBuilder.Build());
			return certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}
	}

	[Test]
	public void ip_address_matches_ip_san() {
		var sut = GenSut("CN=test", new [] {
				("10.123.45.6", CertificateNameType.IpAddress)
		});

		Assert.True(sut.MatchesName("10.123.45.6"));
		Assert.False(sut.MatchesName("10.13.45.6"));
	}

	[Test]
	public void dns_name_matches_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("subdomain.domain.com", CertificateNameType.DnsName)
		});

		Assert.True(sut.MatchesName("subdomain.domain.com"));

		Assert.False(sut.MatchesName("com"));
		Assert.False(sut.MatchesName("domain.com"));
		Assert.False(sut.MatchesName("subdomain"));
		Assert.False(sut.MatchesName("subdomain.domain"));
		Assert.False(sut.MatchesName("subdomain.domain.coms"));
		Assert.False(sut.MatchesName("1subdomain.domain.com"));
		Assert.False(sut.MatchesName("sub.subdomain.domain.com"));
	}

	[Test]
	public void dns_name_matches_dns_san_case_insensitive() {
		var sut = GenSut("CN=test", new [] {
			("website.tld", CertificateNameType.DnsName)
		});

		Assert.True(sut.MatchesName("WeBsIte.tLd"));
	}

	[Test]
	public void cn_is_ignored_when_dns_san_is_present() {
		var sut = GenSut("CN=test", new [] {
			("website.tld", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("test"));
	}

	[Test]
	public void cn_is_ignored_when_ip_san_is_present() {
		var sut = GenSut("CN=test", new [] {
			("127.0.0.1", CertificateNameType.IpAddress)
		});

		Assert.False(sut.MatchesName("test"));
	}

	[Test]
	public void cn_is_considered_when_no_san_is_present() {
		var sut = GenSut("CN=test", Array.Empty<(string name, string type)>());
		Assert.True(sut.MatchesName("test"));
	}

	[Test]
	public void dns_name_matches_cn_case_insensitive() {
		var sut = GenSut("CN=subdomain.domain.com", Array.Empty<(string name, string type)>());
		Assert.True(sut.MatchesName("SubDomain.DoMaiN.COm"));
	}

	[Test]
	public void cn_matches_when_subject_name_contains_other_fields() {
		var sut = GenSut("CN=a.b.c,OU=Engineering,O=Company, C=Country", Array.Empty<(string name, string type)>());
		Assert.True(sut.MatchesName("a.b.c"));
		Assert.False(sut.MatchesName("a.b.c,OU"));
	}

	[Test]
	public void dns_name_matches_wildcard_cn() {
		var sut = GenSut("CN=*.example.com", Array.Empty<(string name, string type)>());

		Assert.True(sut.MatchesName("foo.example.com"));

		Assert.False(sut.MatchesName(".example.com"));
		Assert.False(sut.MatchesName("bar.foo.example.com"));
		Assert.False(sut.MatchesName("foo"));
		Assert.False(sut.MatchesName("foo.example"));
	}

	[Test]
	public void dns_name_does_not_match_wildcard_cn_with_less_than_three_domain_labels() {
		var sut = GenSut("CN=*", Array.Empty<(string name, string type)>());
		Assert.False(sut.MatchesName("tld"));

		sut = GenSut("CN=*.com", Array.Empty<(string name, string type)>());
		Assert.False(sut.MatchesName("example.com"));
	}

	[Test]
	public void dns_name_matches_wildcard_cn_with_four_domain_labels() {
		var sut = GenSut("CN=*.test.example.com", Array.Empty<(string name, string type)>());
		Assert.True(sut.MatchesName("abc.test.example.com"));
	}

	[Test]
	public void dns_name_matches_wildcard_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("*.example.com", CertificateNameType.DnsName)
		});

		Assert.True(sut.MatchesName("foo.example.com"));

		Assert.False(sut.MatchesName(".example.com"));
		Assert.False(sut.MatchesName("bar.foo.example.com"));
		Assert.False(sut.MatchesName("foo"));
		Assert.False(sut.MatchesName("foo.example"));
	}

	[Test]
	public void dns_name_matches_wildcard_dns_san_case_insensitive() {
		var sut = GenSut("CN=test", new [] {
			("*.example.com", CertificateNameType.DnsName)
		});

		Assert.True(sut.MatchesName("ABcdEF.ExAmPle.CoM"));
	}

	[Test]
	public void dns_name_does_not_match_partial_wildcard_prefix_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("*s.abcd", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("s.abcd"));
		Assert.False(sut.MatchesName("tests.abcd"));
	}

	[Test]
	public void dns_name_does_not_match_partial_wildcard_suffix_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("t*.abc", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("t.abc"));
		Assert.False(sut.MatchesName("test.abc"));
	}

	[Test]
	public void dns_name_does_not_match_partial_wildcard_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("abc*def.tld", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("abcdef.tld"));
		Assert.False(sut.MatchesName("abcHELLOdef.tld"));
	}

	[Test]
	public void dns_name_does_not_match_wildcard_in_non_first_dns_san_label() {
		var sut = GenSut("CN=test", new [] {
			("hello.*.com", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("hello.test.com"));
		Assert.False(sut.MatchesName("hello.*.com"));
	}

	[Test]
	public void dns_name_does_not_match_multiple_wildcards_in_first_dns_san_label() {
		var sut = GenSut("CN=test", new [] {
			("a*bc*d.test.com", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("a*bc*d.test.com"));
		Assert.False(sut.MatchesName("abcd.test.com"));
		Assert.False(sut.MatchesName("a1bc2d.test.com"));
	}

	[Test]
	public void invalid_dns_name_does_not_match_wildcard_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("*.example.com", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("*.example.com"));
		Assert.False(sut.MatchesName("te+st.example.com"));
		Assert.False(sut.MatchesName("te$st.example.com"));
		Assert.False(sut.MatchesName("te=st.example.com"));
	}

	[Test]
	public void ip_address_does_not_match_wildcard_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("*", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("127.0.0.1"));
	}

	[Test]
	public void ip_address_does_not_match_wildcard_cn() {
		var sut = GenSut("CN=*", Array.Empty<(string name, string type)>());
		Assert.False(sut.MatchesName("127.0.0.1"));
	}

	[Test]
	public void dns_name_matches_internationalized_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("سلام", CertificateNameType.DnsName),
			("和平.tld", CertificateNameType.DnsName),
			("world.สันติภาพ.com", CertificateNameType.DnsName)
		});

		Assert.True(sut.MatchesName("سلام"));
		Assert.True(sut.MatchesName("xn--mgbx5cf"));

		Assert.True(sut.MatchesName("和平.tld"));
		Assert.True(sut.MatchesName("xn--0tr63u.tld"));

		Assert.True(sut.MatchesName("world.สันติภาพ.com"));
		Assert.True(sut.MatchesName("world.xn--m3chqh1c2bko.com"));
	}

	[Test]
	public void dns_name_matches_internationalized_dns_san_with_ace_prefix() {
		var sut = GenSut("CN=test", new [] {
			("xn--mgbx5cf", CertificateNameType.DnsName),
			("xn--0tr63u.tld", CertificateNameType.DnsName),
			("world.xn--m3chqh1c2bko.com", CertificateNameType.DnsName)
		});

		Assert.True(sut.MatchesName("سلام"));
		Assert.True(sut.MatchesName("xn--mgbx5cf"));

		Assert.True(sut.MatchesName("和平.tld"));
		Assert.True(sut.MatchesName("xn--0tr63u.tld"));

		Assert.True(sut.MatchesName("world.สันติภาพ.com"));
		Assert.True(sut.MatchesName("world.xn--m3chqh1c2bko.com"));
	}

	[Test]
	public void dns_name_matches_internationalized_dns_san_case_insensitive() {
		var sut = GenSut("CN=test", new [] {
			("سلام", CertificateNameType.DnsName)
		});

		Assert.True(sut.MatchesName("Xn--MgBx5Cf"));
	}

	[Test]
	public void dns_name_matches_internationalized_cn() {
		var sut = GenSut("CN=xn--mgbx5cf", Array.Empty<(string name, string type)>());
		Assert.True(sut.MatchesName("سلام"));
		Assert.True(sut.MatchesName("xn--mgbx5cf"));
	}

	[Test]
	public void dns_name_matches_internationalized_cn_case_insensitive() {
		var sut = GenSut("CN=xn--mgbx5cf", Array.Empty<(string name, string type)>());
		Assert.True(sut.MatchesName("Xn--MgBx5Cf"));
	}

	[Test]
	public void dns_name_matches_wildcard_internationalized_dns_san() {
		var sut = GenSut("CN=test", new [] {
			("*.สันติภาพ.com", CertificateNameType.DnsName)
		});

		Assert.True(sut.MatchesName("world.สันติภาพ.com"));
		Assert.True(sut.MatchesName("world.xn--m3chqh1c2bko.com"));

		Assert.True(sut.MatchesName("สันติภาพ.สันติภาพ.com"));
		Assert.True(sut.MatchesName("xn--m3chqh1c2bko.xn--m3chqh1c2bko.com"));
	}

	[Test]
	public void dns_name_does_not_match_internationalized_dns_san_with_illegal_wildcard_characters() {
		var sut = GenSut("CN=test", new [] {
			("*.สันติภาพ*.com", CertificateNameType.DnsName),
			("*.xn--*-yxflvj1d7blq.com", CertificateNameType.DnsName)
		});

		Assert.False(sut.MatchesName("world.สันติภาพ*.com"));
		Assert.False(sut.MatchesName("world.xn--*-yxflvj1d7blq.com"));
	}
}
