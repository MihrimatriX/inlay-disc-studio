# Standard template checklist (M4)

Checked on the reference discs (`tests/golden/*.html`, screenshots from the real launcher with
`node scripts/screenshot.mjs`, Lighthouse via Chrome on the unhosted `Menu.html`).

## §7.2 principles

| # | Principle | Sinema (t1) | Albüm (t2) | Galeri (t3) | Oyun (t4) | Hepsi (menu) | Paket | Dizi |
|---|---|---|---|---|---|---|---|---|
| 1 | Real picture full screen, UI almost invisible; title block bottom-left over two gradients | ✓ | ✓ (blurred cover) | ✓ (light mosaic, §4.4) | ✓ | ✓ | ✓ | ✓ |
| 2 | At most two buttons; details behind the secondary one | ✓ İzle + Bölümler | ✓ Çal + Karışık çal | ✓ Slayt gösterisi | ✓ Kur + Kurmadan oyna | ✓ covers only | ✓ Seçilenleri kur | ✓ İzle + Bölümler |
| 3 | Single-kind discs skip the main menu (§7.0) | ✓ | ✓ | ✓ | ✓ | n/a | ✓ | ✓ |
| 4 | No technical terms (codec, engine, path, label, capacity); credits behind "Hakkında" | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| 5 | Natural feedback ("Kurulum başlatılıyor…"), never a call name | ✓ toasts from `ui.*` | ✓ | ✓ | ✓ | ✓ | ✓ row states | ✓ |
| 6 | ← → / Tab, Enter, Esc/Backspace, gamepad; View Transitions; `#<itemId>` deep links | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

## Automated checks (all reference discs)

- Every control on the opening page reachable with arrow keys alone: **0 unreachable** on all 7.
- axe-core WCAG 2 A/AA in the launcher: **no violations** on all 7.
- Lighthouse accessibility (Chrome, `Menu.html`): **100** for Sinema, Albüm, Galeri, Oyun, Hepsi.
- Lint: no errors for every standard theme × every sample manifest (15 combinations).
- Network: every off-disc request answered 403 by the launcher; fonts load from the disc.

## Visual match with `design/screens/t1–t4.png`

Side-by-side review of `artifacts/preview/t1-film.png … t4-oyun.png` against the references: same grid, type scale
(92/600 title, 15 meta, 19/300 summary), gradients, 54 px pill buttons, cream primary, 3 px cream focus ring,
t3 mosaic spans (wide = 2 columns, tall = 2 rows), t4 compatibility line and 168×95 screenshots.

Deliberate differences: the focus ring is drawn on the focused primary button (the references show no focus);
dates in t3 read "Haziran – Ağustos 2025" (the reference omits the year because its title carries it).
