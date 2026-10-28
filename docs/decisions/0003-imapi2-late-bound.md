# 0003 — IMAPI2 is driven late-bound (IDispatch)

**Status:** accepted (M1)

IMAPI2/IMAPI2FS objects are dual interfaces. The builder is not AOT, so `Inlay.Burn` creates them by ProgID and
calls them through `dynamic` (IDispatch) instead of re-declaring `[ComImport]` vtables from `imapi2.h`/`imapi2fs.h`.
A single slot out of order in a hand-written vtable corrupts calls silently; a misspelled IDispatch member fails
loudly with a `COMException`/`RuntimeBinderException`. Streams cross as `System.Runtime.InteropServices.ComTypes.IStream`.

Only event sinks (burn progress, M7) need a typed dispinterface declaration.

Objects are created and released on one STA thread (`Sta.Run`), released with `Marshal.FinalReleaseComObject`.
