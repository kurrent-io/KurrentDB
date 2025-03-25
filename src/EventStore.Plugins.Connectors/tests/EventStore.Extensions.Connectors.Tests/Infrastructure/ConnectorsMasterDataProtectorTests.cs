// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Connect.Connectors;
using EventStore.Connectors.Connect.Components.Connectors;
using Kurrent.Surge.DataProtection;
using Microsoft.Extensions.Configuration;

namespace EventStore.Extensions.Connectors.Tests;

public class ConnectorsMasterDataProtectorTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Fact]
    public void protect_fails_without_token_on_sensitive_data() {
        // Arrange
        IConnectorDataProtector sut = new ConnectorsMasterDataProtector(
            Fixture.DataProtector,
            new DataProtectionOptions { Token = DataProtectionConstants.NoOpToken }
        );

        var settings = new Dictionary<string, string?> {
            ["instanceTypeName"]        = "kafka-sink",
            ["authentication:password"] = "plaintext"
        };

        // Act
        var protect = () => sut.Protect("connectorId", settings);

        // Assert
        protect.Should()
            .Throw<DataProtectionException>()
            .WithMessage("Data protection token not found!*");
    }

    [Fact]
    public void unprotect_unprotects() {
        // Arrange
        IConnectorDataProtector sut = new ConnectorsMasterDataProtector(
            Fixture.DataProtector,
            new DataProtectionOptions { Token = "SOME-VALID-TOKEN" }
        );

        var settings = new Dictionary<string, string?> {
            ["instanceTypeName"]        = "kafka-sink",
            ["authenTication:Password"] = "plaintext"
        };

        var expectedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var protectedSettings = sut.Protect("connectorId", settings);

        var targetConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(protectedSettings)
            .Build();

        // Act
        sut.Unprotect(targetConfiguration);

        // Assert
        targetConfiguration.AsEnumerable().Should().BeEquivalentTo(expectedConfiguration.AsEnumerable());
    }

    [Fact]
    public void unprotect_returns_original_data_without_token() {
        // Arrange
        IConnectorDataProtector sut = new ConnectorsMasterDataProtector(
            Fixture.DataProtector,
            new DataProtectionOptions { Token = DataProtectionConstants.NoOpToken }
        );

        var settings = new Dictionary<string, string?> {
            ["instanceTypeName"]        = "kafka-sink",
            ["authentication:password"] = "plaintext"
        };

        var expectedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var targetConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(expectedConfiguration.AsEnumerable())
            .Build();

        // Act
        sut.Unprotect(targetConfiguration);

        // Assert
        targetConfiguration.Should().BeEquivalentTo(expectedConfiguration);
    }

    [Fact]
    public void unprotect_returns_original_data_when_data_was_not_protected_and_is_not_base64_encoded() {
        // Arrange
        IConnectorDataProtector sut = new ConnectorsMasterDataProtector(
            Fixture.DataProtector,
            new DataProtectionOptions { Token = "SOME-VALID-TOKEN" }
        );

        var settings = new Dictionary<string, string?> {
            ["instanceTypeName"]        = "kafka-sink",
            ["authentication:password"] = "plaintext"
        };

        var expectedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var targetConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(expectedConfiguration.AsEnumerable())
            .Build();

        // Act
        sut.Unprotect(targetConfiguration);

        // Assert
        targetConfiguration.Should().BeEquivalentTo(expectedConfiguration);
    }
}