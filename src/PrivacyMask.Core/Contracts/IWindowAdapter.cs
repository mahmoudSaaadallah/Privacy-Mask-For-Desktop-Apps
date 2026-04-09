using System.Collections.Generic;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Contracts;

public interface IWindowAdapter
{
    AppId AppId { get; }

    bool IsMatch(WindowSnapshot snapshot, AppProfile profile);

    LayoutPreset SelectPreset(WindowSnapshot snapshot, AppProfile profile, IReadOnlyList<LayoutPreset> presets);
}
