# 0005 — CD-Extra (M8) is not built in this release

**Status:** accepted (user decision)

M8 asks to prove feasibility on real media before shipping. The development machine has no optical drive, so a
CD-DA first session (`IDiscFormat2TrackAtOnce`, `DoNotFinalizeMedia`) followed by a data session cannot be verified.
The feature stays off: `album.audioCd` is never set to `true` by the builder and templates never show the
"Bu disk müzik çalarlarda da çalar" note.

To revisit: on a machine with a CD-R writer, prototype (1) TAO audio session with 44.1 kHz/16-bit PCM from FFmpeg,
(2) leave media open, (3) `IDiscFormat2Data` with `MultisessionInterfaces` + `ImportFileSystem` for the second
session, (4) check playback on a standalone CD player and that Explorer shows the data session.
