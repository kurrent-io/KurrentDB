using EventStore.Connectors.Connect.Components.Connectors;
using EventStore.Connectors.Elasticsearch;
using EventStore.Connectors.EventStoreDB;
using EventStore.Connectors.Http;
using EventStore.Connectors.Kafka;
using EventStore.Connectors.Management;
using EventStore.Connectors.MongoDB;
using EventStore.Connectors.RabbitMQ;
using EventStore.Connectors.Serilog;
using EventStore.Toolkit.Testing.Fixtures;
using EventStore.Toolkit.Testing.Xunit;

namespace EventStore.Connectors.Tests.Management;

[Trait("Category", "Licensing")]
public class ConnectorsLicenseServiceTests(ITestOutputHelper output, LicensingFixture fixture) : FastTests<LicensingFixture>(output, fixture) {
    [Theory]
    [ClassData(typeof(AllConnectorsTestCases))]
    public void licenses_are_valid_when_specific_entitlements_exist(ConnectorCatalogueItem info) =>
        CheckConnectorLicense(info.ConnectorType, info.RequiredEntitlements, expectedResult: true);

    [Theory]
    [ClassData(typeof(AllConnectorsTestCases))]
    public void licenses_are_valid_when_all_wildcard_entitlement_exists(ConnectorCatalogueItem info) =>
        CheckConnectorLicense(info.ConnectorType, ["ALL"], expectedResult: true);

    [Theory]
    [ClassData(typeof(FreeConnectorsTestCases))]
    public void license_is_valid_for_free_connectors_when_no_entitlements_exists(ConnectorCatalogueItem info) =>
        CheckConnectorLicense(info.ConnectorType, [], expectedResult: true);

    [Theory]
    [ClassData(typeof(CommercialConnectorsTestCases))]
    public void license_is_invalid_for_commercial_connectors_when_no_entitlements_exists(ConnectorCatalogueItem info) =>
        CheckConnectorLicense(info.ConnectorType, [], expectedResult: false);

    [Theory]
    [ClassData(typeof(CommercialConnectorsTestCases))]
    public void license_is_valid_for_commercial_connectors_when_all_wildcard_entitlement_exists(ConnectorCatalogueItem info) =>
        CheckConnectorLicense(info.ConnectorType, ["ALL"], expectedResult: true);

    void CheckConnectorLicense(Type connectorType, string[] entitlements, bool expectedResult) {
        var license = Fixture.NewLicense(entitlements);
        var sut     = new ConnectorsLicenseService(Fixture.NewLicenseObservable(license), Fixture.LicensingLogger);

        sut.CheckLicense(connectorType).Should().Be(expectedResult);
    }

    [Fact]
    public void license_without_entitlements_should_allow_free_connectors() {
        var license = Fixture.NewLicense([]);
        var sut     = new ConnectorsLicenseService(Fixture.NewLicenseObservable(license), Fixture.LicensingLogger);

        sut.CheckLicense<HttpSink>().Should().BeTrue();
        sut.CheckLicense<SerilogSink>().Should().BeTrue();
        sut.CheckLicense<KafkaSink>().Should().BeFalse();
        sut.CheckLicense<RabbitMqSink>().Should().BeFalse();
        sut.CheckLicense<MongoDbSink>().Should().BeFalse();
        sut.CheckLicense<EventStoreDbSink>().Should().BeFalse();
        sut.CheckLicense<ElasticsearchSink>().Should().BeFalse();
    }

    [Theory]
    [ClassData(typeof(ConnectorDefaultTestData))]
    public void empty_license_should_work_with_default((Type Connector, bool Allowed) data) {
        new ConnectorsLicenseService(Fixture.NewEmptyLicenseObservable(), Fixture.LicensingLogger)
            .CheckLicense(data.Connector).Should().Be(data.Allowed);
    }

    class ConnectorDefaultTestData : TestCaseGenerator<(Type Connector, bool Allowed)> {
        protected override IEnumerable<object[]> Data() =>
            ConnectorCatalogue.GetConnectors().Select(connector => (object[])[(connector.ConnectorType, !connector.RequiresLicense)]);
    }

    class FreeConnectorsTestCases : TestCaseGenerator<ConnectorCatalogueItem> {
        protected override IEnumerable<object[]> Data() =>
            ConnectorCatalogue.GetConnectors()
                .Where(x => !x.RequiresLicense)
                .Select(x => (object[]) [x]);
    }

    class CommercialConnectorsTestCases : TestCaseGenerator<ConnectorCatalogueItem> {
        protected override IEnumerable<object[]> Data() =>
            ConnectorCatalogue.GetConnectors()
                .Where(x => x.RequiresLicense)
                .Select(x => (object[]) [x]);
    }

    class AllConnectorsTestCases : TestCaseGenerator<ConnectorCatalogueItem> {
        protected override IEnumerable<object[]> Data() =>
            ConnectorCatalogue.GetConnectors().Select(x => (object[]) [x]);
    }
}