using System.Collections.Concurrent;
using DotNext.Collections.Generic;
using EventStore.Connectors.Connect.Components.Connectors;
using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management;

public class ConnectorsLicenseService {
    static readonly object Locker = new();

    public const string AllWildcardEntitlement = "ALL";

    public ConnectorsLicenseService(IObservable<License> licenses, string publicKey, ILogger<ConnectorsLicenseService> logger) {
        PublicKey         = publicKey;
        Logger            = logger;
        AllowedConnectors = [];

        ResetRequirements();

        licenses.Subscribe(OnLicense, OnLicenseError);
    }

    public ConnectorsLicenseService(ILicenseService licenseService,  ILogger<ConnectorsLicenseService> logger)
        : this(licenseService.Licenses, LicenseConstants.LicensePublicKey, logger) { }

    public ConnectorsLicenseService(IObservable<License> licenses, ILogger<ConnectorsLicenseService> logger)
        : this(licenses, LicenseConstants.LicensePublicKey, logger) { }

    ConcurrentDictionary<Type, bool> AllowedConnectors { get; }
    string                           PublicKey         { get; }
    ILogger                          Logger            { get; }

    public bool CheckLicense(Type connectorType) =>
        AllowedConnectors.TryGetValue(connectorType, out var allowed) && allowed;

    public bool CheckLicense<T>() => CheckLicense(typeof(T));

    public bool CheckLicense(string alias, out ConnectorCatalogueItem item) =>
        ConnectorCatalogue.TryGetConnector(alias, out item) && CheckLicense(item.ConnectorType);

    void ResetRequirements() {
        lock (Locker) {
            ConnectorCatalogue.GetConnectors()
                .ForEach(x => AllowedConnectors[x.ConnectorType] = !x.RequiresLicense);
        }
    }

    async void OnLicense(License license) {
        var isValid = await license.TryValidateAsync(PublicKey);
        if (isValid) {
            if (license.HasEntitlements([AllWildcardEntitlement], out _)) {
                lock (Locker) {
                    AllowedConnectors.ForEach(x => AllowedConnectors[x.Key] = true);
                }
            }
            else {
                lock (Locker) {
                    ConnectorCatalogue.GetConnectors()
                        .Where(x => x.RequiresLicense)
                        .ForEach(connector => AllowedConnectors[connector.ConnectorType] = license.HasEntitlements(connector.RequiredEntitlements, out _));
                }
            }
        }
        else {
            ResetRequirements();
        }

        Logger.LogInformation(
            "Allowed Connectors: {AllowedConnectors}",
            AllowedConnectors.Where(x => x.Value).Select(x => x.Key.Name).ToList()
        );
    }

    async void OnLicenseError(Exception ex) {
        ResetRequirements();
        Logger.LogInformation(
            "Allowed Connectors: {AllowedConnectors}",
            AllowedConnectors.Where(x => x.Value).Select(x => x.Key.Name).ToList()
        );
    }
}