using EventStore.ClusterNode;
using EventStore.Connectors.Control;
using EventStore.Core.Certificates;
using EventStore.System.Testing;
using EventStore.System.Testing.Fixtures;
using EventStore.Toolkit.Testing;
using EventStore.Toolkit.Testing.Fixtures;
using Serilog;
using Serilog.Extensions.Logging;

Logging.Initialize();

Log.Warning("Hello darkness my old friend...");

var app = new ClusterVNodeApp();

await app.Start();


//
// var configuration = new ConfigurationBuilder()
//     .AddInMemoryCollection(
//         new Dictionary<string, string?> {
//             { "EventStore:Application:TelemetryOptout", "true" },
//             { "EventStore:Application:Insecure", "true" },
//             { "EventStore:Database:MemDb", "true" },
//             { "EventStore:Logging:LogLevel", EventStore.Common.Options.LogLevel.Default.ToString() }, // super hack to ignore esdb's absurd logging config
//             { "EventStore:Logging:DisableLogFile", "true" },
//             { "EventStore:Interface:DisableAdminUi", "true" },
//             { "EventStore:DevMode:Dev", "true" }
//         }
//     )
//     .Build();
//
//
// var nodeOptions = configuration.GetClusterVNodeOptions();
//
// var esdb = new ClusterVNodeHostedService(nodeOptions, new OptionsCertificateProvider(), nodeOptions.ConfigurationRoot);
//
// var builder = WebApplication.CreateSlimBuilder();
//
// builder.Logging.AddSerilog(Log.Logger);
//
// esdb.Node.Startup.ConfigureServices(builder.Services);
//
// builder.Services.AddSingleton<IHostedService>(esdb);
//
// await using var app = builder.Build();
//
// esdb.Node.Startup.Configure(app);
//
// try {
//     await app.StartAsync();
//
//     //await esdb.StartAsync(default);
//
//     // await NodeSystemMonitor.WaitUntilReady(esdb.Node, Timeout.InfiniteTimeSpan);
//
//     await new NodeLifetimeService(esdb.Node.MainBus, app.Services.GetRequiredService<ILogger<NodeLifetimeService>>())
//         .WaitForLeadershipAsync(Timeout.InfiniteTimeSpan);
//
//     Log.Warning("Do not go gentle into that good night, "
//               + "Old age should burn and rave at close of day; "
//               + "Rage, rage against the dying of the light.");
//
//     Log.Warning("Press any key to stop the application...");
//     Console.ReadKey(); // Waits here until a key is pressed
//     Log.Warning("Stopping application...");
//
//     return 0;
// }
// catch (Exception ex) {
//     Log.Fatal(ex, "Host terminated unexpectedly.");
//     return 1;
// }
// finally {
//     await Log.CloseAndFlushAsync();
// }





//
// ClusterVNodeActivator.Initialize();








//
//
//
// var configuration = new ConfigurationBuilder()
//     .AddInMemoryCollection(
//         new Dictionary<string, string?> {
//             { "EventStore:Application:TelemetryOptout", "true" },
//             { "EventStore:Application:Insecure", "true" },
//             { "EventStore:Database:MemDb", "true" },
//             { "EventStore:Cluster:LeaderElectionTimeoutMs", "10000" },
//             { "EventStore:Logging:LogLevel", "Verbose" },
//             { "EventStore:Logging:DisableLogFile", "false" },
//             { "EventStore:Interface:DisableAdminUi", "false" }
//         }
//     )
//     .Build();
//
//
//
//
//
// //                                                  this is necessary now. use DevCertificateProvider for dev mode
// //                                                           v
// var service = new ClusterVNodeHostedService(NodeOptions, new OptionsCertificateProvider(), NodeOptions.ConfigurationRoot);
//
// var builder = WebApplication.CreateBuilder();
// service.Node.Startup.ConfigureServices(builder.Services);
// var app = builder.Build();
// service.Node.Startup.Configure(app);
//
// await service.StartAsync(default);
//
// await Task.Delay(10_000);
// Console.WriteLine("Goodbye, World!");

//
// // var configuration = EventStoreConfiguration.Build(args);
// //
// // Log.Logger = EventStoreLoggerConfiguration.ConsoleLog;
//
// var options = ClusterVNodeOptions.FromConfiguration(configuration);
//
// EventStoreLoggerConfiguration.Initialize(
//     string.IsNullOrWhiteSpace(options.Logging.Log) ? Locations.DefaultLogDirectory : options.Logging.Log,
//     options.GetComponentName(),
//     options.Logging.LogConsoleFormat,
//     options.Logging.LogFileSize,
//     options.Logging.LogFileInterval,
//     options.Logging.LogFileRetentionCount,
//     options.Logging.DisableLogFile,
//     options.Logging.LogConfig
// );
//
// Log.Logger = EventStoreLoggerConfiguration.ConsoleLog;
//
// var nodeOptions = configuration.GetClusterVNodeOptions();
//
// Log.Information("Dev mode is enabled.");
// Log.Warning(
//     "\n==============================================================================================================\n" +
//     "DEV MODE IS ON. THIS MODE IS *NOT* RECOMMENDED FOR PRODUCTION USE.\n"                                               +
//     "DEV MODE WILL GENERATE AND TRUST DEV CERTIFICATES FOR RUNNING A SINGLE SECURE NODE ON LOCALHOST.\n"                 +
//     "==============================================================================================================\n");
// var manager = CertificateManager.Instance;
// var result  = manager.EnsureDevelopmentCertificate(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));
// if (result is not (EnsureCertificateResult.Succeeded or EnsureCertificateResult.ValidCertificatePresent)) {
//     Log.Fatal("Could not ensure dev certificate is available. Reason: {result}", result);
//     return 1;
// }
//
// var userCerts    = manager.ListCertificates(StoreName.My, StoreLocation.CurrentUser, true);
// var machineCerts = manager.ListCertificates(StoreName.My, StoreLocation.LocalMachine, true);
// var certs        = userCerts.Concat(machineCerts).ToList();
//
// if (!certs.Any()) {
//     Log.Fatal("Could not create dev certificate.");
//     return 1;
// }
//
// if (!manager.IsTrusted(certs[0]) && RuntimeInformation.IsWindows) {
//     Log.Information("Dev certificate {cert} is not trusted. Adding it to the trusted store.", certs[0]);
//     manager.TrustCertificate(certs[0]);
// } else {
//     Log.Warning("Automatically trusting dev certs is only supported on Windows.\n" +
//                 "Please trust certificate {cert} if it's not trusted already.", certs[0]);
// }
//
// Log.Information("Running in dev mode using certificate '{cert}'", certs[0]);
//
// var certificateProvider = new DevCertificateProvider(certs[0]);
//
// var service = new ClusterVNodeHostedService(nodeOptions, certificateProvider, nodeOptions.ConfigurationRoot);
//
// await ClusterVNodeActivator.NodeSystemMonitor.WaitUntilReady(service.Node, Timeout.InfiniteTimeSpan);
//
// return 0;