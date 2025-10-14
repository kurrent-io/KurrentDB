using System;
namespace FakePlugin {
    public class FakePlugin : ICloneable {
        public object Clone() => new FakePlugin();
    }
}
