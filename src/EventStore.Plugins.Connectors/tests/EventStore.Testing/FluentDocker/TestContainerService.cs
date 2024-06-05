using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

namespace EventStore.Testing.FluentDocker;

public abstract class TestContainerService : TestService<IContainerService, ContainerBuilder>;