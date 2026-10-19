# Console compilation versions

Increment AssemblyVersion and AssemblyFileVersion before every console
compilation. `build.ps1` runs `bump_version.ps1` automatically.

For a direct cached MSBuild Compile, run `bump_version.ps1` first, then capture
the source-input ledger and compile. Every new compilation gets a new version,
including a repair compile. Packaging an existing checked binary does not bump
its version; the installer reads the version from that binary.
