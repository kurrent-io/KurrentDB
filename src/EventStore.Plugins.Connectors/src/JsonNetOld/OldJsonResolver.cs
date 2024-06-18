using McMaster.NETCore.Plugins;
using Newtonsoft.Json;

namespace JsonNetOld;

public class OldJsonResolver : IPlugin {
    public string Name    => nameof(OldJsonResolver);
    public string Version => typeof(JsonConvert).Assembly.GetName().Version!.ToString();
}