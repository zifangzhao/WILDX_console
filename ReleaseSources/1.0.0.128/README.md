# WILD-X 1.0.0.128

Exact checked build128 and reused SDK115 compiler inputs are retained in compiled-sources.zip and source-inputs.json. No application compilation occurred during packaging. Other working-tree edits are excluded.

Adds automatic post-recording PC-clock timestamp correction, a standalone recursive correction script, distinct retained IMU sample journaling/correction, and the previously checked SD test workflow. PC/network clock correction remains separate. Missing clock evidence stays explicitly unavailable.

Bundled firmware and both helper applications are byte-identical to release105. The optional high-resolution USB timing firmware used in the 70-second V1/V2 experiments is not bundled or automatically installed. Existing firmware uses available coarse timing or marks precision unavailable. The measured neural clock intervals do not qualify physical ADC/GPIO or IMU latency.

All 43 installer payload files were extracted and hash-verified. The installer was not executed. Detailed software and earlier hardware evidence is in checks/. The Run WILD-X finish checkbox uses the existing shell launch function.
