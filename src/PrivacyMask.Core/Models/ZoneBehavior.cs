using System;

namespace PrivacyMask.Core.Models;

[Flags]
public enum ZoneBehavior
{
    None = 0,
    RevealOnHover = 1,
    HideDuringTemporaryReveal = 2,
}
