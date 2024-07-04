using System.Runtime;
using System.Security.Cryptography.X509Certificates;
using EventStore.ClusterNode;
using EventStore.Common.DevCertificates;
using EventStore.Common.Log;
using EventStore.Common.Utils;
using EventStore.Core;
using EventStore.Core.Certificates;
using EventStore.Core.Configuration;
using EventStore.Testing;
using EventStore.Testing.Fixtures;
using Microsoft.Extensions.Configuration;
using Serilog;

Console.WriteLine("Hello darkness my old friend...");

Logging.Initialize();

// ClusterVNodeActivator.Initialize();

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(
        new Dictionary<string, string?> {
            { "EventStore:Application:TelemetryOptout", "true" },
            { "EventStore:Application:Insecure", "true" },
            { "EventStore:Database:MemDb", "true" },
            { "EventStore:Cluster:LeaderElectionTimeoutMs", "10000" },
            { "EventStore:Logging:LogLevel", "Verbose" },
            { "EventStore:Logging:DisableLogFile", "false" },
            { "EventStore:Interface:DisableAdminUi", "false" }
        }
    )
    .Build();

// var configuration = EventStoreConfiguration.Build(args);
//
// Log.Logger = EventStoreLoggerConfiguration.ConsoleLog;

var options = ClusterVNodeOptions.FromConfiguration(configuration);

EventStoreLoggerConfiguration.Initialize(
    string.IsNullOrWhiteSpace(options.Logging.Log) ? Locations.DefaultLogDirectory : options.Logging.Log,
    options.GetComponentName(),
    options.Logging.LogConsoleFormat,
    options.Logging.LogFileSize,
    options.Logging.LogFileInterval,
    options.Logging.LogFileRetentionCount,
    options.Logging.DisableLogFile,
    options.Logging.LogConfig
);

Log.Logger = EventStoreLoggerConfiguration.ConsoleLog;

var nodeOptions = configuration.GetClusterVNodeOptions();

Log.Information("Dev mode is enabled.");
Log.Warning(
    "\n==============================================================================================================\n" +
    "DEV MODE IS ON. THIS MODE IS *NOT* RECOMMENDED FOR PRODUCTION USE.\n"                                               +
    "DEV MODE WILL GENERATE AND TRUST DEV CERTIFICATES FOR RUNNING A SINGLE SECURE NODE ON LOCALHOST.\n"                 +
    "==============================================================================================================\n");
var manager = CertificateManager.Instance;
var result  = manager.EnsureDevelopmentCertificate(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));
if (result is not (EnsureCertificateResult.Succeeded or EnsureCertificateResult.ValidCertificatePresent)) {
    Log.Fatal("Could not ensure dev certificate is available. Reason: {result}", result);
    return 1;
}

var userCerts    = manager.ListCertificates(StoreName.My, StoreLocation.CurrentUser, true);
var machineCerts = manager.ListCertificates(StoreName.My, StoreLocation.LocalMachine, true);
var certs        = userCerts.Concat(machineCerts).ToList();

if (!certs.Any()) {
    Log.Fatal("Could not create dev certificate.");
    return 1;
}

if (!manager.IsTrusted(certs[0]) && RuntimeInformation.IsWindows) {
    Log.Information("Dev certificate {cert} is not trusted. Adding it to the trusted store.", certs[0]);
    manager.TrustCertificate(certs[0]);
} else {
    Log.Warning("Automatically trusting dev certs is only supported on Windows.\n" +
                "Please trust certificate {cert} if it's not trusted already.", certs[0]);
}

Log.Information("Running in dev mode using certificate '{cert}'", certs[0]);

var certificateProvider = new DevCertificateProvider(certs[0]);

var service = new ClusterVNodeHostedService(nodeOptions, certificateProvider, nodeOptions.ConfigurationRoot);

await ClusterVNodeActivator.NodeSystemMonitor.WaitUntilReady(service.Node, Timeout.InfiniteTimeSpan);

return 0;