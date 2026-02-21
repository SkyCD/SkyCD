using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Models;
using Microsoft.EntityFrameworkCore;
using SkyCD.Data.VirtualFileSystem;

namespace SkyCD.ViewModels
{
    using SkyCD.Models.VirtualFileSystem;
    using SkyCD.Services;

    public partial class MainWindowViewModel : ViewModelBase, IDisposable
    {
        [ObservableProperty]
        private ObservableCollection<FolderItem> _folders = new();

        [ObservableProperty]
        private ObservableCollection<FileItem> _files = new();

        [ObservableProperty]
        private FolderItem? _selectedFolder;

        partial void OnSelectedFolderChanged(FolderItem? value)
        {
            LoadFilesForFolder(value?.Id);
        }

        public MainWindowViewModel()
        {
            LoadFoldersFromDatabase();
            // initialize from saved settings
            _showStatusBar = SettingsService.Current.ShowStatusBar;
            _viewMode = SettingsService.Current.ViewMode;

            // load saved details column widths
            _detailsNameColumnWidth = SettingsService.Current.DetailsNameColumnWidth;
            _detailsTypeColumnWidth = SettingsService.Current.DetailsTypeColumnWidth;

            LoadData();
        }

        [ObservableProperty]
        private ListViewMode _viewMode;

        partial void OnViewModeChanged(ListViewMode value)
        {
            SettingsService.Current.ViewMode = value;
            SettingsService.Save();

            // notify dependent boolean properties and layout properties
            OnPropertyChanged(nameof(IsTilesView));
            OnPropertyChanged(nameof(IsSmallIconsView));
            OnPropertyChanged(nameof(IsLargeIconsView));
            OnPropertyChanged(nameof(IsListView));
            OnPropertyChanged(nameof(IsDetailsView));
            OnPropertyChanged(nameof(IsIconGridMode));
            OnPropertyChanged(nameof(IsIconMode));
            OnPropertyChanged(nameof(IconSize));
            OnPropertyChanged(nameof(TextMaxWidth));
        }

        public bool IsTilesView { get => ViewMode == ListViewMode.Tiles; set { if (value) ViewMode = ListViewMode.Tiles; } }
        public bool IsSmallIconsView { get => ViewMode == ListViewMode.SmallIcons; set { if (value) ViewMode = ListViewMode.SmallIcons; } }
        public bool IsLargeIconsView { get => ViewMode == ListViewMode.LargeIcons; set { if (value) ViewMode = ListViewMode.LargeIcons; } }
        public bool IsListView { get => ViewMode == ListViewMode.List; set { if (value) ViewMode = ListViewMode.List; } }
        public bool IsDetailsView { get => ViewMode == ListViewMode.Details; set { if (value) ViewMode = ListViewMode.Details; } }

        // true for icon-based grid modes (tiles/small/large), false for list or details
        public bool IsIconGridMode => ViewMode == ListViewMode.Tiles || ViewMode == ListViewMode.SmallIcons || ViewMode == ListViewMode.LargeIcons;

        // layout helpers bound from XAML
        public double IconSize
        {
            get
            {
                return ViewMode switch
                {
                    ListViewMode.SmallIcons => 16,
                    ListViewMode.Tiles => 24,
                    ListViewMode.List => 20,
                    ListViewMode.Details => 20,
                    _ => 32,
                };
            }
        }

        public double TextMaxWidth
        {
            get
            {
                return ViewMode switch
                {
                    ListViewMode.SmallIcons => 80,
                    ListViewMode.Tiles => 100,
                    ListViewMode.List => 400,
                    ListViewMode.Details => 400,
                    _ => 100,
                };
            }

        }

        // true when using icon-based modes (tiles/small/large/list) and false for Details
        public bool IsIconMode => ViewMode != ListViewMode.Details;

        private double _detailsNameColumnWidth;
        public double DetailsNameWidth
        {
            get => _detailsNameColumnWidth;
            set
            {
                if (value != _detailsNameColumnWidth)
                {
                    _detailsNameColumnWidth = value;
                    OnPropertyChanged(nameof(DetailsNameWidth));
                    SettingsService.Current.DetailsNameColumnWidth = value;
                    SettingsService.Save();
                }
            }
        }

        private double _detailsTypeColumnWidth;
        public double DetailsTypeWidth
        {
            get => _detailsTypeColumnWidth;
            set
            {
                if (value != _detailsTypeColumnWidth)
                {
                    _detailsTypeColumnWidth = value;
                    OnPropertyChanged(nameof(DetailsTypeWidth));
                    SettingsService.Current.DetailsTypeColumnWidth = value;
                    SettingsService.Save();
                }
            }
        }
        partial void OnShowStatusBarChanged(bool value)
        {
            SettingsService.Current.ShowStatusBar = value;
            SettingsService.Save();
        }

        [ObservableProperty]
        private bool _showStatusBar;

        private void LoadData()
        {
            if (Folders == null)
                Folders = new ObservableCollection<FolderItem>();

            LoadFilesForFolder(null);
        }

        private void LoadFilesForFolder(int? parentId)
        {
            Files.Clear();

            if (parentId == null)
            {
                // by default don't show files when no folder selected
                return;
            }

            var folder = FindFolderById(parentId.Value);
            if (folder != null)
            {
                foreach (var child in folder.Children)
                {
                    if (child is FileItem file && file.Type == FileItemType.File)
                        Files.Add(file);
                }
            }
        }

        private FolderItem? FindFolderById(int id)
        {
            foreach (var root in Folders)
            {
                var found = FindRecursive(root, id);
                if (found != null)
                    return found;
            }

            return null;
        }

        private FolderItem? FindRecursive(IMediaChild node, int id)
        {
            if (node is FileSystemItem fsNode)
            {
                if (fsNode.Id == id && fsNode is FolderItem f)
                    return f;

                if (fsNode is FolderItem folder)
                {
                    foreach (var child in folder.Children)
                    {
                        var found = FindRecursive(child, id);
                        if (found != null)
                            return found;
                    }
                }
            }

            return null;
        }

        private void LoadFoldersFromDatabase()
        {
            try
            {
                // ensure database schema is created/migrated
                DbMigrationRunner.EnsureMigrated();

                var options = new DbContextOptionsBuilder<VirtualFileSystemContext>()
                    .UseSqlite("Data Source=virtualfs.db")
                    .Options;

                using var db = new VirtualFileSystemContext(options);
                var service = new VirtualFileSystemService(db);
                var loaded = service.LoadFolders();

                if (loaded != null && loaded.Count > 0)
                {
                    Folders = loaded;
                }
            }
            catch
            {
               
            }
        }

        public void Dispose()
        {
            // nothing to dispose
        }
    }
}
