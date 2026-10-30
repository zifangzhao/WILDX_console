# V2 Grid16 append: prepared test-only delivery

The sparse fused HEX combines recovery 6408DBB8, APP 7F238587 and USB
74FC7104. Each component's original manifest is preserved; retained device
configuration at 0x0801C000–0x0801DFFF is absent. The component identities,
runtime asset allowlist and incremental-source archive hash are recorded in
`catalog-input-draft.json`.

This is preparation only. The new pair is not represented as hardware
qualified or published. The APP `.xenp` contains the existing APP seal and
body. The WILD-X updater accepts the APP image ID, so the USB component is
provided at runtime as a sealed HEX, not an unsupported USB `.xenp`. Raw
bodies, manifests, source ZIP and evidence are repository-only material.

The source ZIP contains exact changed source files and recorded build/object
identities and link maps; `evidence/` holds additional native test receipts
and fixtures. The existing 6CB incremental archive is referenced by hash, not
copied. Its seven unavailable historical preimages and unhashed included
download source mean complete historical rebuild provenance is not claimed.
