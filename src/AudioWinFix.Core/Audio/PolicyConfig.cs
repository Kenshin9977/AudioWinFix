using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>
/// Sets the Windows default audio endpoint for a given role via the undocumented
/// IPolicyConfig COM interface (same one the Sound control panel uses).
/// ponytail: the full vtable is declared even though only SetDefaultEndpoint is
/// called — COM dispatches by slot, so the order and count must match exactly.
/// Verified on Win10/Win11 x64. If SetDefaultEndpoint no-ops on a future build,
/// the IPolicyConfigVista variant (different slot order) is the fallback.
/// </summary>
public static class PolicyConfig
{
    public static int SetDefault(string deviceId, Role role)
    {
        var client = (IPolicyConfig)new CPolicyConfigClient();
        try { return client.SetDefaultEndpoint(deviceId, role); }
        finally { Marshal.ReleaseComObject(client); }
    }

    // Not sealed: the (IPolicyConfig) cast below is a runtime COM QueryInterface,
    // which the compiler only permits when the coclass could implement the
    // interface — a sealed class provably can't, giving CS0030.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class CPolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, bool @default, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string id, bool @default, IntPtr def, IntPtr min);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, bool store, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, bool store, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint(string id, Role role); // slot 10
        [PreserveSig] int SetEndpointVisibility(string id, bool visible);
    }
}
