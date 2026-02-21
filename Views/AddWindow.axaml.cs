using Avalonia.Controls;
using Avalonia.Interactivity;
using System.IO;
using System.Linq;
using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace SkyCD.Views
{
    public partial class AddWindow : Window
    {
        public System.IO.DriveInfo? SelectedDrive { get; private set; }
        public string? EnteredName { get; private set; }

        private DispatcherTimer? _refreshTimer;

        public ObservableCollection<DriveInfo> PossibleDrives { get; } = new ObservableCollection<DriveInfo>();
        public AddWindow()
        {
            InitializeComponent();
            // populate initial drives
            PopulateDrives();

            // refresh periodically to detect drive changes
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (s, e) => RefreshDrives();
            _refreshTimer.Start();
        }

        private void PopulateDrives()
        {
            PossibleDrives.Clear();
            foreach (var d in DriveInfo.GetDrives().OrderBy(d => d.Name))
                PossibleDrives.Add(d);
            EnsureSelection();
        }

        private void EnsureSelection()
        {
            try
            {
                var list = this.FindControl<ListBox>("MediaList");
                if (list != null && list.SelectedIndex < 0 && PossibleDrives.Count > 0)
                {
                    list.SelectedIndex = 0;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void RefreshDrives()
        {
            try
            {
                var currentNames = DriveInfo.GetDrives().Select(d => d.Name).ToHashSet();
                var existingNames = PossibleDrives.Select(d => d.Name).ToHashSet();
                if (!currentNames.SetEquals(existingNames))
                {
                    PopulateDrives();
                }
            }
            catch
            {
                // ignore errors while querying drives
            }
        }

        private void OnAddClicked(object? sender, RoutedEventArgs e)
        {
            var list = this.FindControl<ListBox>("MediaList");
            var nameBox = this.FindControl<TextBox>("NameTextBox");
            SelectedDrive = list?.SelectedItem as System.IO.DriveInfo;
            EnteredName = nameBox?.Text;
            Close();
        }

        private void OnCancelClicked(object? sender, RoutedEventArgs e)
        {
            _refreshTimer?.Stop();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _refreshTimer?.Stop();
        }
    }
}
