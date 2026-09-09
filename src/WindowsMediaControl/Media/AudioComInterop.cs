using System.Runtime.InteropServices;

namespace WindowsMediaControl.Media;

// Single canonical CoreAudio COM interop surface. These types must be declared exactly once per
// process: duplicate CoClass declarations with the same CLSID in different files produce distinct
// CLR identities for the same COM object, which surfaces as InvalidCastException when an RCW
// created through one declaration is used as the other (seen in widget session.open paths where
// the poll loop and the UI session raced through SystemAudio and AppAudio concurrently).
internal enum EDataFlow
{
	Render = 0,
}

internal enum ERole
{
	Multimedia = 1,
}

internal enum CLSCTX
{
	All = 23,
}

internal enum DeviceState
{
	Active = 1,
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
	[PreserveSig]
	int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IMMDeviceCollection? ppDevices);

	[PreserveSig]
	int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? ppDevice);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
	[PreserveSig]
	int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

	[PreserveSig]
	int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);

	[PreserveSig]
	int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf06")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
	[PreserveSig]
	int GetCount(out int pcDevices);

	[PreserveSig]
	int Item(int nDevice, out IMMDevice? ppDevice);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
	[PreserveSig]
	int RegisterControlChangeNotify(IntPtr pNotify);

	[PreserveSig]
	int UnregisterControlChangeNotify(IntPtr pNotify);

	[PreserveSig]
	int GetChannelCount(out int pnChannelCount);

	[PreserveSig]
	int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);

	[PreserveSig]
	int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);

	[PreserveSig]
	int GetMasterVolumeLevel(out float pfLevelDB);

	[PreserveSig]
	int GetMasterVolumeLevelScalar(out float pfLevel);

	[PreserveSig]
	int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);

	[PreserveSig]
	int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
	[PreserveSig]
	int GetAudioSessionControl(in Guid sessionId, uint streamFlags, out IntPtr sessionControl);

	[PreserveSig]
	int GetSimpleAudioVolume(in Guid sessionId, uint streamFlags, out IntPtr audioVolume);

	[PreserveSig]
	int GetSessionEnumerator(out IAudioSessionEnumerator? ppSessionEnum);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
	[PreserveSig]
	int GetCount(out int sessionCount);

	[PreserveSig]
	int GetSession(int sessionCount, out IAudioSessionControl2? session);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
	[PreserveSig]
	int GetState(out int pRetVal);

	[PreserveSig]
	int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

	[PreserveSig]
	int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

	[PreserveSig]
	int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

	[PreserveSig]
	int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

	[PreserveSig]
	int GetGroupingParam(out Guid pRetVal);

	[PreserveSig]
	int SetGroupingParam(ref Guid grouping, ref Guid eventContext);

	[PreserveSig]
	int RegisterAudioSessionNotification(IntPtr notifications);

	[PreserveSig]
	int UnregisterAudioSessionNotification(IntPtr notifications);

	[PreserveSig]
	int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

	[PreserveSig]
	int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

	[PreserveSig]
	int GetProcessId(out int pRetVal);

	[PreserveSig]
	int IsSystemSoundsSession();
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
	[PreserveSig]
	int SetMasterVolume(float level, Guid eventContext);

	[PreserveSig]
	int GetMasterVolume(out float level);

	[PreserveSig]
	int SetMute(bool mute, Guid eventContext);

	[PreserveSig]
	int GetMute(out bool mute);
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient
{
}

[ComImport]
[Guid("F8679F50-850A-4EBC-BF62-5F5672649466")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
	[PreserveSig]
	int GetMixFormat();

	[PreserveSig]
	int GetDeviceFormat();

	[PreserveSig]
	int ResetDeviceFormat();

	[PreserveSig]
	int SetDeviceFormat();

	[PreserveSig]
	int GetProcessingPeriod();

	[PreserveSig]
	int SetProcessingPeriod();

	[PreserveSig]
	int GetShareMode();

	[PreserveSig]
	int SetShareMode();

	[PreserveSig]
	int GetPropertyValue();

	[PreserveSig]
	int SetPropertyValue();

	[PreserveSig]
	int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

	[PreserveSig]
	int SetEndpointVisibility();
}
