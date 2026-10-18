# PrivacyMask Requirements

## End-user requirements

- Windows 10 or Windows 11, 64-bit
- At least one conventional Windows desktop application to protect; WhatsApp
  Desktop and Telegram Desktop have ready-made profiles
- A screen resolution of at least 1280 x 720 is recommended
- Administrator rights are not required for normal use

## Developer requirements

- Windows 10 or Windows 11, 64-bit
- .NET 10 SDK
- Visual Studio 2022 or later with the `.NET desktop development` workload, or a compatible editor with WPF support
- Visual Studio or Build Tools 2022 or later with the `Desktop development with C++`
  workload and Windows SDK when publishing/installing from source; the stable
  launcher uses Native AOT and requires the Windows C++ linker
- PowerShell 5.1 or later
- PowerShell 7 for update schema checks and signed release packaging (not required by end users or the existing installer)
- Git

## Optional tools

- GitHub Desktop for simple repository operations
- Windows Terminal for a better command-line experience
