#pragma warning disable CA1822 // Mark members as static

// ReSharper disable InconsistentNaming

using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge;
using KurrentDB.Api.Streams;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.Sample;
using KurrentDB.Testing.Sample.HomeAutomation;
using SchemaFormat = KurrentDB.Protocol.V2.Streams.SchemaFormat;
using SchemaInfo = KurrentDB.Protocol.V2.Streams.SchemaInfo;
using StreamRevision = KurrentDB.Api.Streams.StreamRevision;

namespace KurrentDB.Api.Tests.Fixtures;

public partial class ClusterVNodeTestContext {
    /// <summary>
    /// Creates a unique stream name for KurrentDB operations, combining the given category with a short unique identifier.
    /// </summary>
    /// <param name="category">The category associated with the stream name.</param>
    /// <returns>A StreamName instance containing the generated stream name.</returns>
    public StreamName NewStreamName([CallerMemberName] string category = "") => StreamName.From($"{category}-{TestUid.New()}");

    public HomeAutomationSimulation SimulateIotEvents(int numberOfEvents) {
        var events = HomeAutomationSimulator.ForHome("").Simulate(numberOfEvents);

        var homeId = Guid.NewGuid();
        var stream = $"Home-{homeId}";

        var records = events.Aggregate(new List<AppendRecord>(), (seed, evt) => {
            dynamic iotEvent = evt;

            var recordId  = iotEvent.EventId.ToString();
            var timestamp = iotEvent.Timestamp;

            var record = new AppendRecord {
                RecordId = recordId,
                Data     = UnsafeByteOperations.UnsafeWrap(JsonSerializer.SerializeToUtf8Bytes(evt)),
                Schema   = new SchemaInfo {
                    Name   = evt.GetType().Name,
                    Format = SchemaFormat.Json,
                },
                Properties = {
                    { "tests.iot.stream", Value.ForString(stream) },
                    { "tests.iot.event-sequence", Value.ForNumber(seed.Count + 1) }
                },
                Timestamp = timestamp
            };

            seed.Add(record);

            return seed;
        });

        return new(
            homeId,
            new AppendRequest {
                Stream  = stream,
                Records = { records }
            }
        );
    }

    public SimulatedGame SimulateGame(GamesAvailable game, int take = int.MaxValue, Protocol.V2.Streams.SchemaFormat dataFormat = Protocol.V2.Streams.SchemaFormat.Json) {
        if (dataFormat != Protocol.V2.Streams.SchemaFormat.Json)
            throw new NotSupportedException($"The data format '{dataFormat}' is not supported for game simulations. Only 'Json' is supported.");

        var simulatedGame = GameSimulator.Simulate(game);

        var stream = $"{game}-{simulatedGame.GameId}";

        var records = simulatedGame.GameEvents.Take(take).Aggregate(new List<AppendRecord>(), (seed, evt) => {
            var moveId = Guid.NewGuid().ToString();

            var record = new AppendRecord {
                RecordId = moveId,
                Data     = UnsafeByteOperations.UnsafeWrap(JsonSerializer.SerializeToUtf8Bytes(evt)),
                Schema   = new SchemaInfo {
                    Name   = evt.GetType().Name,
                    Format = dataFormat,
                },
                Properties = {
                    { "tests.game.id", Value.ForString(simulatedGame.GameId.ToString()) },
                    { "tests.game.name", Value.ForString(nameof(TicTacToe)) },
                    { "tests.game.stream", Value.ForString(stream) },
                    { "tests.game.move-id", Value.ForString(moveId) },
                    { "tests.game.move-sequence", Value.ForNumber(seed.Count + 1) }
                }
            };

            seed.Add(record);

            return seed;
        });

        return new(
            simulatedGame.GameId,
            new AppendRequest {
                Stream  = stream,
                Records = { records }
            }
        );
    }

    public SimulatedGame SimulateGame(int take = int.MaxValue, Protocol.V2.Streams.SchemaFormat dataFormat = Protocol.V2.Streams.SchemaFormat.Json) =>
        SimulateGame(GamesAvailable.TicTacToe, take, dataFormat);
}

public record HomeAutomationSimulation(Guid HomeId, AppendRequest Request) : IEnumerable<AppendRecord> {
    public StreamName Stream => Request.Stream;

    public IEnumerator<AppendRecord> GetEnumerator() => Request.Records.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator AppendRequest(HomeAutomationSimulation _)               => _.Request;
    public static implicit operator RepeatedField<AppendRecord>(HomeAutomationSimulation _) => _.Request.Records;
}


public record SeededGame(SimulatedGame SimulatedGame, LogPosition Position, StreamRevision Revision) : IEnumerable<AppendRecord> {
    public StreamName Stream => SimulatedGame.Stream;

    public IEnumerator<AppendRecord> GetEnumerator() => SimulatedGame.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator AppendRequest(SeededGame _)               => _.SimulatedGame;
    public static implicit operator RepeatedField<AppendRecord>(SeededGame _) => _.SimulatedGame;
}

public record SimulatedGame(Guid GameId, AppendRequest Request) : IEnumerable<AppendRecord> {
    public StreamName Stream => Request.Stream;

    public IEnumerator<AppendRecord> GetEnumerator() => Request.Records.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator AppendRequest(SimulatedGame _)               => _.Request;
    public static implicit operator RepeatedField<AppendRecord>(SimulatedGame _) => _.Request.Records;
}
