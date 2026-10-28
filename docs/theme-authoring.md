# Disk menüsü şablonu yazmak

Bir şablon (`.disctheme`) bir zip dosyasıdır. Builder'da **Disk menüsü › HTML olarak düzenle** ile standart bir
şablonu kopyalayıp düzenleyebilir ya da sıfırdan yazabilirsin. Şablon diskteki menünün tamamıdır: Inlay onu yakma
anında manifestle birlikte işler (render) ve `.disc/ui/index.html` olarak diske yazar.

## Paket yapısı

```
theme.json          ← zorunlu: kimlik, ayarlar, yetenekler (schemas/theme.schema.json)
index.liquid        ← giriş sayfası (theme.json › entry)
partials/*.liquid   ← {% include 'partials/x' %} ile kullanılan parçalar
assets/             ← CSS, JS, görseller (diske olduğu gibi kopyalanır)
fonts/              ← gömülü yazı tipleri ve lisans metinleri (internetten font yüklenemez)
```

Standart şablonlar (`themes/standard-*`) ortak parçalarını `themes/_shared` klasöründen alır; dışa aktarılan
`.disctheme` bu parçaları da içerir, yani kopyaladığın şablon kendi başına çalışır.

## theme.json

```json
{
  "id": "me.benim.sinema", "name": "Benim sinemam", "version": "1.0.0",
  "modes": ["movie", "mixed"], "engine": "liquid", "entry": "index.liquid", "minLauncher": "0.1.0",
  "capabilities": ["play", "run"],
  "settings": {
    "accent": { "type": "color", "label": "Vurgu rengi", "default": "auto:accent" },
    "blur":   { "type": "range", "label": "Bulanıklık", "min": 0, "max": 120, "step": 10, "unit": "px", "default": 70 }
  },
  "budget": { "maxAssetsMb": 15 }
}
```

- `settings` builder'ın **Ayarlar** formunu üretir. Değerlere şablonda `{{ settings.blur }}`, CSS'te
  `var(--ds-blur)` ile erişilir (camelCase anahtarlar CSS'te kebab-case olur: `showCast` → `--ds-show-cast`).
- Renk ayarı `auto` ya da `auto:<bg|surface|accent|text|muted>` olabilir: değer kapaktan çıkarılan paletten gelir.
- `engine: "static"` seçersen HTML olduğu gibi kopyalanır; veriyi `window.disc.manifest`'ten okursun.

## Liquid modeli

| Değişken | İçerik |
|---|---|
| `disc` | `manifest.disc` (title, subtitle, home, featured, locale…) |
| `items` | öğeler, id'ye göre sözlük: `{{ items['mv-x'].title }}` |
| `all_items` | öğeler, dizi |
| `sections` | bölümler; her birinde `entries` = o bölümün öğeleri |
| `actions` / `all_actions` | izin verilen eylemler (sözlük / dizi) |
| `theme.palette` | bg, surface, accent, accentText, text, muted |
| `settings` | çözülmüş ayar değerleri |
| `credits`, `licenses` | künye ve lisanslar ("Hakkında" için) |
| `hosted` | launcher içinde mi (`false`: tarayıcıda açılan Menu.html) |
| `text` | arayüz metinleri (tr-TR / en-US) |

Şablon yalnızca veri görür; .NET nesnelerine erişemez. Tüm `{{ }}` çıktıları HTML olarak kaçışlanır.

### Filtreler

| Filtre | Örnek | Sonuç |
|---|---|---|
| `duration` | `{{ 7322 \| duration }}` | 2 sa 2 dk |
| `clock` | `{{ 201 \| clock }}` | 3:21 |
| `filesize` | `{{ 4520000000 \| filesize }}` | 4,21 GB |
| `relurl` | `{{ item.cover \| relurl }}` | https://disc.local/Film/…/poster.webp |
| `t` | `{{ 'ui.watch' \| t }}` | İzle (`{0}` yer tutucuları argümanlarla dolar) |
| `json` | `<script type="application/json">{{ item \| json }}</script>` | script içinde güvenli JSON |

Disk dosyalarına **her zaman** `relurl` ile bağlan: aynı şablon hem launcher'da hem tarayıcıdaki `Menu.html`'de çalışır.

## Sayfadan launcher'a: `window.disc`

Tam tanım `sdk/disc-sdk.d.ts`. En çok kullanılanlar:

```js
disc.run("install-oyun")              // manifest.actions içindeki bir id; başka hiçbir şey çalıştırılamaz
disc.play("mv-film", { startSec: 0 }) // videoda paylaşılan tam ekran oynatıcı açılır
disc.playlist("al-albüm", undefined, { shuffle: true })
disc.installBundle(["ap-oyun", "ap-editor"])
disc.progress("mv-film")              // { positionSec, durationSec, watched } | null
disc.on("player", state => …)         // ayrıca: action-started, action-exited, bundle-progress, media-key, gamepad
```

HTML'de `data-action="<action id>"` olan bir düğmeye tıklamak `disc.run` ile aynıdır (standart `ds.js`).

İki disk ayarı şablona bırakılır; standart `ds.js` ikisini de uygular, kendi şablonun da okuyabilir:

- `disc.autoplay` ("Disk takılınca › Hemen oynat"): açılışta öne çıkan öğenin birincil eylemini kendiliğinden çalıştır.
- `disc.remote === false` ("Kumanda ile gezinme" kapalı): oyun kolunu dinleme; launcher da `gamepad` olayı göndermez.

**Önizleme.** Builder'daki önizleme diske giden aynı `index.html`'dir; SDK orada launcher yerine bir taklitle konuşur:
`disc.system()` makul değerler döner, `disc.run` / `installBundle` hiçbir şey başlatmadan başarılı olur, yalnızca
LibVLC gerektiren videolar önizlemede oynamaz (`unsupported-codec`). Yani önizlemede çalışan menü diskte de çalışır.

## Lint (yakmadan önce)

| Kural | Önem | Ne zaman |
|---|---|---|
| `liquid-syntax`, `liquid-runtime` | hata | şablon işlenemiyor |
| `unknown-action` | hata | `disc.run('x')` ya da `data-action="x"` manifestte yok |
| `external-resource`, `external-url` | hata | `http(s)://` (disc.local dışı) kaynak, bağlantı ya da CSS `url()` |
| `missing-asset` | hata | göreli bir yol pakette yok |
| `asset-budget` | hata | assets + fonts `budget.maxAssetsMb`'ı aşıyor |
| `capability` | uyarı | SDK çağrısı `capabilities` listesinde değil |
| `contrast` | uyarı | ayar rengi ya da metin, zemin üzerinde okunaksız |

Hata varsa **Diske yaz** kapalıdır. Menü ayrıca diskte hiçbir ağ isteği yapamaz: launcher disk dışındaki her isteği
reddeder ve her sayfaya bir Content-Security-Policy eklenir.

## İyi menü için

`design-prompt.md` §4: gerçek görsel tam ekran, en fazla iki düğme, teknik terim yok, ← → Enter Esc ile her şeye
ulaşılabilir, `prefers-reduced-motion` desteklenir. Standart şablonların kontrol listesi: `docs/template-checklist.md`.
