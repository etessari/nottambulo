// Windows tray app that keeps the PC awake (even with the screen locked and the lid closed),
// e.g. so long-running jobs and AI agents can keep working, and can simulate user activity with F15.
// Builds with the .NET Framework 4.x csc.exe that ships with Windows (C# 5): see build.ps1.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Nottambulo")]
[assembly: System.Reflection.AssemblyProduct("Nottambulo")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace Nottambulo
{
    static class Program
    {
        public const string AppName = "Nottambulo";

        [STAThread]
        static int Main(string[] args)
        {
            // Used by build.ps1 to generate the .exe icon from the same drawing as the tray icon.
            if (args.Length == 2 && args[0] == "--export-icon")
            {
                FaceIcon.SaveIco(args[1]);
                return 0;
            }

            bool created;
            using (var mutex = new Mutex(true, "Local\\" + AppName + "-TrayApp", out created))
            {
                if (!created)
                {
                    MessageBox.Show(AppName + " is already running: look for its icon in the notification area.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------ Win32

    static class Native
    {
        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_DISPLAY_REQUIRED = 0x00000002;
        public const byte VK_F15 = 0x7E;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint flags);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr handle);

        [DllImport("powrprof.dll")]
        public static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);

        [DllImport("powrprof.dll")]
        public static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);

        [DllImport("powrprof.dll")]
        public static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);

        [DllImport("powrprof.dll")]
        public static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);

        [DllImport("powrprof.dll")]
        public static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);

        [DllImport("powrprof.dll")]
        public static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    }

    // ------------------------------------------------------------------ Lid

    /// Reads and writes the power plan's "lid close action" (0 = do nothing).
    static class Lid
    {
        static readonly Guid SubButtons = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");
        static readonly Guid LidAction = new Guid("5ca83367-6e45-459f-a27b-476b1d01c936");

        public static Guid ActiveScheme()
        {
            IntPtr p;
            Check(Native.PowerGetActiveScheme(IntPtr.Zero, out p), "reading active power plan");
            try { return (Guid)Marshal.PtrToStructure(p, typeof(Guid)); }
            finally { Native.LocalFree(p); }
        }

        public static void Read(Guid scheme, out uint ac, out uint dc)
        {
            Guid sub = SubButtons, setting = LidAction;
            Check(Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out ac), "reading lid action (plugged in)");
            Check(Native.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out dc), "reading lid action (on battery)");
        }

        public static void Write(Guid scheme, uint ac, uint dc)
        {
            Guid sub = SubButtons, setting = LidAction;
            Check(Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, ac), "writing lid action (plugged in)");
            Check(Native.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, dc), "writing lid action (on battery)");
            // Written values only take effect once the plan is re-applied.
            if (scheme == ActiveScheme())
                Check(Native.PowerSetActiveScheme(IntPtr.Zero, ref scheme), "re-applying power plan");
        }

        static void Check(uint err, string what)
        {
            if (err != 0)
                throw new Win32Exception((int)err, what + ": " + new Win32Exception((int)err).Message);
        }
    }

    // ------------------------------------------------------------------ Settings

    class Settings
    {
        public bool KeepScreenOn = true;
        public bool LidDoNothing = true;
        public bool LidOnBattery = true;
        public bool SimulateActivity = true;
        public bool Notifications = true;

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Program.AppName);
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) map[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                s.KeepScreenOn = Get(map, "screen", s.KeepScreenOn);
                s.LidDoNothing = Get(map, "lid", s.LidDoNothing);
                s.LidOnBattery = Get(map, "lidBattery", s.LidOnBattery);
                s.SimulateActivity = Get(map, "simulateActivity", s.SimulateActivity);
                s.Notifications = Get(map, "notifications", s.Notifications);
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FilePath, new[]
                {
                    "screen=" + KeepScreenOn,
                    "lid=" + LidDoNothing,
                    "lidBattery=" + LidOnBattery,
                    "simulateActivity=" + SimulateActivity,
                    "notifications=" + Notifications,
                });
            }
            catch { }
        }

        static bool Get(Dictionary<string, string> map, string key, bool def)
        {
            string v;
            bool b;
            return map.TryGetValue(key, out v) && bool.TryParse(v, out b) ? b : def;
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = Program.AppName;

        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool IsEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(Name) != null;
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(Name, Command);
                else k.DeleteValue(Name, false);
            }
        }

        /// If the .exe was moved, update the registered path.
        public static void RefreshPath()
        {
            if (IsEnabled()) Set(true);
        }
    }

    // ------------------------------------------------------------------ Icon

    /// A stylised sleepy face: awake = tired, bloodshot eyes under heavy lids; off = asleep, eyes closed.
    static class FaceIcon
    {
        static readonly Color FaceOn = Color.FromArgb(246, 184, 70);    // amber: readable on light and dark taskbars
        static readonly Color FaceOff = Color.FromArgb(160, 160, 160);
        static readonly Color InkOn = Color.FromArgb(92, 56, 14);
        static readonly Color InkOff = Color.FromArgb(70, 70, 70);
        static readonly Color Bags = Color.FromArgb(150, 116, 60, 150);
        static readonly Color Vein = Color.FromArgb(214, 38, 52);
        static readonly Color Pupil = Color.FromArgb(40, 28, 20);

        public static Bitmap Draw(int size, bool on)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float k = size / 32f;
                g.ScaleTransform(k, k);
                bool tiny = size < 24;
                Color face = on ? FaceOn : FaceOff, ink = on ? InkOn : InkOff;

                using (var faceBrush = new SolidBrush(face))
                using (var outline = new Pen(ink, tiny ? 2f : 1.4f))
                using (var stroke = new Pen(ink, tiny ? 2.6f : 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.FillEllipse(faceBrush, 1f, 1f, 30f, 30f);
                    g.DrawEllipse(outline, 1f, 1f, 30f, 30f);

                    if (on)
                    {
                        Eye(g, 10f, 14f, tiny, faceBrush, stroke);
                        Eye(g, 22f, 14f, tiny, faceBrush, stroke);
                        // Flat, unimpressed mouth
                        g.DrawLine(stroke, 13.5f, 24f, 18.5f, 24f);
                    }
                    else
                    {
                        // Closed eyes and a relaxed mouth
                        g.DrawArc(stroke, 6.5f, 11f, 7f, 5f, 20, 140);
                        g.DrawArc(stroke, 18.5f, 11f, 7f, 5f, 20, 140);
                        g.DrawArc(stroke, 13f, 20.5f, 6f, 4f, 20, 140);
                    }
                }
            }
            return bmp;
        }

        /// A bloodshot eye with a droopy upper lid and a bag underneath.
        static void Eye(Graphics g, float cx, float cy, bool tiny, Brush faceBrush, Pen stroke)
        {
            float rx = 5.4f, ry = 4.8f;
            var rect = new RectangleF(cx - rx, cy - ry, rx * 2, ry * 2);

            if (!tiny)
                using (var bags = new Pen(Bags, 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(bags, cx - rx + 1.5f, cy + 1.6f, rx * 2 - 3f, ry * 1.6f, 40, 100);

            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(rect);
                var saved = g.Clip;
                g.SetClip(clip);

                g.FillEllipse(Brushes.White, rect);
                using (var vein = new Pen(Vein, tiny ? 1.1f : 0.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    // Little branching veins from both corners towards the pupil
                    g.DrawLine(vein, cx - rx, cy + 0.6f, cx - 2.6f, cy + 1.2f);
                    g.DrawLine(vein, cx - 3.6f, cy + 1.0f, cx - 3.0f, cy + 2.6f);
                    g.DrawLine(vein, cx + rx, cy + 0.8f, cx + 2.6f, cy + 1.0f);
                    g.DrawLine(vein, cx + 3.8f, cy + 0.9f, cx + 3.3f, cy + 2.8f);
                    if (!tiny)
                    {
                        g.DrawLine(vein, cx - 4.2f, cy + 3.0f, cx - 2.2f, cy + 2.4f);
                        g.DrawLine(vein, cx + 4.0f, cy - 0.4f, cx + 2.8f, cy - 0.2f);
                    }
                }
                using (var pupil = new SolidBrush(Pupil))
                    g.FillEllipse(pupil, cx - 1.9f, cy - 0.6f, 3.8f, 3.8f);

                // Heavy upper lid covering ~45% of the eye
                g.FillRectangle(faceBrush, rect.Left - 1, rect.Top - 1, rect.Width + 2, ry * 0.9f + 1);
                g.Clip = saved;
            }
            // Only the lower rim and the lid edge: a full outline would read as glasses
            using (var rim = new Pen(stroke.Color, tiny ? 1.2f : 0.9f))
                g.DrawArc(rim, rect, 0, 180);
            g.DrawLine(stroke, rect.Left + 0.2f, cy - ry * 0.1f, rect.Right - 0.2f, cy - ry * 0.1f);
        }

        public static Icon Create(int size, bool on)
        {
            using (var bmp = Draw(size, on))
            {
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        /// Writes a multi-resolution .ico (PNG entries, supported since Windows Vista).
        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Draw(s, true))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }

            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }

    // ------------------------------------------------------------------ App

    class TrayApp : ApplicationContext
    {
        const int F15IntervalSeconds = 50;

        static readonly string LidBackupPath = Path.Combine(Settings.Dir, "lid-backup.txt");
        readonly object lidLock = new object();

        readonly Settings settings = Settings.Load();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly Icon iconOn, iconOff;

        ToolStripMenuItem statusItem, stopItem, screenItem, lidItem, lidBatteryItem, activityItem, notifyItem, autostartItem;

        bool active;
        DateTime? until;
        int f15Counter;

        public TrayApp()
        {
            int size = SystemInformation.SmallIconSize.Width;
            iconOn = FaceIcon.Create(size, true);
            iconOff = FaceIcon.Create(size, false);

            BuildMenu();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
            timer.Tick += OnTick;

            SystemEvents.SessionEnding += (s, e) => RestoreLid();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Application.ApplicationExit += (s, e) => Cleanup();

            try { Autostart.RefreshPath(); } catch { }

            // If the app didn't exit cleanly last time (crash, power loss), put the lid setting back.
            if (File.Exists(LidBackupPath))
            {
                RestoreLid();
                Notify("Restored the lid setting left over from the last session.");
            }

            UpdateUi();
            tray.Visible = true;
        }

        // ---------------------------------------------------- Menu

        void BuildMenu()
        {
            statusItem = new ToolStripMenuItem { Enabled = false };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());

            var forever = new ToolStripMenuItem("Keep awake until turned off", null, (s, e) => Activate(null));
            forever.Font = new Font(forever.Font, FontStyle.Bold);
            menu.Items.Add(forever);
            menu.Items.Add(new ToolStripMenuItem("Keep awake for 30 minutes", null, (s, e) => Activate(TimeSpan.FromMinutes(30))));
            menu.Items.Add(new ToolStripMenuItem("Keep awake for 1 hour", null, (s, e) => Activate(TimeSpan.FromHours(1))));
            menu.Items.Add(new ToolStripMenuItem("Keep awake for 2 hours", null, (s, e) => Activate(TimeSpan.FromHours(2))));
            menu.Items.Add(new ToolStripMenuItem("Keep awake for 4 hours", null, (s, e) => Activate(TimeSpan.FromHours(4))));
            stopItem = new ToolStripMenuItem("Turn off", null, (s, e) => Deactivate("Turned off."));
            menu.Items.Add(stopItem);
            menu.Items.Add(new ToolStripSeparator());

            screenItem = Check("Keep screen on", settings.KeepScreenOn, v =>
            {
                settings.KeepScreenOn = v;
                if (active) ApplyExecutionState();
            });
            lidItem = Check("Stay awake with the lid closed", settings.LidDoNothing, v =>
            {
                settings.LidDoNothing = v;
                lidBatteryItem.Enabled = v;
                if (active) { RestoreLid(); if (v) ApplyLid(); }
            });
            lidBatteryItem = Check("    …also on battery", settings.LidOnBattery, v =>
            {
                settings.LidOnBattery = v;
                if (active && settings.LidDoNothing) { RestoreLid(); ApplyLid(); }
            });
            lidBatteryItem.Enabled = settings.LidDoNothing;
            activityItem = Check("Simulate activity (F15 every 50 s)", settings.SimulateActivity, v =>
            {
                settings.SimulateActivity = v;
                f15Counter = 0;
            });
            menu.Items.Add(new ToolStripSeparator());
            notifyItem = Check("Notifications", settings.Notifications, v => settings.Notifications = v);

            autostartItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
            try { autostartItem.Checked = Autostart.IsEnabled(); } catch { }
            autostartItem.Click += (s, e) =>
            {
                try { Autostart.Set(autostartItem.Checked); }
                catch (Exception ex) { autostartItem.Checked = !autostartItem.Checked; Notify("Could not change startup setting: " + ex.Message); }
            };
            menu.Items.Add(autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitThread()));

            menu.Opening += (s, e) => UpdateUi();
        }

        ToolStripMenuItem Check(string text, bool initial, Action<bool> onChange)
        {
            var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = initial };
            item.CheckedChanged += (s, e) => { onChange(item.Checked); settings.Save(); UpdateUi(); };
            menu.Items.Add(item);
            return item;
        }

        // ---------------------------------------------------- State

        void Toggle()
        {
            if (active) Deactivate("Turned off.");
            else Activate(null);
        }

        void Activate(TimeSpan? duration)
        {
            until = duration.HasValue ? DateTime.Now + duration.Value : (DateTime?)null;
            if (!active)
            {
                active = true;
                f15Counter = 0;
                ApplyExecutionState();
                if (settings.LidDoNothing) ApplyLid();
                timer.Start();
            }
            UpdateUi();
            Notify(until.HasValue
                ? "Awake until " + until.Value.ToString("t") + "."
                : "Awake until you turn it off.");
        }

        void Deactivate(string message)
        {
            if (!active) return;
            active = false;
            until = null;
            timer.Stop();
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            RestoreLid();
            UpdateUi();
            if (message != null) Notify(message);
        }

        void ApplyExecutionState()
        {
            uint flags = Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED;
            if (settings.KeepScreenOn) flags |= Native.ES_DISPLAY_REQUIRED;
            Native.SetThreadExecutionState(flags);
        }

        void OnTick(object sender, EventArgs e)
        {
            if (until.HasValue && DateTime.Now >= until.Value)
            {
                Deactivate("Time's up: normal sleep settings are back.");
                return;
            }
            if (settings.SimulateActivity && ++f15Counter >= F15IntervalSeconds)
            {
                f15Counter = 0;
                Native.keybd_event(Native.VK_F15, 0, 0, UIntPtr.Zero);
                Native.keybd_event(Native.VK_F15, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            UpdateUi();
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            // After resume (e.g. Sleep from the Start menu) re-apply the state on the UI thread.
            if (e.Mode == PowerModes.Resume && active)
                menu.BeginInvoke(new Action(ApplyExecutionState));
        }

        // ---------------------------------------------------- Lid

        void ApplyLid()
        {
            lock (lidLock)
            {
                if (File.Exists(LidBackupPath)) return; // already applied
                try
                {
                    Guid scheme = Lid.ActiveScheme();
                    uint ac, dc;
                    Lid.Read(scheme, out ac, out dc);
                    Directory.CreateDirectory(Settings.Dir);
                    File.WriteAllText(LidBackupPath, scheme + "|" + ac + "|" + dc);
                    Lid.Write(scheme, 0, settings.LidOnBattery ? 0 : dc);
                }
                catch (Exception ex)
                {
                    try { File.Delete(LidBackupPath); } catch { }
                    Notify("Could not change the lid setting: " + ex.Message);
                }
            }
        }

        void RestoreLid()
        {
            lock (lidLock)
            {
                if (!File.Exists(LidBackupPath)) return;
                try
                {
                    var parts = File.ReadAllText(LidBackupPath).Trim().Split('|');
                    Lid.Write(new Guid(parts[0]),
                        uint.Parse(parts[1], CultureInfo.InvariantCulture),
                        uint.Parse(parts[2], CultureInfo.InvariantCulture));
                    File.Delete(LidBackupPath);
                }
                catch (Exception ex)
                {
                    Notify("Could not restore the lid setting: " + ex.Message);
                }
            }
        }

        // ---------------------------------------------------- UI

        string StatusText()
        {
            if (!active) return "Off";
            if (!until.HasValue) return "Awake until turned off";
            TimeSpan left = until.Value - DateTime.Now;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            string rest = left.TotalHours >= 1
                ? string.Format("{0}h {1:00}m", (int)left.TotalHours, left.Minutes)
                : string.Format("{0}m {1:00}s", left.Minutes, left.Seconds);
            return "Awake, " + rest + " left (until " + until.Value.ToString("t") + ")";
        }

        void UpdateUi()
        {
            string status = StatusText();
            statusItem.Text = status;
            stopItem.Enabled = active;
            tray.Icon = active ? iconOn : iconOff;
            string tip = Program.AppName + ": " + status;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void Notify(string text)
        {
            if (settings.Notifications && tray.Visible)
                tray.ShowBalloonTip(3000, Program.AppName, text, ToolTipIcon.None);
        }

        protected override void ExitThreadCore()
        {
            Cleanup();
            base.ExitThreadCore();
        }

        bool cleanedUp;

        void Cleanup()
        {
            if (cleanedUp) return;
            cleanedUp = true;
            Deactivate(null);
            RestoreLid();
            tray.Visible = false;
            tray.Dispose();
            iconOn.Dispose();
            iconOff.Dispose();
        }
    }
}
