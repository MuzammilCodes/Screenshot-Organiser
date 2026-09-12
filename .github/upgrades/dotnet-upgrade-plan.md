# .NET 10 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that a .NET 10 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 10 upgrade.
3. Upgrade Screenshot Organiser\Screenshot Organiser.csproj

## Settings

This section contains settings and data used by execution steps.

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects or their dependencies that need version update in projects that reference them.

| Package Name                       | Current Version | New Version | Description                    |
|:-----------------------------------|:---------------:|:-----------:|:-------------------------------|
| CommunityToolkit.Maui              | 7.0.1           | 15.0.1      | Recommended for .NET 10        |
| Microsoft.Extensions.Logging.Debug | 8.0.0           | 10.0.12     | Recommended for .NET 10        |
| Microsoft.Maui.Controls            | 8.0.91          | 10.0.101    | Recommended for .NET 10        |

### Project upgrade details

#### Screenshot Organiser\Screenshot Organiser.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0-android` to `net10.0-android`
  - `TargetPlatformVersion` should be changed from `34` to `36` (Android 16 / API level 36)

NuGet packages changes:
  - CommunityToolkit.Maui should be updated from `7.0.1` to `15.0.1` (*recommended for .NET 10*)
  - Microsoft.Extensions.Logging.Debug should be updated from `8.0.0` to `10.0.12` (*recommended for .NET 10*)
  - Microsoft.Maui.Controls should be updated from `8.0.91` to `10.0.101` (*recommended for .NET 10*)
