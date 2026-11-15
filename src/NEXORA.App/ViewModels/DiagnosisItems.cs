using NEXORA.Core.Diagnostics;
using NEXORA.App.Mvvm;

namespace NEXORA.App.ViewModels;

public sealed class FixItem
{
    public FixItem(string label, RelayCommand apply) { Label = label; Apply = apply; }
    public string Label { get; }
    public RelayCommand Apply { get; }
}

public sealed class DiagnosisItem
{
    public DiagnosisItem(Diagnosis d, Action<FixAction> apply)
    {
        Title = d.Title; Explanation = d.Explanation;
        SeverityColor = d.Severity switch { Severity.Error => "#F87171", Severity.Warning => "#FBBF24", _ => "#60A5FA" };
        Fixes = d.Fixes.Select(f => new FixItem(f.Label, new RelayCommand(() => apply(f)))).ToList();
    }
    public string Title { get; }
    public string Explanation { get; }
    public string SeverityColor { get; }
    public IReadOnlyList<FixItem> Fixes { get; }
}
