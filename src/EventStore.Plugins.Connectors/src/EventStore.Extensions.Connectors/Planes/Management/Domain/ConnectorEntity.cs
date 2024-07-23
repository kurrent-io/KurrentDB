using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using Eventuous;

namespace EventStore.Connectors.Management;

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
            State = ConnectorState.Stopped
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
            State = ConnectorState.Activating,
            CurrentRevision = new ConnectorRevision {
                Number    = state.CurrentRevision.Number,
                Settings  = { evt.Settings },
                CreatedAt = evt.Timestamp
            }
        });

        On<ConnectorDeactivating>((state, _) => state with {
            State = ConnectorState.Deactivating
        });

        On<ConnectorRunning>((state, _) => state with {
            State = ConnectorState.Running
        });

        On<ConnectorStopped>((state, _) => state with {
            State = ConnectorState.Stopped
        });

        On<ConnectorFailed>((state, _) => state with {
            State = ConnectorState.Stopped
            // TODO JC: What do we want to do with the error details?
        });

        On<ConnectorReset>((state, evt) => state with {
            LogPosition = evt.LogPosition
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

    /// <summary>
    /// The current log position of the connector.
    /// </summary>
    public ulong? LogPosition { get; init; } = null;

    /// <summary>
    /// Indicator if the connector is deleted.
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

    public ConnectorEntity EnsureCommittedLogPositionIsValid(ulong newLogPosition) {
        if (newLogPosition < LogPosition)
            throw new DomainException($"Connector {Id} new log position {newLogPosition} precedes the actual {LogPosition}");

        return this;
    }
}