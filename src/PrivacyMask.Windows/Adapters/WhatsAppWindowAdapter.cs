using System.Collections.Generic;
using System.Linq;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Windows.Adapters;

public sealed class WhatsAppWindowAdapter : WindowAdapterBase
{
    public override AppId AppId => AppId.WhatsApp;

    protected override LayoutPreset SelectPresetCore(WindowSnapshot snapshot, IReadOnlyList<LayoutPreset> presets)
    {
        return snapshot.Bounds.Width >= 900
            ? presets.First(preset => preset.LayoutVariant == "wide")
            : presets.First(preset => preset.LayoutVariant == "compact");
    }
}
