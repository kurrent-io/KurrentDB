// See https://aka.ms/new-console-template for more information

using PluginLoaderLab;

var conectorsDirectoryPath = Path.Combine(AppContext.BaseDirectory, "plugins");

var catalog = ConnectorsCatalog.From(conectorsDirectoryPath);

var sinks      = catalog.ScanSinks();
var validators = catalog.ScanSinkValidators();

foreach (var type in sinks)
    Console.WriteLine("Sink Connector: {0}", type.FullName);

foreach (var type in validators)
    Console.WriteLine("Connector Validator: {0}", type.FullName);