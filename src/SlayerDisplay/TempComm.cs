using System.Runtime.InteropServices;

namespace SlayerDisplay;

/// <summary>
/// P/Invoke para o TempComm.dll nativo da NTECH (é ele que fala USB com o display).
/// Assinaturas copiadas do XKWLib.DllWrapper original. Todas devolvem 0 em caso de erro.
/// </summary>
internal static class TempComm
{
    private const string Dll = "TempComm.dll";

    [DllImport(Dll)] public static extern void SetCheckUsb(byte[] vidPid, uint byteSize, uint maxQty);
    [DllImport(Dll)] public static extern uint DeviceOpen(uint index);
    [DllImport(Dll)] public static extern void DeviceClose();
    [DllImport(Dll)] public static extern void CheckOnlineDevice();
    [DllImport(Dll)] public static extern uint GetErrorCode();

    [DllImport(Dll)] public static extern uint SetDeviceTime(byte[] data, uint num);
    [DllImport(Dll)] public static extern uint SetCpuDynamicInfo(byte[] data, uint num);
    [DllImport(Dll)] public static extern uint SetGpuDynamicInfo(byte[] data, uint num);
    [DllImport(Dll)] public static extern uint SetMemDynamicInfo(byte[] data, uint num);
}
