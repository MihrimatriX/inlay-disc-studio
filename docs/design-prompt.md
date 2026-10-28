# Inlay Disc Studio — Tasarım Promptu

> Bu dosya Inlay Disc Studio'nun (kısa adı Inlay) görsel dilini ve ekranlarını tanımlar. Uygulamayı yazan ajan (bkz. `app-prompt.md`) arayüzü
> **bu dosyaya ve `design/screens/` altındaki referanslara birebir uyarak** kurar. Ekranlar referanstır:
> piksel ölçüleri, renkler, yazı tipleri ve metinler buradan alınır. Bir şey burada yoksa yeni bir görsel
> fikir icat etme; mevcut bileşenlerden türet.

---

## 0. İki ayrı yüzey

| Yüzey | Kim kullanır | Karakter |
|---|---|---|
| **Builder** (Inlay Disc Studio) | Diski hazırlayan kişi, Windows masaüstünde | Modern, koyu, sade bir **disk yakma aracı**. İlk bakışta "disk yakıcı" olduğu anlaşılır; kalabalık değildir. |
| **Disk menüsü şablonları** | Diski takan izleyici/oyuncu, PC veya TV | Doğal, sinematik, **içerik odaklı**. Gerçek görsel tam ekran; arayüz neredeyse görünmez. |

İkisi birbirine karışmaz: builder'ın turuncu vurgusu ve araç dili disk menüsüne taşınmaz; disk menüsü
nötr krem/siyah butonlarla içeriğin rengini öne çıkarır.

---

## 1. Tasarım ilkeleri (bağlayıcı)

1. **Bir ekran, bir iş.** Her ekranda tek bir birincil eylem vardır (turuncu degrade buton). Diğer her şey ikincildir.
2. **Kullanıcının dili.** Teknik terim (codec, remux, UDF, LibVLC, manifest) arayüzde görünmez. "MP4'e aktar" değil
   "her bilgisayarda açılır"; "UDF" değil hiçbir şey. Teknik ayrıntı yalnızca Ayarlar › Gelişmiş'te.
3. **Disk yakıcı kimliği.** Gümüş/yanardöner disk görseli, sürücü durumu, CD / DVD / DVD DL **doluluk cetveli** ve
   "Diske yaz" eylemi her zaman tanınır biçimde bulunur. Bunlar ürünün imzasıdır, kaldırılmaz.
4. **Gerçek içerik, çizim değil.** Kapak, afiş, fotoğraf her zaman gerçek görseldir. Yer tutucu gerekiyorsa
   nötr gri kutu kullanılır; CSS ile çizilmiş sahte illüstrasyon yapılmaz.
5. **Sakinlik.** Az kenarlık, ince ışık çizgileri, yumuşak gölge. Aynı anda en fazla bir parlama/glow öğesi.
6. **Erişilebilirlik.** Metin kontrastı ≥ 4.5:1, dokunma hedefleri ≥ 40 px (builder) / ≥ 44 px (disk menüsü),
   tam klavye desteği, görünür odak halkası, `prefers-reduced-motion` desteği.

---

## 2. Builder tasarım sistemi

### 2.1 Renk token'ları (koyu tema, varsayılan)

| Token | Değer | Kullanım |
|---|---|---|
| `bg` | `#0f1013` + üstte `radial-gradient(70% 60% at 0% 0%, #20222a, transparent 70%)` | Pencere zemini (Mica benzeri) |
| `glow` | `radial-gradient(50% 50% at 100% 100%, rgba(255,106,43,.07), transparent 70%)` | Yalnızca Başlangıç ekranı sağ alt |
| `surface` | `linear-gradient(180deg, #17181c, #131417)` | Ana içerik paneli, yan paneller |
| `surface-line` | `inset 0 0 0 1px rgba(255,255,255,.06)` | Panel kenarı (border yerine) |
| `control` | `rgba(255,255,255,.06)` + `inset 0 0 0 1px rgba(255,255,255,.08)` | İkincil buton, input, select |
| `nav-active` | `rgba(255,255,255,.07)` + solda 3 px `accent` çizgi | Seçili kenar menü öğesi |
| `text` | `#ececef` | Birincil metin |
| `text-2` | `#c9cad1` | Kenar menü, ikincil metin |
| `muted` | `#9a9ca6` | Açıklama, başlık çubuğu |
| `faint` | `#6d6f78` | Bölüm etiketleri (BÜYÜK HARF), cetvel etiketleri |
| `accent` | `#ff5a1f` | Seçim çizgisi, cetvel dolgusu, odak |
| `accent-grad` | `linear-gradient(135deg, #ff8a3d, #ff5a1f)` | Birincil buton, ilerleme/doluluk dolgusu |
| `accent-soft` | `rgba(255,106,43,.14)` → `.02` (yatay degrade) | Seçili tablo satırı |
| `ok` | `#4ade80` (glow: `0 0 10px #4ade80`) | Sürücü hazır noktası, "sığıyor" |
| `warn` | `#fbbf24` | İmzasız paket, sığmıyor uyarısı |

Vurgu rengi Ayarlar'dan değiştirilebilir (turuncu varsayılan; mavi `#4c7dff`, mor `#a855f7`). Açık tema ikinci
aşamadır; token adları aynı kalır.

### 2.2 Tipografi

- Yazı tipi: **Geist** (300–700), yedek `Segoe UI Variable Text`, `Segoe UI`. Uygulamaya gömülür (OFL).
- Ölçek: başlık 36/600 (−0.03em), panel başlığı 24/600, gövde 15/400, açıklama 16/400 `muted`,
  küçük 13.5, bölüm etiketi 12/500 BÜYÜK HARF +0.06em `faint`.
- Rakamlar `font-variant-numeric: tabular-nums` (boyutlar, yüzdeler, süreler).

### 2.3 Şekil, boşluk, gölge

- Köşe yarıçapı: panel 18, kart/buton/input 10–12, hap etiket 999, disk görseli 50%.
- Boşluk ölçeği: 4 · 6 · 10 · 14 · 18 · 22 · 26 · 30 · 40. Panel iç boşluğu 22–24.
- Pencere: başlık çubuğu 44 px, araç çubuğu ~50 px; içerik paneli pencere kenarlarından 14 px içeride.
- Gölge: panel `0 30px 60px -30px rgba(0,0,0,.6)`; birincil buton `0 10px 24px -10px accent, inset 0 1px 0 rgba(255,255,255,.25)`;
  diyalog `0 40px 100px -30px rgba(0,0,0,.9)`.

### 2.4 Bileşenler

| Bileşen | Tanım |
|---|---|
| **Başlık çubuğu** | 44 px; solda 18 px disk ikonu (koyu göbek + turuncu konik degrade) + "Inlay" (600) + `/ Proje adı` (`muted`); sağda Windows pencere düğmeleri (46×44). |
| **Kenar menü** | 276 px; grup etiketleri ("YENİ DİSK", "ARAÇLAR"); öğe: 19 px çizgi ikon + metin, 10/12 padding, 10 radius; seçili: `nav-active`. Altta "Ayarlar". |
| **Araç çubuğu** | Geri (‹ + metin), dikey ayraç, Ekle / Kaldır; sağda bağlamsal düğme ("Disk menüsü"). |
| **Birincil buton** | 44 px yükseklik, 24–26 px yatay; `accent-grad`, beyaz 600 metin. Ekranda yalnızca bir tane. |
| **İkincil buton** | 36–44 px; `control`. |
| **Tablo** | Başlık 12.5 BÜYÜK HARF `faint`; satır 10/24 padding, satır arası 1 px `rgba(255,255,255,.05)`; ilk sütunda 40×60 küçük resim (6 radius); tür sütununda hap etiket. Seçili satır `accent-soft`. |
| **Hap etiket** | 4/10 padding, 13 px; nötr `rgba(255,255,255,.06)` / `text-2`, vurgulu `rgba(255,106,43,.16)` / `#ffb38a`. |
| **Bırakma alanı** | 1.5 px kesik `rgba(255,255,255,.1)`, 14 radius, ortada `faint` metin. Sürüklerken: kenar `accent`, zemin `accent-soft`. |
| **Doluluk cetveli** | 10 px yükseklik, `rgba(255,255,255,.07)` zemin; dolgu `accent-grad` + glow. İşaretler: CD 700 MB (1 px, %25 beyaz), hedef disk (2 px, `text`, etiketi kalın), DVD DL 8,5 GB sağda. Ölçek 0–8,5 GB doğrusal. Altında etiket satırı; sağda "4,40 GB · sığıyor" + "Diske yaz". Sığmazsa dolgu hedef çizgisini geçer, metin `warn`: "0,6 GB fazla · DVD DL gerekli". |
| **Sürücü kapsülü** | 999 radius, `control` zemin; yeşil nokta + "E: ASUS DRW-24D5MT · Boş DVD-R, 4,7 GB". Disk yoksa gri nokta "Sürücüde disk yok". |
| **Disk görseli** | Yanardöner: `conic-gradient(from 210deg, #c9d3ff, #f4c7e4, #ffdcb4, #fff1d6, #c6efe0, #b9c6f5, #e7c3ef, #c9d3ff)` + merkez göbek halkaları + `soft-light` parlama; arkasında `rgba(255,106,43,.14)` radyal ışık. Yazma sırasında kapak görseli basılı disk döner (2.6 s/tur). |
| **Diyalog** | 580 px, 22 radius, buzlu cam (`rgba(34,35,41,.92)` + `backdrop-filter: blur(30px)`); arkadaki pencere 6 px bulanık ve %55 karartılmış. |
| **İlerleme çubuğu** | 8 px, `accent-grad` + glow; üstünde büyük yüzde (28/600) ve sağda kalan süre. |
| **Seçim kartı (şablon)** | 70 px yükseklikte görsel + ad; seçili: `0 0 0 2px surface, 0 0 0 4px accent` halka. |

İkonlar: 1.6 px çizgi kalınlığında, yuvarlak uçlu, 19–20 px (Fluent System Icons veya eşdeğeri). Emoji yok.

---

## 3. Builder ekranları

Referanslar: `design/screens/01–04.html` ve `.png` (1440×900).

### 3.1 Başlangıç (`01-baslangic`)
- Solda kenar menü: **Yeni disk** → Film ya da dizi · Müzik · Fotoğraf · Oyun ya da program · Veri diski;
  **Araçlar** → Disk kopyala · ISO kalıbı yaz · Diski sil · Son projeler; altta Ayarlar.
- Sağda tek panel: ortada 280 px yanardöner disk, başlık seçili türe göre ("Film diski hazırla", "Müzik diski hazırla"…),
  tek cümle açıklama, **Klasör seç** (birincil) + Dosya ekle; altta sürücü kapsülü.
- Panelin tamamı bırakma hedefidir. Klasör bırakılınca tür otomatik tanınır ve menüdeki seçim kendiliğinden değişir.

### 3.2 Disk içeriği (`02-disk-icerigi`)
- Araç çubuğu: Geri · Ekle · Kaldır … Disk menüsü.
- Tablo: Ad (küçük resim + ad + tek satır bilgi) · Tür (hap) · Boyut. Ana öğe vurgulu satır.
- Eşleşme gerektiğinde (TMDB/IGDB) ana satırın altında satır içi bir kart açılır: "Bu film hangisi?" + en fazla 3 seçenek
  + "Bilgileri kendim yazacağım". Ayrı bir ekran açılmaz.
- Tablonun altında bırakma alanı; en altta doluluk cetveli + **Diske yaz**.
- Dönüştürme gereken dosyalar sessizce işaretlenir; satırda yalnızca küçük gri not: "her bilgisayarda açılacak şekilde hazırlanacak".

### 3.3 Disk menüsü (`03-disk-menusu`)
- Solda büyük önizleme (gerçek şablon render'ı, 16:9), arkasında afişin bulanık ışığı.
- Sağda 300 px panel: Şablon (görselli seçim kartları) · Disk takılınca (Menüyü göster / Hemen oynat) · Disk adı ·
  Disk simgesi (afişten, Değiştir) · Kumanda ile gezinme.
- Araç çubuğunda "HTML olarak düzenle" (kod editörünü açar; bkz. 3.6). Altta Tamam + **Diske yaz**.

### 3.4 Diske yazılıyor (`04-diske-yaziliyor`)
- Modal diyalog: dönen kapaklı disk, "Diske yazılıyor", "Proje · Disk türü · Sürücü", büyük yüzde, kalan süre,
  ilerleme çubuğu, "2,73 / 4,40 GB · 8x", onay kutusu "Bitince kontrol et ve diski çıkar", İptal.
- Aşamalar metinle değişir: "Hazırlanıyor…" → "Diske yazılıyor" → "Kontrol ediliyor" → "Hazır". Bitince diyalog
  yeşil onay + "Disk hazır" + **Bir kopya daha** / Kapat.

### 3.5 Durumlar (çizilecek, aynı dille)
| Durum | Davranış |
|---|---|
| Sürücüde disk yok | Kapsül gri; "Diske yaz" yerine "Disk tak" yazar, tıklayınca tepsiyi açar; yanında "ISO olarak kaydet". |
| Sığmıyor | Cetvel `warn`; öneri satırı: "DVD DL kullan" veya "Fragmanı çıkar (−86 MB)". |
| Eşleşme bulunamadı | Satır içi kart: "Bilgi bulunamadı" + elle giriş alanları (ad, yıl, afiş seç). |
| Yazma hatası | Diyalog kırmızı değil, sakin: "Yazma tamamlanamadı. Disk kullanılamaz hale gelmiş olabilir." + Tekrar dene / Ayrıntılar. |
| İlk açılış | Başlangıç ekranı aynı; ek karşılama sihirbazı yok. |

### 3.6 Kod editörü (çizilmedi, kurallar)
"HTML olarak düzenle" aynı pencerede 3.3'ün yerini alır: solda CodeMirror (koyu, Geist Mono), sağda canlı önizleme,
altta veri paneli (manifest ağacı) ve uyarılar. Aynı token'lar; birincil buton "Kaydet".

---

## 4. Disk menüsü şablonları

Referanslar: `design/screens/t1–t4.html` ve `.png` (1440×810, 16:9). Tüm şablonlar 1280×720 – 3840×2160 arası
ölçeklenir ve TV mesafesinden okunur.

### 4.1 Ortak kurallar
- Yazı tipi **Outfit** (200–700). Başlık 92/600 (−0.035em), bilgi satırı 15, özet 19/300.
- **Gerçek görsel tam ekran**; üzerinde iki degrade: soldan `rgba(bg,.82)→0` (%0–70), alttan `rgba(bg,.85)→0` (%0–45).
- Metin bloğu sol altta (80 px kenar boşluğu), en fazla 580 px genişlik.
- **En fazla iki buton:** birincil (krem `#f6f2ec` zemin, koyu metin; tema ayarıyla kehribar/buz mavisi olabilir) +
  ikincil (`rgba(255,255,255,.16)` + 12 px blur). Yükseklik 54, tam yuvarlak.
- Teknik bilgi yok (codec, dosya adı, disk etiketi). Künye "Hakkında" arkasında.
- Ayrıntılar (bölümler, oyuncular, gereksinimler) ikincil butonla açılan **alt panelde**; ana ekran sade kalır.
- Gezinme: ← → / Tab, Enter, Esc; gamepad D-pad/A/B. Odak halkası 3 px krem.

### 4.2 Film — Sinema (`t1-film`)
Tam ekran backdrop · üst solda stüdyo/disk adı (14, %70) · sol altta: yıl · tür · süre / başlık / tek cümle özet /
**İzle** + Bölümler. Yarım kalmışsa birincil "Kaldığın yerden devam et". Bölümler paneli: 16:9 küçük resimler + zaman.
Dizi: aynı ekran, birincil "İzle: 1. Sezon 3. Bölüm", ikincil "Bölümler" (sezon sekmeli liste).

### 4.3 Müzik — Albüm (`t2-muzik`)
Zemin: kapağın 70 px bulanık hali (%55) + koyu örtü. Solda 500 px kare kapak (6 radius, derin gölge), sağda albüm adı
(56/600), sanatçı · yıl, **Çal** + Karışık çal, parça listesi (no · ad · süre, satır arası ince çizgi). Çalan parça
satırı hafif aydınlatılır; çalar çubuğu yalnızca çalarken altta belirir.

### 4.4 Fotoğraf — Galeri (`t3-fotograf`)
Açık zemin `#f7f6f2`, koyu metin. Üstte başlık (48/500) + tarih aralığı · adet, sağda **Slayt gösterisi** (siyah).
Altta kenardan kenara mozaik (4 sütun, 8 px aralık, 4 radius; yatay kareler 2 sütun, dikeyler 2 satır kaplar).
Tıklayınca tam ekran görüntüleyici; slayt gösterisinde Ken Burns + isteğe bağlı müzik.

### 4.5 Oyun (`t4-oyun`)
Tam ekran sanat görseli · sol altta: tür · yıl · sürüm / oyun adı (logo varsa şeffaf logo görseli) / tek cümle /
yeşil uyumluluk satırı "✓ Bu bilgisayarda çalışır · 4 GB yer gerekir" (sağlanmıyorsa sarı "! 8 GB bellek önerilir") /
**Kur** + Kurmadan oyna. Sağ altta 3 ekran görüntüsü (168×95). Kurulumdan sonra birincil buton "Oyna" olur.
Çok kurulumlu disk: aynı görsel zemin üzerinde onay kutulu sade liste + "Seçilenleri kur".

### 4.6 Karışık disk
Film şablonunun düzeni; sol altta öne çıkan öğe, altında türlere göre tek sıra küçük kapak (film 2:3, albüm kare,
fotoğraf kare, oyun 16:9). Seçim değişince arka plan yumuşak geçişle değişir.

---

## 5. Görsel varlıklar

- Referans görseller (`design/img/`) Unsplash/Picsum'dan alınmış **örnek** fotoğraflardır; üründe kullanıcının kendi
  afiş/kapak/fotoğrafları ve TMDB / IGDB / Steam / SteamGridDB görselleri gelir.
- Uygulama ikonu: koyu göbekli disk + turuncu konik degrade (`#ff8a3d → #ff5a1f → #ffb38a`). 16–256 px ICO.
- Yazı tipleri: Geist (builder), Outfit (şablonlar) — ikisi de OFL, dosya olarak gömülür; çalışma anında internetten yüklenmez.

---

## 6. Teslim kontrol listesi (tasarım uyumu)

- [ ] Builder ekranları `design/screens/01–04.png` ile yan yana konduğunda düzen, renk ve tipografi eşleşiyor.
- [ ] Her ekranda yalnızca bir turuncu birincil buton var.
- [ ] Arayüzde hiçbir teknik terim yok (Ayarlar › Gelişmiş hariç).
- [ ] Doluluk cetveli CD / DVD / DVD DL işaretlerini gösteriyor; sığmama durumu öneriyle birlikte çiziliyor.
- [ ] Şablonlar `t1–t4.png` ile eşleşiyor; ana ekranda ≤ 2 buton; gerçek görsel tam ekran.
- [ ] Kontrast, klavye ve azaltılmış hareket kontrolleri geçiyor.
