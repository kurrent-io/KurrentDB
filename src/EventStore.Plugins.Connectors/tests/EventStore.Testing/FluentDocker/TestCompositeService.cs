using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

namespace EventStore.Testing.FluentDocker;

public abstract class TestCompositeService : TestService<ICompositeService, CompositeBuilder>;