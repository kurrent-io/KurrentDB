// ReSharper disable CheckNamespace

using System.Text;
using EventStore.Streaming;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Configuration;

namespace EventStore.IO.Connectors;

public interface IConnectorsFactory {
    IProcessor Create(string connectorId, IConfiguration configuration);
    
    public IProcessor Create(string connectorId, Dictionary<string, string?> settings) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
		
        return Create(connectorId, configuration);
    }
    
    public IProcessor CreateFromJson(string connectorId, Stream jsonStream) {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(jsonStream)
            .Build();
		
        return Create(connectorId, configuration);
    }
    
    public IProcessor CreateFromJson(string connectorId, string jsonSettings) => 
        CreateFromJson(connectorId, MemoryStreamManager.GetStream(Encoding.UTF8.GetBytes(jsonSettings)));

    public IProcessor CreateFromIni(string connectorId, Stream iniStream) {
        var configuration = new ConfigurationBuilder()
            .AddIniStream(iniStream)
            .Build();
		
        return Create(connectorId, configuration);
    }
	
    public IProcessor CreateFromIni(string connectorId, string iniSettings) => 
        CreateFromIni(connectorId, MemoryStreamManager.GetStream(Encoding.UTF8.GetBytes(iniSettings)));
}