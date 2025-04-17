using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using Eventuous;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Connectors.Management;

[PublicAPI]
public record ConnectorEntity : State<ConnectorEntity> {
    public ConnectorEntity() {
        On<ConnectorCreated>((state, evt) => state with {
            Id = evt.ConnectorId,
            Name = evt.Name,
            CurrentRevision = new ConnectorRevision {
                Number    = 0,
                Settings  = { evt.Settings },
                CreatedAt = evt.Timestamp
            },
            IsDeleted = false, // Always force IsDeleted false on Created for specific scenarios
            State = ConnectorState.Stopped,
            StateTimestamp = evt.Timestamp
        });

        On<ConnectorReconfigured>((state, evt) => state with {
            CurrentRevision = new ConnectorRevision {
                Number    = evt.Revision,
                Settings  = { evt.Settings },
                CreatedAt = evt.Timestamp
            }
        });

        On<ConnectorDeleted>((state, evt) => state with {
            IsDeleted = true,
            Id = evt.ConnectorId,
            StateTimestamp = evt.Timestamp
        });

        On<ConnectorRenamed>((state, evt) => state with {
            Name = evt.Name
        });

        On<ConnectorActivating>((state, evt) => state with {
            CurrentRevision = new ConnectorRevision {
                Number    = state.CurrentRevision.Number,
                Settings  = { evt.Settings },
                CreatedAt = evt.Timestamp
            },
            State = ConnectorState.Activating,
            StateTimestamp = evt.Timestamp,
        });

        On<ConnectorDeactivating>((state, evt) => state with {
            State = ConnectorState.Deactivating,
            StateTimestamp = evt.Timestamp
        });

        On<ConnectorRunning>((state, evt) => state with {
            State = ConnectorState.Running,
            StateTimestamp = evt.Timestamp
        });

        On<ConnectorStopped>((state, evt) => state with {
            State = ConnectorState.Stopped,
            StateTimestamp = evt.Timestamp
        });

        On<ConnectorFailed>((state, evt) => state with {
            State = ConnectorState.Stopped,
            StateTimestamp = evt.Timestamp
        });
    }

    /// <summary>
    /// Unique identifier of the connector.
    /// </summary>
    public string Id { get; init; } = Guid.Empty.ToString();

    /// <summary>
    /// Name of the connector.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// The current connector settings.
    /// </summary>
    public ConnectorRevision CurrentRevision { get; init; } = new();

    /// <summary>
    /// The state of the connector.
    /// </summary>
    public ConnectorState State { get; init; } = ConnectorState.Unknown;

    public Timestamp StateTimestamp { get; init; }

    /// <summary>
    /// Indicates if the connector is deleted.
    /// </summary>
    public bool IsDeleted { get; init; }

    /// <summary>
    /// Indicates the entity is new if it was not previously deleted and has no identifier.
    /// </summary>
    public bool IsNew => !IsDeleted && Id != Guid.Empty.ToString();

    public ConnectorEntity EnsureIsNew() {
        if (!IsDeleted && Id != Guid.Empty.ToString())
            throw new DomainExceptions.EntityAlreadyExists("Connector", Id);

        return this;
    }

    public ConnectorEntity EnsureNotDeleted() {
        // this should just cover the edge case where the entity was
        // deleted logically but the stream was not deleted yet
        if (IsDeleted)
            throw new DomainExceptions.EntityDeleted("Connector", Id, StateTimestamp.ToDateTimeOffset());

        return this;
    }

    public ConnectorEntity EnsureStopped() {
        if (State is not ConnectorState.Stopped)
            throw new DomainException($"Connector {Id} must be stopped. Current state: {State}");

        return this;
    }

    public ConnectorEntity EnsureRunning() {
        if (State is not (ConnectorState.Running or ConnectorState.Activating))
            throw new DomainException($"Connector {Id} must be running. Current state: {State}");

        return this;
    }
}