// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using System.Text;
using RestSharp;
using RestSharp.Interceptors;

namespace EventStore.Licensing.Keygen;

public class KeygenSignatureInterceptor : Interceptor {
	readonly RSA _rsa;

	public KeygenSignatureInterceptor() {
		_rsa = RSA.Create();
		_rsa.ImportFromPem(SigningKey);
	}

	public override ValueTask BeforeDeserialization(RestResponse response, CancellationToken cancellationToken) {
		var verified = ValidateResponse(response);
		if (!verified) {
			throw new InvalidOperationException("Keygen signature verification failed");
		}
		return ValueTask.CompletedTask;
	}

	bool ValidateResponse(RestResponse response) {
		var bytes = SHA256.HashData(response.RawBytes ?? []);
		var digest = $"sha-256={Convert.ToBase64String(bytes)}";
		var header = response.GetHeader("Digest");
		if (header != digest)
			return false;

		var resource = response.ResponseUri!.AbsolutePath;
		var signingData = $"""
                           (request-target): {response.Request.Method.ToString().ToLowerInvariant()} {resource}
                           host: api.keygen.sh
                           date: {response.GetHeader("Date")}
                           digest: {digest}
                           """;
		var signatureHeader = response.GetHeader("Keygen-Signature");
		var signature = signatureHeader?.Split(", ")
			.Select(x => {
				var index = x.IndexOf('=');
				return (x[..index], x[(index + 1)..].Trim('"'));
			})
			.Where(x => x.Item1 == "signature")
			.Select(x => x.Item2)
			.FirstOrDefault();
		if (signature == null)
			return false;

		var data = Encoding.UTF8.GetBytes(signingData);
		var sig = Convert.FromBase64String(signature);
		var verified = _rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		return verified;
	}

	const string SigningKey = "";
}

static class ResponseExtensions {
	public static string? GetHeader(this RestResponse response, string name) {
		return response.Headers?.FirstOrDefault(x => x.Name == name)?.Value?.ToString();
	}
}
