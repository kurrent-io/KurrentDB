// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.LoadTesting;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using KurrentDB.SecondaryIndexing.Tests.Observability;
using Microsoft.Extensions.Configuration;
using Serilog;

// currently Microsoft.Testing.Platform is running this executable, probably to discover the tests that are in it,
// probably because it has transitive dependencies on Microsoft.Testing.Platform.
// This temporary workaround returns early in that case.
if (args.Contains("--diagnostic-output-directory"))
	return;

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

var loadTest = new LoadTest(generator, environment, observer);

await loadTest.Run(config);
