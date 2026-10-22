using System.Text.Json;

namespace SetupPulse.Core;

public static class ProductFeatures
{
    public static bool LiveMonitorAlignment { get; } = ReadLiveMonitorAlignment();
    static bool ReadLiveMonitorAlignment()
    {
        using var stream = typeof(ProductFeatures).Assembly.GetManifestResourceStream("ProductFeatures")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("liveMonitorAlignment").GetBoolean();
    }
}
