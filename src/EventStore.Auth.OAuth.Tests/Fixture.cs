// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Services;
using Polly;
using Xunit.Abstractions;

namespace EventStore.Auth.OAuth.Tests;

internal class Fixture : IDisposable {
	private const string PluginConfigurationPath = "/etc/kurrentdb/oauth.conf";

	private const string BuildConfiguration =
#if DEBUG
		"Debug";
#else
			"Release";
#endif

	private static string CertificateDirectory => Path.Join(BuildDirectory, "testcerts");

	private static string BuildDirectory => Environment.CurrentDirectory;

	private static string PluginSourceDirectory {
		get {
			// x64 has different number of levels than AnyCPU
			var target = "EventStore.Auth.OAuth";
			var i = BuildDirectory.LastIndexOf(target) + target.Length;
			return BuildDirectory[0..i];
		}
	}

	private static string PluginPublishDirectory =>
		Path.Combine(BuildDirectory, "oauth");

	private static string ESImage {
		get {
			var imageWithTag = Environment.GetEnvironmentVariable("DB_IMAGE");
			if (!string.IsNullOrWhiteSpace(imageWithTag))
				return imageWithTag;

			var version = Assembly.GetAssembly(typeof(OAuthAuthenticationPlugin)).GetName().Version;

			var tag = (version.Major, version.Minor) switch {
				(24, 4) => TagForMajorMinor(version),
				(24, 6) => TagForMajorMinor(version),
				_ => "ci"
			};

			var image = "docker.eventstore.com/eventstore-staging-ee/eventstoredb-ee";
			return image + ":" + tag;
		}
	}

	private static string TagForMajorMinor(Version version) =>
		$"{version.Major}.{version.Minor}.0-nightly-x64-8.0-bookworm-slim";

	private IContainerService _eventStore;
	private readonly ITestOutputHelper _output;
	private readonly FileInfo _pluginConfiguration;
	private readonly string[] _containerEnv;
	public IdpFixture IdentityServer { get; }

	private Fixture(ITestOutputHelper output, params string[] env) {
		var defaultEnv = new[] {
			"KURRENTDB_CERTIFICATE_FILE=/opt/kurrentdb/certs/test.crt",
			"KURRENTDB_CERTIFICATE_PRIVATE_KEY_FILE=/opt/kurrentdb/certs/test.key",
			"KURRENTDB_TRUSTED_ROOT_CERTIFICATES_PATH=/opt/kurrentdb/certs/",
			$"KURRENTDB_AUTHENTICATION_CONFIG={PluginConfigurationPath}",
			"KURRENTDB_AUTHENTICATION_TYPE=oauth",
			"KURRENTDB_LOG_FAILED_AUTHENTICATION_ATTEMPTS=true",
			"KURRENTDB_LOG_HTTP_REQUESTS=true",
			"KURRENTDB__LICENSING__LICENSE_KEY=test",
			"KURRENTDB__LICENSING__BASE_URL=https://localhost:1"
		};

		_output = output;
		_containerEnv = defaultEnv.Concat(env).ToArray();
		_pluginConfiguration = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

		IdentityServer = new IdpFixture(_output);
	}

	public static async ValueTask<Fixture> Create(ITestOutputHelper output, params string[] env) {
		var fixture = new Fixture(output, env);
		await fixture.Start();

		return fixture;
	}

	private async ValueTask Start() {
		await IdentityServer.Start();
		await PublishPlugin(PluginSourceDirectory);
		await WritePluginConfiguration();
		await GenerateSelfSignedCertificateKeyPair(CertificateDirectory);
		await Task.Delay(TimeSpan.FromSeconds(2));

		var imageName = "kurrentdb-for-oauth-plugins-unit-tests";

		using var x = new Builder()
			.DefineImage(imageName)
			// start with enterprise edition
			.From(ESImage)
			// remove all the plugins. if the EventStore.Plugins reference has changed the server in
			// the nightly cannot necessarily even load the commercial HA plugins yet
			// (these tests need to pass in order to update the commercial HA plugins)
			.Run("mv /opt/kurrentdb/plugins /opt/kurrentdb/all-plugins")
			// reinstate the tcp plugin
			.Run("mkdir -p /opt/kurrentdb/plugins")
			.Run("cp -r /opt/kurrentdb/all-plugins/EventStore.TcpPlugin /opt/kurrentdb/plugins/EventStore.TcpPlugin")
			.Build();

		_eventStore = new Builder()
			.UseContainer()
			.UseImage(imageName)
			.WithEnvironment(_containerEnv)
			.WithName("es-oauth-tests")
			.Mount(PluginPublishDirectory, "/opt/kurrentdb/plugins/oauth", MountType.ReadOnly)
			.Mount(CertificateDirectory, "/opt/kurrentdb/certs", MountType.ReadOnly)
			.Mount(_pluginConfiguration.FullName, PluginConfigurationPath, MountType.ReadOnly)
			.ExposePort(1113, 1113)
			.ExposePort(2113, 2113)
			.Build();

		_eventStore.Start();
		_eventStore.ShipContainerLogs(_output);

		try {
			await Policy.Handle<Exception>()
				.WaitAndRetryAsync(5, retryCount => TimeSpan.FromSeconds(retryCount * retryCount))
				.ExecuteAsync(async () => {
					using var client = new HttpClient(new SocketsHttpHandler {
						SslOptions = {
							RemoteCertificateValidationCallback = delegate { return true; }
						}
					});
					using var response = await client.GetAsync("https://localhost:2113/");
					if (response.StatusCode >= HttpStatusCode.BadRequest) {
						throw new Exception($"Health check failed with status code {response.StatusCode}");
					}
				});
		} catch (Exception) {
			_eventStore.Dispose();
			IdentityServer.Dispose();
			throw;
		}
	}

	private async Task WritePluginConfiguration() {
		await using (var stream = _pluginConfiguration.Create()) {
			await using var writer = new StreamWriter(stream);
			await writer.WriteAsync($@"---
OAuth:
  Issuer: {IdentityServer.Issuer}
  Audience: eventstore
  Insecure: true
  DisableIssuerValidation: true");
		}

		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			chmod(_pluginConfiguration.FullName, Convert.ToInt32("0755", 8));
		}
	}

	[DllImport("libc", SetLastError = true)]
	private static extern int chmod(string pathname, int mode);

	private async Task GenerateSelfSignedCertificateKeyPair(string outputDirectory) {
		if (Directory.Exists(outputDirectory)) {
			Directory.Delete(outputDirectory, true);
		}

		Directory.CreateDirectory(outputDirectory);

		using var rsa = RSA.Create();
		var certReq = new CertificateRequest("CN=eventstoredb-node", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		var sanBuilder = new SubjectAlternativeNameBuilder();
		sanBuilder.AddIpAddress(IPAddress.Loopback);
		certReq.CertificateExtensions.Add(sanBuilder.Build());
		var certificate = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));

		await using (var streamWriter = new StreamWriter(Path.Join(outputDirectory, "test.crt"), false))
		{
			var certBytes = certificate.Export(X509ContentType.Cert);
			var certString = Convert.ToBase64String(certBytes);

			await streamWriter.WriteLineAsync("-----BEGIN CERTIFICATE-----");
			for (var i = 0; i < certString.Length; i += 64) {
				await streamWriter.WriteLineAsync(certString.Substring(i, Math.Min(i+64, certString.Length) - i));
			}
			await streamWriter.WriteLineAsync("-----END CERTIFICATE-----");
		}

		await using (var streamWriter = new StreamWriter(Path.Join(outputDirectory, "test.key"), false)) {
			var keyBytes = rsa.ExportRSAPrivateKey();
			var keyString = Convert.ToBase64String(keyBytes);

			await streamWriter.WriteLineAsync("-----BEGIN RSA PRIVATE KEY-----");
			for (var i = 0; i < keyString.Length; i += 64) {
				await streamWriter.WriteLineAsync(keyString.Substring(i, Math.Min(i+64, keyString.Length) - i));
			}
			await streamWriter.WriteLineAsync("-----END RSA PRIVATE KEY-----");
		}
	}

	private static async Task PublishPlugin(string pluginDirectory) {
		var tcs = new TaskCompletionSource<bool>();
		using var process = new Process {
			StartInfo = new ProcessStartInfo {
				FileName = "dotnet",
				Arguments = $"publish --configuration {BuildConfiguration} --framework=net8.0 --output {PluginPublishDirectory}",
				WorkingDirectory = pluginDirectory,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			},
			EnableRaisingEvents = true
		};
		process.Exited += (_, e) => tcs.SetResult(default);
		process.Start();

		var stdout = process.StandardOutput.ReadToEndAsync();
		var stderr = process.StandardError.ReadToEndAsync();

		await Task.WhenAll(tcs.Task, stdout, stderr);

		if (process.ExitCode != 0) {
			throw new Exception(stdout.Result);
		}
	}

	public void Dispose() {
		_eventStore?.Dispose();
		IdentityServer?.Dispose();
		_pluginConfiguration?.Delete();
		if (Directory.Exists(CertificateDirectory)) {
			Directory.Delete(CertificateDirectory, true);
		}
	}
}
