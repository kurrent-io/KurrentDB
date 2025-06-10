using KurrentDB.SecondaryIndexing.LoadTesting;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Generators;
using KurrentDB.SecondaryIndexing.LoadTesting.Observability;
using Microsoft.Extensions.Configuration;

var config =
	new ConfigurationBuilder()
		.AddJsonFile("appsettings.json", optional: true)
		.AddEnvironmentVariables()
		.AddCommandLine(args)
		.Build()
		.Get<LoadTestConfig>()
	?? new LoadTestConfig { DuckDbConnectionString = "DUMMY", KurrentDBConnectionString = "DUMMY" };

Console.WriteLine(
	$"Running with {config.PartitionsCount} partitions, {config.CategoriesCount} categories, {config.TotalMessagesCount} messages");

var generator = new MessageGenerator();
var publisher = new DummyPublisher();
var appender = new PublisherBasedMessageAppender(publisher);
var observer = new SimpleMessagesBatchObserver();

var loadTest = new LoadTest(generator, appender, observer);
await loadTest.Run(config);
