using System.Collections.ObjectModel;
using NEXORA.App.Mvvm;
using System.Diagnostics;
using NEXORA.Core.Auth;
using NEXORA.Infrastructure.Auth;

namespace NEXORA.App.ViewModels;

public sealed class AccountViewModel : PageViewModel
{
    private readonly AppServices _s;
    private string _status = "";
    private bool _slim;
    private string _newName = "";
    private CapeInfo? _cape;
    private CancellationTokenSource? _loginCts;
    private bool _isLoggingIn;

    public AccountViewModel(AppServices s) : base("Аккаунт", "\uE77B")
    {
        _s = s;
        s.Account.Changed += () => Ui.Run(Refresh);
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsLoggedIn, Fail);
        CancelLoginCommand = new RelayCommand(() => _loginCts?.Cancel(), () => IsLoggingIn);
        OpenSetupLinkCommand = new RelayCommand(p => OpenSetupLink(p as string));
        LogoutCommand = new RelayCommand(() => { _s.Account.Logout(); Status = "Вы вышли из аккаунта."; }, () => IsLoggedIn);
        UploadSkinCommand = new AsyncRelayCommand(p => UploadSkinAsync((string?)p), () => IsLoggedIn, Fail);
        CheckNameCommand = new AsyncRelayCommand(CheckNameAsync, () => IsLoggedIn && !string.IsNullOrWhiteSpace(NewName), Fail);
        ChangeNameCommand = new AsyncRelayCommand(ChangeNameAsync, () => IsLoggedIn && !string.IsNullOrWhiteSpace(NewName), Fail);
        ApplyCapeCommand = new AsyncRelayCommand(ApplyCapeAsync, () => IsLoggedIn && SelectedCape is not null, Fail);
        HideCapeCommand = new AsyncRelayCommand(HideCapeAsync, () => IsLoggedIn, Fail);
        Refresh();
    }

    public ObservableCollection<CapeInfo> Capes { get; } = new();
    public bool IsLoggedIn => _s.Account.Current is not null;
    public bool IsLoggedOut => !IsLoggedIn;
    public string PlayerName => _s.Account.Current?.Profile.Name ?? "";
    public string PlayerId => _s.Account.Current?.Profile.Id is { Length: 32 } id
        ? $"{id[..8]}-{id[8..12]}-{id[12..16]}-{id[16..20]}-{id[20..]}" : "";
    public string? SkinUrl => _s.Account.Current?.Profile.ActiveSkin?.Url;
    public bool NeedsSetup => AuthDefaults.Resolve(_s.Settings.MicrosoftClientId) is null;
    public bool IsLoggingIn { get => _isLoggingIn; private set => SetProperty(ref _isLoggingIn, value); }
    public string SetupHint => NeedsSetup
        ? "Вход через Microsoft ещё не настроен: нужен Client ID приложения Azure, одобренный Mojang. Это делается один раз (разработчиком лаунчера) — инструкция в docs/AUTH.md."
        : "Нажмите кнопку: откроется сайт Microsoft, подтвердите вход и вернитесь в лаунчер. Пароль в лаунчере не вводится.";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool SlimArms { get => _slim; set => SetProperty(ref _slim, value); }
    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }
    public CapeInfo? SelectedCape { get => _cape; set => SetProperty(ref _cape, value); }

    public AsyncRelayCommand LoginCommand { get; }
    public RelayCommand CancelLoginCommand { get; }
    public RelayCommand OpenSetupLinkCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public AsyncRelayCommand UploadSkinCommand { get; }
    public AsyncRelayCommand CheckNameCommand { get; }
    public AsyncRelayCommand ChangeNameCommand { get; }
    public AsyncRelayCommand ApplyCapeCommand { get; }
    public AsyncRelayCommand HideCapeCommand { get; }

    private void Refresh()
    {
        Capes.Clear();
        if (_s.Account.Current is { } c) foreach (var cape in c.Profile.Capes) Capes.Add(cape);
        SelectedCape = _s.Account.Current?.Profile.ActiveCape ?? Capes.FirstOrDefault();
        foreach (var n in new[] { nameof(IsLoggedIn), nameof(IsLoggedOut), nameof(PlayerName), nameof(PlayerId), nameof(SkinUrl), nameof(SetupHint), nameof(NeedsSetup) })
            OnPropertyChanged(n);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task LoginAsync()
    {
        Status = "Открыт браузер: подтвердите вход на сайте Microsoft…";
        _loginCts = new CancellationTokenSource();
        IsLoggingIn = true;
        try
        {
            var s = await _s.Account.LoginAsync(_loginCts.Token);
            Status = "Вход выполнен: " + s.Profile.Name;
            Ui.Activate();
        }
        catch (OperationCanceledException) { Status = "Вход отменён."; }
        finally { IsLoggingIn = false; _loginCts?.Dispose(); _loginCts = null; }
    }

    private async Task<MinecraftSession> Token() => await _s.Account.EnsureValidAsync(CancellationToken.None);

    private async Task UploadSkinAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var data = await File.ReadAllBytesAsync(path);
        var session = await Token();
        Status = "Загрузка скина…";
        var profile = await MinecraftProfileApi.UploadSkinAsync(HttpOf(), session.AccessToken, data, SlimArms, CancellationToken.None);
        _s.Account.ApplyProfile(profile);
        Status = "Скин обновлён. В игре он появится после перезахода.";
    }

    private async Task CheckNameAsync()
    {
        var session = await Token();
        var ok = await MinecraftProfileApi.IsNameAvailableAsync(HttpOf(), session.AccessToken, NewName.Trim(), CancellationToken.None);
        Status = ok ? $"Ник «{NewName.Trim()}» свободен." : $"Ник «{NewName.Trim()}» занят или недоступен.";
    }

    private async Task ChangeNameAsync()
    {
        var session = await Token();
        var profile = await MinecraftProfileApi.ChangeNameAsync(HttpOf(), session.AccessToken, NewName.Trim(), CancellationToken.None);
        _s.Account.ApplyProfile(profile);
        NewName = "";
        Status = "Ник изменён на «" + profile.Name + "». Mojang разрешает менять ник не чаще раза в 30 дней.";
    }

    private async Task ApplyCapeAsync()
    {
        var session = await Token();
        await MinecraftProfileApi.SetCapeAsync(HttpOf(), session.AccessToken, SelectedCape!.Id, CancellationToken.None);
        await _s.Account.ReloadProfileAsync(CancellationToken.None);
        Status = "Плащ надет.";
    }

    private async Task HideCapeAsync()
    {
        var session = await Token();
        await MinecraftProfileApi.SetCapeAsync(HttpOf(), session.AccessToken, null, CancellationToken.None);
        await _s.Account.ReloadProfileAsync(CancellationToken.None);
        Status = "Плащ снят.";
    }

    private static readonly HashSet<string> SetupLinks = new()
    {
        "https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade",
        "https://aka.ms/mce-reviewappid",
        "https://minecraft.wiki/w/Microsoft_authentication",
    };

    private void OpenSetupLink(string? url)
    {
        if (url is null || !SetupLinks.Contains(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private NEXORA.Infrastructure.Net.SafeHttp HttpOf() => _s.Http;
    private void Fail(Exception ex) => Status = "Ошибка: " + ErrorText.From(ex);
}
