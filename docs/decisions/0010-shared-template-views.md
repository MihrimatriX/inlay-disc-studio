# 0010 — Standard templates share one set of views and a small runtime

**Status:** accepted (M4)

On a mixed disc the movie page must look like t1, the album page like t2, the gallery like t3 and the game like
t4. So the item pages are not per-template: `themes/_shared/partials` renders every view once (menu, collections,
bundle and one section per item), and `assets/ds.js` shows one at a time (hash routing, View Transitions, spatial
arrow/gamepad navigation, toasts, music bar, gallery viewer, compatibility line, bundle install). The five
standard packages differ in their entry, defaults and settings (Sinema/Sade variant, album blur, gallery columns,
screenshots, primary button colour). Exported `.disctheme` files include the shared layer, so a copied template is
self-contained.

Templates are authored at 1440×810 and scale with `font-size: min(100vw/90, 100vh/50.625)`, which keeps 16:9
proportions from 1280×720 to 3840×2160.
