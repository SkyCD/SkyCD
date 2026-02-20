using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Models;

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
            SeedSampleData();
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

        private void SeedSampleData()
        {
            var documents = new FolderItem { Id = 1, Name = "Documents", Type = FileItemType.Folder };
            var pictures = new FolderItem { Id = 2, Name = "Pictures", Type = FileItemType.Folder };
            var music = new FolderItem { Id = 3, Name = "Music", Type = FileItemType.Folder };

            var work = new FolderItem { Id = 4, Name = "Work", Type = FileItemType.Folder, Parent = documents };
            var personal = new FolderItem { Id = 5, Name = "Personal", Type = FileItemType.Folder, Parent = documents };

            documents.Children.Add(work);
            documents.Children.Add(personal);

            work.Children.Add(new FileItem { Id = 6, Name = "Report.docx", Type = FileItemType.File, Parent = work });
            work.Children.Add(new FileItem { Id = 7, Name = "Notes.txt", Type = FileItemType.File, Parent = work });

            personal.Children.Add(new FileItem { Id = 8, Name = "Resume.pdf", Type = FileItemType.File, Parent = personal });

            pictures.Children.Add(new FileItem { Id = 9, Name = "Vacation.jpg", Type = FileItemType.File, Parent = pictures });
            pictures.Children.Add(new FileItem { Id = 10, Name = "Family.jpg", Type = FileItemType.File, Parent = pictures });

            music.Children.Add(new FileItem { Id = 11, Name = "Song1.mp3", Type = FileItemType.File, Parent = music });
            music.Children.Add(new FileItem { Id = 12, Name = "Song2.mp3", Type = FileItemType.File, Parent = music });

            Folders = new ObservableCollection<FolderItem> { documents, pictures, music };
        }

        public void Dispose()
        {
            // nothing to dispose
        }
    }
}
