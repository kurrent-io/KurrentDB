// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Core.Tests.Certificates;

public class subject_alternative_names {
	private X509Certificate2 GenSut((string name, string type)[] sans)  {
		using (RSA rsa = RSA.Create())
		{
			var certReq = new CertificateRequest("CN=hello", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			var sanBuilder = new SubjectAlternativeNameBuilder();
			foreach (var (name, type) in sans) {
				switch (type) {
					case CertificateNameType.IpAddress:
						sanBuilder.AddIpAddress(IPAddress.Parse(name));
						break;
					case CertificateNameType.DnsName:
						sanBuilder.AddDnsName(name);
						break;
					case "email":
						sanBuilder.AddEmailAddress(name);
						break;
					case "uri":
						sanBuilder.AddUri(new Uri(name));
						break;
					case "upn":
						sanBuilder.AddUserPrincipalName(name);
						break;
				}
			}

			certReq.CertificateExtensions.Add(sanBuilder.Build());
			return certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}
	}

	[Test]
	public void can_read_one_ip_address_from_san() {
		var sut = GenSut(new [] { ("127.0.0.1", CertificateNameType.IpAddress) });
		var sans = sut.GetSubjectAlternativeNames().ToArray();
		Assert.AreEqual(1, sans.Length);
		Assert.AreEqual("127.0.0.1", sans[0].name);
		Assert.AreEqual(CertificateNameType.IpAddress, sans[0].type);
	}

	[Test]
	public void can_read_one_dns_name_from_san() {
		var sut = GenSut(new [] { ("hello.world", CertificateNameType.DnsName) });
		var sans = sut.GetSubjectAlternativeNames().ToArray();
		Assert.AreEqual(1, sans.Length);
		Assert.AreEqual("hello.world", sans[0].name);
		Assert.AreEqual(CertificateNameType.DnsName, sans[0].type);
	}

	[Test]
	public void can_read_multiple_ips_and_dns_names_from_san() {
		var sut = GenSut(new [] {
			("127.0.0.1", CertificateNameType.IpAddress),
			("hello.world", CertificateNameType.DnsName),
			("10.0.0.2", CertificateNameType.IpAddress),
			("good.bye.world", CertificateNameType.DnsName)
		});
		var sans = sut.GetSubjectAlternativeNames().ToArray();
		Assert.AreEqual(4, sans.Length);
		Assert.AreEqual(new [] {"10.0.0.2", "127.0.0.1"},
			sans.Where(san => san.type == CertificateNameType.IpAddress)
				.Select(x => x.name)
				.OrderBy(x => x).ToArray());
		Assert.AreEqual(new [] {"good.bye.world", "hello.world"},
			sans.Where(san => san.type == CertificateNameType.DnsName)
				.Select(x => x.name)
				.OrderBy(x => x).ToArray());
	}

	[Test]
	public void can_read_multiple_ips_and_dns_names_from_san_with_other_name_types() {
		var sut = GenSut(new [] {
			("https://uritest/path", "uri"),
			("127.0.0.1", CertificateNameType.IpAddress),
			("test@test.com", "email"),
			("hello.world", CertificateNameType.DnsName),
			("https://hello.com/test", "uri"),
			("10.0.0.2", CertificateNameType.IpAddress),
			("user@domain.com", "upn"),
			("good.bye.world", CertificateNameType.DnsName),
			("test2@test2.com", "email"),
		});
		var sans = sut.GetSubjectAlternativeNames().ToArray();
		Assert.AreEqual(4, sans.Length);
		Assert.AreEqual(new [] {"10.0.0.2", "127.0.0.1"},
			sans.Where(san => san.type == CertificateNameType.IpAddress)
				.Select(x => x.name)
				.OrderBy(x => x).ToArray());
		Assert.AreEqual(new [] {"good.bye.world", "hello.world"},
			sans.Where(san => san.type == CertificateNameType.DnsName)
				.Select(x => x.name)
				.OrderBy(x => x).ToArray());
	}

}
