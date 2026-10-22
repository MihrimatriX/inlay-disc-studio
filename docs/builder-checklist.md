# Builder checklist (M6, design-prompt.md §6)

The screens were captured from the builder's own WebView2 page over CDP with `node scripts/builder-shot.mjs`, using
the sample folder from `scripts/make-sample.mjs`, once with its `.nfo` and once without (no TMDB key, so the
"Bilgi bulunamadı" card shows). The native 44 px title bar is not part of the page capture; it was checked in the
running window.

| Check | Result |
|---|---|
| Side by side with `design/screens/01–04.png`: layout, colour, type | ✓ 01 nav 276 px, 280 px disc, 36/600 title, drive capsule; 02 table with 40×60 thumbnails, accent row and pills, dashed drop zone, ruler marks at 8,2 % / 55,3 %; 03 16:9 preview with blurred glow, 300 px panel, Sinema/Sade cards with the 2 px + 4 px ring; 04 dialog 580 px, spinning cover disc, 28/600 percent, 8 px bar |
| One orange primary button per screen | ✓ Klasör seç · ISO olarak kaydet · ISO olarak kaydet · Kaydet / Kopyala ve düzenle (code mode) · Tekrar dene (error) |
| No technical terms outside Ayarlar › Gelişmiş | ✓ Notes say "her bilgisayarda açılacak şekilde hazırlanacak"; codec, UDF, LibVLC and encoder only appear under Gelişmiş |
| Ruler shows CD / DVD / DVD DL, the over-full case with suggestions | ✓ `Footer.tsx`: warn fill, "0,6 GB fazla · DVD DL gerekli", up to three suggestion buttons (`Capacity.Suggest`) |
| Contrast, keyboard, reduced motion | ✓ axe-core WCAG 2 A/AA: no violations on 01, 02, 03, code mode, Ayarlar. Template cards and the kind menu are arrow-key groups, the native bar keeps the system shortcuts, `prefers-reduced-motion` stops the disc spin and transitions |

Deviation: the `faint` token is `#84868f` (design `#6d6f78`, 3.5:1) so section labels and table headers pass 4.5:1
(ADR 0013).

Keyboard (§11): Ctrl+O choose folder, Ctrl+S save (asks for a place the first time), Ctrl+B make the disc,
Delete remove selected rows, F2 rename, Ctrl+Z / Ctrl+Y undo and redo, Esc closes dialogs.
