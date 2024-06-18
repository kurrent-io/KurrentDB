namespace McMaster.NETCore.Plugins;

public interface IPlugin {
    string Name { get; }
    string Version { get; }
}