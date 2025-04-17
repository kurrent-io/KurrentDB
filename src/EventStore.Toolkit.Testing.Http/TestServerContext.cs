// ReSharper disable StaticMemberInGenericType

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventStore.Toolkit.Testing.Http;

[PublicAPI]
public class TestServerContext<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class {
    public readonly string TestClassCorrelationId = Guid.NewGuid().ToString("N");

    static readonly TestOutputHelperSink TestSink       = new();
    static readonly TestIdEnricher       TestIdEnricher = new();

    static TestServerContext() {
        bool.TryParse(Environment.GetEnvironmentVariable("OUT_CI_VAR"), out var isCiPipeline);

        // This static logger is initialised once statically, and reused across all tests.
        var loggerConfig = new LoggerConfiguration()
            .WriteTo.Sink(TestSink)
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithThreadName()
            .Enrich.With(TestIdEnricher);

        if (!isCiPipeline)
            loggerConfig.WriteTo.Seq("http://localhost:5341");

        Log.Logger = loggerConfig.CreateLogger();
    }

    public TestServerContext(ITestOutputHelper outputHelper, TestServerStartMode startMode) {
        OutputHelper = outputHelper;

        if (startMode == TestServerStartMode.DontStartHost) return;

        TestSink.SwitchTo(OutputHelper);
        TestIdEnricher.UpdateId(TestClassCorrelationId);

        try {
            BeforeStart().GetAwaiter().GetResult();
            CreateDefaultClient();
            AfterStart().GetAwaiter().GetResult();
        }
        catch (Exception ex) {
            OutputHelper.WriteLine(ex.ToString());
        }
    }

    public ITestOutputHelper OutputHelper { get; }

    protected virtual void ConfigureConfiguration(IDictionary<string, string?> configuration) { }
    protected virtual void ConfigureServices(IServiceCollection services) { }

    protected virtual Task BeforeStart() => Task.CompletedTask;
    protected virtual Task AfterStart()  => Task.CompletedTask;

    protected override IWebHostBuilder? CreateWebHostBuilder() {
        var inMemCollection = new Dictionary<string, string?>();

        ConfigureConfiguration(inMemCollection);

        // https://github.com/dotnet/aspnetcore/issues/37680#issuecomment-1331559463
        TestConfiguration.Create(cb => cb.AddInMemoryCollection(inMemCollection));

        return base.CreateWebHostBuilder();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.ConfigureServices(
            services => {
                // other stuff
                ConfigureServices(services);
            }
        );
    }

    public T  Service<T>() where T : notnull         => Services.GetRequiredService<T>();
    public T? OptionalService<T>() where T : notnull => Services.GetService<T>();
}