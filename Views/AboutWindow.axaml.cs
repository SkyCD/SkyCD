using Avalonia.Controls;
// removed unused Shapes to avoid Path ambiguity with System.IO.Path
using Avalonia.Interactivity;
using SkyCD.Models.SystemInfo;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Timers;
using Avalonia.Threading;

namespace SkyCD.Views
{
    public partial class AboutWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<UsedAssemblyInfo> LoadedAssemblies { get; } = new();

        private readonly Process _currentProcess = Process.GetCurrentProcess();
        private readonly Timer _updateTimer;
        private TimeSpan _lastTotalProcessorTime;
        private DateTime _lastSampleTime;

        private string _cpuUsage = "0 %";
        private string _workingSet = "0 MB";
        private string _privateMemory = "0 MB";
        private string _managedHeap = "0 MB";
        private string _threadCount = "0";
        private string _uptime = "0s";
        private double _cpuPercent = 0.0;
        private double _memoryPercent = 0.0;
        private string _threadInfo = "0";
        private string _uptimeFriendly = "0s";
        private string _startTime = "";

        public string LicensePath
        {
            get
            {
                return System.IO.Path.Combine(AppContext.BaseDirectory, "LICENSE.md");
            }
        }

        public string LicenseText {
            get {
                var projectLicensePath = LicensePath;
                if (File.Exists(projectLicensePath))
                {
                    return File.ReadAllText(projectLicensePath);
                }

                return "Not found. Expected at: " + projectLicensePath;
            }
        }

        public string Version
        {
            get
            {
                return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";
            }
        }

        public AboutWindow()
        {
            InitializeComponent();
            DataContext = this;
            BuildContent();
            // Markdown control is referenced directly in XAML; dynamic loader not needed
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;
            _lastSampleTime = DateTime.UtcNow;

            _updateTimer = new Timer(1000);
            _updateTimer.Elapsed += (s, e) => Dispatcher.UIThread.Post(UpdateStats);
            _updateTimer.Start();

            this.Closed += (_, __) => _updateTimer?.Stop();
        }
        private void BuildContent()
        {
            var loadedAssemblies = Services.AssemblyInfoScanner.ScanLoadedAssemblies();
            LoadedAssemblies.Clear();

            foreach (var a in loadedAssemblies)
            {
                LoadedAssemblies.Add(a);
            }         
        }

        private void UpdateStats()
        {
            try
            {
                _currentProcess.Refresh();

                var now = DateTime.UtcNow;
                var curTotal = _currentProcess.TotalProcessorTime;
                var cpu = 0.0;
                var elapsed = (now - _lastSampleTime).TotalMilliseconds;
                if (elapsed > 1)
                {
                    var usedMs = (curTotal - _lastTotalProcessorTime).TotalMilliseconds;
                    cpu = (usedMs / elapsed) / Environment.ProcessorCount * 100.0;
                }

                _lastSampleTime = now;
                _lastTotalProcessorTime = curTotal;

                CpuUsage = cpu.ToString("0.0") + " %";
                CpuPercent = cpu;
                WorkingSet = FormatBytes(_currentProcess.WorkingSet64);
                PrivateMemory = FormatBytes(_currentProcess.PrivateMemorySize64);
                ManagedHeap = FormatBytes(GC.GetTotalMemory(false));
                var totalAvail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (totalAvail > 0)
                {
                    MemoryPercent = Math.Min(100.0, (double)_currentProcess.WorkingSet64 / (double)totalAvail * 100.0);
                }
                else
                {
                    MemoryPercent = 0.0;
                }
                ThreadCount = _currentProcess.Threads.Count.ToString();
                ThreadInfo = $"{_currentProcess.Threads.Count} threads";
                var up = DateTime.Now - _currentProcess.StartTime;
                Uptime = up.ToString(@"d\.hh\:mm\:ss");
                UptimeFriendly = FormatFriendlyTime(up);
                StartTime = _currentProcess.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                // ignore
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.0") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0") + " MB";
            double gb = mb / 1024.0;
            return gb.ToString("0.0") + " GB";
        }

        public string CpuUsage { get => _cpuUsage; set { if (value != _cpuUsage) { _cpuUsage = value; OnPropertyChanged(); } } }
        public double CpuPercent { get => _cpuPercent; set { if (value != _cpuPercent) { _cpuPercent = value; OnPropertyChanged(); } } }
        public string WorkingSet { get => _workingSet; set { if (value != _workingSet) { _workingSet = value; OnPropertyChanged(); } } }
        public string PrivateMemory { get => _privateMemory; set { if (value != _privateMemory) { _privateMemory = value; OnPropertyChanged(); } } }
        public string ManagedHeap { get => _managedHeap; set { if (value != _managedHeap) { _managedHeap = value; OnPropertyChanged(); } } }
        public double MemoryPercent { get => _memoryPercent; set { if (value != _memoryPercent) { _memoryPercent = value; OnPropertyChanged(); } } }
        public string ThreadCount { get => _threadCount; set { if (value != _threadCount) { _threadCount = value; OnPropertyChanged(); } } }
        public string Uptime { get => _uptime; set { if (value != _uptime) { _uptime = value; OnPropertyChanged(); } } }
        public string ThreadInfo { get => _threadInfo; set { if (value != _threadInfo) { _threadInfo = value; OnPropertyChanged(); } } }
        public string UptimeFriendly { get => _uptimeFriendly; set { if (value != _uptimeFriendly) { _uptimeFriendly = value; OnPropertyChanged(); } } }
        public string StartTime { get => _startTime; set { if (value != _startTime) { _startTime = value; OnPropertyChanged(); } } }

        private static string FormatFriendlyTime(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return string.Format("{0:%d}d {0:hh}h {0:mm}m", ts);
            if (ts.TotalHours >= 1)
                return string.Format("{0:hh}h {0:mm}m {0:ss}s", ts);
            if (ts.TotalMinutes >= 1)
                return string.Format("{0:mm}m {0:ss}s", ts);
            return string.Format("{0:ss}s", ts);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // RenderMarkdown removed; using Markdown.Avalonia control in XAML instead

        // dynamic loader removed; control is declared directly in XAML

        private void OnCloseClicked(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OnOpenProjectLicenseClicked(object? sender, RoutedEventArgs e)
        {

        }
        private void OnOpenLicenseFile(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            TextBlock tb = (TextBlock)sender!;

            Process.Start(new ProcessStartInfo
            {
                FileName = tb.Tag?.ToString(),
                UseShellExecute = true
            });
        }

        private void OnOpenRepositoryUrl(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            TextBlock tb = (TextBlock)sender!;
            var url = tb.Text ?? tb.Tag?.ToString();
            if (string.IsNullOrEmpty(url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
