using System.Text;
using System.Windows.Threading;
using NEXORA.App.Mvvm;
using NEXORA.Infrastructure.Minecraft;

namespace NEXORA.App.ViewModels;

public sealed class HomeViewModel : PageViewModel
{
    private readonly AppServices _s;
    private readonly StringBuilder _log = new();
    private readonly Queue<string> _pending = new();
    private readonly DispatcherTimer _flush;
    private GameProcess? _game;
    private CancellationTokenSource? _cts;
    private string _status = "Готов к запуску";
    private double _progress;
    private bool _busy, _running;
    private string _logText = "";

    public HomeViewModel(AppServices s) : base("Главная", "\uE80F")
    {
        _s = s;
        _flush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _flush.Tick += (_, _) => FlushLog();
        s.Account.Changed += () => Ui.Run(() => { OnPropertyChanged(nameof(AccountText)); CommandManager_Refresh(); });
        PlayCommand = new AsyncRelayCommand(PlayAsync, () => !_busy && !_running && Profiles.Selected is not null, ex => Fail(ex));
        StopCommand = new RelayCommand(Stop, () => _running || _busy);
    }

    public string Welcome => "Добро пожаловать в NEXORA";
    public ProfilesModel Profiles => _s.Profiles;
    public string AccountText => _s.Account.Current is { } c ? "Игрок: " + c.Profile.Name : "Вход не выполнен — откройте вкладку «Аккаунт»";
    public string NoProfileHint => Profiles.Items.Count == 0 ? "Профилей пока нет. Создайте первый на вкладке «Версии»." : "";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }

    public AsyncRelayCommand PlayCommand { get; }
    public RelayCommand StopCommand { get; }

    private static void CommandManager_Refresh() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    private async Task PlayAsync()
    {
        var profile = Profiles.Selected ?? throw new InvalidOperationException("Выберите профиль.");
        _busy = true; Progress = 0; _log.Clear(); LogText = "";
        _cts = new CancellationTokenSource();
        try
        {
            Status = "Проверка аккаунта…";
            var session = await _s.Account.EnsureValidAsync(_cts.Token);
            var progress = new Progress<InstallProgress>(p => { Status = p.Stage; Progress = p.Fraction * 100; });
            var token = _cts.Token;
            _game = await Task.Run(() => _s.Launcher.LaunchAsync(profile, session, _s.Settings, progress,
                line => { lock (_pending) _pending.Enqueue(line); },
                code => Ui.Run(() => OnExit(code)),
                _ => Ui.Run(Profiles.Save), token), token);
            _running = true;
            _flush.Start();
            Status = "Игра запущена";
        }
        catch (OperationCanceledException) { Status = "Запуск отменён"; }
        finally { _busy = false; Progress = 0; }
    }

    private void Stop()
    {
        _cts?.Cancel();
        _game?.Kill();
    }

    private void OnExit(int code)
    {
        _running = false; _game = null;
        FlushLog();
        Status = code == 0 ? "Игра закрыта" : $"Игра завершилась с ошибкой (код {code}). Подробности — в журнале ниже и в Настройках → Журнал.";
        CommandManager_Refresh();
    }

    private void Fail(Exception ex)
    {
        Status = "Ошибка: " + ErrorText.From(ex);
        _busy = false; Progress = 0;
    }

    private void FlushLog()
    {
        string[] lines;
        lock (_pending) { if (_pending.Count == 0) return; lines = _pending.ToArray(); _pending.Clear(); }
        foreach (var l in lines) _log.AppendLine(l);
        if (_log.Length > 40000) _log.Remove(0, _log.Length - 30000);
        LogText = _log.ToString();
    }
}
