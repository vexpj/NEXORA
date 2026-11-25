using NEXORA.App.Mvvm;
using NEXORA.Core.Updates;
using NEXORA.Infrastructure.Updates;

namespace NEXORA.App.ViewModels;

/// <summary>Состояние обновления лаунчера (общее для меню и страницы «Новости»).</summary>
public sealed class UpdateModel : ObservableObject
{
    private readonly AppServices _s;
    private ReleaseInfo? _info;
    private string _status = "Проверка обновлений ещё не выполнялась.";
    private bool _busy;

    public UpdateModel(AppServices s) => _s = s;

    public bool IsAvailable => _info is not null;
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Notes => _info?.Notes ?? "";
    public string AvailableVersion => _info?.Version.ToString() ?? "";

    public async Task CheckAsync(bool silent)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Проверка обновлений…";
        try
        {
            _info = await _s.UpdateSvc.CheckAsync(AppVersion.Parsed, CancellationToken.None);
            Status = _info is null ? $"У вас последняя версия ({AppVersion.Current})." : $"Доступна новая версия {_info.Version} (у вас {AppVersion.Current}).";
        }
        catch (Exception ex)
        {
            _info = null;
            Status = silent ? "Не удалось проверить обновления." : "Не удалось проверить обновления: " + ErrorText.From(ex);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsAvailable)); OnPropertyChanged(nameof(Notes)); OnPropertyChanged(nameof(AvailableVersion));
        }
    }

    /// <summary>Скачивает и проверяет установщик, запускает его и закрывает лаунчер. При любой ошибке лаунчер остаётся прежней версии.</summary>
    public async Task InstallAsync()
    {
        if (_info is null || IsBusy) return;
        var release = _info;
        IsBusy = true;
        try
        {
            Status = "Загрузка и проверка обновления…";
            var path = await Task.Run(() => _s.UpdateSvc.DownloadAsync(release, CancellationToken.None));
            Status = "Запуск установщика. Подтвердите запрос прав администратора…";
            UpdateService.RunInstaller(path);
            await Task.Delay(1000);
            Ui.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Status = "Установка отменена в окне прав администратора. Лаунчер остался прежней версии; попробуйте снова.";
        }
        catch (Exception ex)
        {
            Status = "Обновление не удалось, лаунчер остался прежней версии. " + ErrorText.From(ex);
        }
        finally { IsBusy = false; }
    }
}
