# WILDX 1.0.0.226

Restores the old WILD PC-time field in scheduled IMU exports. V1 and V2 USB recordings and SD exports share the 32-byte analogin row format.

Build 226 includes the checked host closed-loop and USB retry changes. The installer uses the published 222 firmware bundle.

Validation includes 829 grid checks, 637 legacy checks, 234 SD association checks per runner, and replay of retained V1/V2 USB and SD captures. The V2 replay captures had unqualified PC time. See [qualification](qualification.json).
