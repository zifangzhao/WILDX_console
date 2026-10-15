# Storage source labels — WILDX 1.0.0.31

USB entries in Data Download now show **Neuropixels V1 USB** or
**Neuropixels V2 USB**, followed by the MCU UID. Both explicit refresh and
automatic discovery use the same label helper and existing generation identity
parsers. Unknown identities show **version unknown** with the original path.
The underlying source path, selection binding and download behavior are retained.

The isolated GUI build passed five actual-application label checks covering the
V1 identity from the supplied screenshot, case-insensitive matching, bare and
prefixed V2 identities, and unknown identities. All 13 extracted installer files
match the checked build; 12 runtime dependencies are unchanged from release 30.
No device enumeration, hardware commands or installer execution were performed.

Private evidence: `_artifacts/download_version_labels_20261014/`.
