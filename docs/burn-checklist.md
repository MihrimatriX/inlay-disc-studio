# Burn checklist (M7, §12 "Manuel kontrol listesi")

Automated coverage (no recorder needed): `tests/Inlay.Burn.Tests/BurnTests.cs` (recommended speed, error mapping,
SHA-256 verification against a mounted ISO incl. changed/missing files, sector check, raw-read disc copy that mounts
again) and `tests/Inlay.Builder.Tests` (recorder choice, drive capsule text, no-recorder error).

The rows below need real hardware and are **not yet run**: the machine used during development has no optical
drive. Tick each one with the drive model and date when it is done.

## Before the first row

1. Install `InlaySetup-<version>.exe` (`scripts/package-installer.ps1`, or the release asset) on the PC with the
   recorder. It is unsigned until a certificate is set up, so SmartScreen asks first: **More info › Run anyway**.
2. Check **Ayarlar › Gelişmiş**: the recorder is listed under *Sürücü*, and *Yazdıktan sonra SHA-256 ile doğrula* is on.
3. Content: any film folder (one video, optional `poster.jpg`/`fanart.jpg`), a folder of photos and, for row 3,
   anything over 4,7 GB. With the repo at hand, `node scripts/make-sample.mjs artifacts/sample/src` writes a film
   folder and a mixed collection.
4. Keep blank media of each kind ready; rows 5, 9 and 11 also need a used disc and a rewritable one.

## What to send back for each row

- The row number, the result (✓ or what happened instead) and the date.
- Drive model and firmware (Device Manager › DVD/CD-ROM drives), and the media's brand, type and rated speed.
- `%LOCALAPPDATA%\Inlay\logs\jobs.log`: one line per job start (Inlay version, Windows build, the drive and the disc
  in it), per stage with the drive's speed, and the end: the verification report, or the full error with IMAPI2's
  HRESULT that the dialog's calm text leaves out. Rename it after the row (`row-03.log`) and delete it before the
  next row, so each log holds one run.
- A photo of the screen when the dialog shows anything other than "Disk hazır".

| # | Medium | Steps | Expected | Result |
|---|---|---|---|---|
| 1 | CD-R 700 MB | Photo project → Diske yaz, "Bitince kontrol et" on | Stages Hazırlanıyor → Diske yazılıyor → Disk kapatılıyor → Kontrol ediliyor → Disk hazır; report "N dosyanın hepsi doğru okundu" + speed graph; disc ejected; AutoPlay shows the icon and "Fotoğrafları göster" | ☐ |
| 2 | DVD-R 4,7 GB | Film project (sample from `scripts/make-sample.mjs`) | Same; launcher opens from the disc, film plays | ☐ |
| 3 | DVD+R DL 8,5 GB | Content over 4,7 GB | Ruler target DVD DL; burn and verify pass | ☐ |
| 4 | Any | Burn with no disc in the drive | Primary shows "Disk tak" and opens the tray; capsule grey "Sürücüde disk yok" | ☐ |
| 5 | Any | Insert a used non-rewritable disc | "Takılı disk boş değil…" | ☐ |
| 6 | Any | Content larger than the inserted disc | "Takılı disk bu içerik için küçük"; burn refused with "sığmıyor" text | ☐ |
| 7 | Any | Start a second burn from another Inlay window / while one runs | "Sürücü şu an başka bir iş yapıyor…" | ☐ |
| 8 | Any | Cancel during writing | Dialog closes, drive released; disc may be unusable (expected) | ☐ |
| 9 | CD-RW / DVD-RW | Araçlar › Diski sil, confirm | "Disk siliniyor" → "Disk silindi"; capsule shows blank medium | ☐ |
| 10 | Any | Araçlar › ISO kalıbı yaz with an exported ISO | Burn + sector check pass | ☐ |
| 11 | Data disc → blank | Araçlar › Disk kopyala: Diski oku, insert blank, Diske yaz | Copy mounts with identical files | ☐ |
| 12 | Laptop drive (no tray motor) | Burn with check on | Waits for the tray to be closed by hand, then verifies | ☐ |
| 13 | Any | Leave the PC idle during a long burn | No sleep until the burn ends | ☐ |
