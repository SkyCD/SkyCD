using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Documents.Enum;
using SkyCD.UI.Controls.Lists;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly CatalogDocumentRepository? catalogRepository;
    private readonly bool shouldSeedDefaultCatalog;
    private readonly IReadOnlyList<CatalogDocument>? inMemoryCatalogEntries;
    private readonly IStringLocalizer propertyValueLocalizer;
    private IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByKey;
    private IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByTitle;
    private readonly Dictionary<string, string> commentsByObjectKey = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<CatalogDocument>> addedItemsByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HashSet<string>> deletedItemNamesByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, string>> renamedBrowserItemNamesByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> statusTransitions = [];
    private readonly List<int> progressTransitions = [];
    private readonly List<StatusVariantDocument> statusVariants = [];
    private IReadOnlyList<MainMenuItemViewModel> topMenuItems;
    private static string DefaultStatusText => LocalizationService.Current.Get(TextKeys.Done);
    private const string ItemStatusNamePropertyKey = "StatusName";
    private const string ItemStatusIconGlyphPropertyKey = "StatusIconGlyph";
    private const string ItemStatusIconColorPropertyKey = "StatusIconColor";
    private const string ItemStatusIconTextPropertyKey = "StatusIconText";

    public event EventHandler? AddToListRequested;
    public event EventHandler? NewCatalogRequested;
    public event EventHandler? OpenCatalogRequested;
    public event EventHandler? SaveCatalogAsRequested;
    public event EventHandler? SaveCatalogRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler<OptionsDialogRequestedEventArgs>? OptionsRequested;
    public event EventHandler<PropertiesDialogRequestedEventArgs>? PropertiesRequested;
    public event EventHandler? ExitRequested;

    private MenuExtensionManager menuExtensionManager;
    private CancellationTokenSource? pluginCommandCancellation;

    public void RefreshPluginMenuServices(MenuExtensionManager updatedManager)
    {
        menuExtensionManager = updatedManager ?? throw new ArgumentNullException(nameof(updatedManager));
        topMenuItems = BuildTopMenuItems();
        OnPropertyChanged(nameof(TopMenuItems));
        OnPropertyChanged(nameof(FileMenuItems));
        OnPropertyChanged(nameof(EditMenuItems));
        OnPropertyChanged(nameof(ViewMenuItems));
        OnPropertyChanged(nameof(ToolsMenuItems));
        OnPropertyChanged(nameof(HelpMenuItems));
    }

    private MainWindowViewModel(
        IRepository<CatalogDocument> catalogRepository,
        IStringLocalizer propertyValueLocalizer,
        MenuExtensionManager menuExtensionManager,
        bool shouldSeedDefaultCatalog)
    {
        this.catalogRepository = catalogRepository as CatalogDocumentRepository
                                 ?? throw new InvalidOperationException(
                                     "Catalog repository must be CatalogDocumentRepository.");
        this.propertyValueLocalizer =
            propertyValueLocalizer ?? throw new ArgumentNullException(nameof(propertyValueLocalizer));
        this.menuExtensionManager = menuExtensionManager ?? throw new ArgumentNullException(nameof(menuExtensionManager));
        this.shouldSeedDefaultCatalog = shouldSeedDefaultCatalog;

        EnsureSeedData();
        TreeNodes = GetTreeNodes();

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        treeNodesByTitle = allTreeNodes.ToDictionary(static node => node.Title, StringComparer.OrdinalIgnoreCase);
        topMenuItems = BuildTopMenuItems();
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
        RefreshTopMenuState();
        LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public MainWindowViewModel(
        RepositoryManager repositoryManager)
        : this(
            repositoryManager.For<CatalogDocument>(),
            new PropertyValueLocalizer(),
            CreateEmptyMenuExtensionManager(),
            RegisterAppStart(repositoryManager))
    {
    }

    private MainWindowViewModel(
        IReadOnlyList<CatalogDocument> catalogEntries,
        IStringLocalizer? propertyValueLocalizer = null,
        MenuExtensionManager? menuExtensionManager = null)
    {
        inMemoryCatalogEntries = catalogEntries ?? throw new ArgumentNullException(nameof(catalogEntries));
        this.propertyValueLocalizer = propertyValueLocalizer ?? new PropertyValueLocalizer();
        this.menuExtensionManager = menuExtensionManager ?? CreateEmptyMenuExtensionManager();
        shouldSeedDefaultCatalog = false;
        TreeNodes = BuildTreeNodesFromEntries(inMemoryCatalogEntries);

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        treeNodesByTitle = allTreeNodes.ToDictionary(static node => node.Title, StringComparer.OrdinalIgnoreCase);
        topMenuItems = BuildTopMenuItems();
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
        RefreshTopMenuState();
        LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public void RefreshLocalization()
    {
        RefreshPluginMenuServices(menuExtensionManager);
        OnPropertyChanged(nameof(BrowserDetailsColumns));
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        StatusText = DefaultStatusText;
        RefreshBrowserItemsForSelection();
    }

    public static MainWindowViewModel CreateForInMemoryCatalog(
        IReadOnlyList<CatalogDocument> catalogEntries,
        IStringLocalizer? propertyValueLocalizer = null,
        MenuExtensionManager? menuExtensionManager = null)
    {
        return new MainWindowViewModel(catalogEntries, propertyValueLocalizer, menuExtensionManager);
    }

    private static MenuExtensionManager CreateEmptyMenuExtensionManager()
    {
        return new MenuExtensionManager(Array.Empty<IMenuPluginCapability>());
    }

    private static bool RegisterAppStart(RepositoryManager repositoryManager)
    {
        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        options.AppStartCount++;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);
        return options.AppStartCount <= 1;
    }

    public IReadOnlyList<BrowserTreeNode> TreeNodes { get; private set; }

    public IReadOnlyList<BrowserDetailsColumn> BrowserDetailsColumns =>
    [
        new()
        {
            Header = LocalizationService.Current.Get(TextKeys.Name2), ValuePath = "Name",
            Width = new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star)
        },
        new()
        {
            Header = LocalizationService.Current.Get(TextKeys.Type), ValuePath = "DisplayType",
            Width = new Avalonia.Controls.GridLength(150, Avalonia.Controls.GridUnitType.Pixel)
        },
        new()
        {
            Header = LocalizationService.Current.Get(TextKeys.Size), ValuePath = "DisplaySize",
            Width = new Avalonia.Controls.GridLength(120, Avalonia.Controls.GridUnitType.Pixel),
            HeaderAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            ValueAlignment = Avalonia.Layout.HorizontalAlignment.Right
        }
    ];

    public bool IsSaveEnabled => IsDirtyDocument;

    public bool IsDeleteEnabled => GetSelectedBrowserItems().Count > 0;

    public bool IsPropertiesEnabled => SelectedBrowserItem is not null || SelectedTreeNode is not null;

    public string ProgressText => $"{ProgressValue}%";

    public bool IsTilesViewChecked => CurrentViewMode == BrowserViewMode.Tiles;

    public bool IsSmallIconsViewChecked => CurrentViewMode == BrowserViewMode.SmallIcons;

    public bool IsLargeIconsViewChecked => CurrentViewMode == BrowserViewMode.LargeIcons;

    public bool IsListViewChecked => CurrentViewMode == BrowserViewMode.List;

    public bool IsDetailsViewChecked => CurrentViewMode == BrowserViewMode.Details;

    public bool IsSortByNameChecked => IsSortMode("Name");

    public bool IsSortByTypeChecked => IsSortMode("Type");

    public bool IsSortBySizeChecked => IsSortMode("Size");

    public bool IsDetailsMode => CurrentViewMode == BrowserViewMode.Details;

    public bool IsListMode => CurrentViewMode == BrowserViewMode.List;

    public bool IsSmallIconsMode => CurrentViewMode == BrowserViewMode.SmallIcons;

    public bool IsLargeIconsMode => CurrentViewMode == BrowserViewMode.LargeIcons;

    public bool IsIconGridMode =>
        CurrentViewMode is BrowserViewMode.Tiles or BrowserViewMode.SmallIcons or BrowserViewMode.LargeIcons;

    public bool IsListLikeMode => !IsIconGridMode;

    public bool IsTilesMode => CurrentViewMode == BrowserViewMode.Tiles;

    public double BrowserIconFontSize => CurrentViewMode switch
    {
        BrowserViewMode.SmallIcons => 14,
        BrowserViewMode.LargeIcons => 24,
        BrowserViewMode.Tiles => 20,
        _ => 16
    };

    public double BrowserGridItemWidth => CurrentViewMode switch
    {
        BrowserViewMode.SmallIcons => 120,
        BrowserViewMode.LargeIcons => 170,
        BrowserViewMode.Tiles => 300,
        _ => 220
    };

    public double BrowserGridItemHeight => CurrentViewMode switch
    {
        BrowserViewMode.LargeIcons => 90,
        BrowserViewMode.Tiles => 80,
        _ => 60
    };

    public bool ShowDetailsColumns => CurrentViewMode == BrowserViewMode.Details;

    public IReadOnlyList<string> StatusTransitions => statusTransitions;

    public IReadOnlyList<int> ProgressTransitions => progressTransitions;

    public IReadOnlyList<MainMenuItemViewModel> TopMenuItems => topMenuItems;
    public IReadOnlyList<MainMenuItemViewModel> FileMenuItems => topMenuItems[0].Items;
    public IReadOnlyList<MainMenuItemViewModel> EditMenuItems => topMenuItems[1].Items;
    public IReadOnlyList<MainMenuItemViewModel> ViewMenuItems => topMenuItems[2].Items;
    public IReadOnlyList<MainMenuItemViewModel> ToolsMenuItems => topMenuItems[3].Items;
    public IReadOnlyList<MainMenuItemViewModel> HelpMenuItems => topMenuItems[4].Items;

    public IReadOnlyList<MainMenuItemViewModel> BrowserContextMenuItems => BuildBrowserContextMenuItems();

    public IReadOnlyList<MainMenuItemViewModel> TreeContextMenuItems => BuildTreeContextMenuItems();

    [ObservableProperty] private IReadOnlyList<CatalogDocument> browserItems = [];

    [ObservableProperty] private BrowserTreeNode? selectedTreeNode;

    [ObservableProperty] private CatalogDocument? selectedBrowserItem;
    [ObservableProperty] private System.Collections.IList selectedBrowserItems = new List<CatalogDocument>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTilesViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsSmallIconsViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsLargeIconsViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsListViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsDetailsViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsDetailsMode))]
    [NotifyPropertyChangedFor(nameof(IsListMode))]
    [NotifyPropertyChangedFor(nameof(IsSmallIconsMode))]
    [NotifyPropertyChangedFor(nameof(IsLargeIconsMode))]
    [NotifyPropertyChangedFor(nameof(IsIconGridMode))]
    [NotifyPropertyChangedFor(nameof(IsListLikeMode))]
    [NotifyPropertyChangedFor(nameof(IsTilesMode))]
    [NotifyPropertyChangedFor(nameof(BrowserIconFontSize))]
    [NotifyPropertyChangedFor(nameof(BrowserGridItemWidth))]
    [NotifyPropertyChangedFor(nameof(BrowserGridItemHeight))]
    [NotifyPropertyChangedFor(nameof(ShowDetailsColumns))]
    private BrowserViewMode currentViewMode = BrowserViewMode.Details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortByNameChecked))]
    [Nouß~õ¶‰ËkºwµçHØ]Ú
^Ù\[Ûˆ^
BˆÂˆİ]\Õ^HØØ[^˜][Û”Ù\šXÙKİ\œ™[‘›Ü›X]
^Ù^\Ë”YÚ[ÛÛ[X[™\œ›Ü‹^“Y\ÜØYÙJNÂˆBˆš[˜[HÈYÚ[ÛÛ[X[™Ø[˜Ù[][ÛˆH[ÈBˆB‚ˆš]˜]HÙX[YÛ\ÜÈšY]Ó[Ù[ÜİÛÛ[X[™\JXZ[•Ú[™İÕšY]Ó[Ù[šY]Ó[Ù[
HˆPØ][ÙĞØ\\™RÜİ\BˆÂˆš]˜]H™XYÛ›Hİš[™ÏÈ›ÛİYHšY]Ó[Ù[•™YS›Ù\Ë‘š\œİÜ‘Y˜][

OË’Ù^NÂˆš]˜]H™XYÛ›Hİš[™ÏÈØ][ÙÔ]HšY]Ó[Ù[İ\œ™[Ø][ÙÔ]Â‚ˆX›XÈ\Ş[˜È\ÚÈ[\ÜØ\\™Yš[\Ğ\Ş[˜ÊT™XYÛ›S\İØ\\™YØ][ÙÑš[Oˆš[\ËØ[˜Ù[][Û•ÚÙ[ˆØ[˜Ù[][Û•ÚÙ[ˆHY˜][
BˆÂˆ˜\ˆ™\ÜÚ]ÜHHšY]Ó[Ù[˜Ø][ÙÔ™\ÜÚ]ÜNÂˆYˆ
™\ÜÚ]ÜH\È[›ÛİY\È[Ø][ÙÔ]OHšY]Ó[Ù[İ\œ™[Ø][ÙÔ]
Bˆ›İÈ™]È[İ[ÛØ\\™PØ][ÙÕ[˜]˜Z[X›Q^Ù\[ÛŠ
NÂˆ]ØZ]\ÚË”[Š

HOˆ™\ÜÚ]ÜKÛÛXİ[Û‹‘]X˜\ÙK’[˜]Ú


HO‚ˆÂˆİš[™È›Û\Šİš[™È\™[İš[™È˜[YJBˆÂˆ˜\ˆ^\İ[™ÈH™\ÜÚ]ÜK‘Ù]Ú[™[“ÙŠ\™[
K‘š\œİÜ‘Y˜][
Oˆ“˜[YHOH˜[YH	‰ˆ•\HOHØ][ÙÑØİ[Y[\K‘›Û\ŠNÂˆYˆ
^\İ[™È\È›İ[
H™]\›ˆ^\İ[™Ë’YÂˆ˜\ˆYHİZY“™]ÑİZY

K•Ôİš[™Ê“ˆŠNÂˆ™\ÜÚ]ÜK”Ø]™JY™]ÈØ][ÙÑØİ[Y[ÈYHY\™[YH\™[˜[YHH˜[YK\HHØ][ÙÑØİ[Y[\K‘›Û\ˆJNÂˆ™]\›ˆYÂˆBˆ›Ü™XXÚ
˜\ˆš[H[ˆš[\ÊBˆÂˆØ[˜Ù[][Û•ÚÙ[‹•›İÒYØ[˜Ù[][Û”™\]Y\İY

NÂˆ˜\ˆ\™[H›Û\Š›Û\Š›ÛİYš[K‘›Û\ŠKš[K‘]JNÂˆ˜\ˆYHİZY“™]ÑİZY

K•Ôİš[™Ê“ˆŠNÂˆ˜\ˆ›Ü\Y\ÈH™]È›Ü\Y\ĞÛÛXİ[ÛŠ
NÂˆ›Ü™XXÚ
˜\ˆ›Ü\H[ˆš[K”›Ü\Y\ÊH›Ü\Y\ÖÜ›Ü\K’Ù^WHH›Ü\K•˜[YNÂˆ™\ÜÚ]ÜK”Ø]™JY™]ÈØ][ÙÑØİ[Y[ÈYHY\™[YH\™[˜[YHHš[K“˜[YKÚ^™HHš[K”Ú^™K›Ü\Y\ÈH›Ü\Y\ÈJNÂˆBˆJKØ[˜Ù[][Û•ÚÙ[ŠNÂˆšY]Ó[Ù[•™YS›Ù\ÈHšY]Ó[Ù[‘Ù]™YS›Ù\Ê
NÂˆ˜\ˆ›Ù\ÈH›][“›Ù\ÊšY]Ó[Ù[•™YS›Ù\ÊK•Ğ\œ˜^J
NÂˆšY]Ó[Ù[™YS›Ù\ĞRÙ^HH›Ù\Ë•ÑXİ[Û˜\JOˆ’Ù^Kİš[™ĞÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂˆšY]Ó[Ù[™YS›Ù\ĞU]HH›Ù\Ë‘Ü›İ\JOˆ•]Kİš[™ĞÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJK•ÑXİ[Û˜\JOˆ’Ù^KOˆ‘š\œİ

Kİš[™ĞÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂˆšY]Ó[Ù[“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ™YS›Ù\ÊJNÂˆšY]Ó[Ù[”™Yœ™\Úœ›İÜÙ\’][\Ñ›Ü”Ù[Xİ[ÛŠ
NÂˆšY]Ó[Ù[’\Ñ\QØİ[Y[HYNÂˆBˆX›XÈ\ÚÈ˜]šYØ]UÓ›ÙP\Ş[˜ÊÛ™È›ÙRYØ[˜Ù[][Û•ÚÙ[ˆØ[˜Ù[][Û•ÚÙ[ˆHY˜][
BˆÂˆšY]Ó[Ù[”İ]\Õ^HØØ[^˜][Û”Ù\šXÙKİ\œ™[‘›Ü›X]
^Ù^\Ë“˜]šYØ]YÓ›ÙK›ÙRY
NÂˆ™]\›ˆ\ÚËÛÛ\]Y\ÚÎÂˆB‚ˆX›XÈ\ÚÈ›İYP\Ş[˜Êİš[™ÈY\ÜØYÙKØ[˜Ù[][Û•ÚÙ[ˆØ[˜Ù[][Û•ÚÙ[ˆHY˜][
BˆÂˆšY]Ó[Ù[”İ]\Õ^HY\ÜØYÙNÂˆ™]\›ˆ\ÚËÛÛ\]Y\ÚÎÂˆBˆB‚ˆš]˜]Hİ]XÈXZ[“Y[R][UšY]Ó[Ù[Ù\\˜]ÜŠ
BˆÂˆ™]\›ˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆH‹HˆNÂˆB‚ˆš]˜]H›ÚY™Yœ™\ÚÜY[Tİ]J
BˆÂˆ˜\ˆRÙ^HH›][“Y[R][\ÊÜY[R][\ÊBˆ•Ú\™J][HOˆ\İš[™Ë’\Ó[Ü•Ú]TÜXÙJ][K’Ù^JJBˆ•ÑXİ[Û˜\J][HOˆ][K’Ù^HKİš[™ĞÛÛ\\™\‹“Ü™[˜[
NÂ‚ˆÙ]Y[RXY\ŠRÙ^Kœİ]\Ø˜\ˆ‹ÚXÚÙYXY\Š\Ôİ]\Ğ˜\•š\ÚX›KØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”İ]\Ğ˜\ŠJJNÂˆÙ]Y[RXY\ŠRÙ^KšY]×İ[\È‹ÚXÚÙYXY\Š\Õ[\ÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë•[\ÊJJNÂˆÙ]Y[RXY\ŠRÙ^KšY]×ÜÛX[‹ÚXÚÙYXY\Š\ÔÛX[XÛÛœÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”ÛX[XÛÛœÌŠJJNÂˆÙ]Y[RXY\ŠRÙ^KšY]×Û\™ÙH‹ÚXÚÙYXY\Š\Ó\™ÙRXÛÛœÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“\™ÙRXÛÛœÓY[JJJNÂˆÙ]Y[RXY\ŠRÙ^KšY]×Û\İ‹ÚXÚÙYXY\Š\Ó\İšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“\İ
JJNÂˆÙ]Y[RXY\ŠRÙ^KšY]×Ù]Z[È‹ÚXÚÙYXY\Š\Ñ]Z[ÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë‘]Z[ÊJJNÂˆÙ]Y[RXY\ŠRÙ^KœÛÜÛ˜[YH‹ÚXÚÙYXY\Š\ÔÛÜS˜[YPÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“˜[YJJJNÂˆÙ]Y[RXY\ŠRÙ^KœÛÜİ\H‹ÚXÚÙYXY\Š\ÔÛÜU\PÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë•\LŠJJNÂˆÙ]Y[RXY\ŠRÙ^KœÛÜÜÚ^™H‹ÚXÚÙYXY\Š\ÔÛÜTÚ^™PÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”Ú^™LŠJJNÂˆÙ]Y[Q[˜X›Y
RÙ^K™Y]Üİ]\È‹\Ñ[]Q[˜X›Y
NÂˆÙ]Y[R][\ÊRÙ^K™Y]Üİ]\È‹Z[İ]\ÓY[R][\Ê
JNÂˆB‚ˆš]˜]Hİ]XÈQ[[Y\˜X›OXZ[“Y[R][UšY]Ó[Ù[ˆ›][“Y[R][\ÊQ[[Y\˜X›OXZ[“Y[R][UšY]Ó[Ù[ˆ][\ÊBˆÂˆ›Ü™XXÚ
˜\ˆ][H[ˆ][\ÊBˆÂˆZY[™]\›ˆ][NÂˆ›Ü™XXÚ
˜\ˆÚ[[ˆ›][“Y[R][\Ê][K’][\ÊJBˆÂˆZY[™]\›ˆÚ[ÂˆBˆBˆB‚ˆš]˜]Hİ]XÈ›ÚYÙ]Y[RXY\ŠˆT™XYÛ›QXİ[Û˜\Oİš[™ËXZ[“Y[R][UšY]Ó[Ù[ˆRÙ^Kˆİš[™ÈÙ^Kˆİš[™ÈXY\ŠBˆÂˆYˆ
RÙ^K•QÙ]˜[YJÙ^Kİ]˜\ˆ][JJBˆÂˆ][K’XY\ˆHXY\ÂˆBˆB‚ˆš]˜]Hİ]XÈ›ÚYÙ]Y[Q[˜X›Y
ˆT™XYÛ›QXİ[Û˜\Oİš[™ËXZ[“Y[R][UšY]Ó[Ù[ˆRÙ^Kˆİš[™ÈÙ^Kˆ›ÛÛ\Ñ[˜X›Y
BˆÂˆYˆ
RÙ^K•QÙ]˜[YJÙ^Kİ]˜\ˆ][JJBˆÂˆ][K’\Ñ[˜X›YH\Ñ[˜X›YÂˆBˆB‚ˆš]˜]Hİ]XÈ›ÚYÙ]Y[R][\ÊˆT™XYÛ›QXİ[Û˜\Oİš[™ËXZ[“Y[R][UšY]Ó[Ù[ˆRÙ^Kˆİš[™ÈÙ^KˆT™XYÛ›S\İXZ[“Y[R][UšY]Ó[Ù[ˆ][\ÊBˆÂˆYˆ
RÙ^K•QÙ]˜[YJÙ^Kİ]˜\ˆ][JJBˆÂˆ][K’][\ÈH][\ÎÂˆBˆB‚ˆš]˜]Hİ]XÈİš[™ÈÚXÚÙYXY\Š›ÛÛ\ĞÚXÚÙYİš[™È]JBˆÂˆ™]\›ˆ]NÂˆB‚ˆš]˜]Hİ]XÈXZ[“Y[R][UšY]Ó[Ù[ÚXÚÙYY[R][Jˆ›ÛÛ\ĞÚXÚÙYˆİš[™È]KˆT™[^PÛÛ[X[™ÛÛ[X[™ˆØš™XİÈÛÛ[X[™\˜[Y]\ˆH[ˆİš[™ÏÈÙ^HH[
BˆÂˆ™]\›ˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÂˆÙ^HHÙ^KˆXY\ˆHÚXÚÙYXY\Š\ĞÚXÚÙY]JKˆXÛÛˆH\ĞÚXÚÙYÈ¸§$Èˆˆ—LL‹ˆÛÛ[X[™HÛÛ[X[™ˆÛÛ[X[™\˜[Y]\ˆHÛÛ[X[™\˜[Y]\‚ˆNÂˆB‚ˆš]˜]HT™XYÛ›S\İXZ[“Y[R][UšY]Ó[Ù[ˆZ[œ›İÜÙ\ÛÛ^Y[R][\Ê
BˆÂˆ™]\›‚ˆÂˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë‘^[™
KÛÛ[X[™H^[™Ù[Xİ[ÛÛÛ[X[™ÛÛ[X[™\˜[Y]\ˆH›\İˆKˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\ËÛÛ\ÙJKÛÛ[X[™HÛÛ\ÙTÙ[Xİ[ÛÛÛ[X[™ÛÛ[X[™\˜[Y]\ˆH›\İˆKˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÂˆXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”İ]\ÊKˆ][\ÈHZ[İ]\ÓY[R][\Ê
BˆKˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÂˆXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë•šY]ÊKˆ][\ÈBˆÂˆÚXÚÙYY[R][J\ÔÛX[XÛÛœÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”ÛX[XÛÛœÌŠKÙ]šY]Ó[ÙPÛÛ[X[™”ÛX[XÛÛœÈŠKˆÚXÚÙYY[R][J\Ó\™ÙRXÛÛœÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“\™ÙRXÛÛœÓY[JKÙ]šY]Ó[ÙPÛÛ[X[™“\™ÙRXÛÛœÈŠKˆÚXÚÙYY[R][J\Ó\İšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“\İ
KÙ]šY]Ó[ÙPÛÛ[X[™“\İŠKˆÚXÚÙYY[R][J\Ñ]Z[ÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë‘]Z[ÊKÙ]šY]Ó[ÙPÛÛ[X[™‘]Z[ÈŠKˆÚXÚÙYY[R][J\Õ[\ÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë•[\ÊKÙ]šY]Ó[ÙPÛÛ[X[™•[\ÈŠBˆBˆKˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÂˆXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë\œ˜[™ÙRXÛÛœĞLŠKˆ][\ÈBˆÂˆÚXÚÙYY[R][J\ÔÛÜS˜[YPÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“˜[YJKÙ]ÛÜ[ÙPÛÛ[X[™“˜[YHŠKˆÚXÚÙYY[R][J\ÔÛÜU\PÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë•\LŠKÙ]ÛÜ[ÙPÛÛ[X[™•\HŠBˆBˆKˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”™Yœ™\Ú
KÛÛ[X[™H™Yœ™\ÚÛÛ[X[™KˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\ËY
KÛÛ[X[™HY][PÛÛ[X[™Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë‘[]JKÛÛ[X[™H[]R][PÛÛ[X[™KˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”›Ü\Y\ÊKÛÛ[X[™HÜ[”›Ü\Y\ĞÛÛ[X[™BˆNÂˆB‚ˆš]˜]HT™XYÛ›S\İXZ[“Y[R][UšY]Ó[Ù[ˆZ[™YPÛÛ^Y[R][\Ê
BˆÂˆ™]\›‚ˆÂˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë‘^[™
KÛÛ[X[™H^[™Ù[Xİ[ÛÛÛ[X[™ÛÛ[X[™\˜[Y]\ˆH™YHˆKˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\ËÛÛ\ÙJKÛÛ[X[™HÛÛ\ÙTÙ[Xİ[ÛÛÛ[X[™ÛÛ[X[™\˜[Y]\ˆH™YHˆKˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÂˆXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë•šY]ÊKˆ][\ÈBˆÂˆÚXÚÙYY[R][J\ÔÛX[XÛÛœÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”ÛX[XÛÛœÌŠKÙ]šY]Ó[ÙPÛÛ[X[™”ÛX[XÛÛœÈŠKˆÚXÚÙYY[R][J\Ó\™ÙRXÛÛœÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“\™ÙRXÛÛœÓY[JKÙ]šY]Ó[ÙPÛÛ[X[™“\™ÙRXÛÛœÈŠKˆÚXÚÙYY[R][J\Ó\İšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“\İ
KÙ]šY]Ó[ÙPÛÛ[X[™“\İŠKˆÚXÚÙYY[R][J\Ñ]Z[ÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë‘]Z[ÊKÙ]šY]Ó[ÙPÛÛ[X[™‘]Z[ÈŠKˆÚXÚÙYY[R][J\Õ[\ÕšY]ĞÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë•[\ÊKÙ]šY]Ó[ÙPÛÛ[X[™•[\ÈŠBˆBˆKˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÂˆXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë\œ˜[™ÙRXÛÛœĞLŠKˆ][\ÈBˆÂˆÚXÚÙYY[R][J\ÔÛÜS˜[YPÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë“˜[YJKÙ]ÛÜ[ÙPÛÛ[X[™“˜[YHŠKˆÚXÚÙYY[R][J\ÔÛÜU\PÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë•\LŠKÙ]ÛÜ[ÙPÛÛ[X[™•\HŠKˆÚXÚÙYY[R][J\ÔÛÜTÚ^™PÚXÚÙYØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”Ú^™LŠKÙ]ÛÜ[ÙPÛÛ[X[™”Ú^™HŠBˆBˆKˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”™Yœ™\Ú
KÛÛ[X[™H™Yœ™\ÚÛÛ[X[™KˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\ËY
KÛÛ[X[™HY][PÛÛ[X[™Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë‘[]JKÛÛ[X[™H[]R][PÛÛ[X[™KˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\ËÛÜJKİÙ^HHİ›
ĞÈ‹ÛÛ[X[™HÛÜPÛÛ[X[™Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”\İJKİÙ^HHİ›
Õˆ‹ÛÛ[X[™H\İPÛÛ[X[™Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ëİ]Y[JKİÙ^HHİ›
Ö‹ÛÛ[X[™Hİ]ÛÛ[X[™KˆÙ\\˜]ÜŠ
Kˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ÈXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë”›Ü\Y\ÊKÛÛ[X[™HÜ[”›Ü\Y\ĞÛÛ[X[™BˆNÂˆB‚ˆš]˜]H›ÚY[œİ\™TÙYY]J
BˆÂˆYˆ
\Úİ[ÙYYY˜][Ø][ÙÈØ][ÙÔ™\ÜÚ]ÜH\È[Ø][ÙÔ™\ÜÚ]ÜK‘Ù][

KÛİ[ˆ
BˆÂˆ™]\›ÂˆB‚ˆ›Ü™XXÚ
˜\ˆ[H[ˆØ][ÙÔ™\ÜÚ]ÜKÜ™X]QY˜][[šY\Ê
JBˆÂˆØ][ÙÔ™\ÜÚ]ÜK”Ø]™J[K’Y[JNÂˆBˆB‚ˆš]˜]HT™XYÛ›S\İœ›İÜÙ\•™YS›ÙOˆÙ]™YS›Ù\Ê
BˆÂˆYˆ
Ø][ÙÔ™\ÜÚ]ÜH\È›İ[
BˆÂˆ˜\ˆ›ÛİÈHØ][ÙÔ™\ÜÚ]ÜBˆ‘Ù]›ÛİÊ
Bˆ•Ú\™J\Õ™YUš\ÚX›U\JBˆ•Ğ\œ˜^J
NÂ‚ˆ˜\ˆ™YS›Ù\ÈH›ÛİÂˆ”Ù[Xİ
›ÛİO‚ˆÂˆ˜\ˆ\ØÙ[™[ÈHØ][ÙÔ™\ÜÚ]ÜBˆ‘Ù]\ØÙ[™[ÓÙŠ›Ûİ’Y
Bˆ•Ú\™J\Õ™YUš\ÚX›U\JBˆ•Ó\İ

NÂˆ\ØÙ[™[ËY
›Ûİ
NÂ‚ˆ˜\ˆT\™[H\ØÙ[™[Âˆ‘Ü›İ\J[HOˆ[K”\™[YÏÈ—×Ü›Ûİ×È‹İš[™ĞÛÛ\\™\‹“Ü™[˜[
Bˆ•ÑXİ[Û˜\JÜ›İ\OˆÜ›İ\’Ù^KÜ›İ\OˆÜ›İ\•Ó\İ

Kİš[™ĞÛÛ\\™\‹“Ü™[˜[
NÂ‚ˆ™]\›ˆZ[™YS›ÙQœ›ÛSÛÚİ\
›ÛİT\™[\Ñ^[™YˆYJNÂˆJBˆ•Ğ\œ˜^J
NÂ‚ˆYˆ
™YS›Ù\Ë“[™İˆ
BˆÂˆ™]\›ˆ™YS›Ù\ÎÂˆB‚ˆ™]\›ˆ×NÂˆB‚ˆ™]\›ˆZ[™YS›Ù\Ñœ›ÛQ[šY\Ê[“Y[[ÜPØ][ÙÑ[šY\ÈÏÈ×JNÂˆB‚ˆš]˜]HT™XYÛ›S\İØ][ÙÑØİ[Y[ˆÙ]œ›İÜÙ\’][\Êİš[™È›ÙRÙ^JBˆÂˆYˆ
İš[™Ë’\Ó[Ü•Ú]TÜXÙJ›ÙRÙ^JJBˆÂˆ™]\›ˆ×NÂˆB‚ˆYˆ
Ø][ÙÔ™\ÜÚ]ÜH\È›İ[
BˆÂˆ˜\ˆ[šY\ÈHØ][ÙÔ™\ÜÚ]ÜK‘Ù]Ú[™[“ÙŠ›ÙRÙ^JNÂˆ™]\›ˆ[šY\ÎÂˆB‚ˆ˜\ˆ[“Y[[ÜQ[šY\ÈH
[“Y[[ÜPØ][ÙÑ[šY\ÈÏÈ×JBˆ•Ú\™J][HOˆİš[™Ë‘\]X[Ê][K”\™[Y›ÙRÙ^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆ•Ğ\œ˜^J
NÂˆ™]\›ˆ[“Y[[ÜQ[šY\ÎÂˆB‚ˆš]˜]H›ÚY[]PØ][ÙÑ[Qœ›ÛT™\ÜÚ]ÜJØ][ÙÑØİ[Y[][JBˆÂˆYˆ
Ø][ÙÔ™\ÜÚ]ÜH\È[
BˆÂˆ™]\›ÂˆB‚ˆ˜\ˆ\ØÙ[™[YÈHØ][ÙÔ™\ÜÚ]ÜBˆ‘Ù]\ØÙ[™[ÓÙŠ][K’Y
Bˆ”Ù[Xİ
İ]XÈ[HOˆ[K’Y
Bˆ•Ú\™Jİ]XÈYOˆ\İš[™Ë’\Ó[Ü•Ú]TÜXÙJY
JBˆ‘\İ[˜İ
İš[™ĞÛÛ\\™\‹“Ü™[˜[
Bˆ•Ğ\œ˜^J
NÂ‚ˆ›Ü™XXÚ
˜\ˆ\ØÙ[™[Y[ˆ\ØÙ[™[YÊBˆÂˆ\Ú[™È˜\ˆ\ØÙ[™[Øİ[Y[HØ][ÙÔ™\ÜÚ]ÜKÛÛXİ[Û‹‘Ù]Øİ[Y[
\ØÙ[™[Y
NÂˆYˆ
\ØÙ[™[Øİ[Y[\È›İ[
BˆÂˆØ][ÙÔ™\ÜÚ]ÜKÛÛXİ[Û‹‘[]J\ØÙ[™[Øİ[Y[
NÂˆBˆB‚ˆ\Ú[™È˜\ˆ\™Ù]Øİ[Y[HØ][ÙÔ™\ÜÚ]ÜKÛÛXİ[Û‹‘Ù]Øİ[Y[
][K’Y
NÂˆYˆ
\™Ù]Øİ[Y[\È›İ[
BˆÂˆØ][ÙÔ™\ÜÚ]ÜKÛÛXİ[Û‹‘[]J\™Ù]Øİ[Y[
NÂˆBˆB‚ˆš]˜]H›Ü\Y\ĞÛÛXİ[ÛˆÙ]œ›İÜÙ\’][R[™›Ô›Ü\Y\Êİš[™È][RY
BˆÂˆYˆ
İš[™Ë’\Ó[Ü•Ú]TÜXÙJ][RY
JBˆÂˆ™]\›ˆ™]È›Ü\Y\ĞÛÛXİ[ÛŠ
NÂˆB‚ˆYˆ
Ø][ÙÔ™\ÜÚ]ÜH\È›İ[
BˆÂˆ™]\›ˆØ][ÙÔ™\ÜÚ]ÜK‘Ù]
][RY
OË”›Ü\Y\ÈÏÈ™]È›Ü\Y\ĞÛÛXİ[ÛŠ
NÂˆB‚ˆ™]\›ˆ
[“Y[[ÜPØ][ÙÑ[šY\ÈÏÈ×JBˆ‘š\œİÜ‘Y˜][
[HOˆİš[™Ë‘\]X[Ê[K’Y][RYİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆË”›Ü\Y\ÈÏÈ™]È›Ü\Y\ĞÛÛXİ[ÛŠ
NÂˆB‚ˆš]˜]Hİ]XÈœ›İÜÙ\•™YS›ÙHZ[™YS›ÙQœ›ÛSÛÚİ\
ˆØ][ÙÑØİ[Y[[KˆT™XYÛ›QXİ[Û˜\Oİš[™Ë\İØ][ÙÑØİ[Y[ˆT\™[ˆ›ÛÛ\Ñ^[™Y
BˆÂˆT\™[•QÙ]˜[YJ[K’Yİ]˜\ˆÚ[™[“Ùİ\œ™[
NÂ‚ˆ˜\ˆÚ[™[ˆH
Ú[™[“Ùİ\œ™[ÏÈ×JBˆ”Ù[Xİ
Ú[OˆZ[™YS›ÙQœ›ÛSÛÚİ\
Ú[T\™[\Ñ^[™Yˆ˜[ÙJJBˆ•Ğ\œ˜^J
NÂ‚ˆ™]\›ˆ™]Èœ›İÜÙ\•™YS›ÙJˆ[K’Yˆ[K“˜[YKˆ[K•\K”™\ÛÛ™RXÛÛ‘Û\

KˆÚ[™[‹ˆ\Ñ^[™Y
NÂˆB‚ˆš]˜]Hİ]XÈT™XYÛ›S\İœ›İÜÙ\•™YS›ÙOˆZ[™YS›Ù\Ñœ›ÛQ[šY\ÊT™XYÛ›S\İØ][ÙÑØİ[Y[ˆ[šY\ÊBˆÂˆ˜\ˆš[\™Y[šY\ÈH[šY\Âˆ•Ú\™J\Õ™YUš\ÚX›U\JBˆ•Ó\İ

NÂˆ˜\ˆRYHš[\™Y[šY\Ë•ÑXİ[Û˜\J[HOˆ[K’Yİš[™ĞÛÛ\\™\‹“Ü™[˜[
NÂ‚ˆ™]\›ˆš[\™Y[šY\Âˆ•Ú\™J[HOˆİš[™Ë’\Ó[Ü•Ú]TÜXÙJ[K”\™[Y
JBˆ”Ù[Xİ
[HOˆZ[™YS›ÙJ[KRY\Ñ^[™YˆYJJBˆ•Ğ\œ˜^J
NÂˆB‚ˆš]˜]Hİ]XÈœ›İÜÙ\•™YS›ÙHZ[™YS›ÙJˆØ][ÙÑØİ[Y[[KˆT™XYÛ›QXİ[Û˜\Oİš[™ËØ][ÙÑØİ[Y[ˆRYˆ›ÛÛ\Ñ^[™Y
BˆÂˆ˜\ˆÚ[™[ˆHRY•˜[Y\Âˆ•Ú\™JØ[™Y]HOˆİš[™Ë‘\]X[ÊØ[™Y]K”\™[Y[K’Yİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆ”Ù[Xİ
Ú[OˆZ[™YS›ÙJÚ[RY\Ñ^[™Yˆ˜[ÙJJBˆ•Ğ\œ˜^J
NÂ‚ˆ™]\›ˆ™]Èœ›İÜÙ\•™YS›ÙJˆ[K’Yˆ[K“˜[YKˆ[K•\K”™\ÛÛ™RXÛÛ‘Û\

KˆÚ[™[‹ˆ\Ñ^[™Y
NÂˆB‚ˆš]˜]Hİ]XÈ›ÛÛ\Õ™YUš\ÚX›U\JØ][ÙÑØİ[Y[[JBˆÂˆ™]\›ˆ[K•\H\ÈØ][ÙÑØİ[Y[\K‘›Û\‚ˆÜˆØ][ÙÑØİ[Y[\K“™]ÛÜšÑ›Û\‚ˆÜˆØ][ÙÑØİ[Y[\K“YYXNÂˆB‚ˆš]˜]Hİ]XÈ›ÛÛ\Ğ\ÜÚYÛ™Yİ]\ÊØ][ÙÑØİ[Y[È][JBˆÂˆYˆ
][H\È[
BˆÂˆ™]\›ˆ˜[ÙNÂˆB‚ˆYˆ
\İš[™Ë’\Ó[Ü•Ú]TÜXÙJ][K”İ]\ÊJBˆÂˆ™]\›ˆYNÂˆB‚ˆ™]\›ˆ][K”›Ü\Y\Ë•QÙ]˜[YJ][Tİ]\Ó˜[YT›Ü\RÙ^Kİ]˜\ˆ˜[YJH	‰‚ˆ\İš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜[YOË•Ôİš[™Ê
JNÂˆB‚ˆš]˜]Hİ]XÈ›ÛÛ\Ğ\ÜÚYÛ™Yİ]\ÊØ][ÙÑØİ[Y[È][Kİš[™ÏÈ^XİYİ]\Ó˜[YJBˆÂˆYˆ
][H\È[
BˆÂˆ™]\›ˆ˜[ÙNÂˆB‚ˆ˜\ˆ\ÜÚYÛ™Yİ]\Ó˜[YHH\İš[™Ë’\Ó[Ü•Ú]TÜXÙJ][K”İ]\ÊBˆÈ][K”İ]\Âˆˆ][K”›Ü\Y\Ë•QÙ]˜[YJ][Tİ]\Ó˜[YT›Ü\RÙ^Kİ]˜\ˆ˜[YJBˆÈ˜[YOË•Ôİš[™Ê
Bˆˆ[Â‚ˆYˆ
İš[™Ë’\Ó[Ü•Ú]TÜXÙJ\ÜÚYÛ™Yİ]\Ó˜[YJJBˆÂˆ™]\›ˆ^XİYİ]\Ó˜[YH\È[ÂˆB‚ˆYˆ
^XİYİ]\Ó˜[YH\È[
BˆÂˆ™]\›ˆYNÂˆB‚ˆ™]\›ˆİš[™Ë‘\]X[Ê\ÜÚYÛ™Yİ]\Ó˜[YK^XİYİ]\Ó˜[YKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂˆB‚ˆš]˜]HT™XYÛ›S\İXZ[“Y[R][UšY]Ó[Ù[ˆZ[İ]\ÓY[R][\Ê
BˆÂˆ˜\ˆ\ÑY˜][\ÜÚYÛ™YHR\Ğ\ÜÚYÛ™Yİ]\ÊÙ[XİYœ›İÜÙ\’][JNÂ‚ˆ˜\ˆİ]\Ò][\ÈH™]È\İXZ[“Y[R][UšY]Ó[Ù[‚ˆÂˆ™]Ê
BˆÂˆXY\ˆHØØ[^˜][Û”Ù\šXÙKİ\œ™[‘Ù]
^Ù^\Ë‘Y˜][
KˆXÛÛˆH¸¥âÈ‹ˆÛÛ[X[™H\Pœ›İÜÙ\’][Tİ]\ĞÛÛ[X[™ˆÛÛ[X[™\˜[Y]\ˆH[ˆ\Ñ[˜X›YHZ\ÑY˜][\ÜÚYÛ™YˆBˆNÂ‚ˆİ]\Ò][\ËY˜[™ÙJİ]\Õ˜\šX[Âˆ”Ù[Xİ
˜\šX[O‚ˆÂˆ˜\ˆ\Ğ\ÜÚYÛ™YH\Ğ\ÜÚYÛ™Yİ]\ÊÙ[XİYœ›İÜÙ\’][K˜\šX[“˜[YJNÂˆ™]\›ˆ™]ÈXZ[“Y[R][UšY]Ó[Ù[ˆÂˆXY\ˆH˜\šX[“˜[YKˆXÛÛˆH™]Èİ]\ÓY[RXÛÛŠ˜\šX[’XÛÛ‘Û\˜\šX[’XÛÛÛÛÜŠKˆÛÛ[X[™H\Pœ›İÜÙ\’][Tİ]\ĞÛÛ[X[™ˆÛÛ[X[™\˜[Y]\ˆH˜\šX[“˜[YKˆ\Ñ[˜X›YHZ\Ğ\ÜÚYÛ™YˆNÂˆJJNÂ‚ˆ™]\›ˆİ]\Ò][\ÎÂˆB‚ˆš]˜]HT™XYÛ›S\İØ][ÙÑØİ[Y[ˆÙ]Ù[XİYœ›İÜÙ\’][\Ê
BˆÂˆYˆ
Ù[XİYœ›İÜÙ\’][\ËÛİ[ˆ
BˆÂˆ™]\›ˆÙ[XİYœ›İÜÙ\’][\Ë“Ù•\OØ][ÙÑØİ[Y[Š
K•Ğ\œ˜^J
NÂˆB‚ˆ™]\›ˆÙ[XİYœ›İÜÙ\’][H\È[È×HˆÔÙ[XİYœ›İÜÙ\’][WNÂˆB‚ˆš]˜]Hİ]XÈ›ÛÛ\Ôİ]\Ğ\XØX›UÒ][Jİ]\Õ˜\šX[Øİ[Y[İ]\ËØ][ÙÑØİ[Y[][JBˆÂˆ™]\›ˆ\Ôİ]\Ğ\XØX›UÕ\Jİ]\Ë][K•\JNÂˆB‚ˆš]˜]Hİ]XÈ›ÛÛ\Ôİ]\Ğ\XØX›UÕ\Jİ]\Õ˜\šX[Øİ[Y[İ]\ËØ][ÙÑØİ[Y[\H][U\JBˆÂˆYˆ
İ]\Ë’][U\\È\ÈÈÛİ[ˆˆJBˆÂˆ™]\›ˆİ]\Ë’][U\\ËÛÛZ[œÊ][U\JNÂˆB‚ˆ™]\›ˆYNÂˆB‚ˆš]˜]Hİ]XÈİš[™È™\ÛÛ™SY[Tİ]\ÒXÛÛ•^
İš[™ÈXÛÛ‘Û\Ù^JHOˆXÛÛ‘Û\Ù^NÂŸB