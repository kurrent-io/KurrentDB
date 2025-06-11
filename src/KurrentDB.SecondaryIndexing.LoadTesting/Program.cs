using KurrentDB.Common.Log;
using KurrentDB.Common.Options;
using KurrentDB.SecondaryIndexing.LoadTesting;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments;
using KurrentDB.SecondaryIndexing.LoadTesting.Generators;
using KurrentDB.SecondaryIndexing.LoadTesting.Observability;
using KurrentDB.Surge.Testing;
using Microsoft.Extensions.Configuration;
using Serilog;

var config =
	new ConfigurationBuilder()
		.AddJsonFile("appsettings.json", optional: true)
		.AddEnvironmentVariables()
		.AddCommandLine(args)
		.Build()
		.Get<LoadTestConfig>()
	?? new LoadTestConfig { DuckDbConnectionString = "DUMMY", KurrentDBConnectionString = "DUMMY" };

Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Debug()
	.WriteTo.Console()
	.CreateLogger();

Console.WriteLine(
	$"Running {config.EnvironmentType} with {config.PartitionsCount} partitions, {config.CategoriesCount} categories, {config.TotalMessagesCount} messages");

var generator = new MessageGenerator();
var environment = LoadTestEnvironment.For(config);
var observer = new SimpleMessagesBatchObserver();

var loadTest = new LoadTest(generator, environment.MessageBatchAppender, observer);
await loadTest.Run(config);
