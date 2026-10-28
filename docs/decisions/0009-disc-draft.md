# 0009 — Analysis produces a DiscDraft: manifest + file jobs

**Status:** accepted (M3)

The builder needs one model that the UI edits, the capacity ruler measures and the burn executes. Analysis
(scan → probe → group → metadata) ends in `Composer.Compose`, which returns a `DiscDraft`:

- `Manifest`: schema-valid, with **disc** paths (§8.1 layout: Film/, Dizi/, Muzik/, Fotograflar/, Setup/, Belgeler/,
  app artwork under `.disc/art/<id>/` so program folders stay byte-identical copies);
- `Files`: disc path → job (`CopyJob`, `MediaJob`, `VttJob`, `ImageJob`, `FrameJob`, `IconJob`, `BytesJob`), each with
  a size estimate for the ruler and `Capacity.Suggest`.

`Materializer.Run` executes the jobs into `%LOCALAPPDATA%\Inlay\work\<project>\` (copies are imaged straight from
the source) and patches output-only facts (BlurHash) into the manifest. Online metadata is downloaded into the
image cache when the user picks a match, so composing never needs the network.
