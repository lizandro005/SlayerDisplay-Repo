using LibreHardwareMonitor.Hardware;

namespace SlayerDisplay;

/// <summary>
/// Substitui o SLAYERCPULiveTemperature: lê sensores com o LibreHardwareMonitor
/// (suporta Zen 5) e envia-os ao display pelo TempComm.dll, com o mesmo formato
/// de bytes que o XKWLibHelp.SendDynamicData() original.
/// </summary>
internal sealed class DisplayWorker(ILogger<DisplayWorker> log) : BackgroundService
{
    // PIDVIDHelp original, "SLAYERCPULiveTemperature": VID 11365 (0x2C65), PID 4097 (0x1001)
    private static readonly byte[] VidPid = [0x2C, 0x65, 0x10, 0x01];
    private const uint MaxDevices = 20;
    private const int SendDelayMs = 500;

    private string? _lastError;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
        computer.Open();
        TempComm.SetCheckUsb(VidPid, (uint)VidPid.Length, MaxDevices);

        bool open = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!open)
                    {
                        if (TempComm.DeviceOpen(1) == 0)
                            throw new IOException($"DeviceOpen falhou (erro {TempComm.GetErrorCode()}). O display está ligado ao USB interno?");
                        open = true;
                        log.LogInformation("Display aberto.");
                    }

                    Send(computer);
                    _lastError = null;
                }
                catch (Exception ex)
                {
                    if (ex.Message != _lastError) // não encher o log com o mesmo erro
                    {
                        log.LogWarning("{Message}", ex.Message);
                        _lastError = ex.Message;
                    }
                    if (open) { TempComm.DeviceClose(); open = false; }
                }

                await Task.Delay(SendDelayMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (open) TempComm.DeviceClose();
            computer.Close();
        }
    }

    private void Send(Computer computer)
    {
        SendTime();

        // GPU primeiro, como no original (falha aqui não é fatal)
        var gpuEntries = new List<byte[]>();
        foreach (var gpu in computer.Hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
        {
            gpu.Update();
            float? load = Find(gpu, SensorType.Load, "GPU Core");
            float? temp = Find(gpu, SensorType.Temperature, "GPU Core");
            gpuEntries.Add(Entry(load ?? 0, temp ?? 0, 0));
        }
        if (gpuEntries.Count > 0)
        {
            var buf = Concat(gpuEntries);
            TempComm.SetGpuDynamicInfo(buf, (uint)buf.Length);
        }

        // CPU: entrada "package" + uma por core, 8 bytes cada: [load, temp, clockLo, clockHi, 0,0,0,0]
        var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)
                  ?? throw new InvalidOperationException("CPU não encontrado pelo LibreHardwareMonitor.");
        cpu.Update();

        float cpuTemp = Find(cpu, SensorType.Temperature, "Core (Tctl/Tdie)")
                        ?? Find(cpu, SensorType.Temperature, "Tctl")
                        ?? Find(cpu, SensorType.Temperature, "CPU Package")
                        ?? cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
                                      .Select(s => s.Value).Max()
                        ?? throw new InvalidOperationException("Sem temperatura do CPU. Instala o PawnIO (winget install namazso.PawnIO) e corre como administrador/serviço.");
        float cpuLoad = Find(cpu, SensorType.Load, "CPU Total") ?? 0;

        var coreClocks = cpu.Sensors
            .Where(s => s.SensorType == SensorType.Clock && s.Value.HasValue && s.Name.StartsWith("Core #"))
            .OrderBy(s => s.Index)
            .ToList();
        float avgClock = coreClocks.Count > 0 ? coreClocks.Average(s => s.Value!.Value) : 0;

        var cpuEntries = new List<byte[]> { Entry(cpuLoad, cpuTemp, avgClock) };
        foreach (var c in coreClocks)
        {
            float coreLoad = Find(cpu, SensorType.Load, "CPU " + c.Name) ?? 0; // "CPU Core #1"
            cpuEntries.Add(Entry(coreLoad, 0, c.Value!.Value));
        }
        var cpuBuf = Concat(cpuEntries);
        if (TempComm.SetCpuDynamicInfo(cpuBuf, (uint)cpuBuf.Length) == 0)
            throw new IOException($"Envio CPU falhou (erro {TempComm.GetErrorCode()}).");

        // RAM: byte 0 = % usada
        var mem = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
        if (mem != null)
        {
            mem.Update();
            float? used = Find(mem, SensorType.Load, "Memory");
            if (used.HasValue)
            {
                var buf = Entry(used.Value, 0, 0);
                TempComm.SetMemDynamicInfo(buf, (uint)buf.Length);
            }
        }
    }

    private static void SendTime()
    {
        var now = DateTime.Now;
        byte[] t =
        [
            (byte)(now.Year / 100), (byte)(now.Year % 100), (byte)now.Month, (byte)now.Day,
            (byte)now.DayOfWeek, (byte)now.Hour, (byte)now.Minute, (byte)now.Second,
            (byte)(now.Millisecond / 10),
        ];
        if (TempComm.SetDeviceTime(t, (uint)t.Length) == 0)
            throw new IOException($"Envio da hora falhou (erro {TempComm.GetErrorCode()}).");
    }

    private static float? Find(IHardware hw, SensorType type, string name) =>
        hw.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name && s.Value.HasValue)?.Value;

    private static byte[] Entry(float load, float temp, float clockMhz)
    {
        int clk = (int)clockMhz;
        return [ToByte(load), ToByte(temp), (byte)(clk & 0xFF), (byte)((clk >> 8) & 0xFF), 0, 0, 0, 0];
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)v, 0, 255);

    private static byte[] Concat(List<byte[]> parts) => parts.SelectMany(p => p).ToArray();
}
