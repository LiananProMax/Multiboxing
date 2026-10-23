using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EveOPreview.View;
using Drawing = System.Drawing;
using Media = System.Windows.Media;

namespace Multiboxing.Views;

/// <summary>
/// WPF 实现的 IMainFormView，替代原 WinForms MainForm。
/// Presenter 通过该接口读写约 30 个设置项；WPF 侧通过 *Wpf 包装属性绑定。
/// </summary>
public sealed class PreviewViewModel : IMainFormView, INotifyPropertyChanged
{
    private readonly Dispatcher _dispatcher;
    private bool _suppress;
    private Drawing.Size _minSize = new(20, 20);
    private Drawing.Size _maxSize = new(20, 20);

    private bool _minimizeToTray;
    private double _thumbnailOpacity = 0.5;
    private bool _enableClientLayoutTracking;
    private bool _hideActiveClientThumbnail;
    private bool _minimizeInactiveClients;
    private bool _hideCaptionOnClients;
    private ViewAnimationStyle _windowsAnimationStyle;
    private bool _showThumbnailsAlwaysOnTop = true;
    private bool _preventPreviews;
    private bool _hideThumbnailsOnLostFocus;
    private bool _enablePerClientThumbnailLayouts;
    private Drawing.Size _thumbnailSize = new(384, 216);
    private bool _keepThumbnailAspectRatio = true;
    private bool _useThumbnailScale = true;
    private double _thumbnailScale = 0.2;
    private bool _enableThumbnailZoom;
    private int _thumbnailZoomFactor = 2;
    private ViewZoomAnchor _thumbnailZoomAnchor = ViewZoomAnchor.NW;
    private ViewZoomAnchor _overlayLabelAnchor = ViewZoomAnchor.NW;
    private ViewZoomAnchor _cycleGroupIndicatorAnchor = ViewZoomAnchor.NW;
    private bool _showThumbnailOverlays = true;
    private bool _showThumbnailFrames;
    private bool _lockThumbnailLocation;
    private bool _thumbnailSnapToGrid;
    private int _thumbnailSnapToGridSizeX = 100;
    private int _thumbnailSnapToGridSizeY = 50;
    private bool _enableActiveClientHighlight;
    private Drawing.Color _activeClientHighlightColor = Drawing.Color.FromArgb(125, 211, 252);
    private Drawing.Color _preventPreviewColor = Drawing.Color.Purple;
    private Drawing.Color _overlayLabelColor = Drawing.Color.FromArgb(241, 245, 249);
    private Drawing.Font _overlayLabelFont = new(Drawing.FontFamily.GenericSansSerif, 10f, Drawing.FontStyle.Bold);
    private string _iconName = string.Empty;
    private string _versionText = string.Empty;
    private string _documentationUrl = string.Empty;
    private bool _isPreviewRunning;

    public PreviewViewModel()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        TogglePreviewCommand = new RelayCommand(() => ServiceToggleRequested?.Invoke(!_isPreviewRunning));
        OpenDocumentationCommand = new RelayCommand(() => DocumentationLinkActivated?.Invoke());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<bool>? ServiceToggleRequested;
    public ICommand TogglePreviewCommand { get; }
    public ICommand OpenDocumentationCommand { get; }
    public ObservableCollection<PreviewClientItem> Clients { get; } = new();
    public int ClientCount => Clients.Count;
    public string PreviewStatusText => _isPreviewRunning ? "预览运行中" : "预览已停止";
    public string TogglePreviewText => _isPreviewRunning ? "停止预览" : "启动预览";
    public string VersionText { get => _versionText; private set => SetField(ref _versionText, value, nameof(VersionText), notifySettings: false); }
    public string DocumentationUrl { get => _documentationUrl; private set => SetField(ref _documentationUrl, value, nameof(DocumentationUrl), notifySettings: false); }
    public bool IsPreviewRunning => _isPreviewRunning;

    internal void SetPreviewRunning(bool value)
    {
        if (_isPreviewRunning == value) { OnChanged(nameof(PreviewStatusText)); OnChanged(nameof(TogglePreviewText)); return; }
        _isPreviewRunning = value;
        OnChanged(nameof(IsPreviewRunning)); OnChanged(nameof(PreviewStatusText)); OnChanged(nameof(TogglePreviewText));
    }

    // 窗口动作请求（由 MainWindow 接线）
    public Action? HideWindowRequested { get; set; }
    public Action? MinimizeWindowRequested { get; set; }
    public Action? CloseWindowRequested { get; set; }

    #region IView
    public void Show()
    {
        _suppress = true;
        try { FormActivated?.Invoke(); }
        finally { _suppress = false; }
        RefreshAll();
    }
    public void Hide() => HideWindowRequested?.Invoke();
    public void Close() => CloseWindowRequested?.Invoke();
    public void Minimize() => MinimizeWindowRequested?.Invoke();
    #endregion

    internal void RaiseFormCloseRequested(ViewCloseRequest request) => FormCloseRequested?.Invoke(request);
    internal void RaiseApplicationExitRequested() => ApplicationExitRequested?.Invoke();
    internal void NotifyFormMinimized() => FormMinimized?.Invoke();
    public void RequestExit() => ApplicationExitRequested?.Invoke();

    public void SetDocumentationUrl(string url) => DocumentationUrl = url;
    public void SetVersionInfo(string version) => VersionText = version;
    public void SetThumbnailSizeLimitations(Drawing.Size minimumSize, Drawing.Size maximumSize) { _minSize = minimumSize; _maxSize = maximumSize; }
    public void RefreshZoomSettings() => OnChanged(nameof(IsZoomSettingsEnabled));
    public bool IsZoomSettingsEnabled => _enableThumbnailZoom;

    public void AddThumbnails(IList<IThumbnailDescription> thumbnails) => RunOnUi(() =>
    {
        foreach (var d in thumbnails) Clients.Add(new PreviewClientItem(d, title => ThumbnailStateChanged?.Invoke(title)));
        OnChanged(nameof(ClientCount));
    });
    public void RemoveThumbnails(IList<IThumbnailDescription> thumbnails) => RunOnUi(() =>
    {
        var titles = new HashSet<string>(thumbnails.Select(d => d.Title));
        for (int i = Clients.Count - 1; i >= 0; i--) if (titles.Contains(Clients[i].Title)) Clients.RemoveAt(i);
        OnChanged(nameof(ClientCount));
    });

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }

    #region IMainFormView properties
    public bool MinimizeToTray { get => _minimizeToTray; set => SetField(ref _minimizeToTray, value, nameof(MinimizeToTray)); }
    public double ThumbnailOpacity { get => _thumbnailOpacity; set => SetField(ref _thumbnailOpacity, Math.Clamp(value, 0.1, 1.0), nameof(ThumbnailOpacity)); }
    public bool EnableClientLayoutTracking { get => _enableClientLayoutTracking; set => SetField(ref _enableClientLayoutTracking, value, nameof(EnableClientLayoutTracking)); }
    public bool HideActiveClientThumbnail { get => _hideActiveClientThumbnail; set => SetField(ref _hideActiveClientThumbnail, value, nameof(HideActiveClientThumbnail)); }
    public bool MinimizeInactiveClients { get => _minimizeInactiveClients; set => SetField(ref _minimizeInactiveClients, value, nameof(MinimizeInactiveClients)); }
    public bool HideCaptionOnClients { get => _hideCaptionOnClients; set => SetField(ref _hideCaptionOnClients, value, nameof(HideCaptionOnClients)); }
    public ViewAnimationStyle WindowsAnimationStyle { get => _windowsAnimationStyle; set => SetField(ref _windowsAnimationStyle, value, nameof(WindowsAnimationStyle)); }
    public bool ShowThumbnailsAlwaysOnTop { get => _showThumbnailsAlwaysOnTop; set => SetField(ref _showThumbnailsAlwaysOnTop, value, nameof(ShowThumbnailsAlwaysOnTop)); }
    public bool PreventPreviews { get => _preventPreviews; set => SetField(ref _preventPreviews, value, nameof(PreventPreviews)); }
    public bool HideThumbnailsOnLostFocus { get => _hideThumbnailsOnLostFocus; set => SetField(ref _hideThumbnailsOnLostFocus, value, nameof(HideThumbnailsOnLostFocus)); }
    public bool EnablePerClientThumbnailLayouts { get => _enablePerClientThumbnailLayouts; set => SetField(ref _enablePerClientThumbnailLayouts, value, nameof(EnablePerClientThumbnailLayouts)); }

    public Drawing.Size ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            var clamped = new Drawing.Size(
                Math.Min(Math.Max(value.Width, _minSize.Width), Math.Max(_minSize.Width, _maxSize.Width)),
                Math.Min(Math.Max(value.Height, _minSize.Height), Math.Max(_minSize.Height, _maxSize.Height)));
            if (_thumbnailSize == clamped) { OnChanged(nameof(ThumbnailWidth)); OnChanged(nameof(ThumbnailHeight)); return; }
            _thumbnailSize = clamped;
            OnChanged(nameof(ThumbnailSize)); OnChanged(nameof(ThumbnailWidth)); OnChanged(nameof(ThumbnailHeight));
            if (!_suppress) ThumbnailsSizeChanged?.Invoke();
        }
    }
    public int ThumbnailWidth { get => _thumbnailSize.Width; set => ThumbnailSize = new Drawing.Size(value, _thumbnailSize.Height); }
    public int ThumbnailHeight { get => _thumbnailSize.Height; set => ThumbnailSize = new Drawing.Size(_thumbnailSize.Width, value); }
    public bool KeepThumbnailAspectRatio { get => _keepThumbnailAspectRatio; set => SetField(ref _keepThumbnailAspectRatio, value, nameof(KeepThumbnailAspectRatio)); }

    public bool UseThumbnailScale
    {
        get => _useThumbnailScale;
        set
        {
            if (!SetField(ref _useThumbnailScale, value, nameof(UseThumbnailScale))) return;
            OnChanged(nameof(IsFixedSizeMode));
            if (!_suppress) ThumbnailsSizeChanged?.Invoke();
        }
    }
    public bool IsFixedSizeMode => !_useThumbnailScale;

    public double ThumbnailScale
    {
        get => _thumbnailScale;
        set
        {
            double clamped = double.IsNaN(value) || double.IsInfinity(value) ? 0.2 : Math.Clamp(value, 0.1, 1.0);
            if (Math.Abs(_thumbnailScale - clamped) < 0.000000001) { OnChanged(nameof(ThumbnailScale)); return; }
            _thumbnailScale = clamped;
            OnChanged(nameof(ThumbnailScale));
            if (!_suppress) ThumbnailsSizeChanged?.Invoke();
        }
    }

    public bool EnableThumbnailZoom { get => _enableThumbnailZoom; set { SetField(ref _enableThumbnailZoom, value, nameof(EnableThumbnailZoom)); OnChanged(nameof(IsZoomSettingsEnabled)); } }
    public int ThumbnailZoomFactor { get => _thumbnailZoomFactor; set => SetField(ref _thumbnailZoomFactor, value, nameof(ThumbnailZoomFactor)); }
    public ViewZoomAnchor ThumbnailZoomAnchor { get => _thumbnailZoomAnchor; set => SetField(ref _thumbnailZoomAnchor, value, nameof(ThumbnailZoomAnchor)); }
    public ViewZoomAnchor OverlayLabelAnchor { get => _overlayLabelAnchor; set => SetField(ref _overlayLabelAnchor, value, nameof(OverlayLabelAnchor)); }
    public ViewZoomAnchor CycleGroupIndicatorAnchor { get => _cycleGroupIndicatorAnchor; set => SetField(ref _cycleGroupIndicatorAnchor, value, nameof(CycleGroupIndicatorAnchor)); }
    public bool ShowThumbnailOverlays { get => _showThumbnailOverlays; set => SetField(ref _showThumbnailOverlays, value, nameof(ShowThumbnailOverlays)); }
    public bool ShowThumbnailFrames { get => _showThumbnailFrames; set => SetField(ref _showThumbnailFrames, value, nameof(ShowThumbnailFrames)); }
    public bool LockThumbnailLocation { get => _lockThumbnailLocation; set => SetField(ref _lockThumbnailLocation, value, nameof(LockThumbnailLocation)); }
    public bool ThumbnailSnapToGrid { get => _thumbnailSnapToGrid; set => SetField(ref _thumbnailSnapToGrid, value, nameof(ThumbnailSnapToGrid)); }
    public int ThumbnailSnapToGridSizeX { get => _thumbnailSnapToGridSizeX; set => SetField(ref _thumbnailSnapToGridSizeX, value, nameof(ThumbnailSnapToGridSizeX)); }
    public int ThumbnailSnapToGridSizeY { get => _thumbnailSnapToGridSizeY; set => SetField(ref _thumbnailSnapToGridSizeY, value, nameof(ThumbnailSnapToGridSizeY)); }
    public bool EnableActiveClientHighlight { get => _enableActiveClientHighlight; set => SetField(ref _enableActiveClientHighlight, value, nameof(EnableActiveClientHighlight)); }

    public Drawing.Color ActiveClientHighlightColor { get => _activeClientHighlightColor; set { if (SetField(ref _activeClientHighlightColor, value, nameof(ActiveClientHighlightColor))) OnChanged(nameof(ActiveClientHighlightColorWpf)); } }
    public Drawing.Color PreventPreviewColor { get => _preventPreviewColor; set { if (SetField(ref _preventPreviewColor, value, nameof(PreventPreviewColor))) OnChanged(nameof(PreventPreviewColorWpf)); } }
    public Drawing.Color OverlayLabelColor { get => _overlayLabelColor; set { if (SetField(ref _overlayLabelColor, value, nameof(OverlayLabelColor))) OnChanged(nameof(OverlayLabelColorWpf)); } }

    public Media.Color ActiveClientHighlightColorWpf { get => ToMedia(_activeClientHighlightColor); set => ActiveClientHighlightColor = ToDrawing(value); }
    public Media.Color PreventPreviewColorWpf { get => ToMedia(_preventPreviewColor); set => PreventPreviewColor = ToDrawing(value); }
    public Media.Color OverlayLabelColorWpf { get => ToMedia(_overlayLabelColor); set => OverlayLabelColor = ToDrawing(value); }

    public Drawing.Font OverlayLabelFont
    {
        get => _overlayLabelFont;
        set
        {
            if (value == null) return;
            _overlayLabelFont = value;
            OnChanged(nameof(OverlayLabelFont)); OnChanged(nameof(OverlayFontFamily)); OnChanged(nameof(OverlayFontSize)); OnChanged(nameof(OverlayFontBold)); OnChanged(nameof(OverlayFontItalic));
            NotifySettings();
        }
    }
    public string OverlayFontFamily
    {
        get => _overlayLabelFont.FontFamily.Name;
        set { if (!string.IsNullOrWhiteSpace(value)) OverlayLabelFont = BuildFont(value, _overlayLabelFont.Size, _overlayLabelFont.Style); }
    }
    public double OverlayFontSize
    {
        get => _overlayLabelFont.Size;
        set { if (value >= 6 && value <= 72) OverlayLabelFont = BuildFont(_overlayLabelFont.FontFamily.Name, (float)value, _overlayLabelFont.Style); }
    }
    public bool OverlayFontBold
    {
        get => _overlayLabelFont.Bold;
        set => OverlayLabelFont = BuildFont(_overlayLabelFont.FontFamily.Name, _overlayLabelFont.Size, (_overlayLabelFont.Style & ~Drawing.FontStyle.Bold) | (value ? Drawing.FontStyle.Bold : 0));
    }
    public bool OverlayFontItalic
    {
        get => _overlayLabelFont.Italic;
        set => OverlayLabelFont = BuildFont(_overlayLabelFont.FontFamily.Name, _overlayLabelFont.Size, (_overlayLabelFont.Style & ~Drawing.FontStyle.Italic) | (value ? Drawing.FontStyle.Italic : 0));
    }
    private static Drawing.Font BuildFont(string family, float size, Drawing.FontStyle style)
    {
        try { return new Drawing.Font(family, size, style); }
        catch { return new Drawing.Font(Drawing.FontFamily.GenericSansSerif, size, style); }
    }

    public string IconName { get => _iconName; set => _iconName = value ?? string.Empty; }

    public Action? ApplicationExitRequested { get; set; }
    public Action? FormActivated { get; set; }
    public Action? FormMinimized { get; set; }
    public Action<ViewCloseRequest>? FormCloseRequested { get; set; }
    public Action? ApplicationSettingsChanged { get; set; }
    public Action? ThumbnailsSizeChanged { get; set; }
    public Action<string>? ThumbnailStateChanged { get; set; }
    public Action? DocumentationLinkActivated { get; set; }
    #endregion

    private static Media.Color ToMedia(Drawing.Color c) => Media.Color.FromArgb(c.A, c.R, c.G, c.B);
    private static Drawing.Color ToDrawing(Media.Color c) => Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

    private bool SetField<T>(ref T field, T value, string name, bool notifySettings = true)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        if (notifySettings) NotifySettings();
        return true;
    }
    private void NotifySettings() { if (!_suppress) ApplicationSettingsChanged?.Invoke(); }
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void RefreshAll()
    {
        foreach (var n in new[] { nameof(MinimizeToTray), nameof(ThumbnailOpacity), nameof(EnableClientLayoutTracking), nameof(HideActiveClientThumbnail), nameof(MinimizeInactiveClients), nameof(HideCaptionOnClients), nameof(WindowsAnimationStyle), nameof(ShowThumbnailsAlwaysOnTop), nameof(PreventPreviews), nameof(HideThumbnailsOnLostFocus), nameof(EnablePerClientThumbnailLayouts), nameof(ThumbnailWidth), nameof(ThumbnailHeight), nameof(KeepThumbnailAspectRatio), nameof(UseThumbnailScale), nameof(ThumbnailScale), nameof(IsFixedSizeMode), nameof(EnableThumbnailZoom), nameof(ThumbnailZoomFactor), nameof(ThumbnailZoomAnchor), nameof(OverlayLabelAnchor), nameof(CycleGroupIndicatorAnchor), nameof(ShowThumbnailOverlays), nameof(ShowThumbnailFrames), nameof(LockThumbnailLocation), nameof(ThumbnailSnapToGrid), nameof(ThumbnailSnapToGridSizeX), nameof(ThumbnailSnapToGridSizeY), nameof(EnableActiveClientHighlight), nameof(ActiveClientHighlightColorWpf), nameof(PreventPreviewColorWpf), nameof(OverlayLabelColorWpf), nameof(OverlayFontFamily), nameof(OverlayFontSize), nameof(OverlayFontBold), nameof(OverlayFontItalic), nameof(IsZoomSettingsEnabled) })
            OnChanged(n);
    }
}
