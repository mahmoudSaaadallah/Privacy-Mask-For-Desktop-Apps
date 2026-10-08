using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public readonly record struct ProtectionRenderPolicy(
    bool ShouldRender,
    bool ForceFullWindowMask,
    bool AllowHoverReveal,
    MaskStyle ForcedStyle,
    MaskColorOption ForcedColor,
    double ForcedStrength)
{
    public static ProtectionRenderPolicy ForMode(RuntimeMode mode)
    {
        return mode switch
        {
            RuntimeMode.Off => new ProtectionRenderPolicy(
                ShouldRender: false,
                ForceFullWindowMask: false,
                AllowHoverReveal: false,
                ForcedStyle: MaskStyle.SolidRedact,
                ForcedColor: MaskColorOption.Black,
                ForcedStrength: MaskIntensityScale.Maximum),
            RuntimeMode.Panic => new ProtectionRenderPolicy(
                ShouldRender: true,
                ForceFullWindowMask: true,
                AllowHoverReveal: false,
                ForcedStyle: MaskStyle.SolidRedact,
                ForcedColor: MaskColorOption.Black,
                ForcedStrength: MaskIntensityScale.Maximum),
            _ => new ProtectionRenderPolicy(
                ShouldRender: true,
                ForceFullWindowMask: false,
                AllowHoverReveal: true,
                ForcedStyle: MaskStyle.Blur,
                ForcedColor: MaskColorOption.Black,
                ForcedStrength: MaskIntensityScale.Default),
        };
    }
}
