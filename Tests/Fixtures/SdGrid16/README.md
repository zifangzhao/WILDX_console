These six small SD16 vectors are byte-for-byte copies of the actual native
encoder output supplied from `C:/Frank/Code/XE_NP/_artifacts/grid16_codec_stage`
on 2026-10-30. They are codec fixtures, not hardware evidence or one coherent
complete recording. In particular, the metadata has one observed slot while
the independent data-sector fixture spans twelve slots. Its phase is explicitly
unknown. Tests parse the originals without rewriting them.

`closed_loop_metadata.bin` is the existing actual C producer vector from
`_artifacts/closed_loop_20261027/carrier/metadata.bin`, unchanged SHA256
`F1406086BE6EF06404220DC828CCD2EDD5F377069F94802684A7658C18A947C4`.
The SD16 test relocates its typed configuration/event fields to a synthetic
session, retaining its inner format and checking direct version16 admission.
