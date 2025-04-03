// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using EventStore.Core.Services.Transport.Http.Authentication;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http.Authentication;

public class TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	protected NodeCertificateAuthenticationProvider _provider;

	protected void SetUpProvider() {
		_provider = new NodeCertificateAuthenticationProvider(() => "eventstoredb-node");
	}
}

[TestFixture]
public class
	when_handling_a_request_without_a_client_certificate :
		TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	private bool _authenticateResult;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		var context = new DefaultHttpContext();
		Assert.IsNull(context.Connection.ClientCertificate);
		_authenticateResult = _provider.Authenticate(context, out _);
	}

	[Test]
	public void returns_false() {
		Assert.IsFalse(_authenticateResult);
	}
}

[TestFixture]
public class
	when_handling_a_request_with_a_client_certificate_having_no_san :
		TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	private HttpAuthenticationRequest _authenticateRequest;
	private bool _authenticateResult;
	private HttpContext _context;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		_context = new DefaultHttpContext();
		using (var rsa = RSA.Create()) {
			var certRequest = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256,
				RSASignaturePadding.Pkcs1);
			_context.Connection.ClientCertificate = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}

		_authenticateResult = _provider.Authenticate(_context, out _authenticateRequest);
	}

	[Test]
	public void returns_false() {
		Assert.IsFalse(_authenticateResult);
	}

	[Test]
	public void authentication_request_is_null() {
		Assert.IsNull(_authenticateRequest);
	}

	[Test]
	public void no_roles_are_assigned() {
		Assert.AreEqual(0, _context.User.Claims.Count());
	}
}

[TestFixture]
public class
	when_handling_a_request_with_a_client_certificate_having_an_ip_san_but_without_node_cn :
		TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	private HttpAuthenticationRequest _authenticateRequest;
	private bool _authenticateResult;
	private HttpContext _context;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		_context = new DefaultHttpContext();
		X509Certificate2 certificate;

		using (RSA rsa = RSA.Create())
		{
			var certReq = new CertificateRequest("CN=hello", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			var sanBuilder = new SubjectAlternativeNameBuilder();
			sanBuilder.AddIpAddress(IPAddress.Loopback);
			certReq.CertificateExtensions.Add(sanBuilder.Build());
			certificate = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}

		_context.Connection.ClientCertificate = certificate;
		_authenticateResult = _provider.Authenticate(_context, out _authenticateRequest);
	}

	[Test]
	public void returns_false() {
		Assert.IsFalse(_authenticateResult);
	}

	[Test]
	public void authentication_request_is_null() {
		Assert.IsNull(_authenticateRequest);
	}

	[Test]
	public void no_roles_are_assigned() {
		Assert.AreEqual(0, _context.User.Claims.Count());
	}
}

[TestFixture]
public class
	when_handling_a_request_with_a_client_certificate_having_an_ip_san_and_node_cn :
		TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	private HttpAuthenticationRequest _authenticateRequest;
	private bool _authenticateResult;
	private HttpContext _context;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		_context = new DefaultHttpContext();
		X509Certificate2 certificate;

		using (RSA rsa = RSA.Create())
		{
			var certReq = new CertificateRequest("CN=eventstoredb-node", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

			var sanBuilder = new SubjectAlternativeNameBuilder();
			sanBuilder.AddIpAddress(IPAddress.Loopback);
			certReq.CertificateExtensions.Add(sanBuilder.Build());

			certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));

			certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
				[
					new("1.3.6.1.5.5.7.3.1"), // serverAuth
					new("1.3.6.1.5.5.7.3.2"), // clientAuth
				],
				critical: false));

			certificate = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}

		_context.Connection.ClientCertificate = certificate;
		_authenticateResult = _provider.Authenticate(_context, out _authenticateRequest);
	}

	[Test]
	public void returns_true() {
		Assert.IsTrue(_authenticateResult);
	}

	[Test]
	public async Task passes_authentication() {
		var (status, _) = await _authenticateRequest.AuthenticateAsync();
		Assert.AreEqual(HttpAuthenticationRequestStatus.Authenticated, status);
	}

	[Test]
	public async Task sets_user_to_system_user() {
		var (_, user) = await _authenticateRequest.AuthenticateAsync();
		Assert.AreEqual(SystemAccounts.System.Claims, user.Claims);
	}
}

[TestFixture]
public class
	when_handling_a_request_with_a_client_certificate_having_an_ip_san_and_node_cn_with_additional_subject_details :
		TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	private HttpAuthenticationRequest _authenticateRequest;
	private bool _authenticateResult;
	private HttpContext _context;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		_context = new DefaultHttpContext();
		X509Certificate2 certificate;

		using (RSA rsa = RSA.Create())
		{
			var certReq = new CertificateRequest("C=UK, O=Event Store Ltd, CN=eventstoredb-node", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

			var sanBuilder = new SubjectAlternativeNameBuilder();
			sanBuilder.AddIpAddress(IPAddress.Loopback);
			certReq.CertificateExtensions.Add(sanBuilder.Build());

			certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));

			certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
				[
					new("1.3.6.1.5.5.7.3.1"), // serverAuth
					new("1.3.6.1.5.5.7.3.2"), // clientAuth
				],
				critical: false));

			certificate = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}

		_context.Connection.ClientCertificate = certificate;
		_authenticateResult = _provider.Authenticate(_context, out _authenticateRequest);
	}

	[Test]
	public void returns_true() {
		Assert.IsTrue(_authenticateResult);
	}

	[Test]
	public async Task passes_authentication() {
		var (status, _) = await _authenticateRequest.AuthenticateAsync();
		Assert.AreEqual(HttpAuthenticationRequestStatus.Authenticated, status);
	}

	[Test]
	public async Task sets_user_to_system_user() {
		var (_, user) = await _authenticateRequest.AuthenticateAsync();
		Assert.AreEqual(SystemAccounts.System.Claims, user.Claims);
	}
}


[TestFixture]
public class
	when_handling_a_request_with_a_client_certificate_having_a_dns_san_but_without_node_cn :
		TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	private HttpAuthenticationRequest _authenticateRequest;
	private bool _authenticateResult;
	private HttpContext _context;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		_context = new DefaultHttpContext();
		X509Certificate2 certificate;

		using (RSA rsa = RSA.Create())
		{
			var certReq = new CertificateRequest("CN=hello", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			var sanBuilder = new SubjectAlternativeNameBuilder();
			sanBuilder.AddDnsName("localhost");
			certReq.CertificateExtensions.Add(sanBuilder.Build());
			certificate = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}

		_context.Connection.ClientCertificate = certificate;
		_authenticateResult = _provider.Authenticate(_context, out _authenticateRequest);
	}

	[Test]
	public void returns_false() {
		Assert.IsFalse(_authenticateResult);
	}

	[Test]
	public void authentication_request_is_null() {
		Assert.IsNull(_authenticateRequest);
	}

	[Test]
	public void no_roles_are_assigned() {
		Assert.AreEqual(0, _context.User.Claims.Count());
	}
}

[TestFixture]
public class
	when_handling_a_request_with_a_client_certificate_having_a_dns_san_and_node_cn :
		TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	private HttpAuthenticationRequest _authenticateRequest;
	private bool _authenticateResult;
	private HttpContext _context;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		_context = new DefaultHttpContext();
		X509Certificate2 certificate;

		using (RSA rsa = RSA.Create())
		{
			var certReq = new CertificateRequest("CN=eventstoredb-node", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

			var sanBuilder = new SubjectAlternativeNameBuilder();
			sanBuilder.AddDnsName("localhost");
			certReq.CertificateExtensions.Add(sanBuilder.Build());

			certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));

			certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
				[
					new("1.3.6.1.5.5.7.3.1"), // serverAuth
					new("1.3.6.1.5.5.7.3.2"), // clientAuth
				],
				critical: false));

			certificate = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}

		_context.Connection.ClientCertificate = certificate;
		_authenticateResult = _provider.Authenticate(_context, out _authenticateRequest);
	}

	[Test]
	public void returns_true() {
		Assert.IsTrue(_authenticateResult);
	}

	[Test]
	public async Task passes_authentication() {
		var (status, _) = await _authenticateRequest.AuthenticateAsync();
		Assert.AreEqual(HttpAuthenticationRequestStatus.Authenticated, status);
	}

	[Test]
	public async Task sets_user_to_system_user() {
		var (_, user) = await _authenticateRequest.AuthenticateAsync();
		Assert.AreEqual(SystemAccounts.System.Claims, user.Claims);
	}
}

[TestFixture]
public class
	when_handling_a_request_with_a_client_certificate_having_a_non_dns_or_ip_san_with_node_cn :
		TestFixtureWithNodeCertificateHttpAuthenticationProvider {
	private HttpAuthenticationRequest _authenticateRequest;
	private bool _authenticateResult;
	private HttpContext _context;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		_context = new DefaultHttpContext();
		X509Certificate2 certificate;

		using (RSA rsa = RSA.Create())
		{
			var certReq = new CertificateRequest("CN=eventstoredb-node", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			var sanBuilder = new SubjectAlternativeNameBuilder();
			sanBuilder.AddEmailAddress("hello@hello.org");
			sanBuilder.AddUserPrincipalName("test@test.com");
			sanBuilder.AddUri(new Uri("http://localhost"));
			certReq.CertificateExtensions.Add(sanBuilder.Build());
			certificate = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		}

		_context.Connection.ClientCertificate = certificate;
		_authenticateResult = _provider.Authenticate(_context, out _authenticateRequest);
	}

	[Test]
	public void returns_false() {
		Assert.IsFalse(_authenticateResult);
	}

	[Test]
	public void authentication_request_is_null() {
		Assert.IsNull(_authenticateRequest);
	}

	[Test]
	public void no_roles_are_assigned() {
		Assert.AreEqual(0, _context.User.Claims.Count());
	}
}
