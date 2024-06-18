using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Serialization;
using McMaster.NETCore.Plugins;
using Plugins;
using PluginLoader = McMaster.NETCore.Plugins.PluginLoader;

var newRagingPluginLoader = new RagingPluginLoader();

var newRagingResult = newRagingPluginLoader.Load("/Users/sergio/dev/eventstore/EventStore.Plugins.Connectors/plugins/JsonNetNew/JsonNetNew.dll");



var newPluginLoader = new SomePluginLoader();

var newResult = newPluginLoader.LoadPluginAssemblies("/Users/sergio/dev/eventstore/EventStore.Plugins.Connectors/plugins/JsonNetNew/");

var newPlugin = newResult!.Value.Plugin.Version;

var oldPluginLoader = new SomePluginLoader();

var oldResult = oldPluginLoader.LoadPluginAssemblies("/Users/sergio/dev/eventstore/EventStore.Plugins.Connectors/plugins/JsonNetOld/");

var oldPlugin = oldResult!.Value.Plugin.Version;





var ctxNew = AssemblyLoadContext.GetLoadContext(Assembly.LoadFrom("/Users/sergio/dev/eventstore/EventStore.Plugins.Connectors/plugins/JsonNetNew/JsonNetNew.dll"))!;

var newPluginType = ctxNew.Assemblies
    .SelectMany(a => a.GetTypes())
    .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

// var newPluginInstance = Activator.CreateInstance(newPluginType!);

// var loaderNew = PluginLoader.CreateFromAssemblyFile(
//     Path.GetFullPath("/Users/sergio/dev/eventstore/EventStore.Plugins.Connectors/plugins/JsonNetNew/JsonNetNew.dll"),
//     sharedTypes: [typeof(IPlugin)]);
//
// var pluginNewAss = loaderNew.LoadDefaultAssembly();
// var catalogNew   = new DependencyContextAssemblyCatalog(pluginNewAss);
// var depsNew      = catalogNew.GetAssemblies();


var ctxOld = AssemblyLoadContext.GetLoadContext(Assembly.LoadFrom("/Users/sergio/dev/eventstore/EventStore.Plugins.Connectors/plugins/JsonNetOld/JsonNetOld.dll"))!;

var oldPluginType = ctxOld.Assemblies
    .SelectMany(a => a.GetTypes())
    .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });


var allPluginTypes = ctxOld.Assemblies
    .SelectMany(a => a.GetTypes())
    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
    .ToList();

//var oldPluginInstance = Activator.CreateInstance(oldPluginType!);

//
// var loaderOld = PluginLoader.CreateFromAssemblyFile(
//     Path.GetFullPath("/Users/sergio/dev/eventstore/EventStore.Plugins.Connectors/plugins/JsonNetOld/JsonNetOld.dll"),
//     sharedTypes: [typeof(IPlugin)]);
//
// var pluginOldAss = loaderOld.LoadDefaultAssembly();
// var catalogOld   = new DependencyContextAssemblyCatalog(pluginOldAss);
// var depsOld      = catalogOld.GetAssemblies();



var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default); });

var app = builder.Build();

var sampleTodos = new Todo[] {
    new(1, "Walk the dog"),
    new(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
    new(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
    new(4, "Clean the bathroom"),
    new(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
};

var todosApi = app.MapGroup("/todos");
todosApi.MapGet("/", () => sampleTodos);
todosApi.MapGet(
    "/{id}",
    (int id) =>
        sampleTodos.FirstOrDefault(a => a.Id == id) is { } todo
            ? Results.Ok(todo)
            : Results.NotFound()
);

app.Run();

public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

[JsonSerializable(typeof(Todo[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }