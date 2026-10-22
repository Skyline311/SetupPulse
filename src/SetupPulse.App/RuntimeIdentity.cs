namespace SetupPulse.App;

internal static class RuntimeIdentity
{
    // Keep storage and cross-process coordination compatible with SetupSwitch.
    // Old and new executables must share one instance and transaction lock.
    internal const string LegacyAppName = "SetupSwitch";
    internal const string DataFolderName = LegacyAppName;
    internal const string TransactionMutex = "Local\\" + LegacyAppName + ".Transaction.v1";
    internal const string InstanceMutex = "Local\\" + LegacyAppName + ".Instance.v1";
    internal const string DemoMutex = "Local\\" + LegacyAppName + ".Demo.v1";
    internal const string ShowPipe = LegacyAppName + ".Show";
    internal const string DemoPipe = LegacyAppName + ".Demo";
}
