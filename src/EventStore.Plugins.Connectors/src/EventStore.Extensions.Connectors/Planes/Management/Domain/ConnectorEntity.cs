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

        On<ConnectorDeleted>((state, _) => state with {
            IsDeleted = true
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

        On<ConnectorReset>((state, evt) => state with {
            LogPosition = evt.LogPosition,
            Resetting = true
        });

        On<ConnectorPositionCommitted>((state, evt) => state with {
            LogPosition = evt.LogPosition
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
    /// The last known log position of the connector.
    /// </summary>
    public ulong? LogPosition { get; init; }

    public bool Resetting { get; init; }

    /// <summary>
    /// Indicates if the connector is deleted.
    /// </summary>
    public bool IsDeleted { get; init; }

    public ConnectorEntity EnsureNotDeleted() {
        if (IsDeleted)
            throw new ConnectorDomainExceptions.ConnectorDeletedException(Id);

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

    public ConnectorEntity EnsureNewLogPositionIsValid(ulong newLogPosition) {
        if (newLogPosition < LogPosition)
            throw new DomainException($"Connector {Id} new log position {newLogPosition} precedes the actual {LogPosition}");

        return this;
    }
}