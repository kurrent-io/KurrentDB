using McMaster.NETCore.Plugins;
using Newtonsoft.Json;

namespace JsonNetNew;

public class NewJsonResolver : IPlugin {
    public string Name    => nameof(NewJsonResolver);
    public string Version => typeof(JsonConvert).Assembly.GetName().Version!.ToString();
}