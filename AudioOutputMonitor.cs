using System.Runtime.InteropServices;

namespace BluetoothAudioRelay;

internal sealed class AudioOutputMonitor : IDisposable, IMMNotificationClient
{
    private readonly IMMDeviceEnumerator _enumerator;
    private bool _disposed;

    public AudioOutputMonitor()
    {
        var enumeratorType = Type.GetTypeFromCLSID(
            new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),
            throwOnError: true)!;
        _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(this));
    }

    public event EventHandler? DefaultOutputChanged;

    public string GetDefaultOutputName()
    {
        if (_disposed)
        {
            return "不可用";
        }

        IMMDevice? device = null;
        IPropertyStore? propertyStore = null;
        try
        {
            Marshal.ThrowExceptionForHR(
                _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out propertyStore));
            var key = PropertyKeys.DeviceFriendlyName;
            Marshal.ThrowExceptionForHR(propertyStore.GetValue(ref key, out var value));
            try
            {
                return value.GetString() ?? "未命名输出设备";
            }
            finally
            {
                value.Clear();
            }
        }
        catch (COMException)
        {
            return "未检测到默认输出设备";
        }
        finally
        {
            ReleaseComObject(propertyStore);
            ReleaseComObject(device);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch
        {
        }

        ReleaseComObject(_enumerator);
    }

    int IMMNotificationClient.OnDeviceStateChanged(string deviceId, uint newState) => 0;

    int IMMNotificationClient.OnDeviceAdded(string deviceId) => 0;

    int IMMNotificationClient.OnDeviceRemoved(string deviceId) => 0;

    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
    {
        if (flow == EDataFlow.Render && role is ERole.Multimedia or ERole.Console)
        {
            DefaultOutputChanged?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    int IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}

internal enum EDataFlow
{
    Render,
    Capture,
    All
}

internal enum ERole
{
    Console,
    Multimedia,
    Communications
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams, out IntPtr instance);

    [PreserveSig]
    int OpenPropertyStore(uint accessMode, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint propertyCount);

    [PreserveSig]
    int GetAt(uint propertyIndex, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(
        EDataFlow flow,
        ERole role,
        [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

internal static class PropertyKeys
{
    public static PropertyKey DeviceFriendlyName => new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14
    };
}

[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    [FieldOffset(0)]
    private readonly ushort _valueType;

    [FieldOffset(8)]
    private readonly IntPtr _pointerValue;

    public string? GetString()
    {
        const ushort vtLpwstr = 31;
        return _valueType == vtLpwstr ? Marshal.PtrToStringUni(_pointerValue) : null;
    }

    public void Clear()
    {
        PropVariantClear(ref this);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
