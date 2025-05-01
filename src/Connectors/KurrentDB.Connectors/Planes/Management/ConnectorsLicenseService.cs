// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using DotNext.Collections.Generic;
using EventStore.Plugins.Licensing;
using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Connectors.Planes.Management;

public class ConnectorsLicenseService {
    readonly object Locker = new();

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