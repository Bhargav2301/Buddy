namespace Buddy.Windows;

// Notification policy is shared by the native endpoint callbacks and deterministic tests.
internal sealed class AudioRouteGuard(string endpoint, bool headphonesOnly, Action stop)
{
    internal void DeviceUnavailable(string id) { if (id == endpoint) stop(); }
    internal void DefaultRenderChanged() { if (headphonesOnly) stop(); }
    internal void PropertiesChanged(string id) { if (headphonesOnly && id == endpoint) stop(); }
}
