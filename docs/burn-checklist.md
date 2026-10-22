# Burn checklist (M7, §12 "Manuel kontrol listesi")

Automated coverage (no recorder needed): `tests/Inlay.Burn.Tests/BurnTests.cs` (recommended speed, error mapping,
SHA-256 verification against a mounted ISO incl. changed/missing files, sector check, raw-read disc copy that mounts
again) and `tests/Inlay.Builder.Tests` (recorder choice, drive capsule text, no-recorder error).

The rows below need real hardware and are **not yet run**: the machine used during development has no optical
drive. Tick each one with the drive model and date when it is done.

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
