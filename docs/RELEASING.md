# Releasing

1. Update `ProductInfo.Version`, `CHANGELOG.md`, and documentation.
2. Run bootstrap tests, the x64 Release build, repository hygiene scan, and a
   full `scripts/Build-Windows.ps1` package build.
3. Test Setup and Portable in clean Windows 10 and 11 x64 virtual machines
   without preinstalled Node/dsh.
4. Create an annotated `vX.Y.Z` tag. The release workflow rebuilds from fixed
   Node, dsh, and WebView2 versions; verifies the Node SHA-256; produces Setup,
   Portable, SHA256SUMS, SPDX SBOM, and notices; and creates a pre-release.
5. Compare CI hashes with an independent build where practical and publish
   release notes that state the unsigned SmartScreen limitation.

Never commit downloaded Node archives, `node_modules`, dsh source, binaries
other than the reviewed application icon, logs, WebView profiles, API keys, or
local backup executables.
