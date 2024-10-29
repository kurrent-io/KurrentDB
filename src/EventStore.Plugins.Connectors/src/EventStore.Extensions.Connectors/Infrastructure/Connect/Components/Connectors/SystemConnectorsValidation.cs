// ReSharper disable CheckNamespace

using EventStore.Connectors.Connect.Components.Connectors;

namespace EventStore.Connect.Connectors;

public class SystemConnectorsValidation : ConnectorsMasterValidator {
    protected override bool TryGetConnectorValidator(ConnectorInstanceTypeName connectorTypeName, out IConnectorValidator validator) {
        validator = null!;

        if (!ConnectorCatalogue.TryGetConnector(connectorTypeName, out var connector))
            return false;

        validator = (Activator.CreateInstance(connector.ConnectorValidatorType) as IConnectorValidator)!;
        return true;
    }
}