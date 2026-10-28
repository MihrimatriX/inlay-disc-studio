# 0014 — Burning: IMAPI2 events through dispinterface sinks, verification by reading back

**Status:** accepted (M7)

`Burner` drives IMAPI2 the same late-bound way as `DiscImage` (ADR 0003), on one STA thread per job. It takes
`MsftDiscRecorder2` with exclusive access and checks the medium. `MsftDiscFormat2Data` writes the
`CreateResultImage()` stream (an ISO file's stream for "ISO kalıbı yaz") with `ForceMediaToBeClosed`.

- **Progress:** IMAPI2 reports through `DDiscFormat2DataEvents.Update` (and `DDiscFormat2EraseEvents`). The sinks
  are `[ComVisible]` classes implementing those dispinterfaces, advised through `IConnectionPointContainer`. COM's
  modal loop delivers them on the STA thread while `Write` blocks. Each update gives the stage (validating →
  formatting → writing → finalizing), LBA progress, the speed measured from LBA deltas and IMAPI2's own time
  estimate. Cancelling calls `CancelWrite` from inside the callback.
- **Free space** is read from the disc at burn time (`FreeSectorsOnMedia`, §8.4); the plan's nominal capacity only
  drives the ruler.
- **Speed:** "Önerilen" is the supported speed one below the fastest (§8.5); Gelişmiş also offers the slowest.
- **One write at a time** across the app (a semaphore in `Burner`, a busy flag in the host). The system stays awake
  with `SetThreadExecutionState` on the burning thread.
- **Errors:** IMAPI2 HRESULTs map to the states the UI words calmly: no disc, not blank, too small, buffer underrun,
  drive busy, write-protected, unsupported medium. The rest is "Yazma tamamlanamadı".
- **Verification:** SHA-256 of every source file is taken before writing. After writing, the tray is ejected and
  reloaded (drives that cannot reload wait up to five minutes for the user to close it), every file is read back
  from the volume and compared, and the read speed is sampled each second for the report graph. Because "Bitince
  kontrol et ve diski çıkar" can change while writing, the hashes are always taken and the checkbox is read only
  once writing ends. ISO burns compare sectors: the volume's readable data area against the same length of the
  image. IMAPI2 images run past the file system's size with padding and the closing UDF anchor, and a volume
  handle does not read beyond its file system.
- **Disk kopyala** (ADR 0004) reads the data disc's sectors through `\\.\X:` into an image (the file system's size)
  and burns that image to a blank disc in a second step. The image mounts and holds the same files.

No optical drive was available while this was built. Hashing, verification against a mounted ISO (Windows exposes
it as a CD-ROM volume), the raw-read copy, the error table and the speed choice are covered by tests. Physical burns
are on `docs/burn-checklist.md`.
