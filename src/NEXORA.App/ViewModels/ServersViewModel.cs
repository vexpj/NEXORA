using System.Collections.ObjectModel;
using NEXORA.App.Mvvm;
using NEXORA.Core.Servers;

namespace NEXORA.App.ViewModels;

public sealed class ServerRow : ObservableObject
{
    private string _status = "Проверка…", _motd = "", _players = "", _ping = "";
    private bool _online;
    public ServerRow(ServerEntry e, string profileName) { Entry = e; ProfileName = profileName; }
    public ServerEntry Entry { get; }
    public string Name => Entry.Name;
    public string Address => Entry.Address;
    public string ProfileName { get; set; }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Motd { get => _motd; set => SetProperty(ref _motd, value); }
    public string Players { get => _players; set => SetProperty(ref _players, value); }
    public string Ping { get => _ping; set => SetProperty(ref _ping, value); }
    public bool Online { get => _online; set { if (SetProperty(ref _online, value)) OnPropertyChanged(nameof(StateColor)); } }
    public string StateColor => Online ? "#34D399" : "#F87171";
}

public sealed class ServersViewModel : PageViewModel
{
    private readonly AppServices _s;
    private string _newName = "", _newAddress = "", _status = "";

    public ServersViewModel(AppServices s) : base("Серверы", "\uE774")
    {
        _s = s;
        foreach (var e in s.ServerStore.Load()) Items.Add(new ServerRow(e, ProfileNameOf(e)));
        AddCommand = new AsyncRelayCommand(AddAsync, null, ex => Status = "Ошибка: " + ex.Message);
        RemoveCommand = new RelayCommand(p => Remove(p as ServerRow));
        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, null, ex => Status = "Ошибка: " + ex.Message);
        PlayCommand = new RelayCommand(p => Play(p as ServerRow));
    }

    public ObservableCollection<ServerRow> Items { get; } = new();
    public ProfilesModel Profiles => _s.Profiles;
    public event Action<ServerEndpoint>? PlayRequested;

    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }
    public string NewAddress { get => _newAddress; set => SetProperty(ref _newAddress, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public AsyncRelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand PlayCommand { get; }

    private string ProfileNameOf(ServerEntry e) =>
        _s.Profiles.Items.FirstOrDefault(p => p.Id == e.ProfileId)?.Name ?? "текущий профиль";

    /// <summary>Вызывается при открытии вкладки: обновляет статусы всех серверов.</summary>
    public void OnActivated() => RefreshCommand.Execute(null);

    private async Task AddAsync()
    {
        if (!ServerAddress.TryParse(NewAddress, out var ep))
        { Status = "Адрес не распознан. Примеры: play.example.com или play.example.com:25570"; return; }
        var entry = new ServerEntry
        {
            Name = string.IsNullOrWhiteSpace(NewName) ? ep.Host : NewName.Trim(),
            Address = ep.Port == 25565 ? ep.Host : ep.ToString(),
            ProfileId = Profiles.Selected?.Id,
        };
        var row = new ServerRow(entry, ProfileNameOf(entry));
        Items.Add(row);
        _s.ServerStore.Save(Items.Select(r => r.Entry));
        NewName = ""; NewAddress = ""; Status = "Сервер добавлен.";
        await RefreshRowAsync(row);
    }

    private void Remove(ServerRow? row)
    {
        if (row is null) return;
        Items.Remove(row);
        _s.ServerStore.Save(Items.Select(r => r.Entry));
        Status = $"Сервер «{row.Name}» удалён из избранного.";
    }

    private async Task RefreshAllAsync()
    {
        Status = "Проверка серверов…";
        await Task.WhenAll(Items.ToList().Select(RefreshRowAsync));
        Status = Items.Count == 0 ? "Добавьте первый сервер в избранное." : $"Проверено серверов: {Items.Count}";
    }

    private async Task RefreshRowAsync(ServerRow row)
    {
        if (!ServerAddress.TryParse(row.Address, out var ep)) { row.Status = "неверный адрес"; row.Online = false; return; }
        row.Status = "Проверка…";
        var st = await _s.Pinger.PingAsync(ep, CancellationToken.None);
        row.Online = st.Online;
        row.Status = st.Online ? "В сети" : "Недоступен" + (st.Error is { Length: > 0 } ? " — " + st.Error : "");
        row.Players = st.Online ? $"Игроков: {st.Players}/{st.MaxPlayers}" : "";
        row.Ping = st.Online ? $"{st.LatencyMs} мс" : "";
        row.Motd = st.Online ? $"{st.Version}  ·  {st.Motd}" : "";
    }

    private void Play(ServerRow? row)
    {
        if (row is null) return;
        if (!ServerAddress.TryParse(row.Address, out var ep)) { Status = "Неверный адрес сервера."; return; }
        var profile = Profiles.Items.FirstOrDefault(p => p.Id == row.Entry.ProfileId) ?? Profiles.Selected;
        if (profile is null) { Status = "Сначала создайте профиль на вкладке «Версии»."; return; }
        Profiles.Selected = profile;
        PlayRequested?.Invoke(ep);
    }
}
