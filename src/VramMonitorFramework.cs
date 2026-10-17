using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace VramMonitorPortable
{
    internal sealed class OverlayConfiguration
    {
        internal static bool SaveBlocked;
        internal bool Enabled = true;
        internal bool GpuPowerEnabled;
        internal bool GpuTemperatureEnabled;
        internal bool LightMode;
        internal bool OverlayOnLeft;
        internal decimal UpdateIntervalSeconds = 2m;
        internal int OverlayOpacityPercent = 100;
        internal int Thickness = 3;
        internal int Length = 150;
        internal int GpuTextSize = 16;
        internal int GpuTextRotationDegrees;
        internal int GpuTextSpacing = 3;
        internal int GpuTextOffsetX = 12;
        internal int GpuTextOffsetY = 12;
        internal Color RamColor = Color.FromArgb(255, 255, 0);
        internal Color GpuColor = Color.FromArgb(245, 190, 85);

        private static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.ini"); }
        }

        internal static OverlayConfiguration Load()
        {
            SaveBlocked = false;
            OverlayConfiguration configuration = new OverlayConfiguration();
            try
            {
                string[] source;
                try { source = File.ReadAllLines(ConfigPath); }
                catch (FileNotFoundException) { return configuration; }
                foreach (string rawLine in source)
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") ||
                        (line.StartsWith("[") && line.EndsWith("]"))) continue;
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    bool boolValue;
                    int intValue;
                    decimal decimalValue;
                    Color colorValue;
                    if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase) &&
                        bool.TryParse(value, out boolValue))
                        configuration.Enabled = boolValue;
                    else if (key.Equals("GpuPowerEnabled", StringComparison.OrdinalIgnoreCase) &&
                        bool.TryParse(value, out boolValue))
                        configuration.GpuPowerEnabled = boolValue;
                    else if (key.Equals("GpuTemperatureEnabled", StringComparison.OrdinalIgnoreCase) &&
                        bool.TryParse(value, out boolValue))
                        configuration.GpuTemperatureEnabled = boolValue;
                    else if (key.Equals("LightMode", StringComparison.OrdinalIgnoreCase) &&
                        bool.TryParse(value, out boolValue))
                        configuration.LightMode = boolValue;
                    else if (key.Equals("OverlayOnLeft", StringComparison.OrdinalIgnoreCase) &&
                        bool.TryParse(value, out boolValue))
                        configuration.OverlayOnLeft = boolValue;
                    else if (key.Equals("UpdateIntervalSeconds", StringComparison.OrdinalIgnoreCase) &&
                        decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture, out decimalValue))
                        configuration.UpdateIntervalSeconds = Math.Max(1m,
                            Math.Min(5m, Math.Round(decimalValue)));
                    else if (key.Equals("Thickness", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value, out intValue))
                        configuration.Thickness = Math.Max(1, Math.Min(40, intValue));
                    else if (key.Equals("Length", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value, out intValue))
                        configuration.Length = Math.Max(50, Math.Min(3000, intValue));
                    else if (key.Equals("OverlayOpacityPercent", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value, out intValue))
                        configuration.OverlayOpacityPercent = Math.Max(10, Math.Min(100, intValue));
                    else if (key.Equals("GpuTextSize", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value, out intValue))
                        configuration.GpuTextSize = Math.Max(8, Math.Min(72, intValue));
                    else if (key.Equals("GpuTextRotationDegrees", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value, out intValue))
                        configuration.GpuTextRotationDegrees = NormalizeTextRotation(intValue);
                    else if (key.Equals("GpuTextRotated", StringComparison.OrdinalIgnoreCase) &&
                        bool.TryParse(value, out boolValue))
                        configuration.GpuTextRotationDegrees = boolValue ? 90 : 0;
                    else if (key.Equals("GpuTextSpacing", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value, out intValue))
                        configuration.GpuTextSpacing = Math.Max(-20, Math.Min(20, intValue));
                    else if (key.Equals("GpuTextOffsetX", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value, out intValue))
                        configuration.GpuTextOffsetX = Math.Max(0, Math.Min(3000, intValue));
                    else if (key.Equals("GpuTextOffsetY", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value, out intValue))
                        configuration.GpuTextOffsetY = Math.Max(0, Math.Min(3000, intValue));
                    else if (key.Equals("RamColor", StringComparison.OrdinalIgnoreCase) &&
                        TryParseColor(value, out colorValue))
                        configuration.RamColor = colorValue;
                    else if (key.Equals("GpuColor", StringComparison.OrdinalIgnoreCase) &&
                        TryParseColor(value, out colorValue))
                        configuration.GpuColor = colorValue;
                }
            }
            catch
            {
                SaveBlocked = true;
                return new OverlayConfiguration();
            }
            return configuration;
        }

        internal void Save()
        {
            string[] lines =
            {
                "[Overlay]",
                "Enabled=" + Enabled,
                "GpuPowerEnabled=" + GpuPowerEnabled,
                "GpuTemperatureEnabled=" + GpuTemperatureEnabled,
                "LightMode=" + LightMode,
                "OverlayOnLeft=" + OverlayOnLeft,
                "UpdateIntervalSeconds=" + UpdateIntervalSeconds.ToString("0",
                    System.Globalization.CultureInfo.InvariantCulture),
                "OverlayOpacityPercent=" + OverlayOpacityPercent,
                "Thickness=" + Thickness,
                "Length=" + Length,
                "GpuTextSize=" + GpuTextSize,
                "GpuTextRotationDegrees=" + GpuTextRotationDegrees,
                "GpuTextSpacing=" + GpuTextSpacing,
                "GpuTextOffsetX=" + GpuTextOffsetX,
                "GpuTextOffsetY=" + GpuTextOffsetY,
                "RamColor=" + FormatColor(RamColor),
                "GpuColor=" + FormatColor(GpuColor)
            };
            if (SaveBlocked) throw new IOException("Config.ini could not be read; original file was preserved. Restart after fixing access.");
            string temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                        foreach (string line in lines) writer.WriteLine(line);
                    stream.Flush(true);
                }
                if (File.Exists(ConfigPath)) File.Replace(temporary, ConfigPath, ConfigPath + ".bak");
                else File.Move(temporary, ConfigPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static bool TryParseColor(string value, out Color color)
        {
            color = Color.Empty;
            string hex = value.StartsWith("#") ? value.Substring(1) : value;
            int rgb;
            if (hex.Length != 6 || !int.TryParse(hex,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out rgb)) return false;
            color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }

        private static string FormatColor(Color color)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static int NormalizeTextRotation(int degrees)
        {
            int normalized = ((degrees % 360) + 360) % 360;
            int snapped = ((normalized + 45) / 90 * 90) % 360;
            return snapped == 180 ? 0 : snapped;
        }
    }

    internal static class Program
    {
        private const string SingleInstanceMutexName =
            @"Local\SystemMonitorOverlay.SingleInstance.v1";
        private const string ShowExistingEventName =
            @"Local\SystemMonitorOverlay.ShowExisting.v1";

        private static void SignalExistingInstance()
        {
            try
            {
                using (EventWaitHandle showEvent =
                    EventWaitHandle.OpenExisting(ShowExistingEventName))
                    showEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--monitor-worker")
            {
                MonitorWorker.Run(args[1], args[2]);
                return;
            }
            if (args.Length == 2 && args[0] == "--capacity-test")
            {
                long bytes;
                string error;
                bool success = VramCapacityProbe.TryGetTotalDedicatedBytes(out bytes, out error);
                File.WriteAllText(args[1], success
                    ? string.Format("OK|{0}", bytes)
                    : string.Format("ERROR|{0}", error));
                return;
            }
            if (args.Length == 2 && args[0] == "--gpu-test")
            {
                VramAdapterInfo[] adapters;
                string error;
                if (!VramCapacityProbe.TryGetAdapters(out adapters, out error))
                {
                    File.WriteAllText(args[1], "ERROR|" + error);
                    return;
                }
                VramUsageSnapshot snapshot;
                using (VramCounter testCounter = new VramCounter())
                    snapshot = testCounter.ReadUsage();
                StringBuilder report = new StringBuilder();
                foreach (VramAdapterInfo adapter in adapters)
                {
                    bool active = adapter.LuidLow == snapshot.ActiveLuidLow &&
                        adapter.LuidHigh == snapshot.ActiveLuidHigh;
                    report.AppendLine(string.Format("{0}|{1}|0x{2:X8}|0x{3:X8}",
                        active ? "ACTIVE" : "ADAPTER", adapter.Name,
                        unchecked((uint)adapter.LuidHigh), adapter.LuidLow));
                }
                File.WriteAllText(args[1], report.ToString());
                return;
            }
            if (args.Length == 3 && args[0] == "--telemetry-test")
            {
                GpuTelemetryReading reading;
                string error;
                bool success = GpuTelemetryProvider.TryRead(args[2], out reading, out error);
                File.WriteAllText(args[1], success
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "OK|{0}|{1:F3}|{2}|{3}|{4}|{5}", reading.UtilizationPercent,
                        reading.BoardPowerWatts, reading.TemperatureCelsius,
                        reading.HasUtilization, reading.HasBoardPower, reading.HasTemperature)
                    : "ERROR|" + error);
                return;
            }

            bool createdNew;
            using (Mutex singleInstance = new Mutex(true, SingleInstanceMutexName,
                out createdNew))
            {
                if (!createdNew)
                {
                    bool startupLaunch = args.Length == 1 && args[0] == "--startup";
                    if (!startupLaunch) SignalExistingInstance();
                    return;
                }

                using (EventWaitHandle showEvent = new EventWaitHandle(false,
                    EventResetMode.AutoReset, ShowExistingEventName))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    VramForm form = new VramForm();
                    IntPtr formHandle = form.Handle;
                    RegisteredWaitHandle showRegistration =
                        ThreadPool.RegisterWaitForSingleObject(showEvent,
                            delegate(object state, bool timedOut)
                            {
                                if (form.IsDisposed) return;
                                try
                                {
                                    form.BeginInvoke(new MethodInvoker(
                                        form.RestoreFromTray));
                                }
                                catch (InvalidOperationException) { }
                            }, null, Timeout.Infinite, false);
                    if (args.Length == 1 && args[0] == "--auto-close")
                    {
                        form.Shown += delegate
                        {
                            System.Windows.Forms.Timer closeTimer =
                                new System.Windows.Forms.Timer();
                            closeTimer.Interval = 3000;
                            closeTimer.Tick += delegate
                            {
                                closeTimer.Stop();
                                closeTimer.Dispose();
                                form.ExitApplication();
                            };
                            closeTimer.Start();
                        };
                    }
                    try { Application.Run(form); }
                    finally { showRegistration.Unregister(null); }
                }
            }
        }
    }

    internal sealed class VramForm : Form
    {
        private readonly Label valueLabel;
        private readonly Label vramPercentLabel;
        private readonly Label telemetryLabel;
        private readonly Label gpuNameLabel;
        private readonly Label statusLabel;
        private readonly Label ramLabel;
        private readonly Label ramPercentLabel;
        private readonly ToggleSwitchControl startupToggle;
        private readonly ToggleSwitchControl overlayToggle;
        private readonly ToggleSwitchControl gpuPowerToggle;
        private readonly ToggleSwitchControl gpuTemperatureToggle;
        private readonly ToggleSwitchControl themeToggle;
        private readonly ToggleSwitchControl overlaySideToggle;
        private readonly ComboBox gpuTextRotationInput;
        private readonly NumericUpDown overlayThicknessInput;
        private readonly NumericUpDown overlayLengthInput;
        private readonly NumericUpDown gpuTextSizeInput;
        private readonly NumericUpDown gpuTextSpacingInput;
        private readonly NumericUpDown gpuTextOffsetXInput;
        private readonly NumericUpDown gpuTextOffsetYInput;
        private readonly NumericUpDown updateIntervalInput;
        private readonly NumericUpDown overlayOpacityInput;
        private readonly Button ramColorButton;
        private readonly Button gpuColorButton;
        private readonly Button closeButton;
        private readonly System.Threading.Timer readTimer;
        private readonly System.Threading.Timer telemetryTimer;
        private readonly System.Windows.Forms.Timer ramTimer;
        private readonly System.Windows.Forms.Timer fullscreenTimer;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private OverlayIndicatorForm overlayForm;
        private int latestVramPercent = -1;
        private int latestRamPercent = -1;
        private int latestGpuPowerWatts = -1;
        private int latestGpuTemperatureCelsius = -1;
        private volatile int updateIntervalMilliseconds = 2000;
        private readonly MonitorWorkerClient vramWorker = new MonitorWorkerClient("vram");
        private readonly MonitorWorkerClient telemetryWorker = new MonitorWorkerClient("telemetry");
        private readonly System.Windows.Forms.Timer configSaveTimer;
        private bool configDirty;
        private string configSaveError;
        private bool initializing = true;
        private int readInProgress;
        private int telemetryInProgress;
        private volatile bool closing;
        private bool behindFullscreen;
        private DateTime nextVramAttemptUtc = DateTime.MinValue;
        private volatile string activeGpuKey = "";
        private DateTime nextTelemetryAttemptUtc = DateTime.MinValue;

        public VramForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            OverlayConfiguration savedConfiguration = OverlayConfiguration.Load();
            Text = "SYSTEM Monitor Overlay";
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            // Create the settings window invisibly and start directly in the tray.
            // Opacity stays at zero until the first Show event has completed so the
            // native window cannot flash on screen during handle creation.
            ShowInTaskbar = false;
            ClientSize = new Size(340, 526);
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.White;
            Opacity = 0d;
            DoubleBuffered = true;

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 18, area.Top + 18);

            Label title = MakeLabel("SYSTEM Monitor Overlay", 16, 10, 210, 18,
                Color.FromArgb(160, 170, 185), 8.5f, FontStyle.Bold);
            themeToggle = new ToggleSwitchControl();
            themeToggle.Text = "ライト ↔ ダーク";
            themeToggle.Location = new Point(16, 29);
            themeToggle.Size = new Size(308, 24);
            // Left/off is Light, right/on is Dark to match the label order.
            themeToggle.Checked = !savedConfiguration.LightMode;
            themeToggle.CheckedChanged += ThemeToggleChanged;
            startupToggle = new ToggleSwitchControl();
            startupToggle.Text = "スタートアップに登録";
            startupToggle.Location = new Point(16, 56);
            startupToggle.Size = new Size(308, 24);
            startupToggle.Checked = IsStartupRegistered();
            startupToggle.CheckedChanged += StartupToggleChanged;
            overlayToggle = new ToggleSwitchControl();
            overlayToggle.Text = "オーバーレイ表示";
            overlayToggle.Location = new Point(16, 83);
            overlayToggle.Size = new Size(308, 24);
            overlayToggle.Checked = savedConfiguration.Enabled;
            overlayToggle.CheckedChanged += OverlayToggleChanged;
            Label updateIntervalLabel = MakeLabel("更新間隔", 22, 110, 66, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            updateIntervalInput = MakeNumericInput(90, 110, 54, 24, 1, 5, 2);
            updateIntervalInput.Minimum = 1m;
            updateIntervalInput.DecimalPlaces = 0;
            updateIntervalInput.Increment = 1m;
            updateIntervalInput.Value = savedConfiguration.UpdateIntervalSeconds;
            updateIntervalMilliseconds = (int)(updateIntervalInput.Value * 1000m);
            updateIntervalInput.ValueChanged += UpdateIntervalChanged;
            Label updateIntervalUnit = MakeLabel("秒", 147, 110, 22, 24,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);
            Label opacityLabel = MakeLabel("不透明度", 172, 110, 58, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            overlayOpacityInput = MakeNumericInput(232, 110, 54, 24, 10, 100,
                savedConfiguration.OverlayOpacityPercent);
            overlayOpacityInput.Increment = 5m;
            overlayOpacityInput.ValueChanged += OverlayOpacityChanged;
            Label opacityUnit = MakeLabel("%", 289, 110, 22, 24,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);
            overlaySideToggle = new ToggleSwitchControl();
            overlaySideToggle.Text = "左 ↔ 右";
            overlaySideToggle.Location = new Point(16, 137);
            overlaySideToggle.Size = new Size(308, 24);
            // Left/off displays on the left, right/on displays on the right.
            overlaySideToggle.Checked = !savedConfiguration.OverlayOnLeft;
            overlaySideToggle.CheckedChanged += OverlaySideToggleChanged;
            gpuPowerToggle = new ToggleSwitchControl();
            gpuPowerToggle.Text = "GPU電力";
            gpuPowerToggle.Location = new Point(16, 164);
            gpuPowerToggle.Size = new Size(308, 24);
            gpuPowerToggle.Checked = savedConfiguration.GpuPowerEnabled;
            gpuPowerToggle.CheckedChanged += AdditionalGpuMetricToggleChanged;
            gpuTemperatureToggle = new ToggleSwitchControl();
            gpuTemperatureToggle.Text = "GPU温度";
            gpuTemperatureToggle.Location = new Point(16, 191);
            gpuTemperatureToggle.Size = new Size(308, 24);
            gpuTemperatureToggle.Checked = savedConfiguration.GpuTemperatureEnabled;
            gpuTemperatureToggle.CheckedChanged += AdditionalGpuMetricToggleChanged;
            Label gpuTextSizeLabel = MakeLabel("文字サイズ", 22, 218, 66, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            gpuTextSizeInput = MakeNumericInput(90, 218, 54, 24, 8, 72,
                savedConfiguration.GpuTextSize);
            Label gpuTextSizeUnit = MakeLabel("px", 147, 218, 22, 24,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);
            Label gpuTextRotationLabel = MakeLabel("回転", 172, 218, 58, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            gpuTextRotationInput = new ComboBox();
            gpuTextRotationInput.Location = new Point(232, 218);
            gpuTextRotationInput.Size = new Size(82, 24);
            gpuTextRotationInput.DropDownStyle = ComboBoxStyle.DropDownList;
            gpuTextRotationInput.FlatStyle = FlatStyle.Flat;
            gpuTextRotationInput.BackColor = Color.FromArgb(35, 38, 46);
            gpuTextRotationInput.ForeColor = Color.White;
            gpuTextRotationInput.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            gpuTextRotationInput.Items.AddRange(new object[] { "0°", "90°", "270°" });
            gpuTextRotationInput.SelectedIndex = savedConfiguration.GpuTextRotationDegrees == 90
                ? 1 : savedConfiguration.GpuTextRotationDegrees == 270 ? 2 : 0;
            Label gpuTextPositionLabel = MakeLabel("文字 X", 22, 245, 66, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            gpuTextOffsetXInput = MakeNumericInput(90, 245, 54, 24, 0, 3000,
                savedConfiguration.GpuTextOffsetX);
            Label gpuTextXUnit = MakeLabel("px", 147, 245, 22, 24,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);
            Label gpuTextYLabel = MakeLabel("文字 Y", 172, 245, 58, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            gpuTextOffsetYInput = MakeNumericInput(232, 245, 54, 24, 0, 3000,
                savedConfiguration.GpuTextOffsetY);
            Label gpuTextPositionUnit = MakeLabel("px", 289, 245, 22, 24,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);
            Label gpuTextSpacingLabel = MakeLabel("文字間隔", 22, 272, 66, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            gpuTextSpacingInput = MakeNumericInput(90, 272, 54, 24, -20, 20,
                savedConfiguration.GpuTextSpacing);
            Label gpuTextSpacingUnit = MakeLabel("文字", 147, 272, 40, 24,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);
            gpuTextSizeInput.ValueChanged += GpuTextSettingsChanged;
            gpuTextSpacingInput.ValueChanged += GpuTextSettingsChanged;
            gpuTextOffsetXInput.ValueChanged += GpuTextSettingsChanged;
            gpuTextOffsetYInput.ValueChanged += GpuTextSettingsChanged;
            gpuTextRotationInput.SelectedIndexChanged += GpuTextSettingsChanged;
            Label gpuColorLabel = MakeLabel("GPU", 22, 300, 66, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            gpuColorButton = MakeColorButton(90, 302, savedConfiguration.GpuColor,
                "GPUとVRAMの色");
            Label ramColorLabel = MakeLabel("RAM", 172, 300, 58, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            ramColorButton = MakeColorButton(232, 302, savedConfiguration.RamColor,
                "RAMの色");
            gpuColorButton.Click += delegate { ChooseOverlayColor(gpuColorButton); };
            ramColorButton.Click += delegate { ChooseOverlayColor(ramColorButton); };
            Label thicknessLabel = MakeLabel("バー太さ", 22, 334, 66, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            overlayThicknessInput = MakeNumericInput(90, 334, 54, 24, 1, 40,
                savedConfiguration.Thickness);
            Label thicknessUnit = MakeLabel("px", 147, 334, 22, 24,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);
            Label lengthLabel = MakeLabel("バー長さ", 172, 334, 58, 24,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            overlayLengthInput = MakeNumericInput(232, 334, 54, 24, 50,
                3000, savedConfiguration.Length);
            Label lengthUnit = MakeLabel("px", 289, 334, 22, 24,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);
            overlayThicknessInput.ValueChanged += OverlaySettingsChanged;
            overlayLengthInput.ValueChanged += OverlaySettingsChanged;
            telemetryLabel = new TelemetryLabel();
            telemetryLabel.Text = "GPU  --%  BRD   --W  TMP  --℃";
            telemetryLabel.SetBounds(16, 365, 308, 26);
            telemetryLabel.ForeColor = savedConfiguration.GpuColor;
            telemetryLabel.Font = ModernTypography.Create(11f, true, GraphicsUnit.Point);
            telemetryLabel.TextAlign = ContentAlignment.MiddleRight;
            valueLabel = MakeLabel("VRAM  Reading...", 16, 392, 238, 34,
                savedConfiguration.GpuColor, 18f, FontStyle.Bold);
            vramPercentLabel = MakeLabel("--%", 260, 392, 64, 34,
                savedConfiguration.GpuColor, 18f, FontStyle.Bold);
            vramPercentLabel.TextAlign = ContentAlignment.MiddleRight;
            gpuNameLabel = MakeLabel("GPU   Detecting...", 16, 424, 308, 18,
                Color.FromArgb(145, 155, 170), 8.5f, FontStyle.Regular);
            ramLabel = MakeLabel("RAM   Reading...", 16, 446, 238, 34,
                savedConfiguration.RamColor, 18f, FontStyle.Bold);
            ramPercentLabel = MakeLabel("--%", 260, 446, 64, 34,
                savedConfiguration.RamColor, 18f, FontStyle.Bold);
            ramPercentLabel.TextAlign = ContentAlignment.MiddleRight;
            statusLabel = MakeLabel("Updates every " + FormatUpdateInterval() + " seconds",
                16, 498, 305, 18,
                Color.FromArgb(125, 135, 150), 8f, FontStyle.Regular);

            closeButton = new Button();
            closeButton.Text = "x";
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.Size = new Size(30, 28);
            closeButton.Location = new Point(308, 2);
            closeButton.ForeColor = Color.FromArgb(150, 160, 175);
            closeButton.BackColor = BackColor;
            closeButton.TabStop = false;
            closeButton.Cursor = Cursors.Hand;
            closeButton.Click += delegate { HideToTray(); };

            trayMenu = new ContextMenuStrip();
            ToolStripMenuItem showMenuItem = new ToolStripMenuItem("表示");
            showMenuItem.Font = new Font(showMenuItem.Font, FontStyle.Bold);
            showMenuItem.Click += delegate { RestoreFromTray(); };
            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("終了");
            exitMenuItem.Click += delegate { ExitApplication(); };
            trayMenu.Items.Add(showMenuItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitMenuItem);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = Icon ?? SystemIcons.Application;
            trayIcon.Text = "SYSTEM Monitor Overlay";
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };

            Controls.AddRange(new Control[] { title, themeToggle, startupToggle, overlayToggle,
                updateIntervalLabel, updateIntervalInput, updateIntervalUnit,
                opacityLabel, overlayOpacityInput, opacityUnit,
                overlaySideToggle,
                gpuPowerToggle, gpuTemperatureToggle,
                gpuTextSizeLabel, gpuTextSizeInput, gpuTextSizeUnit,
                gpuTextRotationLabel, gpuTextRotationInput,
                gpuTextPositionLabel, gpuTextOffsetXInput, gpuTextXUnit,
                gpuTextYLabel, gpuTextOffsetYInput, gpuTextPositionUnit,
                gpuTextSpacingLabel, gpuTextSpacingInput, gpuTextSpacingUnit,
                gpuColorLabel, gpuColorButton, ramColorLabel, ramColorButton,
                thicknessLabel, overlayThicknessInput, thicknessUnit,
                lengthLabel, overlayLengthInput, lengthUnit,
                telemetryLabel, valueLabel, vramPercentLabel,
                gpuNameLabel, statusLabel, ramLabel, ramPercentLabel, closeButton });

            ApplyTheme();
            configSaveTimer = new System.Windows.Forms.Timer();
            configSaveTimer.Interval = 400;
            configSaveTimer.Tick += delegate { FlushOverlayConfiguration(); };
            initializing = false;

            MouseEventHandler dragHandler = delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0x00A1, 0x0002, 0);
            };
            MouseDown += dragHandler;
            title.MouseDown += dragHandler;
            telemetryLabel.MouseDown += dragHandler;
            valueLabel.MouseDown += dragHandler;
            vramPercentLabel.MouseDown += dragHandler;
            gpuNameLabel.MouseDown += dragHandler;
            statusLabel.MouseDown += dragHandler;
            ramLabel.MouseDown += dragHandler;
            ramPercentLabel.MouseDown += dragHandler;

            readTimer = new System.Threading.Timer(ReadOnWorkerThread, null,
                Timeout.Infinite, Timeout.Infinite);
            telemetryTimer = new System.Threading.Timer(ReadTelemetryOnWorkerThread, null,
                Timeout.Infinite, Timeout.Infinite);
            ramTimer = new System.Windows.Forms.Timer();
            ramTimer.Interval = updateIntervalMilliseconds;
            ramTimer.Tick += delegate { UpdateRamOnly(); };
            fullscreenTimer = new System.Windows.Forms.Timer();
            fullscreenTimer.Interval = 500;
            fullscreenTimer.Tick += delegate { UpdateFullscreenZOrder(); };
            Shown += delegate
            {
                UpdateRamOnly();
                if (overlayToggle.Checked) ShowOverlay();
                if (OverlayConfiguration.SaveBlocked)
                    statusLabel.Text = "Config.ini read failed; automatic saving disabled";
                ramTimer.Start();
                fullscreenTimer.Start();
                readTimer.Change(0, updateIntervalMilliseconds);
                telemetryTimer.Change(Math.Min(500, updateIntervalMilliseconds),
                    updateIntervalMilliseconds);
                Hide();
                Opacity = 1d;
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!closing && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }
                PrepareForExit();
            };
            ResumeLayout(false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.FromArgb(55, 62, 74)))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "VramMonitorPortable";

        private static string StartupCommand
        {
            get { return "\"" + Application.ExecutablePath + "\" --startup"; }
        }

        private static bool IsStartupRegistered()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
                {
                    if (key == null) return false;
                    foreach (string valueName in key.GetValueNames())
                        if (IsApplicationStartupCommand(key.GetValue(valueName) as string)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsApplicationStartupCommand(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string command = value.Trim();
            const string startupArgument = "--startup";
            if (!command.EndsWith(startupArgument, StringComparison.OrdinalIgnoreCase))
                return false;

            string executable = command.Substring(0,
                command.Length - startupArgument.Length).Trim();
            if (executable.Length >= 2 && executable[0] == '"' &&
                executable[executable.Length - 1] == '"')
                executable = executable.Substring(1, executable.Length - 2);

            string fileName;
            try { fileName = Path.GetFileName(executable); }
            catch { return false; }
            return string.Equals(fileName, "SYSTEM Monitor Overlay.exe",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "VramMonitor-Portable.exe",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static int RemoveAllStartupRegistrations(RegistryKey key, bool keepCurrent)
        {
            int removed = 0;
            foreach (string valueName in key.GetValueNames())
            {
                string command = key.GetValue(valueName) as string;
                if (!IsApplicationStartupCommand(command)) continue;
                if (keepCurrent && valueName.Equals(StartupValueName, StringComparison.OrdinalIgnoreCase)) continue;
                key.DeleteValue(valueName, false);
                removed++;
            }
            return removed;
        }

        private void StartupToggleChanged(object sender, EventArgs e)
        {
            bool requested = startupToggle.Checked;
            try
            {
                int removed = 0;
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
                {
                    if (key == null) throw new InvalidOperationException("Startup registry is unavailable.");
                    // Commit the new registration first; failure must not remove the old one.
                    if (requested)
                        key.SetValue(StartupValueName, StartupCommand, RegistryValueKind.String);
                    removed = RemoveAllStartupRegistrations(key, requested);
                }
                statusLabel.Text = requested
                    ? "Windows startup enabled"
                    : "Windows startup disabled (" + removed + " removed)";
            }
            catch (Exception ex)
            {
                startupToggle.CheckedChanged -= StartupToggleChanged;
                startupToggle.Checked = !requested;
                startupToggle.CheckedChanged += StartupToggleChanged;
                statusLabel.Text = "Startup setting failed: " + ex.Message;
            }
        }
        private static Label MakeLabel(string text, int x, int y, int width, int height,
            Color color, float fontSize, FontStyle style)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.ForeColor = color;
            label.Font = ModernTypography.Create(fontSize, (style & FontStyle.Bold) != 0, GraphicsUnit.Point);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static NumericUpDown MakeNumericInput(int x, int y, int width, int height,
            int minimum, int maximum, int value)
        {
            NumericUpDown input = new NumericUpDown();
            input.Location = new Point(x, y);
            input.Size = new Size(width, height);
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.Value = value;
            input.DecimalPlaces = 0;
            input.TextAlign = HorizontalAlignment.Right;
            input.BorderStyle = BorderStyle.FixedSingle;
            input.BackColor = Color.FromArgb(35, 38, 46);
            input.ForeColor = Color.White;
            input.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            return input;
        }

        private static Button MakeColorButton(int x, int y, Color color, string accessibleName)
        {
            Button button = new Button();
            button.Location = new Point(x, y);
            button.Size = new Size(54, 20);
            button.BackColor = color;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(105, 112, 126);
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.AccessibleName = accessibleName;
            return button;
        }

        private void ChooseOverlayColor(Button button)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = button.BackColor;
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                button.BackColor = dialog.Color;
                UpdateOverlayColors();
                SaveOverlayConfiguration();
            }
        }

        private void UpdateOverlayColors()
        {
            valueLabel.ForeColor = gpuColorButton.BackColor;
            vramPercentLabel.ForeColor = gpuColorButton.BackColor;
            ramLabel.ForeColor = ramColorButton.BackColor;
            ramPercentLabel.ForeColor = ramColorButton.BackColor;
            telemetryLabel.ForeColor = gpuColorButton.BackColor;
            if (overlayForm != null && !overlayForm.IsDisposed)
                overlayForm.SetColors(gpuColorButton.BackColor, ramColorButton.BackColor);
        }

        private void ReadOnWorkerThread(object state)
        {
            if (closing || DateTime.UtcNow < nextVramAttemptUtc ||
                Interlocked.Exchange(ref readInProgress, 1) != 0) return;
            try
            {
                if (closing) return;
                string response = vramWorker.Request("READ");
                string[] fields = response.Split('|');
                if (fields.Length != 6 || fields[0] != "OK") throw new IOException(response);
                long bytes = long.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
                long capacity = long.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture);
                if (bytes < 0 || capacity <= 0) throw new IOException("Invalid VRAM measurement.");
                string key = fields[3] + "|" + fields[4];
                string name = Encoding.UTF8.GetString(Convert.FromBase64String(fields[5]));
                int percent = Math.Max(0, Math.Min(100, (int)Math.Round(bytes / (double)capacity * 100d)));
                nextVramAttemptUtc = DateTime.MinValue;
                PostUpdate(string.Format("VRAM  {0:N2} GiB", bytes / 1073741824d),
                    percent + "%", "GPU   " + name,
                    string.Format("Updated {0:HH:mm:ss}  |  every {1} seconds", DateTime.Now, FormatUpdateInterval()),
                    percent, false, key);
            }
            catch (Exception ex)
            {
                nextVramAttemptUtc = DateTime.UtcNow.AddSeconds(10);
                PostUpdate("VRAM  Unavailable", "--%", "GPU   Unavailable",
                    ex.Message + " | retry in 10s", -1, true, "");
            }
            finally { Interlocked.Exchange(ref readInProgress, 0); }
        }

        private void UpdateRamOnly()
        {
            RamReading ram = GetRamUsage();
            ramLabel.Text = ram.Text;
            ramPercentLabel.Text = ram.Percent;
            latestRamPercent = ram.PercentValue;
            UpdateOverlayUsage();
        }

        private void UpdateFullscreenZOrder()
        {
            if (overlayForm != null && !overlayForm.IsDisposed)
                overlayForm.RefreshDisplayBounds();
            bool fullscreen = IsPrimaryMonitorCoveredByForegroundWindow();
            if (fullscreen == behindFullscreen) return;
            behindFullscreen = fullscreen;
            TopMost = !fullscreen;
            if (fullscreen) SendToBack();
            if (overlayForm != null && !overlayForm.IsDisposed)
                overlayForm.SetBehindFullscreen(fullscreen);
        }

        private bool IsPrimaryMonitorCoveredByForegroundWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Handle || IsIconic(foreground))
                return false;
            uint processId;
            GetWindowThreadProcessId(foreground, out processId);
            if (processId == (uint)Process.GetCurrentProcess().Id) return false;

            StringBuilder className = new StringBuilder(64);
            GetClassName(foreground, className, className.Capacity);
            string windowClass = className.ToString();
            if (windowClass == "Progman" || windowClass == "WorkerW" ||
                windowClass == "Shell_TrayWnd") return false;

            NativeRect windowRect;
            if (!GetWindowRect(foreground, out windowRect)) return false;
            IntPtr monitor = MonitorFromWindow(foreground, 2); // MONITOR_DEFAULTTONEAREST
            MonitorInfo monitorInfo = new MonitorInfo();
            monitorInfo.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo)) return false;
            Rectangle primary = Screen.PrimaryScreen.Bounds;
            if (monitorInfo.Monitor.Left != primary.Left || monitorInfo.Monitor.Top != primary.Top ||
                monitorInfo.Monitor.Right != primary.Right || monitorInfo.Monitor.Bottom != primary.Bottom)
                return false;
            const int tolerance = 2;
            return windowRect.Left <= monitorInfo.Monitor.Left + tolerance &&
                windowRect.Top <= monitorInfo.Monitor.Top + tolerance &&
                windowRect.Right >= monitorInfo.Monitor.Right - tolerance &&
                windowRect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
        }

        private void OverlayToggleChanged(object sender, EventArgs e)
        {
            if (overlayToggle.Checked)
                ShowOverlay();
            else if (overlayForm != null)
            {
                overlayForm.Close();
                overlayForm.Dispose();
                overlayForm = null;
            }
            SaveOverlayConfiguration();
        }

        private void AdditionalGpuMetricToggleChanged(object sender, EventArgs e)
        {
            UpdateOverlayGpuTextOptions();
            SaveOverlayConfiguration();
        }

        private void GpuTextSettingsChanged(object sender, EventArgs e)
        {
            UpdateOverlayGpuTextOptions();
            SaveOverlayConfiguration();
        }

        private void UpdateOverlayGpuTextOptions()
        {
            if (overlayForm == null || overlayForm.IsDisposed) return;
            overlayForm.SetGpuTextOptions(gpuPowerToggle.Checked,
                gpuTemperatureToggle.Checked, (int)gpuTextSizeInput.Value,
                SelectedGpuTextRotationDegrees(), (int)gpuTextSpacingInput.Value,
                (int)gpuTextOffsetXInput.Value, (int)gpuTextOffsetYInput.Value);
        }

        private int SelectedGpuTextRotationDegrees()
        {
            return gpuTextRotationInput.SelectedIndex == 1 ? 90 :
                gpuTextRotationInput.SelectedIndex == 2 ? 270 : 0;
        }

        private void OverlaySideToggleChanged(object sender, EventArgs e)
        {
            if (overlayForm != null && !overlayForm.IsDisposed)
                overlayForm.SetAnchorLeft(!overlaySideToggle.Checked);
            SaveOverlayConfiguration();
        }

        private void ThemeToggleChanged(object sender, EventArgs e)
        {
            ApplyTheme();
            SaveOverlayConfiguration();
        }

        private void ApplyTheme()
        {
            bool light = !themeToggle.Checked;
            BackColor = light ? Color.FromArgb(244, 246, 250) : Color.FromArgb(24, 26, 32);
            ForeColor = light ? Color.FromArgb(30, 34, 42) : Color.White;
            Color secondary = light
                ? Color.FromArgb(72, 80, 94)
                : Color.FromArgb(145, 155, 170);
            foreach (Control control in Controls)
            {
                ToggleSwitchControl toggle = control as ToggleSwitchControl;
                if (toggle != null)
                {
                    toggle.ForeColor = light
                        ? Color.FromArgb(42, 48, 58)
                        : Color.FromArgb(185, 193, 207);
                    toggle.Invalidate();
                    continue;
                }
                NumericUpDown numeric = control as NumericUpDown;
                if (numeric != null)
                {
                    numeric.BackColor = light ? Color.White : Color.FromArgb(35, 38, 46);
                    numeric.ForeColor = light ? Color.FromArgb(30, 34, 42) : Color.White;
                    continue;
                }
                ComboBox combo = control as ComboBox;
                if (combo != null)
                {
                    combo.BackColor = light ? Color.White : Color.FromArgb(35, 38, 46);
                    combo.ForeColor = light ? Color.FromArgb(30, 34, 42) : Color.White;
                    continue;
                }
                Label label = control as Label;
                if (label != null && label != valueLabel && label != vramPercentLabel &&
                    label != ramLabel && label != ramPercentLabel && label != telemetryLabel)
                    label.ForeColor = secondary;
            }
            closeButton.BackColor = BackColor;
            closeButton.ForeColor = secondary;
            UpdateOverlayColors();
            Invalidate();
        }

        private void OverlayOpacityChanged(object sender, EventArgs e)
        {
            if (overlayForm != null && !overlayForm.IsDisposed)
                overlayForm.SetOpacityPercent((int)overlayOpacityInput.Value);
            SaveOverlayConfiguration();
        }

        private void UpdateIntervalChanged(object sender, EventArgs e)
        {
            updateIntervalMilliseconds = (int)(updateIntervalInput.Value * 1000m);
            if (!closing)
            {
                ramTimer.Interval = updateIntervalMilliseconds;
                readTimer.Change(0, updateIntervalMilliseconds);
                telemetryTimer.Change(0, updateIntervalMilliseconds);
                statusLabel.Text = "Updates every " + FormatUpdateInterval() + " seconds";
            }
            SaveOverlayConfiguration();
        }

        private string FormatUpdateInterval()
        {
            return (updateIntervalMilliseconds / 1000d).ToString("0",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private void ShowOverlay()
        {
            if (overlayForm == null || overlayForm.IsDisposed)
                overlayForm = new OverlayIndicatorForm();
            overlayForm.SetGeometry((int)overlayThicknessInput.Value,
                (int)overlayLengthInput.Value);
            overlayForm.SetColors(gpuColorButton.BackColor, ramColorButton.BackColor);
            overlayForm.SetOpacityPercent((int)overlayOpacityInput.Value);
            overlayForm.SetAnchorLeft(!overlaySideToggle.Checked);
            UpdateOverlayGpuTextOptions();
            overlayForm.SetGpuMetrics(latestGpuPowerWatts, latestGpuTemperatureCelsius);
            overlayForm.SetUsage(latestVramPercent, latestRamPercent);
            overlayForm.Show();
            overlayForm.SetBehindFullscreen(behindFullscreen);
        }

        private void OverlaySettingsChanged(object sender, EventArgs e)
        {
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                overlayForm.SetGeometry((int)overlayThicknessInput.Value,
                    (int)overlayLengthInput.Value);
            }
            SaveOverlayConfiguration();
        }

        private void SaveOverlayConfiguration()
        {
            if (initializing || closing) return;
            configDirty = true;
            configSaveTimer.Stop();
            configSaveTimer.Start();
        }

        private void FlushOverlayConfiguration()
        {
            configSaveTimer.Stop();
            if (!configDirty) return;
            try
            {
                OverlayConfiguration configuration = new OverlayConfiguration();
                configuration.Enabled = overlayToggle.Checked;
                configuration.GpuPowerEnabled = gpuPowerToggle.Checked;
                configuration.GpuTemperatureEnabled = gpuTemperatureToggle.Checked;
                configuration.GpuTextSize = (int)gpuTextSizeInput.Value;
                configuration.GpuTextRotationDegrees = SelectedGpuTextRotationDegrees();
                configuration.GpuTextSpacing = (int)gpuTextSpacingInput.Value;
                configuration.GpuTextOffsetX = (int)gpuTextOffsetXInput.Value;
                configuration.GpuTextOffsetY = (int)gpuTextOffsetYInput.Value;
                configuration.LightMode = !themeToggle.Checked;
                configuration.OverlayOnLeft = !overlaySideToggle.Checked;
                configuration.UpdateIntervalSeconds = updateIntervalInput.Value;
                configuration.OverlayOpacityPercent = (int)overlayOpacityInput.Value;
                configuration.Thickness = (int)overlayThicknessInput.Value;
                configuration.Length = (int)overlayLengthInput.Value;
                configuration.RamColor = ramColorButton.BackColor;
                configuration.GpuColor = gpuColorButton.BackColor;
                configuration.Save();
                configDirty = false;
                configSaveError = null;
            }
            catch (Exception ex)
            {
                configSaveError = "Config save failed: " + ex.Message;
                statusLabel.Text = configSaveError;
            }
        }

        private void UpdateOverlayUsage()
        {
            if (overlayForm != null && !overlayForm.IsDisposed)
                overlayForm.SetUsage(latestVramPercent, latestRamPercent);
        }

        private void ReadTelemetryOnWorkerThread(object state)
        {
            if (closing || DateTime.UtcNow < nextTelemetryAttemptUtc ||
                Interlocked.Exchange(ref telemetryInProgress, 1) != 0) return;
            try
            {
                string gpuKey = activeGpuKey;
                if (string.IsNullOrEmpty(gpuKey)) return;
                string text;
                int utilization;
                int powerWatts;
                int temperatureCelsius;
                if (TryReadTelemetryIsolated(gpuKey, out text, out utilization,
                    out powerWatts, out temperatureCelsius))
                {
                    nextTelemetryAttemptUtc = DateTime.MinValue;
                    PostTelemetry(text, utilization, powerWatts, temperatureCelsius, gpuKey);
                }
                else
                {
                    nextTelemetryAttemptUtc = DateTime.UtcNow.AddSeconds(10);
                    PostTelemetry("GPU  --%  BRD   --W  TMP  --℃", -1, -1, -1, gpuKey);
                }
            }
            finally
            {
                Interlocked.Exchange(ref telemetryInProgress, 0);
            }
        }

        private bool TryReadTelemetryIsolated(string gpuKey, out string displayText,
            out int utilizationPercent, out int powerWatts, out int temperatureCelsius)
        {
            displayText = null;
            utilizationPercent = -1;
            powerWatts = -1;
            temperatureCelsius = -1;
            try
            {
                string response = telemetryWorker.Request(gpuKey);
                string[] parts = response.Split('|');
                if (parts.Length < 7 || parts[0] != "OK")
                {
                    TelemetryDiagnostics.Record("GPU LUID=" + gpuKey + "\r\n" + response);
                    return false;
                }
                int utilization;
                double power;
                int temperature;
                bool hasUtilization;
                bool hasPower;
                bool hasTemperature;
                if (!int.TryParse(parts[1], out utilization) ||
                    !double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out power) ||
                    !int.TryParse(parts[3], out temperature) ||
                    !bool.TryParse(parts[4], out hasUtilization) ||
                    !bool.TryParse(parts[5], out hasPower) ||
                    !bool.TryParse(parts[6], out hasTemperature)) return false;
                string diagnostic = parts.Length >= 8
                    ? Encoding.UTF8.GetString(Convert.FromBase64String(parts[7])) : "No vendor diagnostics.";
                TelemetryDiagnostics.Record("GPU LUID=" + gpuKey + "\r\n" + diagnostic +
                    "\r\nAvailable: GPU=" + hasUtilization + "; power=" + hasPower + "; temperature=" + hasTemperature);
                string utilText = hasUtilization ? utilization.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) : "--";
                string powerText = hasPower ? Math.Round(power).ToString("0",
                    System.Globalization.CultureInfo.InvariantCulture) : "--";
                string temperatureText = hasTemperature ? temperature.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) : "--";
                displayText = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "GPU {0,3}%  BRD {1,4}W  TMP {2,3}℃",
                    utilText, powerText, temperatureText);
                if (hasUtilization)
                    utilizationPercent = Math.Max(0, Math.Min(100, utilization));
                if (hasPower) powerWatts = Math.Max(0, (int)Math.Round(power));
                if (hasTemperature) temperatureCelsius = Math.Max(0, temperature);
                return hasUtilization || hasPower || hasTemperature;
            }
            catch (Exception ex)
            {
                TelemetryDiagnostics.Record("GPU LUID=" + gpuKey + "\r\n" + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private void PostTelemetry(string text, int utilizationPercent, int powerWatts,
            int temperatureCelsius, string gpuKey)
        {
            if (closing || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (!closing && activeGpuKey == gpuKey)
                    {
                        telemetryLabel.Text = text;
                        latestGpuPowerWatts = powerWatts;
                        latestGpuTemperatureCelsius = temperatureCelsius;
                        if (overlayForm != null && !overlayForm.IsDisposed)
                            overlayForm.SetGpuMetrics(powerWatts, temperatureCelsius);
                    }
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void PostUpdate(string value, string percent, string name,
            string status, int percentValue, bool error, string gpuKey)
        {
            if (closing || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (closing) return;
                    if (activeGpuKey != gpuKey)
                    {
                        activeGpuKey = gpuKey;
                        latestGpuPowerWatts = latestGpuTemperatureCelsius = -1;
                        telemetryLabel.Text = "GPU  --%  BRD   --W  TMP  --℃";
                        if (overlayForm != null && !overlayForm.IsDisposed) overlayForm.SetGpuMetrics(-1, -1);
                    }
                    valueLabel.Text = value;
                    vramPercentLabel.Text = percent;
                    gpuNameLabel.Text = name;
                    if (!OverlayConfiguration.SaveBlocked) statusLabel.Text = configSaveError ?? status;
                    latestVramPercent = percentValue;
                    UpdateOverlayUsage();
                    Color color = error ? Color.FromArgb(245, 110, 115) : gpuColorButton.BackColor;
                    valueLabel.ForeColor = color;
                    vramPercentLabel.ForeColor = color;
                }));
            }
            catch (InvalidOperationException) { }
        }

        private static RamReading GetRamUsage()
        {
            MemoryStatus memory = new MemoryStatus();
            memory.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            if (!GlobalMemoryStatusEx(ref memory))
                return new RamReading("RAM   Unavailable", "--%", -1);
            ulong used = memory.TotalPhysical - memory.AvailablePhysical;
            return new RamReading(
                string.Format("RAM   {0:N2} GiB", used / 1024d / 1024d / 1024d),
                string.Format("{0}%", memory.MemoryLoad), (int)memory.MemoryLoad);
        }

        private void HideToTray()
        {
            if (closing || !Visible) return;
            // Hide first. Changing ShowInTaskbar recreates the native window handle;
            // doing that while visible can briefly expose an unpainted form.
            Hide();
            ShowInTaskbar = false;
        }

        internal void RestoreFromTray()
        {
            if (closing) return;
            ShowInTaskbar = true;
            Opacity = 1d;
            Show();
            WindowState = FormWindowState.Normal;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 18, area.Top + 18);
            Activate();
            BringToFront();
        }

        internal void ExitApplication()
        {
            if (closing) return;
            PrepareForExit();
            Close();
        }

        private void PrepareForExit()
        {
            if (closing) return;
            closing = true;
            FlushOverlayConfiguration();
            configSaveTimer.Dispose();
            vramWorker.Dispose();
            telemetryWorker.Dispose();
            if (overlayForm != null)
            {
                overlayForm.Close();
                overlayForm.Dispose();
                overlayForm = null;
            }
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
            try
            {
                readTimer.Change(Timeout.Infinite, Timeout.Infinite);
                readTimer.Dispose();
            }
            catch (ObjectDisposedException) { }
            try
            {
                telemetryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                telemetryTimer.Dispose();
            }
            catch (ObjectDisposedException) { }
            ramTimer.Stop();
            ramTimer.Dispose();
            fullscreenTimer.Stop();
            fullscreenTimer.Dispose();
        }

        private sealed class RamReading
        {
            public readonly string Text;
            public readonly string Percent;
            public readonly int PercentValue;
            public RamReading(string text, string percent, int percentValue)
            {
                Text = text;
                Percent = percent;
                PercentValue = percentValue;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect WorkArea;
            public uint Flags;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, int parameter, int data);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
    }

    internal sealed class ToggleSwitchControl : Control
    {
        private bool isChecked;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                EventHandler handler = CheckedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public ToggleSwitchControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.FromArgb(185, 193, 207);
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            AccessibleName = "";
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Text;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle textBounds = new Rectangle(0, 0, Math.Max(0, Width - 58), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            const int switchWidth = 44;
            const int switchHeight = 22;
            int switchX = Width - switchWidth;
            int switchY = (Height - switchHeight) / 2;
            Color trackColor = Checked
                ? Color.FromArgb(45, 190, 120)
                : Color.FromArgb(72, 78, 90);
            using (Brush track = new SolidBrush(trackColor))
            {
                e.Graphics.FillEllipse(track, switchX, switchY, switchHeight, switchHeight);
                e.Graphics.FillEllipse(track, switchX + switchWidth - switchHeight, switchY,
                    switchHeight, switchHeight);
                e.Graphics.FillRectangle(track, switchX + switchHeight / 2, switchY,
                    switchWidth - switchHeight, switchHeight);
            }

            const int knobSize = 16;
            int knobX = Checked ? switchX + switchWidth - knobSize - 3 : switchX + 3;
            int knobY = switchY + 3;
            using (Brush knob = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(knob, knobX, knobY, knobSize, knobSize);

        }
    }

    internal sealed class OverlayIndicatorForm : Form
    {
        private const int MarkerGap = 14;
        private Rectangle displayBounds;
        private int renderBarThickness, renderLength, renderMarkerGap;
        private int renderTextSize, renderOffsetX, renderOffsetY;
        internal double ResolutionScale { get; private set; }
        private int barThickness = 3;
        private int maximumLength = 150;
        private bool showGpuPowerText;
        private bool showGpuTemperatureText;
        private int gpuTextSize = 16;
        private int gpuTextRotationDegrees;
        private int gpuTextSpacing = 3;
        private int gpuTextOffsetX = 12;
        private int gpuTextOffsetY = 12;
        private int gpuPowerWatts = -1;
        private int gpuTemperatureCelsius = -1;
        private int vramPercent;
        private int ramPercent;
        private bool anchorLeft;
        private Color vramColor = Color.FromArgb(255, 47, 52);
        private Color ramColor = Color.FromArgb(255, 255, 0);
        private Color gpuColor = Color.FromArgb(245, 190, 85);
        private byte overlayAlpha = 255;
        private bool renderQueued;
        private OverlayMetricLayout metricLayout;
        private string metricLayoutKey;
        private Bitmap cachedText;
        private string cachedTextKey;
        internal bool LastPresentationSucceeded { get; private set; }

        public OverlayIndicatorForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; // Resolution scaling is applied explicitly, once.
            DoubleBuffered = true;
            UpdateDisplayBounds(Screen.PrimaryScreen.Bounds);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00080000; // WS_EX_LAYERED: per-pixel alpha
                parameters.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT: pass mouse input through
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: keep out of Alt+Tab
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return parameters;
            }
        }

        public void SetUsage(int vram, int ram)
        {
            int nextVram = ClampPercent(vram), nextRam = ClampPercent(ram);
            if (vramPercent == nextVram && ramPercent == nextRam) return;
            vramPercent = nextVram;
            ramPercent = nextRam;
            RequestRender();
        }

        public void SetGeometry(int thickness, int length)
        {
            // Keep configured values in Full-HD reference pixels, never scaled values.
            barThickness = Math.Max(1, Math.Min(40, thickness));
            maximumLength = Math.Max(50, Math.Min(3000, length));
            RecalculateLayout();
        }

        internal static double ScaleForDisplay(Size size)
        {
            // Short-edge scaling also handles ultrawide and portrait displays.
            // Do not make small displays smaller; bound allocations above 8K.
            return Math.Max(1d, Math.Min(4d, Math.Min(size.Width, size.Height) / 1080d));
        }

        private int ScalePixels(int value)
        {
            return (int)Math.Round(value * ResolutionScale, MidpointRounding.AwayFromZero);
        }

        internal void RefreshDisplayBounds()
        {
            Screen primary = Screen.PrimaryScreen;
            if (primary != null) UpdateDisplayBounds(primary.Bounds);
        }

        internal void UpdateDisplayBounds(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds == displayBounds) return;
            displayBounds = bounds;
            ResolutionScale = ScaleForDisplay(bounds.Size);
            RecalculateLayout();
        }

        private void RecalculateLayout()
        {
            if (displayBounds.Width <= 0 || displayBounds.Height <= 0) return;
            int screenMinimum = Math.Min(displayBounds.Width, displayBounds.Height);
            renderBarThickness = Math.Min(screenMinimum, ScalePixels(barThickness));
            renderMarkerGap = ScalePixels(MarkerGap);
            renderLength = Math.Max(1, Math.Min(ScalePixels(maximumLength),
                screenMinimum - renderMarkerGap - renderBarThickness));
            renderTextSize = ScalePixels(gpuTextSize);
            Size textExtent = MeasureGpuTextExtent();
            renderOffsetX = Math.Min(ScalePixels(gpuTextOffsetX),
                Math.Max(0, displayBounds.Width - textExtent.Width));
            renderOffsetY = Math.Min(ScalePixels(gpuTextOffsetY),
                Math.Max(0, displayBounds.Height - textExtent.Height));
            int overlaySize = renderLength + renderMarkerGap + renderBarThickness;
            int requiredWidth = textExtent.IsEmpty ? 0 : renderOffsetX + textExtent.Width;
            int requiredHeight = textExtent.IsEmpty ? 0 : renderOffsetY + textExtent.Height;
            ClientSize = new Size(Math.Min(displayBounds.Width, Math.Max(overlaySize, requiredWidth)),
                Math.Min(displayBounds.Height, Math.Max(overlaySize, requiredHeight)));
            UpdateLocation(displayBounds);
            RequestRender();
        }

        public void SetColors(Color gpu, Color ram)
        {
            vramColor = gpu;
            ramColor = ram;
            gpuColor = gpu;
            RequestRender();
        }

        public void SetGpuTextOptions(bool showPower, bool showTemperature, int textSize,
            int rotationDegrees, int textSpacing, int offsetX, int offsetY)
        {
            showGpuPowerText = showPower;
            showGpuTemperatureText = showTemperature;
            gpuTextSize = Math.Max(8, Math.Min(72, textSize));
            int normalizedRotation = ((rotationDegrees % 360) + 360) % 360;
            gpuTextRotationDegrees = normalizedRotation == 90 || normalizedRotation == 270
                ? normalizedRotation : 0;
            gpuTextSpacing = Math.Max(-20, Math.Min(20, textSpacing));
            gpuTextOffsetX = Math.Max(0, Math.Min(3000, offsetX));
            gpuTextOffsetY = Math.Max(0, Math.Min(3000, offsetY));
            RecalculateLayout();
        }

        public void SetGpuMetrics(int powerWatts, int temperatureCelsius)
        {
            if (gpuPowerWatts == powerWatts && gpuTemperatureCelsius == temperatureCelsius) return;
            gpuPowerWatts = powerWatts;
            gpuTemperatureCelsius = temperatureCelsius;
            if (showGpuPowerText || showGpuTemperatureText) RequestRender();
        }

        private Size MeasureGpuTextExtent()
        {
            string key = renderTextSize + "|" + showGpuPowerText + "|" + showGpuTemperatureText + "|" + gpuTextSpacing;
            if (metricLayout == null || metricLayoutKey != key)
            {
                OverlayMetricLayout replacement = new OverlayMetricLayout(renderTextSize,
                    showGpuPowerText, showGpuTemperatureText, gpuTextSpacing);
                if (metricLayout != null) metricLayout.Dispose();
                metricLayout = replacement;
                metricLayoutKey = key;
            }
            return metricLayout.SizeAtRotation(gpuTextRotationDegrees);
        }

        public void SetOpacityPercent(int opacityPercent)
        {
            overlayAlpha = (byte)Math.Round(Math.Max(10, Math.Min(100, opacityPercent)) * 255d / 100d);
            RequestRender();
        }

        public void SetAnchorLeft(bool left)
        {
            anchorLeft = left;
            UpdateLocation(displayBounds);
            RequestRender();
        }

        public void SetBehindFullscreen(bool behind)
        {
            TopMost = !behind;
            if (behind) SendToBack();
        }

        private void UpdateLocation(Rectangle screen)
        {
            Location = new Point(anchorLeft ? screen.Left : screen.Right - Width, screen.Top);
        }

        internal Bitmap RenderFrame()
        {
            Bitmap frame = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height),
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(frame))
            {
                graphics.Clear(Color.Transparent);
                DrawOverlay(graphics);
            }
            return frame;
        }

        private void DrawOverlay(Graphics graphics)
        {
            int barThickness = renderBarThickness;
            int maximumLength = renderLength;
            int markerGap = renderMarkerGap;
            int cornerX = anchorLeft ? 0 : ClientSize.Width - barThickness;
            int horizontalLength = (int)Math.Round(maximumLength * vramPercent / 100d);
            int verticalLength = (int)Math.Round(maximumLength * ramPercent / 100d);

            using (Brush vramBrush = new SolidBrush(vramColor))
            using (Brush ramBrush = new SolidBrush(ramColor))
            {
                if (horizontalLength > 0 && verticalLength > 0)
                {
                    horizontalLength = Math.Max(barThickness, horizontalLength);
                    verticalLength = Math.Max(barThickness, verticalLength);
                    if (anchorLeft)
                    {
                        if (horizontalLength > barThickness)
                            graphics.FillRectangle(vramBrush, barThickness, 0,
                                horizontalLength - barThickness, barThickness);
                    }
                    else
                    {
                        int horizontalStart = ClientSize.Width - horizontalLength;
                        if (cornerX > horizontalStart)
                            graphics.FillRectangle(vramBrush, horizontalStart, 0,
                                cornerX - horizontalStart, barThickness);
                    }
                    if (verticalLength > barThickness)
                        graphics.FillRectangle(ramBrush, cornerX, barThickness,
                            barThickness, verticalLength - barThickness);

                    // Split the corner square on a 45-degree diagonal so neither line overlaps.
                    if (anchorLeft)
                    {
                        graphics.FillPolygon(vramBrush, new Point[] {
                            new Point(0, 0),
                            new Point(barThickness, 0),
                            new Point(barThickness, barThickness)
                        });
                        graphics.FillPolygon(ramBrush, new Point[] {
                            new Point(0, 0),
                            new Point(barThickness, barThickness),
                            new Point(0, barThickness)
                        });
                    }
                    else
                    {
                        graphics.FillPolygon(vramBrush, new Point[] {
                            new Point(cornerX, 0),
                            new Point(ClientSize.Width, 0),
                            new Point(cornerX, barThickness)
                        });
                        graphics.FillPolygon(ramBrush, new Point[] {
                            new Point(ClientSize.Width, 0),
                            new Point(ClientSize.Width, barThickness),
                            new Point(cornerX, barThickness)
                        });
                    }
                }
                else if (horizontalLength > 0)
                {
                    graphics.FillRectangle(vramBrush,
                        anchorLeft ? 0 : ClientSize.Width - horizontalLength,
                        0, horizontalLength, barThickness);
                }
                else if (verticalLength > 0)
                {
                    graphics.FillRectangle(ramBrush, cornerX, 0,
                        barThickness, verticalLength);
                }

                // Detached squares mark the 100% end points without displaying numbers.
                graphics.FillRectangle(vramBrush,
                    anchorLeft ? maximumLength + markerGap :
                        ClientSize.Width - maximumLength - markerGap - barThickness,
                    0, barThickness, barThickness);
                graphics.FillRectangle(ramBrush,
                    cornerX, maximumLength + markerGap,
                    barThickness, barThickness);
            }

            DrawGpuMetricText(graphics);
        }

        private void DrawGpuMetricText(Graphics graphics)
        {
            if (!showGpuPowerText && !showGpuTemperatureText) return;
            string key = gpuPowerWatts + "|" + gpuTemperatureCelsius + "|" + metricLayoutKey
                + "|" + gpuColor.ToArgb() + "|" + gpuTextRotationDegrees;
            if (cachedText == null || cachedTextKey != key)
            {
                Bitmap replacement = metricLayout.Render(gpuPowerWatts, gpuTemperatureCelsius, gpuColor, gpuTextRotationDegrees);
                if (cachedText != null) cachedText.Dispose();
                cachedText = replacement;
                cachedTextKey = key;
            }
            int x = anchorLeft ? renderOffsetX : ClientSize.Width - renderOffsetX - cachedText.Width;
            graphics.DrawImageUnscaled(cachedText, x, renderOffsetY);
        }

        private void RequestRender()
        {
            if (renderQueued || !IsHandleCreated || !Visible || IsDisposed) return;
            renderQueued = true;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    renderQueued = false;
                    if (Visible && !IsDisposed) PresentOverlay();
                }));
            }
            catch (InvalidOperationException) { renderQueued = false; }
        }

        private void PresentOverlay()
        {
            if (!IsHandleCreated || IsDisposed) return;
            using (Bitmap frame = RenderFrame())
                LastPresentationSucceeded = AlphaWindow.Present(Handle, frame, Location, overlayAlpha);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PresentOverlay();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RequestRender();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            PresentOverlay();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && metricLayout != null)
            {
                metricLayout.Dispose();
                metricLayout = null;
            }
            if (disposing && cachedText != null)
            {
                cachedText.Dispose();
                cachedText = null;
            }
            base.Dispose(disposing);
        }

        private static int ClampPercent(int value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }
    }

    internal sealed class VramUsageSnapshot
    {
        public readonly long TotalBytes;
        public readonly long ActiveBytes;
        public readonly uint ActiveLuidLow;
        public readonly int ActiveLuidHigh;

        public VramUsageSnapshot(long totalBytes, long activeBytes,
            uint activeLuidLow, int activeLuidHigh)
        {
            TotalBytes = totalBytes;
            ActiveBytes = activeBytes;
            ActiveLuidLow = activeLuidLow;
            ActiveLuidHigh = activeLuidHigh;
        }
    }

    internal sealed class VramCounter : IDisposable
    {
        private const uint Success = 0, NewData = 1, MoreData = 0x800007D2, FormatDouble = 0x200;
        private const string WildcardPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
        private readonly IntPtr query;
        private readonly List<CounterEntry> counters = new List<CounterEntry>();

        public VramCounter()
        {
#if TEST_VRAM_FAILURE
            throw new InvalidOperationException("Forced VRAM failure test.");
#endif
            Check(PdhOpenQuery(IntPtr.Zero, UIntPtr.Zero, out query), "Open PDH query");
            try
            {
                foreach (string path in ExpandPaths(WildcardPath))
                {
                    if (path.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    IntPtr counter;
                    Check(PdhAddEnglishCounter(query, path, UIntPtr.Zero, out counter), "Add VRAM counter");
                    uint luidLow;
                    int luidHigh;
                    ParseLuid(path, out luidLow, out luidHigh);
                    counters.Add(new CounterEntry(counter, luidLow, luidHigh));
                }
                if (counters.Count == 0) throw new InvalidOperationException("No physical GPU VRAM counter was found.");
            }
            catch { PdhCloseQuery(query); throw; }
        }

        public VramUsageSnapshot ReadUsage()
        {
#if TEST_WORKER
            if (Environment.GetEnvironmentVariable("SMO_TEST_INVALID_VRAM") == "1")
                return new VramUsageSnapshot(0, RequireValidUsage(-1), 0, 0);
#endif
            Check(PdhCollectQueryData(query), "Read VRAM counters");
            double total = 0;
            double largest = -1;
            uint activeLow = 0;
            int activeHigh = 0;
            foreach (CounterEntry entry in counters)
            {
                uint type;
                FormattedValue value;
                uint result = PdhGetFormattedCounterValue(entry.Counter, FormatDouble, out type, out value);
                if (result == Success && (value.Status == Success || value.Status == NewData) &&
                    !double.IsNaN(value.DoubleValue) && !double.IsInfinity(value.DoubleValue) && value.DoubleValue >= 0)
                {
                    total += value.DoubleValue;
                    if (value.DoubleValue > largest)
                    {
                        largest = value.DoubleValue;
                        activeLow = entry.LuidLow;
                        activeHigh = entry.LuidHigh;
                    }
                }
            }
            long activeBytes = RequireValidUsage(largest);
            return new VramUsageSnapshot(checked((long)total), activeBytes, activeLow, activeHigh);
        }

        internal static long RequireValidUsage(double largest)
        {
            if (largest < 0 || double.IsNaN(largest) || double.IsInfinity(largest))
                throw new IOException("No valid VRAM counters; GPU counters must be recreated.");
            return checked((long)largest);
        }

        public void Dispose() { if (query != IntPtr.Zero) PdhCloseQuery(query); }

        private static IEnumerable<string> ExpandPaths(string wildcardPath)
        {
            uint length = 0;
            uint result = PdhExpandWildCardPath(IntPtr.Zero, wildcardPath, IntPtr.Zero, ref length, 0);
            if (result != MoreData && result != Success) Check(result, "Find VRAM counters");
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length * 2));
            try
            {
                Check(PdhExpandWildCardPath(IntPtr.Zero, wildcardPath, buffer, ref length, 0), "Expand VRAM counters");
                int offset = 0;
                while (offset < length)
                {
                    string path = Marshal.PtrToStringUni(new IntPtr(buffer.ToInt64() + offset * 2));
                    if (string.IsNullOrEmpty(path)) yield break;
                    yield return path;
                    offset += path.Length + 1;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static void Check(uint result, string operation)
        {
            if (result != Success) throw new InvalidOperationException(string.Format("{0} failed (0x{1:X8}).", operation, result));
        }

        private static void ParseLuid(string path, out uint low, out int high)
        {
            low = 0;
            high = 0;
            int marker = path.IndexOf("luid_0x", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return;
            int highStart = marker + 7;
            int lowStart = highStart + 11;
            if (lowStart + 8 > path.Length) return;
            try
            {
                high = unchecked((int)Convert.ToUInt32(path.Substring(highStart, 8), 16));
                low = Convert.ToUInt32(path.Substring(lowStart, 8), 16);
            }
            catch (FormatException) { low = 0; high = 0; }
            catch (OverflowException) { low = 0; high = 0; }
        }

        private sealed class CounterEntry
        {
            public readonly IntPtr Counter;
            public readonly uint LuidLow;
            public readonly int LuidHigh;
            public CounterEntry(IntPtr counter, uint luidLow, int luidHigh)
            {
                Counter = counter;
                LuidLow = luidLow;
                LuidHigh = luidHigh;
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct FormattedValue
        {
            [FieldOffset(0)] public uint Status;
            [FieldOffset(8)] public double DoubleValue;
        }

        [DllImport("pdh.dll")]
        private static extern uint PdhOpenQuery(IntPtr dataSource, UIntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out FormattedValue value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhExpandWildCardPath(IntPtr source, string path, IntPtr list, ref uint length, uint flags);
        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);
    }
}
