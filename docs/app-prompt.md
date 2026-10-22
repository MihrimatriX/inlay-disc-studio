# Inlay Disc Studio — Uygulama Geliştirme Promptu

> Bu dosya, projeyi uçtan uca geliştirecek bir AI kodlama ajanına (Claude Code vb.) verilmek üzere yazıldı.
> Ajan bu dosyayı **tek doğruluk kaynağı** olarak kabul eder. Bu klasördeki `schemas/`, `samples/`, `sdk/`
> ve `prototype/` dosyaları sözleşmenin parçasıdır; onları okumadan kod yazma.
>
> **Görsel tasarımın kaynağı `design-prompt.md` ve `design/screens/` klasörüdür.** Builder arayüzü ve standart disk
> menüsü şablonları bu referanslara birebir uymalıdır. Bu dosya *ne yapılacağını ve nasıl çalışacağını*,
> `design-prompt.md` *nasıl görüneceğini* tanımlar; çelişkide görünüm için `design-prompt.md`, davranış için bu dosya geçerlidir.

---

## 0. Rolün ve çalışma kuralların

Sen kıdemli bir Windows masaüstü ve .NET mühendisisin; aynı zamanda React/TypeScript ve medya işleme
(FFmpeg, LibVLC, kodekler, optik disk dosya sistemleri) konusunda deneyimlisin.

Çalışırken:

1. **Kilometre taşı sırasıyla ilerle** (bölüm 13). Bir taşın kabul kriterleri sağlanmadan sonrakine geçme.
2. Her taşın sonunda: derleme temiz, testler yeşil, kısa bir `CHANGELOG.md` girdisi, tek bir commit.
3. **Sözleşmeyi bozma.** `manifest.schema.json` veya `theme.schema.json` değişirse: şemayı güncelle,
   C# modellerini ve `sdk/manifest.ts` tiplerini yeniden üret, örnekleri doğrula, şema `version`/`$id`
   artışını değerlendir ve `docs/contract-changes.md` dosyasına yaz.
4. Yer tutucu (`TODO: implement`), sahte veri veya "şimdilik" kod bırakma. Bir şeyi yapamıyorsan açıkça
   raporla ve neyin eksik kaldığını yaz.
5. Belirsiz, geri alınması pahalı bir kararda (ör. bir kütüphane lisansı, dosya sistemi formatı) dur ve sor.
   Küçük kararlarda makul varsayımı yap, gerekçesini `docs/decisions/NNNN-*.md` (ADR) olarak yaz.
6. Kullanıcıya görünen tüm metinler **Türkçe** (tr-TR varsayılan) ve kaynak dosyalarından gelir; en-US
   ikinci dil. Kod, tanımlayıcılar, commit mesajları ve yorumlar İngilizce.
7. Güncel API bilgisine ihtiyaç duyduğunda (WebView2, LibVLCSharp, IMAPI2, Fluid, .NET 10) dokümantasyonu
   kontrol et; ezberden sürüm numarası uydurma. Paket sürümlerini sabitle (`Directory.Packages.props`).

---

## 1. Ürün özeti

**Adlandırma:** Ürün adı *Inlay Disc Studio*; uygulama içinde (başlık çubuğu, menüler) kısa ad *Inlay*. Kod ad alanları `Inlay.*`,
exe `Inlay.exe`, kurulum/Store/web adı "Inlay Disc Studio", alt başlık: "Diskini hazırla, menüsünü tasarla, yak."

**Inlay Disc Studio** (kısa adı **Inlay**), Windows için modern bir **optik disk hazırlama (authoring) ve yakma** uygulamasıdır.

Kullanıcı bir klasörü sürükleyip bırakır. Uygulama:

1. İçeriği analiz eder ve türünü tanır: **film, müzik albümü, fotoğraf albümü, oyun/program (exe/msi), belge** veya bunların karışımı.
2. Meta veriyi toplar (film için TMDB, oyun için IGDB / Steam / SteamGridDB, ses etiketleri, EXIF, exe sürüm bilgisi, kodek bilgisi).
3. Medyayı diskte **her makinede oynayacak** şekilde normalize eder (bölüm 6).
4. Kapak/öne çıkan görselden **renk paleti** çıkarır, seçilen şablonu bu paletle temalandırır ve diskte
   açılacak **HTML menüyü** üretir.
5. Diskin Explorer ikonunu, etiketini ve `autorun.inf` dosyasını ayarlar.
6. **IMAPI2** ile ISO üretir veya doğrudan diske yakar, ardından **doğrular**.

Diskte küçük bir **launcher** (`Baslat.exe`, .NET 10 NativeAOT + WebView2) bulunur. Menüyü açar ve sayfanın
istediği eylemleri (izle, çal, kur, diskten oyna, klasörü aç) güvenli biçimde yürütür.
WebView2'nin oynatamadığı medya için diskte **LibVLC** yedek oynatma motoru bulunur.

Standart şablonlar hazır gelir; isteyen kullanıcı **küçük bir editörde** kendi HTML/CSS/Liquid şablonunu yazar.

### Hedefler
- Sadece **Windows 10 (1809+) ve Windows 11**, x64 (ARM64 ikinci aşama).
- Sadece **optik disk**: CD-R 700 MB, DVD±R 4,7 GB (DVD-5), DVD±R DL 8,5 GB (DVD-9). ISO dışa aktarımı test ve arşiv için.
- Görsel kalite: builder "modern, sade, koyu bir disk yakma aracı"; disk menüsü "doğal, sinematik, içerik odaklı".
  Referans: `design-prompt.md` + `design/screens/*.png`. (`prototype/index.html` yalnızca açılış görünümü
  davranışları ve karışık disk ana menüsü için referanstır; görsel dili eskidir.)

### Hedef dışı (bu sürümde yapma)
- macOS/Linux, USB yazma, Blu-ray, DVD-Video (VIDEO_TS) authoring, disk kopyalama/rip, çoklu oturum ekleme
  (CD-Extra hariç, bkz. M8), bulut senkronizasyonu.

---

## 2. Sabit teknik kararlar

| Alan | Karar | Not |
|---|---|---|
| Dil/çatı | **.NET 10**, C# 14 | Tüm native olmayan kod .NET'te kalır |
| Builder kabuğu | **WPF** + `Microsoft.Web.WebView2` | Arayüz React; WPF sadece pencere, sürükle-bırak, COM köprüsü |
| Builder arayüzü | **React 19 + TypeScript + Vite**, CSS Modules veya vanilla-extract | Tailwind kullanma; tasarım token'ları `ui/tokens.css` |
| Launcher | **.NET 10 NativeAOT**, WinForms'suz, **WebView2Aot** bağlamaları | Tek exe, hedef ≤ 10 MB |
| Şablon motoru | **Fluid** (Liquid, .NET) | Kullanıcı şablonları için sandbox'lı |
| Şema doğrulama | **JsonSchema.Net** (C#), `ajv` (TS testleri) | Draft 2020-12 |
| JSON | `System.Text.Json` + source generator | AOT uyumlu olmalı (launcher) |
| Medya analizi | **ffprobe** (FFmpeg LGPL derlemesi, builder ile birlikte gelir) | JSON çıktısı parse edilir |
| Medya dönüştürme | **FFmpeg** CLI (builder tarafı) | Diske FFmpeg konmaz |
| Yedek oynatıcı | **LibVLCSharp 3.x + LibVLC 3.0.x** (stabil) | LibVLC 4 önizleme sürümlerini kullanma |
| Görsel işleme | **Magick.NET** (HEIC/RAW/TIFF/PSD → AVIF/WebP, ICO) | |
| EXIF | **MetadataExtractor** | |
| Ses etiketleri | **TagLibSharp** | |
| Film meta verisi | **TMDB API v3** + `.nfo` (Kodi) okuma | TMDB atıf kuralına uy |
| Oyun meta verisi | **IGDB API v4** (Twitch uygulama token'ı) → **Steam Store `appdetails`** → **SteamGridDB API v2** → exe bilgisi | Bkz. 5.3; anahtarlar DPAPI ile saklanır |
| Builder yazı tipi | **Geist** (OFL, gömülü) | Şablonlarda **Outfit** (OFL, gömülü) |
| Disk yazma | **IMAPI2** (`IFileSystemImage`, `IDiscFormat2Data`, `IDiscFormat2TrackAtOnce`) | Builder AOT değil, klasik COM interop kullan |
| Editör | **CodeMirror 6** | Monaco kullanma (boyut) |
| Test | xUnit + FluentAssertions, Vitest, Playwright (WebView2'ye CDP ile) | |

---

## 3. Depo yapısı

```
inlay/
├─ Directory.Build.props / Directory.Packages.props
├─ schemas/                      ← SÖZLEŞME (bu klasördeki dosyalar)
│  ├─ manifest.schema.json
│  └─ theme.schema.json
├─ samples/                      ← örnek manifest/theme (testlerde fixture olarak kullan)
├─ sdk/                          ← disc-sdk.d.ts + manifest.ts (şemadan üretilir)
├─ src/
│  ├─ Inlay.Core/           ← C# modelleri (şemadan üretilir), RelPath, Id, validation
│  ├─ Inlay.Analysis/       ← tarayıcı, sınıflandırıcılar, meta veri sağlayıcıları
│  ├─ Inlay.Media/          ← ffprobe/FFmpeg sarmalayıcı, normalizasyon planlayıcı, görsel dönüştürme
│  ├─ Inlay.Theming/        ← palet çıkarma, .disctheme yükleyici, Fluid render, lint
│  ├─ Inlay.Imaging/        ← çok boyutlu .ico üretimi
│  ├─ Inlay.Burn/           ← IMAPI2 imaj, yakma, ISO export, doğrulama
│  ├─ Inlay.Builder/        ← WPF + WebView2 host, RPC köprüsü, iş kuyruğu
│  ├─ Inlay.Launcher/       ← NativeAOT exe (diske giden)
│  └─ Inlay.Launcher.Player/← LibVLC oynatıcı penceresi (launcher içinde, ayrı derleme birimi)
├─ ui/
│  ├─ builder/                   ← React builder arayüzü (Vite)
│  ├─ disc-sdk/                  ← window.disc uygulaması (launcher'a gömülen JS)
│  └─ editor/                    ← CodeMirror tabanlı şablon editörü (builder içinde lazy-load)
├─ themes/
│  ├─ standard-mixed/            ← Karışık disk (design-prompt.md §4.6)
│  ├─ standard-movie/            ← "Sinema" (design/screens/t1-film)
│  ├─ standard-album/            ← "Albüm" (t2-muzik)
│  ├─ standard-gallery/          ← "Galeri" (t3-fotograf)
│  └─ standard-app/              ← "Oyun" (t4-oyun) + program paketi görünümü
├─ design/                       ← tasarım referansları (screens/*.html|png, img/)
├─ tests/
└─ docs/ (decisions/, contract-changes.md, user-guide.md)
```

---

## 4. Veri sözleşmesi

### 4.1 Manifest (`schemas/manifest.schema.json`)
- Builder üretir, diske `.disc/manifest.json` olarak yazılır. Launcher ve şablonlar **yalnızca** bunu okur.
- `items[]` beş türden oluşur ve `type` alanıyla ayrılır: `movie`, `album`, `gallery`, `app`, `file`.
- `sections[]` menüdeki rafların sırasını ve görünürlüğünü belirler.
- `actions[]` **izin listesidir**: launcher, burada tanımlı olmayan hiçbir şeyi çalıştırmaz.
- Tüm yollar `relPath`: disk köküne göre göreli, `/` ayraçlı. Mutlak yol, `..` ve URL yasak.
- Her `mediaFile` bir `playback.engine` taşır: `html` | `libvlc` | `shell`. Bu kararı **builder verir**
  (bölüm 6); launcher sadece uygular.

### 4.2 Tema paketi (`schemas/theme.schema.json`)
- `.disctheme` = zip. Kökünde `theme.json`, giriş dosyası (`index.liquid`), partial'lar, `assets/`, `fonts/`.
- `settings` tanımları builder'da **otomatik ayar formunu** üretir. Şablonda `{{ settings.x }}`,
  CSS'te `var(--ds-x)` olarak erişilir.
- Renk varsayılanı `auto` veya `auto:<palet-anahtarı>` olabilir; değer paletten gelir.
- Fontlar pakete gömülür. Şablon internetten hiçbir şey yükleyemez (CSP ile zorlanır, bölüm 10).

### 4.3 Model üretimi
- C# modelleri: şemadan üret (ör. `NJsonSchema` CodeGen veya elle yazılmış, şemaya karşı test edilen
  `record` tipleri). `System.Text.Json` source-gen context'i launcher'da AOT için zorunlu.
- TS tipleri: `npx json-schema-to-typescript` ile `sdk/manifest.ts` (mevcut dosya bu yolla üretildi).
- CI'da: `samples/*.json` şemaya karşı doğrulanır; C# ile serialize → deserialize gidiş-dönüşü test edilir.

---

## 5. Builder: analiz hattı

### 5.1 Tarama
- Bırakılan klasörü (veya birden fazla klasörü) özyinelemeli tara. Gizli/sistem dosyalarını,
  `Thumbs.db`, `desktop.ini`, `.DS_Store` dosyalarını atla.
- Uzun yolları destekle (`\\?\` öneki). Dosya adlarını Joliet/UDF kurallarına göre denetle (bölüm 8.3).

### 5.2 Mod tespiti (sınıflandırıcılar)
Her sınıflandırıcı bir **güven skoru** döndürür; en yüksek skorlu tür(ler) seçilir. Birden fazla tür
eşik üstündeyse mod `mixed` olur ve her tür bir `section` olur.

| Tür | Sinyal |
|---|---|
| movie | ≥ 1 video dosyası > 15 dk; `.nfo`; dosya adında yıl kalıbı (`Title.2010.1080p`) |
| album | ≥ 3 ses dosyası aynı albüm etiketiyle; `cover.jpg`/`folder.jpg`; `.cue` |
| gallery | ≥ 5 görsel; EXIF tarihleri; kısa video klipler (< 2 dk) fotoğraflarla karışık |
| app | `setup.exe`, `install.exe`, `*.msi`, `autorun.inf`; ya da PE dosyası + DLL'ler (portable) |
| file | Kalan belgeler (`.pdf`, `.txt`, `.md`, `.docx`) |

Kullanıcı önerilen sınıflandırmayı arayüzde düzeltebilir; düzeltme kaydedilir.

### 5.3 Meta veri sağlayıcıları
- **Film:** dosya adı ayrıştırıcı → TMDB arama (başlık + yıl) → kullanıcıya "Bu film hangisi?" satır içi
  kartında ilk 3 eşleşmeyi göster → seçilen kayıttan poster, backdrop, logo, özet, türler, oyuncular (ilk 10, fotoğraflı),
  yönetmen, sertifika. Tüm görseller **indirilip diske gömülür** (WebP/AVIF, poster ≤ 780 px genişlik,
  backdrop ≤ 1920 px). `.nfo` varsa önceliklidir. TMDB API anahtarı kullanıcı ayarlarında, DPAPI ile
  şifreli saklanır. Ağ yoksa manuel giriş formu.
- **Albüm:** TagLibSharp ile parça, disk no, sanatçı, yıl, tür, gömülü kapak. `.lrc` dosyalarını parçayla eşle.
- **Galeri:** EXIF tarih/kamera/GPS. Konum adı için ağ çağrısı yapma (gizlilik); sadece koordinatı sakla,
  kullanıcı isterse adı elle yazar. Galeriyi tarihe göre sırala, kullanıcı yeniden sıralayabilir.
- **App:** `FileVersionInfo` (ürün adı, sürüm, yayıncı), ikon çıkarma (`SHGetFileInfo`/`PrivateExtractIcons`
  → 256 px PNG), Authenticode imza kontrolü (`WinVerifyTrust`), MSI ise `ProductName`/`ProductVersion`
  (Windows Installer API). Ön gereksinimleri tanı: `vc_redist*.exe`, `dxsetup.exe`, `ndp*.exe`.
- **Oyun** (`app.category = game`): sınıflandırıcı oyun sinyallerini arar (`UnityPlayer.dll`, `*_Data/`, `Engine/Binaries`
  (Unreal), `steam_api*.dll`, GOG/Inno kurulum adları, klasör adında "Game"/"Oyun"). Sonra sağlayıcı zinciri:
  1. **Ad çıkarma:** klasör adı + `FileVersionInfo.ProductName` + kurulum adından temizlenmiş başlık
     (`Setup_`, sürüm, `GOG`, `-FitGirl` vb. gürültü atılır) ve varsa yıl.
  2. **IGDB (öncelikli):** `POST https://api.igdb.com/v4/games` + Apicalypse sorgusu
     (`search "<ad>"; fields name,first_release_date,summary,genres.name,cover.image_id,artworks.image_id,
     screenshots.image_id,involved_companies.company.name,involved_companies.developer,platforms,external_games.uid,external_games.category; where platforms = (6);`).
     Kimlik doğrulama: kullanıcının Twitch **Client ID + Client Secret**'ı ile `POST https://id.twitch.tv/oauth2/token?grant_type=client_credentials`;
     token süresine göre önbellekle, 4 istek/sn sınırına uy (token bucket). Görseller `images.igdb.com/igdb/image/upload/t_<boyut>/<image_id>.jpg`
     (`t_cover_big`, `t_1080p`, `t_screenshot_big`).
  3. Kullanıcıya **"Bu oyun hangisi?"** satır içi kartında ilk **3** eşleşme (kapak + ad + yıl + geliştirici) ve "Bilgileri kendim yazacağım".
  4. **Steam Store** (anahtarsız): IGDB `external_games` içinden Steam appid (category 1) bulunursa veya Steam araması
     (`store.steampowered.com/api/storesearch`) eşleşirse `store.steampowered.com/api/appdetails?appids=<id>&l=turkish` →
     `pc_requirements.minimum/recommended` (HTML → yapılandırılmış RAM/GPU/disk/OS alanlarına ayrıştır), Türkçe kısa açıklama,
     `header_image`, ekran görüntüleri. **Uyumluluk satırı yalnızca bu yapılandırılmış gereksinimlerden üretilir.**
     Steam'de yoksa gereksinimler gösterilmez (tahmin yok); disk alanı her zaman kurulum boyutundan hesaplanır.
  5. **SteamGridDB** (opsiyonel, kullanıcı API anahtarı): şeffaf **logo** (`/logos/game/<id>`) ve **hero** (`/heroes/game/<id>`);
     şablon logo varsa başlık yerine logoyu kullanır.
  6. **Yedek:** hiçbir kaynak yoksa exe ikonu (256 px), `FileVersionInfo` adı/sürüm/yayıncı ve ikondan çıkan palet; ekran görüntüsü
     bölümü gösterilmez.
  - Görsel önceliği: hero/artwork (SteamGridDB hero > IGDB artwork > Steam `library_hero`/`header_image` > ilk ekran görüntüsü).
  - Hepsi **indirilip diske gömülür** (WebP/AVIF, hero ≤ 1920 px); disk menüsü çalışırken internete çıkmaz.
  - Yanıtlar `%LOCALAPPDATA%\Inlay\cache\meta\` altında 30 gün önbelleklenir. Ağ yoksa manuel giriş.
  - **Atıf ve telif:** künyede "Oyun bilgileri: IGDB.com" ve kullanılan diğer kaynaklar; görseller yalnızca kullanıcının kendi
    diski için kullanılır, builder bunları başka yere dağıtmaz. Steam Store API resmî olarak belgelenmemiştir; hata/oran sınırında
    (HTTP 429) sessizce atlanır.

### 5.4 Palet çıkarma
- Kaynak: `disc.featured` öğesinin backdrop/kapak görseli; her öğe için ayrıca kendi kapağından `palette`.
- Algoritma: görseli 128 px'e küçült → OKLab uzayında k-means (k=6) → popülasyon ve doygunluğa göre
  `bg` (koyu, düşük doygunluk), `surface`, `accent` (en doygun, yeterince parlak), `text`, `muted`.
- **Kontrast güvencesi:** `text`/`bg` ≥ 7:1, `accent`/`bg` ≥ 3:1, `accentText`/`accent` ≥ 4.5:1 (WCAG).
  Sağlanmazsa OKLab L ekseninde kaydırarak düzelt. Birim testleriyle kanıtla.

---

## 6. Medya normalizasyonu ("her kodek çalışsın" stratejisi)

Üç katman:

1. **Yakarken normalize et** (varsayılan): WebView2'nin oynatabileceği biçime kayıpsız (remux) veya kalite
   ayarlı (transcode) dönüştür. Sonuç `engine: "html"` olur.
2. **LibVLC yedeği:** Dönüştürülemeyen ya da kullanıcının "orijinali koru" dediği dosyalar `engine: "libvlc"`
   olur ve diske LibVLC eklenir.
3. **Shell:** Son çare olarak sistemin varsayılan uygulaması açılır (`engine: "shell"`).

### 6.1 Karar tablosu (NormalizationPlanner — saf fonksiyon, tablo testli olmalı)

> **Varsayılan: medya dosyaları dönüştürülmez.** Ses ve video dosyaları diske olduğu gibi konur. WebView2'nin
> oynatabildiği dosyalar `html`, diğerleri `libvlc` motoruyla açılır. Aşağıdaki tablodaki remux, AAC izi, FLAC ve
> transcode adımları yalnızca Ayarlar › Gelişmiş › **Maksimum uyumluluk** açıkken uygulanır. Görseller ve
> altyazılar bu kuralın dışındadır.

| Girdi | Varsayılan karar | Sonuç motoru |
|---|---|---|
| MP4/WebM içinde H.264, VP9, AV1 + AAC/Opus/MP3/FLAC | Dokunma (`+faststart` yoksa remux) | html |
| MKV içinde H.264/AV1/VP9 | **Remux** → MP4 (kayıpsız) | html |
| HEVC/H.265 (herhangi bir kap) | Orijinali koru | libvlc (opsiyon: "Maksimum uyumluluk" → H.264/AV1 transcode → html) |
| MPEG-2, VC-1, MPEG-4 ASP (DivX/Xvid), WMV, RealVideo | Orijinali koru | libvlc (opsiyon: transcode) |
| Ses: AC3, E-AC3, DTS, TrueHD, PCM (video içinde) | Orijinali koru + ek **AAC stereo** (veya Opus) parça ekle | html (ek parçayla), libvlc (orijinal parçayla) |
| Altyazı: SRT, ASS/SSA, mov_text | **WebVTT**'ye dönüştür (ASS stil kaybını kullanıcıya bildir), orijinali libvlc için koru | html |
| Altyazı: PGS, VobSub (görüntü tabanlı) | Orijinali koru | libvlc |
| Müzik: MP3, AAC/M4A, FLAC, Opus, Vorbis, WAV | Dokunma | html |
| Müzik: ALAC, APE, WavPack, TTA | **FLAC'a kayıpsız dönüştür** | html |
| Müzik: WMA, DSD (DSF/DFF), MPC | Orijinali koru | libvlc (opsiyon: FLAC/Opus) |
| Görsel: JPEG, PNG, WebP, AVIF, GIF, BMP, SVG | Görüntüleme sürümü olarak WebP/AVIF ≤ 2560 px üret; orijinal ≤ 2560 px ise aynen kullan | html |
| Görsel: HEIC/HEIF, TIFF, RAW (CR2/CR3/NEF/ARW/DNG/RAF), PSD | **AVIF**'e dönüştür; orijinali "orijinalleri ekle" seçeneğiyle `original/` altına koy | html |
| Tüm görseller | 400 px WebP küçük resim + **BlurHash** üret | — |

- HDR kaynak + "maksimum uyumluluk" transcode'unda tone-mapping uygula (`zscale` + `tonemap=hable`).
- Normalize edilmiş dosyalar diskte hedef konuma; orijinaller seçenekliyse `original/` alt klasörüne.
- Her karar `playback.normalized` ve `playback.original` alanlarına yazılır.
- Disk kapasitesi yetmezse planlayıcı öneri üretir: "orijinalleri dışarıda bırak", "HEVC'yi koru, H.264
  kopyayı atla", "DVD-9'a geç". Kullanıcı seçer.

### 6.2 İş kuyruğu
- FFmpeg süreçlerini paralel değil, CPU çekirdeği sayısına göre sınırlı çalıştır. İlerlemeyi
  `-progress pipe:1` ile oku. İptal edilebilir olmalı (`CancellationToken` → süreci öldür, geçici dosyaları sil).
- Ara dosyalar `%LOCALAPPDATA%\Inlay\work\<projectId>\`. Proje kapatılınca temizle.
- Donanım kodlayıcıları varsa (NVENC/QSV/AMF) kullanıcıya seçenek sun; varsayılan yazılım (`libx264 -crf 20`
  / `libsvtav1`), çünkü kalite daha öngörülebilir.

---

## 7. Şablon sistemi ve editör

### 7.0 Açılış görünümü (içeriğe göre)

Builder, analizden sonra `disc.home` alanını aşağıdaki tabloya göre doldurur. Kullanıcı builder'da değiştirebilir.
Kural: **kullanıcıyı içeriğe en kısa yoldan ulaştır**; tek bir şey varsa ara menü gösterme.

| Disk içeriği | `disc.home` | Açılış görünümü |
|---|---|---|
| Birden fazla türde içerik | `{view:"menu"}` | Ana menü (sahne + türlere göre gruplu sıra) |
| **1 film** (+ isteğe bağlı fragman/ekstralar) | `{view:"item", item}` | Film sayfası, geri düğmesi yerine disk adı |
| **2+ film**, bölüm kalıbı yok | `{view:"collection", kind:"movie"}` | Film koleksiyonu: afiş ızgarası, "Devam et" (yarım kalan film varsa); kart → film sayfası ("Tüm filmler" geri düğmesi) |
| Bölüm kalıbı (`S01E01`, `1x01`, `Sezon 1/…`, `Bölüm 3`) | `{view:"series", item}` | Dizi sayfası: backdrop, "İzle / Devam et: S1 B3", sezon sekmeleri, bölüm listesi (16:9 küçük resim, izleme çubuğu, ✓ İzlendi) |
| **1 albüm** | `{view:"item", item}` | Albüm sayfası |
| **2+ albüm** | `{view:"collection", kind:"album"}` | Müzik koleksiyonu: kare kapak ızgarası (üzerine gelince plak çıkar), "Tümünü karışık çal"; kart → albüm sayfası |
| Etiketsiz/karışık sanatçılı ses dosyaları | `{view:"item", item}` | Albüm sayfası "Çalma listesi" olarak (kapak: ilk 4 parçanın kapak kolajı; parça satırında sanatçı adı) |
| **1 oyun/program** | `{view:"item", item}` | Oyun/program sayfası (uyumluluk kartı, Kur, varsa Diskten oyna) |
| **2+ kurulum** (ve/veya ön gereksinimler) | `{view:"bundle"}` | Program paketi: onay kutulu liste, zorunlular kilitli ve önce kurulur, alt çubukta "N program seçildi · toplam boyut" + **Seçilenleri kur**; satır satır ilerleme ve ✓ Kuruldu |
| Yalnızca fotoğraf | `{view:"item", item}` | Fotoğraf sayfası |
| Yalnızca belge(ler) | `{view:"item"}` veya `menu` | Tek belge sayfası; çok belgede liste |

**Sadece exe özel durumları:**
- Meta veri yoksa (yalnızca `setup.exe`): sayfa exe ikonundan (256 px), `FileVersionInfo` ürün adı/sürüm/yayıncıdan ve
  ikondan çıkarılan paletten üretilir. Ekran görüntüsü yoksa o bölüm hiç gösterilmez (boş kutu yok).
- Kurulum yok, yalnızca çalıştırılabilir oyun (portable): birincil eylem **Oyna**; "Kur" gösterilmez.
- Hem kurulum hem portable: birincil **Kur**, ikincil **Kurmadan diskten oyna**.
- Ön gereksinimler (`vc_redist`, `dxsetup`, `.NET` çalışma zamanı) tek oyun diskinde ayrı öğe olarak gösterilmez;
  "Kur" akışı onları sessizce önce kurar (bir kez UAC). Program paketi görünümünde "Gerekli" etiketiyle kilitli satır olur.
- Paket kurulumu: launcher kendini `--bundle-worker` ile **bir kez** yönetici olarak başlatır, kurulumları `bundle.order`
  sırasıyla `silentArgs` ile yürütür, `bundle-progress` olaylarını sayfaya iletir. Çıkış kodu 3010 → "Yeniden başlatma gerekli"
  bildirimi; 0/3010 dışı → satırda "Kurulamadı · Tekrar dene".
- Kurulum sonrası birincil eylem **Oyna/Başlat** olur (kurulu exe yolu MSI/kayıt defterinden bulunur).

**Sadece film özel durumları:** yarım kalan izleme konumu `%LOCALAPPDATA%` altında saklanır; film sayfasında
"Kaldığın yerden devam et" + "Baştan izle", koleksiyonda başlıkta "Devam et: <film>" ve kartın altında ilerleme çubuğu.

**Sadece müzik özel durumları:** CD-Extra (M8) yakılmışsa sayfada "Bu disk müzik çalarlarda da çalar" notu.
Müzik çubuğu albüm sayfasından çıkınca da çalmaya devam eder.

### 7.1 Render hattı
1. Manifest + seçili `.disctheme` + kullanıcı ayarları → **Fluid** ile `index.html` render edilir
   (`engine: "liquid"`). `engine: "static"` ise dosyalar aynen kopyalanır.
2. Fluid'e verilen model: `disc`, `theme` (palet + ayarlar), `sections`, `items` (id'ye göre sözlük ve dizi),
   `actions`. Ek filtreler: `duration` (sn → "1 sa 13 dk"), `filesize`, `relurl` (relPath → `https://disc.local/...`),
   `t` (yerelleştirme), `json` (güvenli JSON gömme).
3. Fluid `TemplateOptions`: üye erişimi sadece model tiplerine açık, döngü/özyineleme sınırları, zaman aşımı.
4. Render sonrası CSS değişkenleri `:root{--ds-accent:…}` olarak enjekte edilir.
5. **Lint** (yakmadan önce zorunlu, hata varsa yakma engellenir):
   - Var olmayan action id'si çağrısı (`disc.run('x')` ve `data-action="x"` taraması)
   - Eksik asset, dış URL (`http(s)://` disc.local dışında), `<script src>` dış kaynak
   - Toplam asset boyutu > `budget.maxAssetsMb`
   - `capabilities` dışında SDK kullanımı (uyarı)
   - Kontrast hatası (palet + ayar kombinasyonu, uyarı)

### 7.2 Standart şablonlar
Görsel dil, ölçüler ve metinler **`design-prompt.md` §4** ve `design/screens/t1–t4` referanslarından alınır.
Menü bir izleyici/oyuncu içindir, geliştirici için değil. Bağlayıcı ilkeler:

1. **Gerçek görsel tam ekran, arayüz neredeyse görünmez.** Başlık bloğu sol altta; iki degrade karartma ile okunur.
2. **En fazla iki buton:** birincil (İzle / Devam et / Çal / Slayt gösterisi / Kur / Oyna) + ikincil
   (Bölümler / Karışık çal / Kurmadan oyna). Ayrıntılar ikincil eylemle açılan alt panelde.
3. **Tek türlü disklerde ana menü atlanır** (7.0); doğrudan türün sayfası açılır.
4. **Teknik bilgi yok:** kodek, motor (html/libvlc), dosya yolu, disk etiketi, kapasite görünmez. Lisans/künye "Hakkında" arkasında.
5. **Doğal dil geri bildirimi:** "Kurulum başlatılıyor…"; kod/çağrı adı asla görünmez.
6. Gezinme: ← → / Tab, Enter, Esc/Backspace; gamepad D-pad/A/B. Sayfa geçişleri View Transitions; `#<itemId>` ile doğrudan açılır.

Şablonlar:
- `standard-movie` **"Sinema"** (`t1-film`): tam ekran backdrop; yıl·tür·süre, başlık (logo varsa logo), tek cümle özet;
  **İzle** + Bölümler. Yarım kalmışsa birincil "Kaldığın yerden devam et". Bölümler paneli 16:9 küçük resim + zaman; ses/altyazı
  seçimi oynatıcının içinde. Dizi (`home.view = series`): birincil "İzle: 1. Sezon 3. Bölüm", ikincil "Bölümler" (sezon sekmeli).
  Koleksiyon (`collection/movie`): aynı sahne + altta tek sıra 2:3 afiş; seçim değişince backdrop yumuşak geçer.
  İkinci seçenek **"Sade"**: aynı düzen, karartma yerine düz koyu zemin + solda afiş.
- `standard-album` **"Albüm"** (`t2-muzik`): bulanık kapak zemini, solda büyük kare kapak, sağda albüm/sanatçı·yıl,
  **Çal** + Karışık çal, parça listesi. Çalar çubuğu yalnızca çalarken altta; sayfadan çıkınca da çalmaya devam eder.
  `.lrc` varsa parçada "Sözler". Koleksiyon: kare kapak sırası. CD-Extra'da "Bu disk müzik çalarlarda da çalar" notu.
- `standard-gallery` **"Galeri"** (`t3-fotograf`): açık zemin, başlık + tarih aralığı · adet, **Slayt gösterisi**, kenardan kenara
  mozaik; tam ekran görüntüleyici (oklar), videolarda oynat rozeti + süre; slaytta Ken Burns + isteğe bağlı müzik.
- `standard-app` **"Oyun"** (`t4-oyun`): tam ekran hero, tür·yıl·sürüm, ad/logo, tek cümle, **uyumluluk satırı**
  (`disc.system()` ile Steam gereksinimleri karşılaştırılır: "✓ Bu bilgisayarda çalışır · N GB yer gerekir" ya da sarı
  "! 8 GB bellek önerilir"; veri yoksa yalnızca disk alanı), **Kur** + Kurmadan oyna, sağ altta 3 ekran görüntüsü.
  Kurulumdan sonra birincil "Oyna". İmzasız pakette ikincil satırda "Yayıncı imzası yok". Program paketi (`bundle`):
  aynı zemin üzerinde onay kutulu sade liste, zorunlular kilitli, "N program seçildi · toplam boyut" + **Seçilenleri kur**,
  satır satır ilerleme ve ✓ Kuruldu.
- `standard-mixed`: Sinema düzeni; sol altta öne çıkan öğe, altında türlere göre tek sıra küçük kapak (film 2:3, albüm/fotoğraf kare,
  oyun 16:9); Enter türün sayfasını açar. Davranış referansı `prototype/index.html` (görsel dili değil).
- Belge: Sade düzende sayfa önizlemesi, başlık, sayfa sayısı, **Aç** + Klasörde göster.
- Hepsi: 1280×720 – 3840×2160 ve 10 ft mesafeden okunur; tam klavye/gamepad; `prefers-reduced-motion` uyumlu;
  intro animasyonu ≤ 2,5 sn ve atlanabilir. Tema vurgusu paletten (`@property` + 0,6 sn geçiş), birincil buton varsayılan krem `#f6f2ec`.

### 7.3 Editör (builder içinde, üç seviye)
1. **Ayarlar:** `theme.json → settings` tanımlarından otomatik form (renk seçici "otomatik"/manuel, anahtar,
   açılır liste, kaydırıcı, font seçici, görsel seçici, metin). Gruplar `group` alanına göre.
2. **Bölümler:** `sections[]` sürükle-bırak sıralama, gizleme, başlık değiştirme, yerleşim (`row/grid/hero/list`).
3. **Kod:** CodeMirror 6 (HTML/Liquid/CSS vurgulama, Liquid değişken otomatik tamamlama), sağda **canlı önizleme**,
   altta **veri paneli** (manifest ağacı; tıklanınca `{{ items['mv-x'].title }}` ekler) ve **lint paneli**.
   Önizleme, launcher'ın kullandığı **aynı** render + SDK taklidiyle çalışır; sahte tarayıcı önizlemesi yapma.
- Şablon içe/dışa aktarma: `.disctheme`. Standart şablonu "kopyala ve düzenle" ile başlatma.
- Geri al/yinele, otomatik kayıt (`.discproj` içine).

### 7.4 Proje dosyası (`.discproj`)
JSON: kaynak klasörler, kullanıcı düzeltmeleri (sınıflandırma, TMDB eşleşmesi, sıralama), normalizasyon
seçenekleri, seçili tema + ayarlar, kullanıcı şablonu (gömülü), hedef medya, disk etiketi. Mutlak yollar
saklanır; taşınırsa "kaynağı yeniden bağla" akışı.

---

## 8. Disk düzeni, autorun ve yakma

### 8.1 Disk düzeni
```
/autorun.inf
/Baslat.exe                 ← launcher (imzalı)
/.disc/
   manifest.json
   icon.ico
   ui/index.html, ui/assets/…, ui/fonts/…
   player/ (libvlc.dll, libvlccore.dll, plugins/…)   ← sadece gerekiyorsa
   licenses/ (LibVLC LGPL, fontlar OFL, TMDB atfı)
/Film/ /Muzik/ /Fotograflar/ /Setup/ /Belgeler/     ← içerik (adlar yerelleştirilebilir)
/Menu.html                  ← WebView2 yoksa açılacak statik yedek (hosted=false modu)
```
`.disc` klasörüne gizli öznitelik ver (UDF/ISO9660'ta hidden bayrağı).

### 8.2 autorun.inf
```ini
[autorun]
open=Baslat.exe
icon=.disc\icon.ico
label=<disc.title, 32 karakter>
action=<Mod'a göre: "Filmi izle" / "Albümü çal" / "Oyunu kur" / "Menüyü aç">
shellexecute=Baslat.exe
```
- Windows 10/11'de optik diskte otomatik başlamaz; AutoPlay penceresinde `action` metniyle seçenek olarak çıkar.
  Bu beklenen davranıştır; kullanıcı arayüzünde bunu açıkça anlat.
- `icon.ico`: 16, 24, 32, 48, 64, 256 px (256 PNG sıkıştırmalı). Kaynak: kullanıcı ikonu > öne çıkan öğe kapağından
  otomatik yuvarlatılmış kare > varsayılan disk ikonu.

### 8.3 Dosya sistemi
- `IFileSystemImage.FileSystemsToCreate`:
  - Hiç dosya ≥ 4 GiB değilse: **ISO9660 + Joliet + UDF** (en geniş uyumluluk).
  - Herhangi bir dosya ≥ 4 GiB ise: **yalnızca UDF** (ISO9660/Joliet 4 GiB sınırı). Kullanıcıya bilgi ver.
- UDF revizyonu varsayılan **2.01**, ayarlanabilir (1.02 / 2.01 / 2.50).
- Volume label = `disc.label` (büyük harf, `A-Z0-9_`, ≤ 32; ISO9660 için ≤ 16 kısaltılmış biçim).
- Joliet ad sınırı (64 karakter) aşılırsa: UDF-only öner veya otomatik kısalt ve manifest yollarını buna göre üret.

### 8.4 Kapasite
| Medya | Sektör (2048 B) | Bayt |
|---|---|---|
| CD-R 700 MB (80 dk) | 360 000 | 737 280 000 |
| DVD±R (DVD-5) | 2 295 104 | 4 700 372 992 |
| DVD±R DL (DVD-9) | 4 173 824 (DVD+R DL) | 8 547 991 552 |
- Planlama bu değerlerle yapılır; **yakma anında** `IDiscFormat2Data.FreeSectorsOnMedia` ile gerçek değer okunur.
- Dosya sistemi ek yükü için %1,5 pay bırak. Doluluk cetveli `design-prompt.md` §2.4'teki gibi: 0–8,5 GB ölçek, CD/DVD/DVD DL işaretleri;
  sığmazsa `warn` rengi ve öneri ("DVD DL kullan", "Fragmanı çıkar (−86 MB)").

### 8.5 Yakma ve doğrulama
- Sürücü listesi: `MsftDiscMaster2` → `MsftDiscRecorder2`. Medya türü/boşluk kontrolü, yeniden yazılabilir
  medyada hızlı silme seçeneği.
- `IFileSystemImage.CreateResultImage().ImageStream` → `IDiscFormat2Data.Write`. İlerleme `Update` olayından
  (aşamalar: doğrulama, biçimlendirme, yazma, sonlandırma) okunur; tahmini kalan süre gösterilir.
- Yazma hızı seçimi (`SetWriteSpeed`); varsayılan "önerilen" (en yüksek değil, medya için güvenli olanın bir altı).
- Yakma sırasında: uyku engelle (`SetThreadExecutionState`), tüm uygulamada tek yazma işi.
- **Doğrulama:** yakmadan önce her dosyanın SHA-256'sını hesapla; yakma sonrası disk bağlandığında dosyaları geri okuyup
  karşılaştır. Rapor: başarılı / hatalı dosyalar, okuma hızı grafiği.
- **ISO export:** aynı imaj akışını dosyaya yaz. Test döngüsü: ISO → `Mount-DiskImage` → launcher smoke testi.
- IMAPI2 COM interop: builder AOT değil; `[ComImport]` arayüz tanımları veya üretilmiş interop derlemesi kullan.
  COM nesnelerini STA thread'de oluştur, `Marshal.FinalReleaseComObject` ile serbest bırak.

---

## 9. Launcher (`Baslat.exe`)

### 9.1 Yapı
- .NET 10 **NativeAOT**, `win-x64`, `InvariantGlobalization=false` (Türkçe sıralama gerekli),
  `IlcOptimizationPreference=Size`, trim uyarıları sıfır. Hedef boyut ≤ 10 MB.
- Pencere: Win32 (`CreateWindowEx`) veya WebView2Aot örneklerindeki host; **WinForms/WPF kullanma**.
  Per-Monitor-V2 DPI, Windows 11'de **Mica** arka plan (`DwmSetWindowAttribute`, `DWMWA_SYSTEMBACKDROP_TYPE`),
  koyu başlık çubuğu, özel başlık alanı. F11/çift tık tam ekran.
- Tek örnek (`Mutex`): ikinci açılış mevcut pencereyi öne getirir.

### 9.2 WebView2
- **User data folder:** `%LOCALAPPDATA%\Inlay\Launcher\<disc.label-hash>` (disk salt okunur, exe yanına yazma).
- İçerik `file://` ile değil, **sanal ana makine eşlemesi** ile sunulur:
  `SetVirtualHostNameToFolderMapping("disc.local", <disk kökü>, Allow)` → `https://disc.local/.disc/ui/index.html`.
  (Medya dosyalarına range istekleri bu yolla çalışır.)
- `window.disc` SDK'sı `AddScriptToExecuteOnDocumentCreated` ile enjekte edilir (kaynak: `ui/disc-sdk` derlemesi).
- Ayarlar: DevTools sadece debug derlemede; varsayılan bağlam menüsü kapalı; `AreHostObjectsAllowed=false`;
  `NavigationStarting` ile `disc.local` dışına gezinme engellenir; `NewWindowRequested` iptal edilir
  (dış bağlantılar yalnızca `kind: "url"` action'ı ve onayla açılır).
- WebView2 Runtime yoksa: kullanıcıya kısa bir native iletişim kutusu → "Menüyü tarayıcıda aç" (`/Menu.html`,
  `disc.hosted=false` modu: oynat/kur butonları "Dosyayı göster"e dönüşür) veya "WebView2'yi indir" (Microsoft linki).

### 9.3 Mesaj köprüsü
- Protokol `sdk/disc-sdk.d.ts` başındaki yorumdaki gibidir: `{v, req, cmd, id, opts}` → `{v, res, ok, data | error}`.
- Komutlar: `run`, `installBundle`, `play`, `playlist`, `slideshow`, `player.*`, `system`, `setFullscreen`, `close`.
- **Güvenlik kuralları (pazarlıksız):**
  1. Sayfadan gelen hiçbir değer dosya yolu olarak kullanılmaz; sadece `manifest.actions[].id` ve item/track id'leri.
  2. `relPath` → `Path.GetFullPath(Path.Combine(root, rel))` sonucu disk kökü altında değilse reddet.
  3. Manifest yüklenirken şemaya karşı doğrulanır (gömülü şema); geçersizse güvenli hata ekranı.
  4. `elevated: true` → `ShellExecuteEx` + `lpVerb="runas"`. UAC iptali (`ERROR_CANCELLED`) → `error: "cancelled"`.
  5. Aynı action çalışırken tekrar çağrı → `busy`.
- `install`/`run` süreçleri izlenir; `action-started`/`action-exited` olayları sayfaya gönderilir.
  `closeLauncher` alanına uyulur.

### 9.4 Oynatma
- `engine: "html"`: sayfa kendi `<video>/<audio>` elemanını kullanır; SDK sadece durum olaylarını normalize eder.
  Tam ekran film oynatıcı arayüzü SDK'nın paylaşılan bileşenidir (`ui/disc-sdk/player`), tüm şablonlar kullanır.
- `engine: "libvlc"`: **LibVLCSharp** ile ayrı bir video HWND; üstüne **şeffaf arka planlı ikinci bir WebView2**
  penceresi (`DefaultBackgroundColor` alfa 0) konur ve kontroller HTML olarak çizilir (airspace sorunu çözümü).
  Kontroller ve klavye kısayolları html motoruyla aynıdır; kullanıcı motor farkını görmemeli.
- LibVLC dizini yalnızca manifest'te `libvlc` gerektiren dosya varsa diske eklenir. **Eklenti budama:** builder,
  diskteki kodek/kap setine göre gereken `plugins/` alt kümesini seçer (demux, codec, access, audio_output, video_output,
  ihtiyaç halinde subtitle). Bu listeyi test et; eksik eklenti = sessiz oynatma hatası.
- Medya tuşları (`WM_APPCOMMAND`) ve XInput gamepad → `media-key` / `gamepad` olayları.
- Çalma pozisyonu `%LOCALAPPDATA%` altında item başına saklanır ("Kaldığın yerden devam et").

---

## 10. Güvenlik ve dağıtım

- **CSP** (şablon `<meta>` + launcher `WebResourceRequested` başlığı):
  `default-src 'self' https://disc.local; script-src 'self' 'unsafe-inline' https://disc.local; img-src 'self' data: blob: https://disc.local; media-src https://disc.local blob:; connect-src https://disc.local; frame-src 'none'; object-src 'none'`.
- **Kod imzalama:** `Baslat.exe`, builder ve builder kurulum paketi Authenticode ile imzalanır (zaman damgalı).
  İmzasız launcher SmartScreen uyarısı üretir; builder, imza sertifikası yoksa kullanıcıyı uyarır.
- Kullanıcının diske koyduğu imzasız exe'ler için menüde "İmzasız" rozeti ve onay metni (manifest `confirm`).
- Builder hiçbir telemetri göndermez. Ağ çağrıları yalnızca meta veri kaynaklarına: TMDB, IGDB (+ `id.twitch.tv`), Steam Store,
  SteamGridDB; her biri Ayarlar'dan kapatılabilir. Disk menüsü ve launcher hiçbir ağ çağrısı yapmaz.
- **Lisanslar:** LibVLC (LGPL-2.1, dinamik bağlama, lisans metni ve kaynak bilgisi `.disc/licenses/`),
  FFmpeg (builder ile LGPL derlemesi dağıt, `--enable-gpl` bileşenleri kullanma; x264 gerekiyorsa ADR yaz),
  fontlar (OFL), TMDB atfı (logo + "This product uses the TMDB API but is not endorsed or certified by TMDB."),
  IGDB atfı ("Oyun bilgileri: IGDB.com"), SteamGridDB atfı (kullanıldıysa). Twitch/SteamGridDB kimlik bilgileri DPAPI ile saklanır.

---

## 11. Builder arayüzü (UX)

Görünüm, bileşenler, token'lar ve metinler **`design-prompt.md` §2–3** ve `design/screens/01–04` referanslarından alınır.
Sihirbaz/adım göstergesi yoktur; dört ekran doğal bir akışla birbirine bağlanır:

| Ekran | Referans | İş |
|---|---|---|
| **Başlangıç** | `01-baslangic` | Kenar menüde disk türü (Film ya da dizi · Müzik · Fotoğraf · Oyun ya da program · Veri diski) ve araçlar (Disk kopyala, ISO kalıbı yaz, Diski sil, Son projeler). Panel bırakma hedefi; **Klasör seç**. Bırakınca tür otomatik tanınır, analiz arka planda başlar ve Disk içeriği açılır. |
| **Disk içeriği** | `02-disk-icerigi` | Tablo (ad + küçük resim, tür, boyut); tür düzeltme sağ tık menüsünde; TMDB/IGDB eşleşmesi ana satırın altında satır içi kart; normalizasyon kararları kullanıcı dilinde tek gri not; doluluk cetveli + **Diske yaz**. |
| **Disk menüsü** | `03-disk-menusu` | Canlı önizleme (launcher ile aynı render + SDK taklidi), şablon seçimi, "Disk takılınca" (`disc.home`/autoplay), disk adı, disk simgesi, kumanda ile gezinme; "HTML olarak düzenle" → 7.3 editörü. |
| **Diske yazılıyor** | `04-diske-yaziliyor` | Modal: dönen kapaklı disk, aşama metni, yüzde, kalan süre, GB · hız, "Bitince kontrol et ve diski çıkar", İptal; sonunda "Disk hazır" + Bir kopya daha. |

- Sürücü, hız, dosya sistemi, UDF revizyonu, orijinalleri koru, donanım kodlayıcı, API anahtarları gibi teknik seçenekler
  yalnızca **Ayarlar › Gelişmiş**'te. Varsayılanlar 6 ve 8. bölümlerdeki kararlardır.
- "Diske yaz" sürücüde disk yoksa "Disk tak" olur; yanında "ISO olarak kaydet". Kapasite aşımında yakma engellenir, öneri gösterilir.
- Explorer/AutoPlay görünümü Disk menüsü ekranındaki "Disk simgesi" ve "Disk adı" ile temsil edilir; ayrı sahte önizleme yok.
- Klavye: Ctrl+O klasör seç, Ctrl+S kaydet, Ctrl+B diske yaz, Delete kaldır, F2 yeniden adlandır.
- Uygulama WPF penceresinde özel başlık çubuğu (44 px) kullanır; Windows 11'de Mica, aksi halde `bg` degradesi. Snap Layouts desteklenir.
- Koyu tema varsayılan; vurgu rengi (turuncu/mavi/mor) Ayarlar'dan. Açık tema ikinci aşama.

## 12. Test stratejisi

| Katman | Ne test edilir |
|---|---|
| Birim (xUnit) | `RelPath` doğrulama (kaçış denemeleri), sınıflandırıcı skorları, **açılış görünümü karar tablosu (7.0, her satır bir test)**, bölüm adı ayrıştırıcı (S01E01/1x01/Sezon klasörü), **NormalizationPlanner karar tablosu** (her satır bir test), palet kontrastı, kapasite hesabı, Fluid filtreleri, lint kuralları, autorun.inf üretimi |
| Sözleşme | `samples/*.json` → şema; C# gidiş-dönüş; TS tipleri derleniyor; şema negatif testleri (path traversal, http URL, bilinmeyen alan reddediliyor) |
| Altın dosya | Standart şablonların örnek manifestle render çıktısı (HTML snapshot) |
| Medya | Küçük örnek dosya seti (`tests/media/`: H.264 MKV, HEVC, AC3, DTS, SRT, ASS, PGS, APE, ALAC, HEIC, CR2) → plan + gerçek FFmpeg çıktısı ffprobe ile doğrulanır |
| Entegrasyon | ISO üret → `Mount-DiskImage` → launcher'ı `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222` ile başlat → Playwright CDP ile bağlan → menü açıldı mı, `disc.run` izin listesi dışını reddediyor mu, html video oynuyor mu |
| Manuel kontrol listesi | Gerçek CD-R, DVD-R, DVD+R DL yakımı; Windows 10 22H2 ve Windows 11 temiz VM; WebView2'siz makine; 4K ve %150 DPI; gamepad |

---

## 13. Kilometre taşları ve kabul kriterleri

**M0 — İskelet.** Çözüm yapısı, merkezi paket yönetimi, CI (build + test), şema → C#/TS model üretimi,
sözleşme testleri yeşil.

**M1 — ISO + autorun.** Klasör → `manifest.json` (elle yazılmış basit içerik) + `icon.ico` + `autorun.inf` → IMAPI2 ile ISO.
✅ ISO Windows'ta bağlanınca Explorer'da özel ikon/etiket görünür; ≥ 4 GiB dosya içeren ISO UDF-only oluşur.

**M2 — Launcher.** AOT exe, WebView2 sanal host, SDK enjeksiyonu, mesaj köprüsü, izin listesi, `run/install/open/openFolder`,
UAC, WebView2 yok yedeği. ✅ Boyut ≤ 10 MB; izin listesi dışı istek reddedilir (test); salt okunur ISO'dan açılır.

**M3 — Analiz + normalizasyon.** Sınıflandırıcılar, meta veri sağlayıcıları, ffprobe, karar tablosu, FFmpeg kuyruğu, görsel dönüştürme,
palet. ✅ `tests/media` setinin tamamı için plan tablosu testleri geçer; MKV→MP4 remux bit-exact video akışı korur.

**M4 — Şablonlar.** Fluid render hattı, 5 standart şablon (`design/screens/t1–t4` ile görsel olarak eşleşir), lint;
IGDB/Steam/SteamGridDB sağlayıcıları ve uyumluluk satırı.
✅ Altın dosya testleri; render ekran görüntüsü referans PNG ile yan yana kontrol edildi; Lighthouse erişilebilirlik ≥ 90; klavye ile tüm eylemlere ulaşılır; 7.2'deki altı ilkenin
  ve her şablonun kontrol listesi işaretlendi (ör. ana menüde ≤ 2 buton, teknik terim yok,
  tek türlü diskte ana menü atlanıyor).

**M5 — Oynatıcılar.** Paylaşılan HTML oynatıcı bileşeni, LibVLC penceresi + şeffaf overlay, eklenti budama, medya tuşları, gamepad, devam et.
✅ HEVC + DTS MKV ve APE dosyası LibVLC ile oynar; kontroller html motoruyla aynı.

**M6 — Builder arayüzü + editör.** `design/screens/01–04` ile eşleşen dört ekran (`design-prompt.md` §6 kontrol listesi), ayar formu, bölüm düzenleyici, CodeMirror kod modu, canlı önizleme, `.discproj`, `.disctheme` içe/dışa aktarma.
✅ Kullanıcı standart şablonu kopyalayıp düzenleyebilir, lint hatası yakmayı engeller.

**M7 — Yakma + doğrulama.** Sürücü/medya algılama, hız, ilerleme, uyku engelleme, SHA-256 doğrulama raporu, hata durumları
(medya yok, kapasite yetersiz, tampon taşması, sürücü meşgul). ✅ Üç medya türünde gerçek yakma başarılı ve doğrulandı.

**M8 (opsiyonel) — CD-Extra.** Albüm modunda ilk oturuma CD-DA parçalar (`IDiscFormat2TrackAtOnce`, `DoNotFinalizeMedia`),
ikinci oturuma veri. Önce prototiple fizibiliteyi kanıtla, ADR yaz; olmuyorsa özelliği kapat.

---

## 14. Teslim edilecekler

- Derlenebilir çözüm, `README.md` (geliştirici kurulumu: .NET 10 SDK, Node 22+, FFmpeg/ffprobe yolu, LibVLC paket konumu, TMDB anahtarı, Twitch Client ID/Secret, isteğe bağlı SteamGridDB anahtarı).
- Builder için MSIX veya imzalı kurulum paketi.
- `docs/user-guide.md` (Türkçe, ekran görüntülü), `docs/theme-authoring.md` (Liquid modeli, SDK, settings türleri, lint kuralları, örnekler).
- Örnek proje: `samples/` manifestiyle eşleşen gerçek küçük medya seti ve ondan üretilmiş ISO.

---

## 15. Referans dosyalar (bu klasörde)

| Dosya | İçerik |
|---|---|
| `schemas/manifest.schema.json` | Disk manifest sözleşmesi (JSON Schema 2020-12) |
| `schemas/theme.schema.json` | Tema paketi sözleşmesi |
| `samples/manifest.mixed.json` | Karışık mod örnek manifest (şemaya karşı doğrulanmış) |
| `samples/manifest.series.json` | Dizi diski örneği (`home.view = series`) |
| `samples/manifest.bundle.json` | Program paketi örneği (`home.view = bundle`, zorunlu ön gereksinim) |
| `samples/theme.standard-mixed.json` | Standart "Hepsi" teması tanımı |
| `sdk/disc-sdk.d.ts` | Sayfa ↔ launcher SDK arayüzü ve mesaj protokolü |
| `sdk/manifest.ts` | Şemadan üretilmiş TypeScript tipleri |
| `design-prompt.md` | **Görsel tasarım sistemi**: builder token'ları, bileşenler, 4 ekran, durumlar; 4 şablonun kuralları |
| `design/screens/01–04.(html\|png)` | Builder ekranları: Başlangıç, Disk içeriği, Disk menüsü, Diske yazılıyor (1440×900) |
| `design/screens/t1–t4.(html\|png)` | Disk menüsü şablonları: Film, Müzik, Fotoğraf, Oyun (1440×810) |
| `design/img/` | Referanslardaki örnek görseller (Unsplash/Picsum; üründe kullanılmaz) |
| `prototype/index.html` | Açılış görünümü **davranışlarının** prototipi (görsel dili eskidir, `design/` geçerlidir); üstteki "Örnek disk" seçicisiyle 8 senaryo denenebilir (seçici yalnızca prototipte var) |
