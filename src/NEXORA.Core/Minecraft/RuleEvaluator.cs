using System.Text.RegularExpressions;

namespace NEXORA.Core.Minecraft;

public static class RuleEvaluator
{
    /// <summary>Windows 10/11 x64. Функции (демо-режим, свой размер окна и т.п.) по умолчанию выключены.</summary>
    public static bool IsAllowed(IReadOnlyList<Rule> rules, ISet<string>? features = null)
    {
        if (rules.Count == 0) return true;
        var allowed = false;
        foreach (var r in rules)
            if (Matches(r, features)) allowed = r.Allow;
        return allowed;
    }

    private static bool Matches(Rule r, ISet<string>? features)
    {
        if (r.OsName is not null && r.OsName != "windows") return false;
        if (r.OsArch is not null && r.OsArch != "x64" && r.OsArch != "amd64") return false;
        if (r.OsVersion is not null && !Regex.IsMatch("10.0", r.OsVersion)) return false;
        if (r.Features is not null)
            foreach (var f in r.Features)
                if ((features?.Contains(f.Key) ?? false) != f.Value) return false;
        return true;
    }
}
