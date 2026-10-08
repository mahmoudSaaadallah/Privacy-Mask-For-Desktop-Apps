using PrivacyMask.Core.Models;

namespace PrivacyMask.Windows.Adapters;

public sealed class GenericWindowAdapter : WindowAdapterBase
{
    public override AppId AppId => AppId.Custom;

    protected override LayoutPreset SelectPresetCore(
        WindowSnapshot snapshot,
        IReadOnlyList<LayoutPreset> presets)
    {
        return presets.First();
    }
}
