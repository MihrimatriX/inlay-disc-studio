// Builds the sample project's source folders (§14) with the bundled FFmpeg and the art in design/img, so nothing
// copyrighted is involved:
//   <out>/Kuzey Rüzgârı (2023)  a film disc: feature-length film with chapters, trailer, an extra, English
//                               subtitles, poster/fanart and a Kodi .nfo (the user guide's screenshots use it)
//   <out>/Koleksiyon            the mixed disc of samples/manifest.mixed.json: making-of film, soundtrack album,
//                               development diary photos and the manual (its two programs need real installers,
//                               which cannot be synthesized, so they are left out)
// Usage: node scripts/make-sample.mjs <outDir>
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const out = path.resolve(process.argv[2] ?? "artifacts/sample");
const repo = path.resolve(import.meta.dirname, "..");
const ffmpeg = path.join(repo, "tools", "ffmpeg", "bin", "ffmpeg.exe");
/** @param {string} name */
const img = (name) => path.join(repo, "design", "img", name);

/** @param {string[]} args */
const run = (args) => execFileSync(ffmpeg, ["-v", "error", "-y", ...args], { stdio: "inherit" });
/** @param {string} file @param {number} sec @param {number} freq @param {string[]} [extra] */
const clip = (file, sec, freq, extra = []) => run([
  "-f", "lavfi", "-i", `testsrc2=size=640x360:rate=5:duration=${sec}`,
  "-f", "lavfi", "-i", `sine=frequency=${freq}:sample_rate=48000:duration=${sec}`,
  ...extra, "-c:v", "libopenh264", "-b:v", "120k", "-c:a", "aac", "-b:a", "48k", file,
]);

/** A film of 16 minutes (above the 15-minute line where a video counts as a film, §5.2) with four chapters.
 * @param {string} dir @param {string} name @param {string[]} chapters */
function film(dir, name, chapters) {
  fs.mkdirSync(dir, { recursive: true });
  const meta = path.join(dir, "chapters.txt");
  fs.writeFileSync(meta, [";FFMETADATA1", `title=${name}`,
    ...chapters.flatMap((title, i) => ["[CHAPTER]", "TIMEBASE=1/1000", `START=${i * 240000}`, `END=${(i + 1) * 240000}`, `title=${title}`]),
  ].join("\n"));
  clip(path.join(dir, `${path.basename(dir)}.mkv`), 16 * 60, 330, ["-f", "ffmetadata", "-i", meta, "-map", "0:v", "-map", "1:a", "-map_metadata", "2", "-map_chapters", "2"]);
  fs.rmSync(meta);
}

/** @param {string} file @param {string} title @param {string} body */
function pdf(file, title, body) {
  // One page, Helvetica, ASCII text: a real PDF small enough to write by hand (offsets computed below).
  const text = `BT /F1 24 Tf 72 720 Td (${title}) Tj /F1 12 Tf 0 -36 Td (${body}) Tj ET`;
  const objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
    `<< /Length ${text.length} >>\nstream\n${text}\nendstream`,
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
  ];
  let doc = "%PDF-1.4\n";
  const offsets = objects.map((o, i) => { const at = doc.length; doc += `${i + 1} 0 obj\n${o}\nendobj\n`; return at; });
  const xref = doc.length;
  doc += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.map((o) => `${String(o).padStart(10, "0")} 00000 n \n`).join("")}`;
  doc += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  fs.writeFileSync(file, doc, "latin1");
}

// ---------------- film disc ----------------
const kuzey = path.join(out, "Kuzey Rüzgârı (2023)");
film(kuzey, "Kuzey Rüzgârı", ["Açılış", "Yolculuk", "Fırtına", "Dönüş"]);
fs.mkdirSync(path.join(kuzey, "Ekstralar"), { recursive: true });
clip(path.join(kuzey, "Kuzey Rüzgârı - Fragman.mp4"), 20, 440);
clip(path.join(kuzey, "Ekstralar", "Kamera arkası.mp4"), 30, 550);
fs.copyFileSync(img("film-poster.jpg"), path.join(kuzey, "poster.jpg"));
fs.copyFileSync(img("film-hero.jpg"), path.join(kuzey, "fanart.jpg"));
const srt = Array.from({ length: 8 }, (_, i) => {
  /** @param {number} s */
  const t = (s) => `00:${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")},000`;
  return `${i + 1}\n${t(i * 120 + 5)} --> ${t(i * 120 + 9)}\nThe north wind is rising. (${i + 1})\n`;
}).join("\n");
fs.writeFileSync(path.join(kuzey, "Kuzey Rüzgârı (2023).en.srt"), srt);
fs.writeFileSync(path.join(kuzey, "movie.nfo"), `<?xml version="1.0" encoding="UTF-8"?>
<movie>
  <title>Kuzey Rüzgârı</title>
  <originaltitle>North Wind</originaltitle>
  <year>2023</year>
  <genre>Macera</genre>
  <plot>Kuzeyden esen rüzgârın peşine düşen iki kardeş, bir kış boyunca dağların ardındaki köye ulaşmaya çalışır.</plot>
  <tagline>Rüzgâr nereye eserse.</tagline>
  <director>Deniz Aksoy</director>
  <actor><name>Ece Yalın</name><role>Mavi</role></actor>
  <actor><name>Kerem Tuna</name><role>Rüzgâr</role></actor>
</movie>
`);

// ---------------- mixed collection (samples/manifest.mixed.json) ----------------
const col = path.join(out, "Koleksiyon");
const making = path.join(col, "Yapım Hikâyesi (2024)");
film(making, "Yapım Hikâyesi", ["Fikir", "Prototip", "Ekip", "Çıkış"]);
fs.copyFileSync(img("ch2.jpg"), path.join(making, "poster.jpg"));
fs.copyFileSync(img("game-hero.jpg"), path.join(making, "fanart.jpg"));

const ost = path.join(col, "Original Soundtrack");
fs.mkdirSync(ost, { recursive: true });
["Açılış", "Orman", "Final"].forEach((title, i) => run([
  "-f", "lavfi", "-i", `sine=frequency=${220 * (i + 1)}:sample_rate=48000:duration=40`,
  "-metadata", `title=${title}`, "-metadata", "artist=Side Project", "-metadata", "album=Original Soundtrack",
  "-metadata", "date=2024", "-metadata", `track=${i + 1}/3`, "-metadata", "genre=Soundtrack",
  "-c:a", "aac", "-b:a", "96k", path.join(ost, `0${i + 1} - ${title}.m4a`),
]));
fs.copyFileSync(img("album.jpg"), path.join(ost, "cover.jpg"));

const diary = path.join(col, "Geliştirme Günlüğü");
fs.mkdirSync(diary, { recursive: true });
for (let i = 1; i <= 9; i++) fs.copyFileSync(img(`g${i}.jpg`), path.join(diary, `gunluk-${String(i).padStart(2, "0")}.jpg`));

pdf(path.join(col, "Kullanım Kılavuzu.pdf"), "Kullanim Kilavuzu", "Oyunumuz ve Seviye Editoru icin kisa rehber.");
console.log(kuzey);
console.log(col);
