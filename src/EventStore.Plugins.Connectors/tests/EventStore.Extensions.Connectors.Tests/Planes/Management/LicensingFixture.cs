using System.Security.Cryptography;
using EventStore.Connectors.Management;
using EventStore.Plugins.Licensing;
using EventStore.Toolkit.Testing.Fixtures;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Tests.Management;

[PublicAPI]
public class LicensingFixture : FastFixture {
    public ILogger<ConnectorsLicenseService> LicensingLogger => LoggerFactory.CreateLogger<ConnectorsLicenseService>();

    public IObservable<License> NewLicenseObservable(License license) => new SimpleObservable([license]);

    public IObservable<License> NewEmptyLicenseObservable() => new SimpleObservable([]);

    public SecurityKeys GenerateSecurityKeys() => new SecurityKeys(RSA.Create(2_048));

    public License NewLicense(SecurityKeys keys, string[] entitlements) {
        return License.Create(Convert.ToBase64String(keys.Rsa.ExportRSAPublicKey()),
            Convert.ToBase64String(keys.Rsa.ExportRSAPrivateKey()),
            entitlements.ToDictionary(x => x, _ => (object)true));
    }
}

public record SecurityKeys(RSA Rsa) : IDisposable {
    public void Dispose() => Rsa.Dispose();

    public string PublicKey => Convert.ToBase64String(Rsa.ExportRSAPublicKey());
}

class SimpleObservable(IEnumerable<License> licenses) : IObservable<License> {
    IEnumerable<License> Licenses { get; } = licenses;

    public SimpleObservable() : this([]) {}

    public IDisposable Subscribe(IObserver<License> observer) {
        foreach (var license in Licenses)
            observer.OnNext(license);

        return null!;
    }
}