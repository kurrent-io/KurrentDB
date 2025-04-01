using EventStore.Connect.Producers.Configuration;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;

namespace EventStore.Connectors.Connect.Components.Producers;

public class SystemProducerProvider(Func<SystemProducerBuilder> builderFactory) : IProducerProvider {
    public IProducer GetProducer(Func<ProducerOptions, ProducerOptions> configure) {
        var temp = builderFactory();
        var builder = temp with {
            Options = (SystemProducerOptions) configure(temp.Options)
        };

        return builder.Create();
    }
}