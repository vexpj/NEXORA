using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using NEXORA.App.Mvvm;
using NEXORA.Core.Diagnostics;
using NEXORA.Core.Models;
using NEXORA.Core.Perf;
using NEXORA.Core.Servers;
using NEXORA.Infrastructure.Minecraft;
using NEXORA.Infrastructure.Perf;

namespace NEXORA.App.ViewModels;

public sealed class HomeViewModel : PageViewModel
{
    private readonly AppServices _s;
    private readonly StringBuilder _log = new();
    private readonly Queue<string> _pending = new();
    private readonly DispatcherTimer _flush;
    private readonly DispatcherTimer _perfTimer;
    private GameProcess? _game;
    private CancellationTokenSource? _cts;
    private PerformanceMonitor? _monitor;
    private GameProfile? _lastProfile;
    private DateTime _launchedUtc;
    private ServerEndpoint? _pendingServer;
    private bool _busy, _running, _stopRequested, _sampling, _monitoring;
    private int _xmx;
    private string _status = "Готов к запуску", _logText = "", _note = "";
    private double _progress, _cpuValue, _ramValue, _gpuValue;
    private string _cpuText = "", _ramText = "", _gpuText = "", _memoryHint = "";

    public HomeViewModel(AppServices s) : base("Главная", "\uE80F")
    {
        _s = s;
        _flush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _flush.Tick += (_, _) => FlushLog();
        _perfTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _perfTimer.Tick += async (_, _) => await PerfTickAsync();
        s.Account.Changed += () => Ui.Run(() => { OnPropertyChanged(nameof(AccountText)); Refresh(); });
        PlayCommand = new AsyncRelayCommand(PlayAsync, () => !_busy && !_running && Profiles.Selected is not null, Fail);
        StopCommand = new RelayCommand(Stop, () => _running || _busy);
    }

    public string Welcome => "Добро пожаловать в NEXORA";
    public ProfilesModel Profiles => _s.Profiles;
    public string AccountText => _s.Account.Current is { } c ? "Игрок: " + c.Profile.Name : "Вход не выполнен — откройте вкладку «Аккаунт»";
    public string NoProfileHint => Profiles.Items.Count == 0 ? "Профилей пока нет. Создайте первый на вкладке «Версии»." : "";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }

    // --- диагностика вылета ---
    public ObservableCollection<DiagnosisItem> Diagnoses { get; } = new();
    public bool HasProblems => Diagnoses.Count > 0;
    public string DiagnosisNote { get => _note; private set => SetProperty(ref _note, value); }

    // --- мониторинг ---
    public bool Monitoring { get => _monitoring; private set => SetProperty(ref _monitoring, value); }
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }
    public double CpuValue { get => _cpuValue; private set => SetProperty(ref _cpuValue, value); }
    public string RamText { get => _ramText; private set => SetProperty(ref _ramText, value); }
    public double RamValue { get => _ramValue; private set => SetProperty(ref _ramValue, value); }
    public string GpuText { get => _gpuText; private set => SetProperty(ref _gpuText, value); }
    public double GpuValue { get => _gpuValue; private set => SetProperty(ref _gpuValue, value); }
    public string MemoryHint { get => _memoryHint; private set => SetProperty(ref _memoryHint, value); }
    public string FpsText => "FPS: игра не передаёт его лаунчеру (нужен мод-датчик). Показать FPS в игре можно клавишей F3.";

    public AsyncRelayCommand PlayCommand { get; }
    public RelayCommand StopCommand { get; }

    private static void Refresh() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    /// <summary>Запуск профиля с подключением к серверу (вызывается со страницы «Серверы»).</summary>
    public void StartWithServer(ServerEndpoint server)
    {
        _pendingServer = server;
        if (PlayCommand.CanExecute(null)) PlayCommand.Execute(null);
        else { _pendingServer = null; Status = "Игра уже запущена или идёт подготовка."; }
    }

    private async Task PlayAsync()
    {
        var profile = Profiles.Selected ?? throw new InvalidOperationException("Выберите профиль.");
        _lastProfile = profile;
        _busy = true; _stopRequested = false; Progress = 0; _log.Clear(); LogText = ""; DiagnosisNote = "";
        Diagnoses.Clear(); OnPropertyChanged(nameof(HasProblems));
        var server = _pendingServer; _pendingServer = null;
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
                _ => Ui.Run(Profiles.Save), server, token), token);
            _launchedUtc = _game.StartedUtc;
            _running = true;
            _flush.Start();
            StartMonitor(profile);
            Status = server is { } sv ? $"Игра запущена, подключение к {sv.Host}…" : "Игра запущена";
        }
        catch (OperationCanceledException) { Status = "Запуск отменён"; }
        finally { _busy = false; Progress = 0; }
    }

    private void Stop()
    {
        _stopRequested = true;
        _cts?.Cancel();
        _game?.Kill();
    }

    private void OnExit(int code)
    {
        var game = _game; var profile = _lastProfile; var launched = _launchedUtc; var stopped = _stopRequested;
        _running = false; _game = null; _stopRequested = false;
        StopMonitor();
        FlushLog();
        _flush.Stop();
        Refresh();
        if (code == 0 || stopped || profile is null || game is null)
        {
            Status = code == 0 ? "Игра закрыта" : "Игра остановлена";
            return;
        }
        Status = "Игра вылетела. Анализирую причину…";
        _ = AnalyzeAsync(profile, code, launched, game.LogPath);
    }

    private async Task AnalyzeAsync(GameProfile profile, int code, DateTime launched, string logPath)
    {
        try
        {
            await Task.Delay(700);   // дать игре дописать crash-report
            var list = await Task.Run(() => _s.Crash.Analyze(profile, code, launched, logPath, _s.Settings));
            Diagnoses.Clear();
            foreach (var d in list) Diagnoses.Add(new DiagnosisItem(d, ApplyFix));
            OnPropertyChanged(nameof(HasProblems));
            Status = list.Any(d => d.Severity != Severity.Info) ? "Обнаружена проблема запуска" : $"Игра завершилась с ошибкой (код {code})";
        }
        catch (Exception ex) { Status = "Игра вылетела (код " + code + "). Анализ не удался: " + ex.Message; }
    }

    private void ApplyFix(FixAction fix)
    {
        if (_lastProfile is null) return;
        try
        {
            var r = _s.Fixes.Apply(fix, _lastProfile);
            if (r.ProfileChanged) Profiles.Save();
            DiagnosisNote = r.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { DiagnosisNote = "Не удалось применить: " + ex.Message; }
    }

    // ---------- мониторинг производительности ----------
    private void StartMonitor(GameProfile profile)
    {
        StopMonitor();
        if (_game is null) return;
        _xmx = profile.RamMb ?? _s.Settings.RamMb;
        _monitor = _s.CreateMonitor(_game.Id);
        Monitoring = true;
        CpuText = "CPU: …"; RamText = "Память: …"; GpuText = "GPU: …"; MemoryHint = "";
        _perfTimer.Start();
    }

    private void StopMonitor()
    {
        _perfTimer.Stop();
        _monitor?.Dispose(); _monitor = null;
        Monitoring = false;
    }

    private async Task PerfTickAsync()
    {
        if (_monitor is null || _sampling) return;
        _sampling = true;
        try
        {
            var s = await _monitor.SampleAsync(CancellationToken.None);
            if (s is null) return;
            CpuValue = s.CpuPercent; CpuText = $"CPU игры: {s.CpuPercent:0}%";
            RamValue = Math.Min(100, s.ProcessRamMb * 100.0 / Math.Max(1, _xmx));
            RamText = $"Память игры: {s.ProcessRamMb} МБ из {_xmx} МБ  ·  компьютер занят на {s.SystemRamUsedPercent}%";
            MemoryHint = PerfMath.NearMemoryLimit(s.ProcessRamMb, _xmx) ? "Игра почти исчерпала выделенную память. Увеличьте её в «Настройках», иначе возможны лаги и вылет." : "";
            if (s.Gpu is { } g)
            {
                GpuValue = g.UtilPercent;
                GpuText = $"GPU: {g.UtilPercent}%  ·  {g.TempC} °C  ·  видеопамять {g.MemUsedMb}/{g.MemTotalMb} МБ  ({g.Name})";
            }
            else { GpuValue = 0; GpuText = "GPU: показатели доступны только для видеокарт NVIDIA"; }
        }
        catch (Exception) { /* мониторинг не должен мешать игре */ }
        finally { _sampling = false; }
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
