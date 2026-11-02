using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Halo.Core.Search;
using Halo.Infrastructure;
using Halo.Motion;
using Microsoft.Web.WebView2.Core;

namespace Halo.Views;

/// <summary>
/// Google's own results (AI Mode, the Web tab or Lens) rendered inside the notch. Uses the WebView2
/// composition control because a normal WebView2 is a child HWND, which can't live inside a
/// per-pixel-transparent window like the notch.
/// </summary>
public partial class ResultView : UserControl
{
    const string MobileUserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Mobile Safari/537.36";

    string? _desktopUserAgent;

    Task? _init;
    readonly SpringValue _loading = new(0, 0.001);
    IDisposable? _loadingFrames;
    bool _isLoading;
    ulong _latestNavigation;

    public ResultView()
    {
        InitializeComponent();
    }

    internal event Action? CloseRequested;
    internal event Action<SearchMode>? ModeChanged;
    internal event Action<SearchFilter>? FiltersChanged;

    internal bool CompactLayout { get; set; } = true;

    public Uri? CurrentUrl => Web.CoreWebView2?.Source is { Length: > 0 } s ? new Uri(s) : null;

    /// <summary>Starts the browser engine. Safe to call repeatedly; the first call does the work.</summary>
    internal Task EnsureReadyAsync() => _init ??= InitAsync();

    async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebView);
            await Web.EnsureCoreWebView2Async(env);
            var core = Web.CoreWebView2;
            core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            _desktopUserAgent = core.Settings.UserAgent;

            core.NewWindowRequested += (_, e) =>
            {
                // Links that want a new tab go to the user's real browser.
                e.Handled = true;
                OpenExternal(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                _latestNavigation = e.NavigationId;
                ErrorPanel.Visibility = Visibility.Collapsed;
                Notice.Visibility = Visibility.Collapsed;
                SetLoading(true);
            };
            core.NavigationCompleted += (_, e) =>
            {
                // A navigation that was superseded (redirects, a new search) reports as aborted; only the
                // latest one decides what the card shows.
                Log.Info($"Navigation {e.NavigationId} done: ok={e.IsSuccess} http={e.HttpStatusCode} " +
                         $"latest={e.NavigationId == _latestNavigation} {Truncate(core.Source)}");
                if (e.NavigationId != _latestNavigation) return;
                SetLoading(false);
                if (_releasing)
                {
                    SuspendAfterBlank();
                    return;
                }
                // Google's "unusual traffic" check (/sorry/, HTTP 429). The page itself is the fix: the
                // user ticks the box once and the persistent profile keeps the exemption cookie.
                bool robotCheck = e.HttpStatusCode == 429 || (core.Source?.Contains("/sorry/", StringComparison.Ordinal) ?? false);
                Notice.Visibility = robotCheck ? Visibility.Visible : Visibility.Collapsed;
                if (robotCheck)
                {
                    Log.Warn("Google asked for a robot check");
                    return;
                }
                if (e.IsSuccess || e.WebErrorStatus is CoreWebView2WebErrorStatus.OperationCanceled or CoreWebView2WebErrorStatus.ConnectionAborted)
                    return;
                Log.Warn($"Results navigation failed: {e.WebErrorStatus} ({e.HttpStatusCode})");
                ShowError(e.WebErrorStatus == CoreWebView2WebErrorStatus.Disconnected
                    ? "You're offline. Check your connection and try again."
                    : $"Couldn't load the results ({e.WebErrorStatus}).");
            };
            core.ProcessFailed += (_, e) =>
            {
                Log.Warn($"WebView2 process failed: {e.ProcessFailedKind}");
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) _init = null;
            };
            core.NavigateToString("<html><body style='background:#101012'></body></html>");
            Log.Info($"WebView2 ready ({env.BrowserVersionString})");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _init = null;
            ShowError("Search needs the Microsoft Edge WebView2 Runtime. Install it from microsoft.com and restart Halo.");
            throw;
        }
        catch (Exception ex)
        {
            _init = null;
            Log.Error("WebView2 init failed", ex);
            ShowError("The results view couldn't start. See the log for details.");
            throw;
        }
    }

    internal void SetContext(string query, ImageSource? thumbnail, bool lensAvailable, SearchMode mode, SearchFilter filters)
    {
        QueryText.Text = query;
        Thumb.Visibility = thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
        QueryIcon.Visibility = thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
        Thumb.Background = thumbnail is null ? null : new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
        ModeLens.Visibility = lensAvailable ? Visibility.Visible : Visibility.Collapsed;
        SetModeChips(mode);
        FilterReddit.IsChecked = filters.HasFlag(SearchFilter.Reddit);
        FilterRecent.IsChecked = filters.HasFlag(SearchFilter.Recent);
        FilterNoJunk.IsChecked = filters.HasFlag(SearchFilter.NoJunk);
        FilterDocs.IsChecked = filters.HasFlag(SearchFilter.Docs);
        ErrorPanel.Visibility = Visibility.Collapsed;
    }

    void SetModeChips(SearchMode mode)
    {
        ModeAi.IsChecked = mode == SearchMode.Ai;
        ModeWeb.IsChecked = mode == SearchMode.Web;
        ModeLens.IsChecked = mode == SearchMode.Lens;
        bool filtersApply = mode != SearchMode.Lens;
        FilterReddit.IsEnabled = FilterRecent.IsEnabled = FilterNoJunk.IsEnabled = FilterDocs.IsEnabled = filtersApply;
        FilterReddit.Opacity = FilterRecent.Opacity = FilterNoJunk.Opacity = FilterDocs.Opacity = filtersApply ? 1 : 0.4;
    }

    internal async Task NavigateAsync(Uri url)
    {
        await EnsureReadyAsync();
        Wake();
        ErrorPanel.Visibility = Visibility.Collapsed;
        UseUserAgent(mobile: CompactLayout);
        Web.CoreWebView2.Navigate(url.AbsoluteUri);
    }

    /// <summary>
    /// After the card closes: blank the page (stops any video/audio and page scripts) and suspend the
    /// renderer so a hidden results page costs no CPU and less memory until the next search.
    /// </summary>
    internal void Release()
    {
        var core = Web.CoreWebView2;
        if (core is null) return;
        _releasing = true;
        core.NavigateToString("<html><body style='background:#101012'></body></html>");
    }

    bool _releasing;

    async void SuspendAfterBlank()
    {
        _releasing = false;
        var core = Web.CoreWebView2;
        if (core is null || core.IsSuspended) return;
        try
        {
            // Suspending requires the controller to be hidden; the card is collapsed by now or soon will be.
            await Task.Delay(400);
            if (IsVisible || core.IsSuspended) return;
            await core.TrySuspendAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("Suspending results view failed", ex);
        }
    }

    void Wake()
    {
        var core = Web.CoreWebView2;
        if (core is { IsSuspended: true }) core.Resume();
    }

    /// <summary>
    /// AI Mode and Web results get a phone layout that fits the card; Lens uploads go out as the
    /// normal desktop Edge identity, since an upload from a "phone" on a desktop IP looks odd to Google.
    /// </summary>
    void UseUserAgent(bool mobile)
    {
        var settings = Web.CoreWebView2.Settings;
        var want = mobile ? MobileUserAgent : _desktopUserAgent;
        if (want is not null && settings.UserAgent != want) settings.UserAgent = want;
    }

    /// <summary>
    /// Uploads the selection to Lens the same way lens.google.com's own "upload" button does, so the
    /// results page (and its cookies/consent) behave exactly as in a browser.
    /// </summary>
    internal async Task NavigateLensAsync(byte[] png)
    {
        await EnsureReadyAsync();
        Wake();
        ErrorPanel.Visibility = Visibility.Collapsed;
        UseUserAgent(mobile: false);
        var core = Web.CoreWebView2;
        string boundary = "----HaloLens" + Guid.NewGuid().ToString("N");
        var body = new MemoryStream();
        void Write(string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            body.Write(bytes, 0, bytes.Length);
        }
        Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"encoded_image\"; filename=\"halo.png\"\r\nContent-Type: image/png\r\n\r\n");
        body.Write(png, 0, png.Length);
        Write($"\r\n--{boundary}--\r\n");
        body.Position = 0;

        long stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"https://lens.google.com/v3/upload?hl=en&re=df&stcs={stamp}&ep=gsbubb";
        var request = core.Environment.CreateWebResourceRequest(url, "POST", body,
            $"Content-Type: multipart/form-data; boundary={boundary}");
        core.NavigateWithWebResourceRequest(request);
    }

    internal void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        SetLoading(false);
    }

    void SetLoading(bool loading)
    {
        if (loading == _isLoading) return;
        _isLoading = loading;
        if (loading)
        {
            _loading.Snap(0);
            LoadingBar.Opacity = 1;
            _loading.AnimateTo(0.85, 2.4, 1);
        }
        else
        {
            _loading.AnimateTo(1, 0.25, 1);
        }
        _loadingFrames ??= Animator.Subscribe(_ =>
        {
            LoadingBar.Width = _loading.Value * WebFrame.ActualWidth;
            if (!_isLoading && _loading.IsSettled)
            {
                LoadingBar.Opacity = 0;
                _loadingFrames?.Dispose();
                _loadingFrames = null;
            }
        });
    }

    /// <summary>Module chips after the filters (e.g. "Copy text", "Translate").</summary>
    internal void SetExtraChips(IEnumerable<(string Label, Action OnClick)> chips)
    {
        ExtraChips.Children.Clear();
        bool first = true;
        foreach (var (label, onClick) in chips)
        {
            if (first)
            {
                ExtraChips.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = 1, Height = 16, Fill = (Brush)FindResource("Fill3"), Margin = new Thickness(2, 0, 8, 0),
                });
                first = false;
            }
            var chip = new ToggleButton { Content = label, Style = (Style)FindResource("Chip") };
            chip.Click += (_, _) =>
            {
                chip.IsChecked = false;
                onClick();
            };
            ExtraChips.Children.Add(chip);
        }
    }

    void OnFrameSizeChanged(object sender, SizeChangedEventArgs e) =>
        WebFrame.Clip = new RectangleGeometry(new Rect(e.NewSize), 18, 18);

    void OnModeClick(object sender, RoutedEventArgs e)
    {
        var mode = sender == ModeWeb ? SearchMode.Web : sender == ModeLens ? SearchMode.Lens : SearchMode.Ai;
        SetModeChips(mode);
        ModeChanged?.Invoke(mode);
    }

    void OnFilterClick(object sender, RoutedEventArgs e)
    {
        var f = SearchFilter.None;
        if (FilterReddit.IsChecked == true) f |= SearchFilter.Reddit;
        if (FilterRecent.IsChecked == true) f |= SearchFilter.Recent;
        if (FilterNoJunk.IsChecked == true) f |= SearchFilter.NoJunk;
        if (FilterDocs.IsChecked == true) f |= SearchFilter.Docs;
        FiltersChanged?.Invoke(f);
    }

    void OnReload(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();

    void OnOpenInBrowser(object sender, RoutedEventArgs e)
    {
        if (CurrentUrl is { } url && url.Scheme.StartsWith("http")) OpenExternal(url.AbsoluteUri);
        CloseRequested?.Invoke();
    }

    void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    void OnDismissNotice(object sender, RoutedEventArgs e) => Notice.Visibility = Visibility.Collapsed;

    static string Truncate(string? s) => s is null ? "" : s.Length > 90 ? s[..90] + "…" : s;

    internal static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Opening {url} failed", ex); }
    }
}
