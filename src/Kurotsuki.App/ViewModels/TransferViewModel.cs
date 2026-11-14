using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Backup;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.App.ViewModels;

/// <summary>"Send to phone": serves the backup once on the local network and shows the QR code the phone scans.</summary>
public sealed partial class TransferViewModel(AppServices services) : ObservableObject, IDisposable
{
    private TransferServer? _server;

    [ObservableProperty] private bool _includeKeys;
    [ObservableProperty] private Bitmap? _qr;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart))] private bool _running;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _link = "";
    [ObservableProperty] private string _firewallHint = "";
    [ObservableProperty] private string _error = "";

    public bool CanStart => !Running;
    public string Intro => Messages.Get("transfer.intro");
    public string IncludeKeysText => Messages.Get("transfer.includeKeys");

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Show()
    {
        Error = "";
        try
        {
            var include = IncludeKeys; // read now: the server asks for the backup from another thread
            _server = new TransferServer(() => BackupFile.Export(services.Config, services.Store, include),
                TransferServer.DefaultLifetime, state => Dispatcher.UIThread.Post(() => OnState(state)));
            var link = _server.Link();
            Link = link.ToUri();
            Qr = QrBitmap.Render(QrCode.Encode(Link));
            FirewallHint = Messages.Get("transfer.firewall", _server.Port);
            Status = Messages.Get("transfer.waiting");
            Running = true;
            ShowCommand.NotifyCanExecuteChanged();
            AppLog.Info($"Transfer server started on port {_server.Port}");
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException)
        {
            Error = Messages.Get("transfer.error", e.Message);
        }
    }

    private void OnState(TransferServer.State state)
    {
        Status = state switch
        {
            TransferServer.State.Sent => Messages.Get("transfer.sent"),
            TransferServer.State.Expired => Messages.Get("transfer.expired"),
            _ => Messages.Get("transfer.stopped"),
        };
        Running = false;
        Qr = null;
        Link = "";
        ShowCommand.NotifyCanExecuteChanged();
        AppLog.Info($"Transfer finished: {state}");
    }

    public string? ServerToken => _server?.Token;
    public int? ServerPort => _server?.Port;

    [RelayCommand]
    private Task CopyLink() => Shell.CopyToClipboardAsync(Link);

    public void Dispose() => _server?.Dispose();
}
