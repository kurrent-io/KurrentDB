// ReSharper disable CheckNamespace

using Kurrent.Surge.Connectors;
using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;

namespace KurrentDB.Connect.Connectors;

public class SystemConnectorsValidation : ConnectorsMasterValidator {
    public new static readonly SystemConnectorsValidation Instance = new();

    protected override bool TryGetConnectorValidator(ConnectorInstanceTypeName connectorTypeName, out IConnectorValidator validator) {
        validator = null!;

        if (!ConnectorCatalogue.TryGetConnector(connectorTypeName, out var connector))
            return false;

        validator = (Activator.CreateInstance(connector.ConnectorValidatorType) as IConnectorValidator)!;
        return true;
    }
}
