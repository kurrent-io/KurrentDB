// ReSharper disable CheckNamespace

using EventStore.Streaming.Processors;

namespace EventStore.IO.Connectors;

/// <summary>
/// Defines the contract for a sink in the EventStore system.
/// </summary>
public interface ISink {
    /// <summary>
    /// Initializes the sink with the provided SinkContext. This method is optional and can be overridden to perform any necessary setup.
    /// </summary>
    /// <param name="sinkContext">The context for the sink's operation, providing configuration settings and services.</param>
    void Open(SinkContext sinkContext) { }

    /// <summary>
    /// Writes a record to the sink. This method is asynchronous, allowing it to perform I/O operations without blocking the calling thread.
    /// </summary>
    /// <param name="recordContext">The record to be written and the context for the operation.</param>
    Task Write(RecordContext recordContext);

    /// <summary>
    /// Performs any necessary cleanup when the sink is no longer needed.
    /// This method is optional and can be overridden to close resources like file handles or network connections.
    /// </summary>
    ValueTask Close() => ValueTask.CompletedTask;
}