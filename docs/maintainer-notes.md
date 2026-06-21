# Maintainer Notes

This document is for project maintenance and release work.

## Development Checks

Run the following checks before publishing a release:

```text
dotnet build GatewayApp.sln -c Release
dotnet run --project GatewayApp.SmokeTests\GatewayApp.SmokeTests.csproj -c Release
```

The solution enables the built-in .NET analyzers in recommended mode through `Directory.Build.props`.

## Release

Publish releases from GitHub Actions:

1. Update the version in `GatewayApp/GatewayApp.csproj`.
2. Push `main`.
3. Open `Actions` -> `Release` -> `Run workflow`.
4. Leave `version` empty to use the project version, or enter the same version as `GatewayApp.csproj`.

The release workflow creates:

```text
FactoryIOGateway-vX.X.X-win-x64.zip
```

Do not upload the `.exe` as a separate release asset. The ZIP contains the single-file executable.

After the release is created, the release workflow starts the VirusTotal scan workflow.

## Release Security Scan

The release assets are uploaded to VirusTotal and scanned by the `VirusTotal release scan` workflow.
The scan result is written to the GitHub Release notes under `VirusTotal Scan` and to the workflow summary.

Set one of these repository secrets before publishing a release:

```text
VIRUSTOTAL_API_KEY
VT_API_KEY
```

The release scan fails if VirusTotal reports any `malicious` or `suspicious` detections.
