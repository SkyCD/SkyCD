using Avalonia.Controls;
using Avalonia.Input;
using SkyCD.ViewModels;
using Microsoft.EntityFrameworkCore;
using System;
using System.Diagnostics;
using SkyCD.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Interactivity;
using SkyCD.Views;

namespace SkyCD.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // mirror viewmodel properties here so XAML can bind to the window type at compile time
        public bool IsIconMode { get; private set; }
        public bool IsIconGridMode { get; private set; }
        public bool IsDetailsView { get; private set; }
        public bool IsListView { get; private set; }
        public double IconSize { get; private set; }
        public double TextMaxWidth { get; private set; }
        public double DetailsNameWidth { get; private set; }
        public double DetailsTypeWidth { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnOptionsClicked(object? sender, RoutedEventArgs e)
        {
            var opts = new OptionsWindow();
            // show as dialog, non-resizable window set via XAML
            opts.ShowDialog(this);
        }

        private void OnNewClicked(object? sender, RoutedEventArgs e)
        {
            // Clear data in the existing database without deleting the file itself.
            try
            {
                var dbPath = System.IO.Path.Combine(System.AppContext.BaseDirectory ?? ".", "virtualfs.db");
                var connStr = $"Data Source={dbPath}";

                // Ensure database exists (create migrations if missing)
                if (!System.IO.File.Exists(dbPath))
                    DbMigrationRunner.EnsureMigrated(connStr);

                var optionsBuilder = new DbContextOptionsBuilder<SkyCD.Data.VirtualFileSystem.VirtualFileSystemContext>();
                optionsBuilder.UseSqlite(connStr);
                var options = optionsBuilder.Options;

                using var db = new SkyCD.Data.VirtualFileSystem.VirtualFileSystemContext(options);
                // Remove all stored file system items
                db.FileSystemItems.RemoveRange(db.FileSystemItems);
                db.SaveChanges();

                // Reload data in UI by clearing ViewModel collections so the view shows empty
                if (this.DataContext is MainWindowViewModel vm)
                {
                    try
                    {
                        // replace folders with an empty collection and clear selection/files
                        vm.Folders = new System.Collections.ObjectModel.ObservableCollection<SkyCD.Models.VirtualFileSystem.FolderItem>();
                        vm.SelectedFolder = null; // will clear Files via OnSelectedFolderChanged
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void OnAboutClicked(object? sender, RoutedEventArgs e)
        {
            var about = new AboutWindow();
            about.ShowDialog(this);
        }

        private void OnHomePageClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://skycd.sourceforge.net",
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignore failures to open browser
            }
        }

        private void SyncFromViewModel()
        {
            if (DataContext is MainWindowViewModel vm)
            {
                IsIconMode = vm.IsIconMode;
                IsIconGridMode = vm.IsIconGridMode;
                IsListView = vm.IsListView;
                IsDetailsView = vm.IsDetailsView;
                IconSize = vm.IconSize;
                TextMaxWidth = vm.TextMaxWidth;
                DetailsNameWidth = vm.DetailsNameWidth;
                DetailsTypeWidth = vm.DetailsTypeWidth;
                // notify bindings on the Window that mirrored properties changed
                OnPropertyChanged(nameof(IsIconMode));
                OnPropertyChanged(nameof(IsIconGridMode));
                OnPropertyChanged(nameof(IsListView));
                OnPropertyChanged(nameof(IsDetailsView));
                OnPropertyChanged(nameof(IconSize));
                OnPropertyChanged(nameof(TextMaxWidth));
                OnPropertyChanged(nameof(DetailsNameWidth));
                OnPropertyChanged(nameof(DetailsTypeWidth));
            }
            else
            {
                IsIconMode = true;
                IsIconGridMode = true;
                IsListView = false;
                IsDetailsView = false;
                OnPropertyChanged(nameof(IsIconMode));
                OnPropertyChanged(nameof(IsIconGridMode));
                OnPropertyChanged(nameof(IsListView));
                OnPropertyChanged(nameof(IsDetailsView));
            }
        }
        public MainWindow()
        {
            InitializeComponent();
            // initial sync
            SyncFromViewModel();

            // language change handled by App.ReloadMainWindow when options are saved

            // when DataContext becomes available later, sync and listen for changes
            this.DataContextChanged += (_, __) =>
            {
                SyncFromViewModel();
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.PropertyChanged += (s, e) =>
                    {
                        // update mirrored properties on window and raise property changed for bindings
                        SyncFromViewModel();
                        this.InvalidateVisual();
                    };
                }
            };
        }

        // called from XAML GridSplitter PointerReleased to persist column widths
        private void OnDetailsSplitterReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                // try to find header cell width
                var headerName = this.FindControl<TextBlock>("HeaderNameCell");
                if (headerName != null)
                {
                    // set name column width in VM
                    vm.DetailsNameWidth = headerName.Bounds.Width;
                }

                // type column width = remaining width of header grid
                var headerGrid = this.FindControl<Grid>("HeaderGrid");
                if (headerGrid != null && headerName != null)
                {
                    var typeWidth = Math.Max(0, headerGrid.Bounds.Width - headerName.Bounds.Width);
                    vm.DetailsTypeWidth = typeWidth;
                }
            }
        }
    }
}