// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;
using Kurrent.Surge;
using Kurrent.Surge.Connectors;
using KurrentDB.Connectors.Management.Contracts.Events;

namespace KurrentDB.Connectors.Planes.Control.Model;

public sealed class ActiveConnectors : IEnumerable<RegisteredConnector> {
    const string StartPositionKey = "Subscription:StartPosition";

    public ActiveConnectors() : this([], RecordPosition.Earliest) { }

    internal ActiveConnectors(Dictionary<ConnectorId, RegisteredConnector> connectors, RecordPosition position) {
        Connectors = connectors;
        Position   = position;
    }

    Dictionary<ConnectorId, RegisteredConnector> Connectors { get; }

    public RecordPosition Position { get; private set; }

    public RegisteredConnector this[ConnectorId connectorId] => Connectors[connectorId];

    public void Apply(SurgeRecord record) {
        switch (record.Value) {
            case ConnectorActivating activating:
                var settings = activating.Settings.ToDictionary(x => x.Key, x => (string?)x.Value);

                if (activating.StartFrom is not null)
                    settings[StartPositionKey] = activating.StartFrom.LogPosition.ToString();

                Connectors[activating.ConnectorId] = new(activating.ConnectorId, activating.Revision, settings);
                break;

            case ConnectorRunning running when Connectors.TryGetValue(running.ConnectorId, out var connector):
                var runningSettings = connector.Settings.Where(x => x.Key != StartPositionKey).ToDictionary();

                Connectors[running.ConnectorId] = new(connector.ConnectorId, connector.Revision, runningSettings);
                break;

            case ConnectorDeactivating deactivating:
                Connectors.Remove(deactivating.ConnectorId);
                break;
        }

        Advance(record.Position);
    }

    void Advance(RecordPosition position) {
        if (position.LogPosition > Position.LogPosition)
            Position = position;
    }

    public IEnumerator<RegisteredConnector> GetEnumerator() => Connectors.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
