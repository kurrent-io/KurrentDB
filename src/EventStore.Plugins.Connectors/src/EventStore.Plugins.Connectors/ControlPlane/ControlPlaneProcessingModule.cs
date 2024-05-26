using EventStore.Connectors.ControlPlane.Coordination;
using EventStore.Connectors.ControlPlane.Diagnostics;
using EventStore.Connectors.Infrastructure.Telemetry;
using EventStore.Core.Data;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.ControlPlane;

[PublicAPI]
public abstract class ControlPlaneProcessingModule : ProcessingModule {
	protected ControlPlaneProcessingModule() {
		Process(ctx => {
			if (ctx.Record.Value is not GossipUpdatedInMemory evt) return;
			
			NodeInstance = new(
				evt.Members.Single(x => x.InstanceId == evt.NodeId), 
				DateTimeOffset.UtcNow
			);
			
			Diagnostics.Dispatch("NodeInstanceInfoUpdated", new {
				NodeInstanceId = NodeInstance.InstanceId,
				IsLeader       = NodeInstance.IsLeader,
				Timestamp      = NodeInstance.Timestamp
			});
		});
	}

	protected DiagnosticsDispatcher Diagnostics { get; } = new(DiagnosticsName.BaseName);
	
	protected NodeInstanceInfo NodeInstance { get; private set; }
    
    protected void ProcessOnNode<T>(Func<T, string> getTargetNodeId, ProcessRecord<T> handler) =>
        Process<T>(
            async (evt, ctx) => {
	            var targetNodeId = getTargetNodeId(evt);
                if (targetNodeId == NodeInstance.InstanceId.ToString())
                    await handler(evt, ctx);
                else {
                    ctx.Logger.LogNodeIsNotTheTarget(
                        NodeInstance.InstanceId, 
                        NodeInstance.MemberInfo.State, 
                        targetNodeId, 
                        typeof(T).Name
                    );
                }
            }
        );
    
    protected void ProcessOnLeaderNode<T>(ProcessRecord<T> handler) =>
        Process<T>(
            async (evt, ctx) => {
                if (NodeInstance.IsLeader)
                    await handler(evt, ctx);
                else {
                    ctx.Logger.LogNodeIsNotTheLeader(
                        NodeInstance.InstanceId, 
                        NodeInstance.MemberInfo.State, 
                        typeof(T).Name
                    );
                }
            }
        );
}

static partial class ControlPlaneProcessingModuleLogMessages {
    [LoggerMessage(
        Message = "[Node Id: {NodeId} ({State})] Node is not the target node {ExpectedNodeId}. Skipping {MessageType}",
        Level   = LogLevel.Trace
    )]
    internal static partial void LogNodeIsNotTheTarget(
        this ILogger logger, Guid nodeId, VNodeState state, string expectedNodeId, string messageType
    );
    
    [LoggerMessage(
        Message = "[Node Id: {NodeId} ({State})] Node is not the leader node. Skipping {MessageType}",
        Level   = LogLevel.Trace
    )]
    internal static partial void LogNodeIsNotTheLeader(
        this ILogger logger, Guid nodeId, VNodeState state, string messageType
    );
}