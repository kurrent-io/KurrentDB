using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Plugins;
using EventStore.Streaming.Producers;

namespace Library;

public interface ILittleTool  {
    public IPublisher Publisher { get; set; }
}

public class LittleTool : SubsystemsPlugin, ILittleTool {
    public LittleTool() {
        var temp = new DnsEndPoint("localhost", 2113);
        var kebas = temp.GetHost();
    }

    public IProducer Producer { get; set; } = null!;
    public IPublisher Publisher { get; set; } = null!;
}