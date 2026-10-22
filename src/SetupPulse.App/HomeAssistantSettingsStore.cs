using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using SetupPulse.Core;

namespace SetupPulse.App;

internal record HomeAssistantDocument(HomeAssistantOptions Options, string? ProtectedPassword = null);
internal record HomeAssistantSaveRequest(HomeAssistantOptions Options, string? Password = null, bool ClearPassword = false);

[SupportedOSPlatform("windows")]
internal sealed class HomeAssistantSettingsStore(Store store)
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SetupPulse.MQTT.v1");
    public HomeAssistantDocument Load()
    {
        var document = store.Read<HomeAssistantDocument>("home-assistant.json")
            ?? new(new(InstanceId: Guid.NewGuid().ToString("N")));
        HomeAssistantProtocol.Validate(document.Options);
        return document;
    }

    public HomeAssistantDocument Save(HomeAssistantDocument previous, HomeAssistantSaveRequest request)
    {
        var options = request.Options with { InstanceId = previous.Options.InstanceId,
            Host = request.Options.Host.Trim(), DeviceName = request.Options.DeviceName.Trim() };
        HomeAssistantProtocol.Validate(options);
        if (request.Password?.Length > 4096) throw new InvalidDataException("Das MQTT-Passwort ist zu lang.");
        string? password = request.ClearPassword ? null : previous.ProtectedPassword;
        if (!string.IsNullOrEmpty(request.Password))
        {
            var bytes = Encoding.UTF8.GetBytes(request.Password);
            try { password = Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        var document = new HomeAssistantDocument(options, password);
        store.Write("home-assistant.json", document);
        return document;
    }

    public static string Password(HomeAssistantDocument document)
    {
        if (document.ProtectedPassword == null) return "";
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(document.ProtectedPassword), Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
