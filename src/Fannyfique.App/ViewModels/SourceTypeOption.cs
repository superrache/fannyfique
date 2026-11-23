using Fannyfique.Core.Model;

namespace Fannyfique.App.ViewModels;

public sealed record SourceTypeOption(SourceType Type, string Label)
{
    public static IReadOnlyList<SourceTypeOption> All { get; } =
        Enum.GetValues<SourceType>().Select(t => new SourceTypeOption(t, t.Label())).ToList();
}
