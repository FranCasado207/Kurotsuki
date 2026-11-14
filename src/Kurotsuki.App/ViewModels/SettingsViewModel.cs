using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.Download.Comic;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.ViewModels;

/// <summary>One choice in the "add booru" list: a built-in template, or a bare engine that needs a base URL.</summary>
public sealed record BooruKind(string Label, BooruTemplate? Template, BooruEngine? Engine, BooruFlavor? Flavor)
{
    public static readonly IReadOnlyList<BooruKind> All =
    [
        .. Enum.GetValues<BooruTemplate>().Select(t => new BooruKind(t.Label(), t, null, null)),
        new(Messages.Get("next.customKind", "Gelbooru-style"), null, BooruEngine.GelbooruFamily, null),
        new(Messages.Get("next.customKind", "Danbooru-style"), null, BooruEngine.DanbooruSoftware, null),
        new(Messages.Get("next.customKind", "Moebooru"), null, BooruEngine.MoebooruShimmie, BooruFlavor.Moebooru),
        new(Messages.Get("next.customKind", "Shimmie"), null, BooruEngine.MoebooruShimmie, BooruFlavor.Shimmie),
        new(Messages.Get("next.customKind", "e621-style"), null, BooruEngine.E621, null),
        new(Messages.Get("next.customKind", "Szurubooru"), null, BooruEngine.Szurubooru, null),
        new(Messages.Get("next.customKind", "Hydrus Network"), null, BooruEngine.Hydrus, null),
    ];

    public override string ToString() => Label;
}

public sealed record LanguageChoice(string Code, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly string _initialLanguage;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Appearance = new ThemeEditorViewModel(services.Theme);
        var c = services.Config;
        _downloadFolder = c.DownloadFolder ?? "";
        _cacheFolder = c.CacheFolder ?? "";
        _downloadTemplate = c.DownloadTemplate ?? "";
        _sidecar = SidecarFormats.Parse(c.DownloadSidecar);
        _redownloadKnown = c.RedownloadKnown;
        _blacklist = string.Join(' ', c.BlacklistedTags);
        _readerMemory = c.ReaderCacheMegabytes > 0 ? c.ReaderCacheMegabytes : UserConfiguration.DefaultReaderCacheMb;
        _readerHonorBlacklist = c.ReaderHonorBlacklist;
        Boorus = new ObservableCollection<BooruConfiguration>(c.BooruConfigurations);
        _newKind = BooruKind.All[0];
        _saveState = services.Session.Enabled;
        _textSize = TextSizes.OrderBy(t => Math.Abs(t.Size - services.Prefs.TextSize)).First();

        Languages = [new("en", Messages.Get("next.langEnglish")), new("pt", Messages.Get("next.langPortuguese"))];
        _initialLanguage = (c.Language ?? "en").Split('-', '_')[0].ToLowerInvariant();
        _language = Languages.FirstOrDefault(l => l.Code == _initialLanguage) ?? Languages[0];

        var formats = ArchiveFormat.All.Where(f => f.CanWrite).Select(f => f.Extension).ToList();
        var current = (c.PoolArchiveFormat ?? "cbz").Trim().ToLowerInvariant().TrimStart('.');
        if (!formats.Contains(current))
        {
            formats.Add(current); // keep what the config says, even if this app cannot write it (the Java app can)
        }
        PoolFormats = formats;
        _poolFormat = current;
        RefreshCacheSize();
        RefreshAllowed();
    }

    public static IReadOnlyList<SidecarFormat> SidecarChoices { get; } = Enum.GetValues<SidecarFormat>();
    public IReadOnlyList<BooruKind> Kinds => BooruKind.All;
    public IReadOnlyList<LanguageChoice> Languages { get; }
    public IReadOnlyList<string> PoolFormats { get; }
    public string TemplateHelp => Messages.Get("settings.downloadTemplateHint", FileNameTemplate.Tokens);
    public ObservableCollection<BooruConfiguration> Boorus { get; }
    public ThemeEditorViewModel Appearance { get; }

    /// <summary>Posts kept visible from the blacklist report, forgotten again with <see cref="ForgetAllowedCommand"/>.</summary>
    [ObservableProperty] private string _allowedInfo = "";

    private void RefreshAllowed() => AllowedInfo = Messages.Get("next.allowedInfo", _services.BlacklistAllowed.Count);

    [RelayCommand]
    private void ForgetAllowed()
    {
        _services.BlacklistAllowed.Clear();
        RefreshAllowed();
    }

    [ObservableProperty] private string _downloadFolder;
    [ObservableProperty] private string _cacheFolder;
    [ObservableProperty] private string _downloadTemplate;
    [ObservableProperty] private SidecarFormat _sidecar;
    [ObservableProperty] private bool _redownloadKnown;
    [ObservableProperty] private string _blacklist;
    [ObservableProperty] private LanguageChoice _language;
    [ObservableProperty] private string _poolFormat;
    [ObservableProperty] private decimal _readerMemory;
    [ObservableProperty] private bool _readerHonorBlacklist;
    [ObservableProperty] private string _cacheInfo = "";

    [ObservableProperty] private BooruKind _newKind;
    [ObservableProperty] private string _newBaseUrl = "";
    [ObservableProperty] private string _newUserId = "";
    [ObservableProperty] private string _newApiKey = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _saveState;

    public sealed record TextSizeChoice(double Size, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<TextSizeChoice> TextSizes { get; } =
    [
        new(12, Messages.Get("next.textSmall")), new(14, Messages.Get("next.textNormal")),
        new(16, Messages.Get("next.textLarge")), new(18, Messages.Get("next.textHuge")),
    ];

    [ObservableProperty] private TextSizeChoice _textSize = null!;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddBooruCommand))] private bool _tested;
    [ObservableProperty] private bool _testing;
    [ObservableProperty] private string _testResult = "";

    // Any change to the form invalidates an earlier successful test: it was for different settings.
    partial void OnNewKindChanged(BooruKind value) => ResetTest();
    partial void OnNewBaseUrlChanged(string value) => ResetTest();
    partial void OnNewUserIdChanged(string value) => ResetTest();
    partial void OnNewApiKeyChanged(string value) => ResetTest();

    private void ResetTest()
    {
        Tested = false;
        TestResult = "";
    }

    public bool LanguageChanged => Language.Code != _initialLanguage;

    private void RefreshCacheSize()
    {
        try
        {
            CacheInfo = Messages.Get("settings.cacheSize", FormatBytes(_services.Cache.SizeBytes()));
        }
        catch (Exception e)
        {
            CacheInfo = Messages.Get("settings.cacheSizeError", e.Message);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };

    [RelayCommand]
    private async Task ClearCache()
    {
        if (!await Dialogs.ConfirmAsync(Shell.MainWindow, Messages.Get("settings.clearCache"), Messages.Get("settings.confirmClearCache")))
        {
            return;
        }
        try
        {
            CacheInfo = Messages.Get("settings.cacheCleared", _services.Cache.Clear());
        }
        catch (Exception e)
        {
            CacheInfo = Messages.Get("settings.clearCacheError", e.Message);
        }
    }

    private BooruConfiguration? BuildNewBooru()
    {
        var booru = new BooruConfiguration
        {
            Template = NewKind.Template,
            Engine = NewKind.Engine,
            Flavor = NewKind.Flavor,
            CustomBaseUrl = NullIfBlank(NewBaseUrl),
            UserId = NullIfBlank(NewUserId),
            ApiKey = NullIfBlank(NewApiKey),
        };
        try
        {
            booru.Validate();
        }
        catch (InvalidOperationException e)
        {
            Error = NewKind.Template is null ? $"{e.Message} ({Messages.Get("next.needBaseUrl")})" : e.Message;
            return null;
        }
        return booru;
    }

    /// <summary>Asks the booru for one post, the same request a search makes, so a wrong URL, engine or key shows up before it is saved.</summary>
    [RelayCommand]
    private async Task TestBooru()
    {
        Error = "";
        if (BuildNewBooru() is not { } booru)
        {
            return;
        }
        Testing = true;
        Tested = false;
        TestResult = "";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var posts = await _services.Clients.ClientFor(booru).SearchPostsAsync(null, 0, 1, timeout.Token);
            Tested = true;
            TestResult = Messages.Get("next.testOk", posts.Count);
        }
        catch (Exception e)
        {
            Error = Messages.Get("next.testFailed", string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message);
        }
        finally
        {
            Testing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(Tested))]
    private void AddBooru()
    {
        if (!Tested || BuildNewBooru() is not { } booru)
        {
            return;
        }
        Error = booru.SendsCredentialsInClear() ? Messages.Get("next.plainHttp") : "";
        Boorus.Add(booru);
        NewBaseUrl = NewUserId = NewApiKey = "";
        ResetTest();
    }

    [RelayCommand]
    private async Task RemoveBooru(BooruConfiguration? booru)
    {
        if (booru is not null && await Dialogs.ConfirmAsync(Shell.MainWindow, Messages.Get("common.remove"),
                Messages.Get("settings.confirmDeleteBooru")))
        {
            Boorus.Remove(booru);
        }
    }

    /// <summary>Writes the form back into the shared configuration and saves config.json.</summary>
    public void Save()
    {
        var c = _services.Config;
        c.DownloadFolder = NullIfBlank(DownloadFolder);
        c.CacheFolder = NullIfBlank(CacheFolder);
        c.DownloadTemplate = NullIfBlank(DownloadTemplate);
        c.DownloadSidecar = Sidecar.ToString().ToLowerInvariant();
        c.RedownloadKnown = RedownloadKnown;
        c.BlacklistedTags = Blacklist.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(TagBlacklist.Normalize).Distinct().ToList();
        c.Language = Language.Code;
        c.PoolArchiveFormat = PoolFormat;
        c.ReaderCacheMegabytes = (int)ReaderMemory;
        c.ReaderHonorBlacklist = ReaderHonorBlacklist;
        c.BooruConfigurations = [.. Boorus];
        _services.Session.SetEnabled(SaveState);
        _services.Prefs.SetTextSize(TextSize.Size);
        ThemeManager.ApplyTextSize(TextSize.Size);
        Appearance.Commit();
        _services.SaveConfig();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
