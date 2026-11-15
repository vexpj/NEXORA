using System.Text.RegularExpressions;
using NEXORA.Core.Models;

namespace NEXORA.Core.Diagnostics;

/// <summary>Превращает журнал игры и crash-report в понятные причины и безопасные варианты исправления.</summary>
public static class CrashAnalyzer
{
    private const RegexOptions O = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly TimeSpan T = TimeSpan.FromSeconds(2);
    private static Regex R(string p, RegexOptions extra = RegexOptions.None) => new(p, O | extra, T);

    private static readonly HashSet<string> IgnoredIds = new(StringComparer.OrdinalIgnoreCase)
        { "minecraft", "java", "fabricloader", "fabric-loader", "fabric", "forge", "neoforge", "mixin", "fml", "quilt_loader", "javafml", "lowcodefml" };

    private static readonly Regex HeapOom = R(@"OutOfMemoryError: (?!unable to create)|GC overhead limit exceeded|OutOfMemoryError: Java heap space");
    private static readonly Regex NativeOom = R(@"insufficient memory for the Java Runtime|Native memory allocation \((?:mmap|malloc)\) failed|Out of Memory Error \(");
    private static readonly Regex HeapReserve = R(@"Could not reserve enough space for.{0,40}object heap|Invalid maximum heap size|Could not create the Java Virtual Machine");
    private static readonly Regex ClassVersion = R(@"class file version (\d+)\.\d+\)(?:.{0,200}?up to (\d+)\.\d+)?", RegexOptions.Singleline);
    private static readonly Regex MajorMinor = R(@"Unsupported major\.minor version (\d+)\.\d+");
    private static readonly Regex FabricTrigger = R(@"Some of your mods are incompatible|Mod resolution failed|Incompatible mods found|ModResolutionException");
    private static readonly Regex FabricProblem = R(@"Mod '(?<mod>[^']+)' \((?<id>[^)]+)\)[^\n]*?requires (?:any version|version (?<ver>[^\n]*?)) of (?:mod )?'?(?<dep>[^'(\n]+?)'? \((?<depid>[^)]+)\), (?:which is missing|but only the wrong version is present: (?<have>[^\n!]+))");
    private static readonly Regex FabricIncompatible = R(@"Mod '(?<mod>[^']+)' \((?<id>[^)]+)\)[^\n]*? is incompatible with [^\n]*?'(?<other>[^']+)' \((?<otherid>[^)]+)\)");
    private static readonly Regex ForgeDep = R(@"Mod ID: '(?<id>[\w\-.]+)', Requested by: '(?<by>[\w\-.]+)', Expected range: '(?<range>[^']*)', Actual version: '(?<actual>[^']*)'");
    private static readonly Regex MixinTrigger = R(@"Mixin apply failed|InvalidMixinException|MixinTransformerError|Critical injection failure|Mixin transformation of .{1,80} failed|MixinApplyError");
    private static readonly Regex MixinConfig = R(@"(?<![\w\-.])(?:mixins?\.)?(?<id>[\w\-]+?)(?:\.mixins?)?\.json");
    private static readonly Regex ProblemFrame = R(@"Problematic frame:\s*\r?\n#\s*[CVjJ]\s+\[(?<dll>[\w.\-]+?)(?:\+0x[0-9a-f]+)?\]");
    private static readonly Regex AccessViolation = R(@"EXCEPTION_ACCESS_VIOLATION|EXCEPTION_STACK_OVERFLOW|SIGSEGV|A fatal error has been detected by the Java Runtime");
    private static readonly Regex OpenGl = R(@"GLFW error (?:65542|65543|65544)|Pixel format not accepted|WGL: |OpenGL 2\.0 is not supported|Failed to create window|driver does not appear to support OpenGL|GL_INVALID_OPERATION");
    private static readonly Regex Session = R(@"Invalid session|Failed to verify username|Authentication servers are down|401 Unauthorized");
    private static readonly Regex MissingFiles = R(@"Could not find or load main class|Failed to locate library|UnsatisfiedLinkError|ZipException|ZipError|invalid LOC header|NoClassDefFoundError|ClassNotFoundException");
    private static readonly Regex LoaderError = R(@"FormattedException|ModLoadingException|Failed to start Quilt|Failed to load Fabric|net\.fabricmc\.loader.{0,80}Exception");
    private static readonly Regex Description = R(@"^Description:\s*(?<d>.+)$", RegexOptions.Multiline);
    private static readonly Regex ErrorLine = R(@"^.*(?:Exception|Error)\b.*$", RegexOptions.Multiline);

    public static List<Diagnosis> Analyze(CrashContext c)
    {
        var list = new List<Diagnosis>();
        string text = Cap(c.LogText) + "\n" + Cap(c.CrashReportText) + "\n" + Cap(c.JvmCrashText);
        try
        {
            Memory(c, text, list);
            JavaVersion(c, text, list);
            FabricMods(text, list);
            ForgeMods(text, list);
            Mixin(text, list);
            Suspects(text, list);
            NativeCrash(c, text, list);
            Graphics(text, list);
            Account(text, list);
            if (list.Count == 0) { MissingFilesRule(text, list); LoaderGeneric(text, list); }
            if (list.Count == 0) ExitCodeOnly(c, list);
        }
        catch (RegexMatchTimeoutException) { /* слишком странный журнал — перейдём к общему ответу */ }
        if (list.Count == 0) Fallback(c, text, list);
        return list.OrderBy(d => d.Severity).ToList();
    }

    // ---------- память ----------
    public static int SuggestRam(int current, long physicalMb)
    {
        var cap = physicalMb > 0 ? (int)Math.Min(16384, Math.Max(2048, physicalMb * 6 / 10 / 512 * 512)) : 8192;
        var next = (int)(Math.Ceiling(current * 1.5 / 512) * 512);
        next = Math.Max(next, current + 1024);
        return Math.Min(next, cap / 512 * 512);
    }

    public static int SafeLowerRam(int current, long physicalMb)
    {
        var limit = physicalMb > 0 ? (int)(physicalMb * 5 / 10 / 512 * 512) : 4096;
        return Math.Max(2048, Math.Min(current, limit));
    }

    private static void Memory(CrashContext c, string text, List<Diagnosis> list)
    {
        if (HeapReserve.IsMatch(text))
        {
            var safe = SafeLowerRam(c.CurrentRamMb, c.PhysicalMemoryMb);
            var fixes = new List<FixAction>();
            if (safe < c.CurrentRamMb) fixes.Add(new(FixKind.SetRam, $"Выделить {safe} МБ", safe));
            Add(list, "heap-reserve", "Выделено слишком много памяти",
                $"Windows не смог дать игре {c.CurrentRamMb} МБ. Обычно это значит, что столько свободной памяти на компьютере нет." +
                (safe < c.CurrentRamMb ? $" Попробуйте {safe} МБ." : " Закройте другие программы и повторите запуск."), Severity.Error, fixes);
        }
        else if (HeapOom.IsMatch(text))
        {
            var next = SuggestRam(c.CurrentRamMb, c.PhysicalMemoryMb);
            var fixes = new List<FixAction>();
            string extra;
            if (next > c.CurrentRamMb)
            {
                fixes.Add(new(FixKind.SetRam, $"Выделить {next} МБ", next));
                extra = $"Выделено {c.CurrentRamMb} МБ. Рекомендуем {next} МБ.";
            }
            else extra = $"Уже выделено {c.CurrentRamMb} МБ — это почти максимум для вашего компьютера. Уберите самые тяжёлые моды, шейдеры или уменьшите дальность прорисовки.";
            if (c.Loader != LoaderType.Vanilla) fixes.Add(new(FixKind.OpenModsFolder, "Открыть папку модов"));
            Add(list, "oom", "Недостаточно выделенной памяти", "Игре не хватило оперативной памяти и она закрылась. " + extra, Severity.Error, fixes);
        }
        else if (NativeOom.IsMatch(text))
        {
            var safe = SafeLowerRam(c.CurrentRamMb, c.PhysicalMemoryMb);
            var fixes = new List<FixAction>();
            if (safe < c.CurrentRamMb) fixes.Add(new(FixKind.SetRam, $"Выделить {safe} МБ", safe));
            Add(list, "system-memory", "В системе не хватает свободной памяти",
                "Компьютеру самому не хватило памяти. Закройте браузер и другие программы" + (safe < c.CurrentRamMb ? $" или уменьшите выделение до {safe} МБ." : "."), Severity.Error, fixes);
        }
    }

    // ---------- Java ----------
    private static void JavaVersion(CrashContext c, string text, List<Diagnosis> list)
    {
        int needed = 0, have = 0;
        if (ClassVersion.Match(text) is { Success: true } m)
        {
            needed = int.Parse(m.Groups[1].Value) - 44;
            if (m.Groups[2].Success) have = int.Parse(m.Groups[2].Value) - 44;
        }
        else if (MajorMinor.Match(text) is { Success: true } m2) needed = int.Parse(m2.Groups[1].Value) - 44;
        else return;

        var fixes = new List<FixAction>();
        if (c.UsesCustomJava) fixes.Add(new(FixKind.ClearJavaPath, "Использовать Java, подобранную NEXORA"));
        if (c.Loader != LoaderType.Vanilla) fixes.Add(new(FixKind.OpenModsFolder, "Открыть папку модов"));
        var what = needed > 0 ? $"Нужна Java {needed}" + (have > 0 ? $", а запущена Java {have}" : "") + ". " : "";
        Add(list, "java-version", "Неподходящая версия Java",
            what + (c.UsesCustomJava
                ? "Вы выбрали свою Java в настройках — она не подходит к этой версии игры или моду."
                : "Если проблема появилась после установки мода, этот мод рассчитан на более новую версию игры — удалите или обновите его."),
            Severity.Error, fixes);
    }

    // ---------- моды ----------
    private static void FabricMods(string text, List<Diagnosis> list)
    {
        var problems = new List<string>();
        var ids = new List<(string id, string name)>();
        foreach (Match m in FabricProblem.Matches(text))
        {
            var mod = m.Groups["mod"].Value; var dep = m.Groups["dep"].Value.Trim();
            var ver = m.Groups["ver"].Value.Trim(); var have = m.Groups["have"].Value.Trim();
            if (!m.Groups["have"].Success) problems.Add($"Моду «{mod}» нужен мод «{dep}», но его нет.");
            else if (m.Groups["depid"].Value == "minecraft") problems.Add($"Мод «{mod}» не подходит к вашей версии Minecraft ({have}); нужна {ver}.");
            else problems.Add($"Моду «{mod}» нужен «{dep}» версии {ver}, а установлена {have}.");
            ids.Add((m.Groups["id"].Value, mod));
        }
        foreach (Match m in FabricIncompatible.Matches(text))
        {
            problems.Add($"Моды «{m.Groups["mod"].Value}» и «{m.Groups["other"].Value}» несовместимы друг с другом.");
            ids.Add((m.Groups["id"].Value, m.Groups["mod"].Value));
        }
        if (problems.Count == 0 && !FabricTrigger.IsMatch(text)) return;

        var fixes = ids.Where(i => !IgnoredIds.Contains(i.id)).DistinctBy(i => i.id).Take(3)
            .Select(i => new FixAction(FixKind.DisableMod, $"Отключить мод «{i.name}»", 0, i.id)).ToList();
        fixes.Add(new(FixKind.OpenModsFolder, "Открыть папку модов"));
        var body = problems.Count > 0
            ? string.Join(" ", problems.Distinct().Take(5))
            : "Загрузчик Fabric сообщил, что часть модов не подходит к игре или друг к другу.";
        Add(list, "mods-conflict", "Несовместимость модов", body + " Отключение мода обратимо: его можно включить обратно во вкладке «Моды».", Severity.Error, fixes);
    }

    private static void ForgeMods(string text, List<Diagnosis> list)
    {
        var ms = ForgeDep.Matches(text);
        if (ms.Count == 0) return;
        var problems = new List<string>(); var fixes = new List<FixAction>();
        foreach (Match m in ms)
        {
            var actual = m.Groups["actual"].Value;
            problems.Add($"Моду «{m.Groups["by"].Value}» нужен «{m.Groups["id"].Value}» версии {m.Groups["range"].Value}" +
                         (string.IsNullOrWhiteSpace(actual) || actual == "[MISSING]" ? ", но его нет." : $", а установлена {actual}."));
            if (!IgnoredIds.Contains(m.Groups["by"].Value) && fixes.Count < 3 && fixes.All(f => f.Text != m.Groups["by"].Value))
                fixes.Add(new(FixKind.DisableMod, $"Отключить мод «{m.Groups["by"].Value}»", 0, m.Groups["by"].Value));
        }
        fixes.Add(new(FixKind.OpenModsFolder, "Открыть папку модов"));
        Add(list, "mods-conflict", "Несовместимость модов", string.Join(" ", problems.Distinct().Take(5)) + " Установите недостающий мод на вкладке «Моды» или отключите мод, которому он нужен.", Severity.Error, fixes);
    }

    private static void Mixin(string text, List<Diagnosis> list)
    {
        if (!MixinTrigger.IsMatch(text)) return;
        var ids = MixinConfig.Matches(text).Select(m => m.Groups["id"].Value)
            .Where(id => !IgnoredIds.Contains(id) && id.Length >= 3).Distinct().Take(2).ToList();
        var fixes = ids.Select(id => new FixAction(FixKind.DisableMod, $"Отключить мод «{id}»", 0, id)).ToList();
        fixes.Add(new(FixKind.OpenModsFolder, "Открыть папку модов"));
        Add(list, "mixin", "Мод не смог внести изменения в игру",
            "Один из модов несовместим с версией игры или с другим модом (ошибка Mixin)." +
            (ids.Count > 0 ? $" Вероятный виновник: {string.Join(", ", ids)}." : "") + " Обновите мод или отключите его.", Severity.Error, fixes);
    }

    private static void Suspects(string text, List<Diagnosis> list)
    {
        if (list.Any(d => d.Id is "mods-conflict" or "mixin")) return;
        var idx = text.IndexOf("Suspected Mods:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        var lines = text[(idx + "Suspected Mods:".Length)..].Split('\n');
        var found = new List<(string name, string id)>();
        for (var i = 0; i < lines.Length && i < 8; i++)
        {
            var l = lines[i].TrimEnd('\r');
            if (i > 0 && (l.Length == 0 || !char.IsWhiteSpace(l[0]))) break;
            if (l.Contains("None", StringComparison.OrdinalIgnoreCase) && i == 0) return;
            var m = Regex.Match(l, @"(?<name>[^\t(,]+?) \((?<id>[^)]+)\)", RegexOptions.None, T);
            if (m.Success && !IgnoredIds.Contains(m.Groups["id"].Value)) found.Add((m.Groups["name"].Value.Trim(), m.Groups["id"].Value));
        }
        if (found.Count == 0) return;
        var fixes = found.Take(3).Select(f => new FixAction(FixKind.DisableMod, $"Отключить мод «{f.name}»", 0, f.id)).ToList();
        fixes.Add(new(FixKind.OpenModsFolder, "Открыть папку модов"));
        Add(list, "suspect-mod", "Игру остановил мод",
            $"В отчёте о сбое подозревается: {string.Join(", ", found.Select(f => f.name))}. Попробуйте отключить его и запустить снова.", Severity.Error, fixes);
    }

    // ---------- видеодрайвер и системные сбои ----------
    private static void NativeCrash(CrashContext c, string text, List<Diagnosis> list)
    {
        if (!AccessViolation.IsMatch(text) && ProblemFrame.IsMatch(text) == false) return;
        var dll = ProblemFrame.Match(text) is { Success: true } m ? m.Groups["dll"].Value : null;
        if (dll is null) return;
        var lower = dll.ToLowerInvariant();
        string? vendor = Regex.IsMatch(lower, @"^(nvoglv|nvwgf|nvd3d|nvlddmkm|nvcuda|nvapi)", RegexOptions.None, T) ? "NVIDIA"
            : Regex.IsMatch(lower, @"^(atio|atig|amdvlk|amdxx|aticfx|amdocl)", RegexOptions.None, T) ? "AMD"
            : Regex.IsMatch(lower, @"^(ig\d|igxe|igd|igc|igdrcl)", RegexOptions.None, T) ? "Intel" : null;
        if (vendor is not null)
        {
            Add(list, "gpu-driver", $"Сбой видеодрайвера {vendor}",
                $"Игру остановила системная библиотека видеокарты ({dll}). Обычно помогает обновить драйвер видеокарты и отключить оверлеи (Discord, MSI Afterburner, NVIDIA Overlay).",
                Severity.Error, new[] { VendorFix(vendor) });
        }
        else if (Regex.IsMatch(lower, @"hook|overlay|rtss|afterburner|discord|obs|steam|reshade", RegexOptions.None, T))
            Add(list, "overlay", "Сбой из-за стороннего приложения",
                $"Игру остановила библиотека «{dll}» — это похоже на оверлей или инжектор другой программы. Закройте её (Discord, MSI Afterburner, ReShade и т. п.) и запустите снова.", Severity.Error, Array.Empty<FixAction>());
        else
            Add(list, "native-crash", "Аварийное завершение Java",
                $"Игра упала внутри системной библиотеки «{dll}». Чаще всего помогает обновление драйверов и отключение программ-оверлеев.", Severity.Error,
                new[] { new FixAction(FixKind.OpenLogsFolder, "Открыть папку журналов") });
    }

    private static FixAction VendorFix(string vendor) => vendor switch
    {
        "NVIDIA" => new(FixKind.OpenUrl, "Драйверы NVIDIA", 0, "https://www.nvidia.com/Download/index.aspx"),
        "AMD" => new(FixKind.OpenUrl, "Драйверы AMD", 0, "https://www.amd.com/en/support"),
        _ => new(FixKind.OpenUrl, "Драйверы Intel", 0, "https://www.intel.com/content/www/us/en/download-center/home.html"),
    };

    private static void Graphics(string text, List<Diagnosis> list)
    {
        if (list.Any(d => d.Id is "gpu-driver" or "overlay") || !OpenGl.IsMatch(text)) return;
        Add(list, "opengl", "Проблема с графикой (OpenGL)",
            "Игра не смогла создать окно или инициализировать графику. Обычно виноват устаревший или стандартный драйвер Windows. Обновите драйвер видеокарты (NVIDIA / AMD / Intel). Если у вас два видеоадаптера, запустите Java на дискретной видеокарте в настройках графики Windows.",
            Severity.Error, new[] { VendorFix("NVIDIA"), VendorFix("AMD"), VendorFix("Intel") });
    }

    private static void Account(string text, List<Diagnosis> list)
    {
        if (!Session.IsMatch(text)) return;
        Add(list, "session", "Проблема с аккаунтом",
            "Серверу не удалось проверить вашу сессию. Откройте вкладку «Аккаунт», выйдите и войдите снова. Если серверы Mojang недоступны, повторите позже.", Severity.Warning, Array.Empty<FixAction>());
    }

    // ---------- запасные правила ----------
    private static void MissingFilesRule(string text, List<Diagnosis> list)
    {
        if (!MissingFiles.IsMatch(text)) return;
        Add(list, "missing-files", "Повреждены или не найдены файлы игры",
            "Не хватает файла или он повреждён (возможна блокировка антивирусом или недокачанная загрузка). «Восстановление» удалит временные файлы игры, и NEXORA проверит и докачает всё при следующем запуске. Миры и настройки не затрагиваются. Если ошибка осталась, проверьте установленные моды.",
            Severity.Warning, new[] { new FixAction(FixKind.RepairInstall, "Восстановить файлы игры") });
    }

    private static void LoaderGeneric(string text, List<Diagnosis> list)
    {
        if (!LoaderError.IsMatch(text)) return;
        Add(list, "loader", "Ошибка загрузчика модов",
            "Fabric/Forge/NeoForge не смог запуститься. Это бывает из-за несовместимого мода или неполной установки загрузчика. Отключите недавно добавленные моды или переустановите загрузчик кнопкой ниже.",
            Severity.Error, new[] { new FixAction(FixKind.RepairInstall, "Переустановить загрузчик"), new FixAction(FixKind.OpenModsFolder, "Открыть папку модов") });
    }

    private static void ExitCodeOnly(CrashContext c, List<Diagnosis> list)
    {
        if (c.ExitCode is -1073740791 or -1073741819 or -1073740940)
            Add(list, "native-exit", "Аварийное завершение Java",
                $"Игра закрылась с системным кодом {c.ExitCode} без подробностей. Чаще всего причина в драйвере видеокарты или в программах-оверлеях (Discord, MSI Afterburner, ReShade, антивирус). Обновите драйвер и закройте такие программы.",
                Severity.Error, new[] { VendorFix("NVIDIA"), VendorFix("AMD"), VendorFix("Intel") });
    }

    private static void Fallback(CrashContext c, string text, List<Diagnosis> list)
    {
        string? detail = null;
        if (Description.Match(text) is { Success: true } d) detail = d.Groups["d"].Value.Trim();
        else
        {
            var last = ErrorLine.Matches(text).Cast<Match>().LastOrDefault();
            if (last is not null) detail = last.Value.Trim();
        }
        if (detail is { Length: > 240 }) detail = detail[..240] + "…";
        Add(list, "unknown", $"Игра завершилась с ошибкой (код {c.ExitCode})",
            "Точную причину определить не удалось." + (detail is null ? "" : " Последняя ошибка: " + detail) + " Откройте журнал: в нём подробности. Эту ошибку можно показать на форуме сообщества или в чате поддержки.",
            Severity.Info, new[] { new FixAction(FixKind.OpenLogsFolder, "Открыть папку журналов") });
    }

    private static void Add(List<Diagnosis> list, string id, string title, string text, Severity sev, IEnumerable<FixAction> fixes)
    {
        if (list.Any(d => d.Id == id)) return;
        list.Add(new Diagnosis(id, title, text, sev, fixes.ToList()));
    }

    private static string Cap(string? s) => s is null ? "" : s.Length > 2_000_000 ? s[^2_000_000..] : s;
}
