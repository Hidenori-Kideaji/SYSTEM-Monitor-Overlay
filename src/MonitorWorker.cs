using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VramMonitorPortable
{
    // One client per kind of measurement: a hung telemetry driver cannot stop VRAM or RAM.
    internal sealed class MonitorWorkerClient : IDisposable
    {
        private readonly object gate = new object();
        private readonly string kind;
        private bool disposed;
        private Process process;
        private IntPtr job;
        private NamedPipeServerStream pipe;
        private StreamReader reader;
        private StreamWriter writer;
        private IAsyncResult connection;

        internal MonitorWorkerClient(string kind) { this.kind = kind; }

        internal string Request(string request)
        {
            NamedPipeServerStream channel;
            StreamReader input;
            StreamWriter output;
            IAsyncResult pending;
            try
            {
                lock (gate)
                {
                    if (disposed) throw new ObjectDisposedException("MonitorWorkerClient");
                    if (process == null) Start();
                    channel = pipe;
                    input = reader;
                    output = writer;
                    pending = connection;
                }
                if (pending != null)
                {
                    using (WaitHandle wait = pending.AsyncWaitHandle)
                    {
                        if (!wait.WaitOne(3000)) throw new TimeoutException("Worker connection timed out.");
                        channel.EndWaitForConnection(pending);
                    }
                    output.AutoFlush = true;
                    lock (gate) { if (pipe == channel) connection = null; }
                }
                // All pipe I/O has a deadline, including requests to an unresponsive worker.
                Task send = output.WriteLineAsync(request);
                if (!send.Wait(1000)) throw new TimeoutException("Worker request timed out.");
                Task<string> receive = input.ReadLineAsync();
                if (!receive.Wait(2500)) throw new TimeoutException("GPU measurement timed out.");
                string response = receive.Result;
                if (response == null) throw new IOException("Worker disconnected.");
                return response;
            }
            catch
            {
                lock (gate) Stop();
                throw;
            }
        }

        private void Start()
        {
            string name = "SystemMonitorOverlay-" + Guid.NewGuid().ToString("N");
            PipeSecurity security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User,
                    PipeAccessRights.FullControl, AccessControlType.Allow));
            pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
            reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true);
            connection = pipe.BeginWaitForConnection(null, null);
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception();
            JobLimits limits = new JobLimits();
            limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(limits)))
                throw new Win32Exception();

            StartupInfo startup = new StartupInfo();
            startup.Size = Marshal.SizeOf(startup);
            startup.Flags = 0x80; // STARTF_FORCEOFFFEEDBACK
            ProcessInformation information;
            string executable = Application.ExecutablePath;
            StringBuilder command = new StringBuilder("\"" + executable + "\" --monitor-worker " + kind + " " + name);
            // Suspend before assigning the job. A child can never run outside parent ownership.
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false,
                0x08000004, IntPtr.Zero, Path.GetDirectoryName(executable), ref startup, out information))
                throw new Win32Exception();
            try
            {
                if (!AssignProcessToJobObject(job, information.ProcessHandle))
                {
                    TerminateProcess(information.ProcessHandle, 1);
                    throw new Win32Exception();
                }
                process = Process.GetProcessById((int)information.ProcessId);
                if (ResumeThread(information.ThreadHandle) == uint.MaxValue)
                    throw new Win32Exception();
            }
            finally
            {
                CloseHandle(information.ThreadHandle);
                CloseHandle(information.ProcessHandle);
            }
        }

        private void Stop()
        {
            // Closing the job kills a worker even if a native driver call never returns.
            if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
            if (pipe != null) { pipe.Dispose(); pipe = null; }
            if (process != null) { process.Dispose(); process = null; }
            reader = null;
            writer = null;
            connection = null;
        }

        public void Dispose()
        {
            lock (gate) { disposed = true; Stop(); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct JobLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public short Show, ReservedSize;
            public IntPtr ReservedData, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr ProcessHandle, ThreadHandle;
            public uint ProcessId, ThreadId;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int type, ref JobLimits limits, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(string application, StringBuilder command,
            IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags,
            IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation information);
    }

    internal static class MonitorWorker
    {
        internal static void Run(string kind, string name)
        {
            VramCounter counter = null;
            VramAdapterInfo[] adapters = new VramAdapterInfo[0];
            DateTime refresh = DateTime.MinValue;
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", name,
                    PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(3000);
                    using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false)))
                    using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false)))
                    {
                        writer.AutoFlush = true;
                        string request;
                        while ((request = reader.ReadLine()) != null)
                        {
                            try
                            {
#if TEST_WORKER
                                if (request == "HANG") { Thread.Sleep(Timeout.Infinite); }
                                if (request == "PID") { writer.WriteLine(Process.GetCurrentProcess().Id); continue; }
                                if (request == "ERROR") throw new IOException("Injected failure");
#endif
                                if (DateTime.UtcNow >= refresh)
                                {
                                    string error;
                                    if (!VramCapacityProbe.TryGetAdapters(out adapters, out error))
                                        throw new IOException(error);
                                    if (counter != null) { counter.Dispose(); counter = null; }
                                    refresh = DateTime.UtcNow.AddSeconds(30);
                                }
                                if (kind == "vram")
                                {
                                    if (counter == null) counter = new VramCounter();
                                    VramUsageSnapshot sample = counter.ReadUsage();
                                    VramAdapterInfo selected = FindAdapter(adapters, sample.ActiveLuidHigh, sample.ActiveLuidLow);
                                    if (selected == null) throw new IOException("GPU identity changed; retry required.");
                                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                        "OK|{0}|{1}|{2}|{3}|{4}", sample.ActiveBytes, selected.DedicatedBytes,
                                        selected.LuidHigh, selected.LuidLow, Convert.ToBase64String(Encoding.UTF8.GetBytes(selected.Name))));
                                }
                                else if (kind == "telemetry")
                                {
                                    string[] parts = request.Split('|');
                                    VramAdapterInfo selected = FindAdapter(adapters,
                                        int.Parse(parts[0], CultureInfo.InvariantCulture), uint.Parse(parts[1], CultureInfo.InvariantCulture));
                                    if (selected == null) throw new IOException("GPU identity changed; retry required.");
                                    GpuTelemetryReading reading;
                                    string error;
                                    if (!GpuTelemetryProvider.TryRead(selected, HasUniqueName(adapters, selected), out reading, out error))
                                        throw new IOException(DescribeAdapters(adapters, selected) + " | " + error);
                                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                        "OK|{0}|{1:F3}|{2}|{3}|{4}|{5}|{6}", reading.UtilizationPercent,
                                        reading.BoardPowerWatts, reading.TemperatureCelsius,
                                        reading.HasUtilization, reading.HasBoardPower, reading.HasTemperature,
                                        Convert.ToBase64String(Encoding.UTF8.GetBytes(DescribeAdapters(adapters, selected) + "\r\n" +
                                            (reading.Diagnostic ?? "Vendor sensor read completed.")))));
                                }
                                else throw new IOException("Unknown worker kind.");
                            }
                            catch (Exception ex)
                            {
                                if (counter != null) { counter.Dispose(); counter = null; }
                                if (kind == "telemetry") GpuTelemetryProvider.Reset();
                                refresh = DateTime.MinValue;
                                writer.WriteLine("ERROR|" + ex.Message.Replace('\r', ' ').Replace('\n', ' '));
                            }
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (TimeoutException) { }
            finally
            {
                if (counter != null) counter.Dispose();
                if (kind == "telemetry") GpuTelemetryProvider.Reset();
            }
        }

        private static VramAdapterInfo FindAdapter(VramAdapterInfo[] adapters, int high, uint low)
        {
            foreach (VramAdapterInfo adapter in adapters)
                if (adapter.LuidHigh == high && adapter.LuidLow == low) return adapter;
            return null;
        }

        internal static bool HasUniqueName(VramAdapterInfo[] adapters, VramAdapterInfo selected)
        {
            int matches = 0;
            foreach (VramAdapterInfo adapter in adapters)
                if (GpuTelemetryProvider.NormalizeGpuName(adapter.Name) == GpuTelemetryProvider.NormalizeGpuName(selected.Name)) matches++;
            return matches == 1;
        }

        private static string DescribeAdapters(VramAdapterInfo[] adapters, VramAdapterInfo selected)
        {
            StringBuilder text = new StringBuilder("DXGI adapters: ");
            foreach (VramAdapterInfo adapter in adapters)
                text.AppendFormat("{0}{1} (LUID={2}|{3}); ", adapter == selected ? "selected=" : "",
                    adapter.Name, adapter.LuidHigh, adapter.LuidLow);
            return text.ToString();
        }
    }

    internal static class TelemetryDiagnostics
    {
        private static string lastDetail;
        private static DateTime nextWriteUtc;

        internal static void Record(string detail)
        {
            // Keep only one small status file, and never write once per refresh interval.
            if (detail == lastDetail || DateTime.UtcNow < nextWriteUtc) return;
            try
            {
                if (detail.Length > 8192) detail = detail.Substring(0, 8192);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GPU-diagnostics.txt"),
                    "SYSTEM Monitor Overlay / GPU identity fix 2026-09-30\r\n" +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + detail + "\r\n",
                    new UTF8Encoding(false));
                lastDetail = detail;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            nextWriteUtc = DateTime.UtcNow.AddSeconds(30);
        }
    }
}
