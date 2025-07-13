// Copyright 2020 Takuto Nakamura
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

using FormsTimer = System.Windows.Forms.Timer;
using Microsoft.Win32;
using RunCat365.Properties;
using System.ComponentModel;
using System.Diagnostics;
using System.Resources;

namespace RunCat365
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Terminate RunCat365 if there's any existing instance.
            using var procMutex = new Mutex(true, "_RUNCAT_MUTEX", out var result);
            if (!result) return;

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new RunCat365ApplicationContext());
            }
            finally
            {
                procMutex?.ReleaseMutex();
            }
        }
    }

    public class RunCat365ApplicationContext : ApplicationContext
    {
        private const int CPU_TIMER_DEFAULT_INTERVAL = 5000;
        private const int ANIMATE_TIMER_DEFAULT_INTERVAL = 200;
        private readonly PerformanceCounter cpuUsage;
        private readonly ToolStripMenuItem runnerMenu;
        private readonly ToolStripMenuItem themeMenu;
        private readonly ToolStripMenuItem startupMenu;
        private readonly ToolStripMenuItem fpsMaxLimitMenu;
        private readonly NotifyIcon notifyIcon;
        private readonly FormsTimer animateTimer;
        private readonly FormsTimer cpuTimer;
        private Runner runner = Runner.Cat;
        private Theme manualTheme = Theme.System;
        private FPSMaxLimit fpsMaxLimit = FPSMaxLimit.FPS40;
        private int current = 0;
        private float interval;
        private Icon[] icons = [];

        public RunCat365ApplicationContext()
        {
            UserSettings.Default.Reload();
            _ = Enum.TryParse(UserSettings.Default.Runner, out runner);
            _ = Enum.TryParse(UserSettings.Default.Theme, out manualTheme);
            _ = Enum.TryParse(UserSettings.Default.FPSMaxLimit, out fpsMaxLimit);

            Application.ApplicationExit += new EventHandler(OnApplicationExit);

            SystemEvents.UserPreferenceChanged += new UserPreferenceChangedEventHandler(UserPreferenceChanged);

            cpuUsage = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
            _ = cpuUsage.NextValue(); // discards first return value

            var items = new List<ToolStripMenuItem>();
            foreach (Runner r in Enum.GetValues<Runner>())
            {
                var item = new ToolStripMenuItem(r.GetString(), null, SetRunner)
                {
                    Checked = runner == r
                };
                items.Add(item);
            }
            runnerMenu = new ToolStripMenuItem("Runner", null, [.. items]);

            items.Clear();
            foreach (Theme t in Enum.GetValues<Theme>())
            {
                var item = new ToolStripMenuItem(t.GetString(), null, SetThemeIcons)
                {
                    Checked = manualTheme == t
                };
                items.Add(item);
            }
            themeMenu = new ToolStripMenuItem("Theme", null, [.. items]);

            items.Clear();
            foreach (FPSMaxLimit f in Enum.GetValues<FPSMaxLimit>())
            {
                var item = new ToolStripMenuItem(f.GetString(), null, SetFPSMaxLimit)
                {
                    Checked = fpsMaxLimit == f
                };
                items.Add(item);
            }
            fpsMaxLimitMenu = new ToolStripMenuItem("FPS Max Limit", null, [.. items]);

            startupMenu = new ToolStripMenuItem("Startup", null, SetStartup);
            if (IsStartupEnabled())
            {
                startupMenu.Checked = true;
            }

            var appVersion = $"{Application.ProductName} v{Application.ProductVersion}";
            var appVersionMenu = new ToolStripMenuItem(appVersion)
            {
                Enabled = false
            };

            var contextMenuStrip = new ContextMenuStrip(new Container());
            contextMenuStrip.Items.AddRange(
                runnerMenu,
                themeMenu,
                fpsMaxLimitMenu,
                startupMenu,
                new ToolStripSeparator(),
                appVersionMenu,
                new ToolStripMenuItem("Exit", null, Exit)
            );

            SetIcons();

            notifyIcon = new NotifyIcon()
            {
                Icon = icons[0],
                ContextMenuStrip = contextMenuStrip,
                Text = "0.0%",
                Visible = true
            };

            notifyIcon.DoubleClick += new EventHandler(HandleDoubleClick);

            animateTimer = new FormsTimer
            {
                Interval = ANIMATE_TIMER_DEFAULT_INTERVAL
            };
            animateTimer.Tick += new EventHandler(AnimationTick);
            animateTimer.Start();

            cpuTimer = new FormsTimer
            {
                Interval = CPU_TIMER_DEFAULT_INTERVAL
            };
            cpuTimer.Tick += new EventHandler(CPUTick);
            cpuTimer.Start();
        }

        private void OnApplicationExit(object? sender, EventArgs e)
        {
            UserSettings.Default.Runner = runner.ToString();
            UserSettings.Default.Theme = manualTheme.ToString();
            UserSettings.Default.FPSMaxLimit = fpsMaxLimit.ToString();
            UserSettings.Default.Save();
        }

        private static bool IsStartupEnabled()
        {
            var keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var rKey = Registry.CurrentUser.OpenSubKey(keyName);
            if (rKey is null) return false;
            var value = (rKey.GetValue(Application.ProductName) is not null);
            rKey.Close();
            return value;
        }

        private static Theme GetSystemTheme()
        {
            var keyName = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            using var rKey = Registry.CurrentUser.OpenSubKey(keyName);
            if (rKey is null) return Theme.Light;
            var value = rKey.GetValue("SystemUsesLightTheme");
            rKey.Close();
            if (value is null) return Theme.Light;
            return (int)value == 0 ? Theme.Dark : Theme.Light;
        }

        private void SetIcons()
        {
            Theme systemTheme = GetSystemTheme();
            var prefix = (manualTheme == Theme.System ? systemTheme : manualTheme).GetString();
            var runnerName = runner.GetString();
            ResourceManager rm = Resources.ResourceManager;
            var capacity = runner.GetFrameNumber();
            var list = new List<Icon>(capacity);
            for (int i = 0; i < capacity; i++)
            {
                var iconName = $"{prefix}_{runnerName}_{i}".ToLower();
                var icon = rm.GetObject(iconName);
                if (icon is null) continue;
                list.Add((Icon)icon);
            }
            icons = [.. list];
        }

        private static void UpdateCheckedState(ToolStripMenuItem sender, ToolStripMenuItem menu)
        {
            foreach (ToolStripMenuItem item in menu.DropDownItems)
            {
                item.Checked = false;
            }
            sender.Checked = true;
        }

        private void SetRunner(object? sender, EventArgs e)
        {
            if (sender is null) return;
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            UpdateCheckedState(item, runnerMenu);
            _ = Enum.TryParse(item.Text, out runner);
            SetIcons();
        }

        private void SetThemeIcons(object? sender, EventArgs e)
        {
            if (sender is null) return;
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            UpdateCheckedState(item, themeMenu);
            _ = Enum.TryParse(item.Text, out manualTheme);
            SetIcons();
        }

        private void SetFPSMaxLimit(object? sender, EventArgs e)
        {
            if (sender is null) return;
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            UpdateCheckedState(item, fpsMaxLimitMenu);
            var text = item.Text;
            if (text is null) return;
            fpsMaxLimit = _FPSMaxLimit.Parse(text);
        }

        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General) SetIcons();
        }

        private void SetStartup(object? sender, EventArgs e)
        {
            var productName = Application.ProductName;
            if (productName is null) return;
            var keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var rKey = Registry.CurrentUser.OpenSubKey(keyName, true);
            if (rKey is null) return;
            if (!startupMenu.Checked)
            {
                var fileName = Environment.ProcessPath;
                if (fileName != null)
                {
                    rKey.SetValue(productName, fileName);
                }
            }
            else
            {
                rKey.DeleteValue(productName, false);
            }
            rKey.Close();
            startupMenu.Checked = !startupMenu.Checked;
        }

        private void Exit(object? sender, EventArgs e)
        {
            cpuUsage.Close();
            animateTimer.Stop();
            cpuTimer.Stop();
            notifyIcon.Visible = false;
            Application.Exit();
        }

        private void AnimationTick(object? sender, EventArgs e)
        {
            if (icons.Length <= current) current = 0;
            notifyIcon.Icon = icons[current];
            current = (current + 1) % icons.Length;
        }

        private void CPUTick(object? state, EventArgs e)
        {
            // Range of CPU percentage: 0-100 (%)
            var cpuPercentage = Math.Min(100, cpuUsage.NextValue());
            notifyIcon.Text = $"CPU: {cpuPercentage:f1}%";
            // Range of interval: 25-500 (ms) = 2-40 (fps)
            interval = 500.0f / (float)Math.Max(1.0f, (cpuPercentage / 5.0f) * fpsMaxLimit.GetRate());

            animateTimer.Stop();
            animateTimer.Interval = (int)interval;
            animateTimer.Start();
        }

        private void HandleDoubleClick(object? Sender, EventArgs e)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                UseShellExecute = false,
                Arguments = " -c Start-Process taskmgr.exe",
                CreateNoWindow = true,
            };
            Process.Start(startInfo);
        }
    }
}