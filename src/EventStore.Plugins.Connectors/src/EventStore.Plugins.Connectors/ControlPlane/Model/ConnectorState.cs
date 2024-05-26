namespace EventStore.Connectors.ControlPlane;

public enum ConnectorState {
    Unspecified  = 0,
    Activating   = 1,
    Running      = 2,
    Suspended    = 3,
    Deactivating = 4,
    Stopped      = 5,
    Failed       = 6
}