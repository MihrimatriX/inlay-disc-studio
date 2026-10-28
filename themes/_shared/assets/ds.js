// Inlay standard disc menu runtime: views, focus/gamepad navigation, actions with natural feedback, music bar,
// gallery viewer, compatibility line, bundle install. Talks to the launcher only through window.disc (sdk/disc-sdk.d.ts).
(() => {
  "use strict";
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const data = JSON.parse($("#ds-data").textContent);
  const T = (key, ...args) => (data.text[key] ?? key).replace(/\{(\d+)\}/g, (_, i) => args[+i] ?? "");
  const disc = window.disc;
  const m = disc.manifest;
  const items = Object.fromEntries(m.items.map((i) => [i.id, i]));
  const actionsById = Object.fromEntries(m.actions.map((a) => [a.id, a]));
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const root = $("#views");

  // A missing or damaged picture disappears instead of showing the broken-image icon.
  document.addEventListener("error", (e) => { if (e.target instanceof HTMLImageElement) e.target.style.visibility = "hidden"; }, true);
  document.addEventListener("load", (e) => { if (e.target instanceof HTMLImageElement) e.target.style.visibility = ""; }, true);
  // Pictures that failed before this script ran (it loads last) never reach the listener above.
  $$("img[src]").forEach((i) => { if (i.complete && i.naturalWidth === 0) i.style.visibility = "hidden"; });

  // ---------------- views and history ----------------
  const stack = [];
  const homeId = () => {
    const h = m.disc.home;
    if (h.view === "menu") return "v-menu";
    if (h.view === "collection") return "v-collection";
    if (h.view === "bundle") return "v-bundle";
    return "v-" + h.item;
  };
  const current = () => stack[stack.length - 1];
  const viewEl = (id) => document.getElementById(id);

  function show(id, push = true) {
    if (!viewEl(id)) id = homeId();
    const apply = () => {
      $$(".view", root).forEach((v) => (v.hidden = v.id !== id));
      $$(".sheet.open").forEach(closeSheet);
      if (push) stack.push(id);
      updateBack();
      const item = items[id.slice(2)];
      if (item?.palette) document.documentElement.style.setProperty("--ds-accent", item.palette.accent);
      history.replaceState(null, "", id === homeId() ? location.pathname : "#" + id.slice(2));
      focusFirst(viewEl(id));
      viewEl(id).dispatchEvent(new CustomEvent("ds:shown"));
    };
    if (document.startViewTransition && !reduced && stack.length) document.startViewTransition(apply);
    else apply();
  }

  function back() {
    if ($(".sheet.open")) return closeSheet($(".sheet.open"));
    if ($("#viewer").open) return closeViewer();
    if (stack.length > 1) {
      stack.pop();
      show(current(), false);
    }
  }

  // §7.0: no back button on the page a single-item disc opens to — the disc's name stands there instead.
  function updateBack() {
    const v = viewEl(current());
    const parent = stack[stack.length - 2];
    $$("[data-back]", v).forEach((b) => {
      b.hidden = !parent;
      if (parent) b.querySelector("span").textContent = viewEl(parent)?.dataset.label ?? T("ui.back");
    });
    $$("[data-disc-name]", v).forEach((s) => (s.hidden = !!parent));
  }

  // HTML controls only: SVG <use href> would match [href], has no offsetParent at all and cannot take focus.
  const focusable = (scope) =>
    $$("button:not([disabled]):not([hidden]), a[href], [tabindex]:not([tabindex='-1'])", scope)
      .filter((e) => e instanceof HTMLElement && (e.offsetParent !== null || e === document.activeElement));

  function focusFirst(scope) {
    const target = $(".card[data-open]", scope) ?? $(".btn.primary:not([hidden]), .btn.dark", scope) ?? focusable(scope)[0];
    target?.focus({ preventScroll: true });
  }

  // ---------------- spatial navigation: arrows, Enter, Esc/Backspace, gamepad (§7.2.6) ----------------
  function scope() {
    if ($("#viewer").open) return $("#viewer");
    return $(".sheet.open") ?? viewEl(current());
  }

  function move(dir) {
    const all = focusable(scope());
    const from = document.activeElement && all.includes(document.activeElement) ? document.activeElement : null;
    if (!from) return all[0]?.focus();
    const a = from.getBoundingClientRect();
    const ax = a.left + a.width / 2, ay = a.top + a.height / 2;
    let best = null, bestScore = Infinity;
    for (const el of all) {
      if (el === from) continue;
      const b = el.getBoundingClientRect();
      const bx = b.left + b.width / 2, by = b.top + b.height / 2;
      const dx = bx - ax, dy = by - ay;
      const ok = dir === "left" ? dx < -4 : dir === "right" ? dx > 4 : dir === "up" ? dy < -4 : dy > 4;
      if (!ok) continue;
      const main = dir === "left" || dir === "right" ? Math.abs(dx) : Math.abs(dy);
      const cross = dir === "left" || dir === "right" ? Math.abs(dy) : Math.abs(dx);
      const score = main + cross * 2.5;
      if (score < bestScore) (best = el), (bestScore = score);
    }
    if (best) {
      best.focus({ preventScroll: false });
      best.scrollIntoView({ block: "nearest", inline: "nearest", behavior: reduced ? "auto" : "smooth" });
    }
  }

  addEventListener("keydown", (e) => {
    if (e.altKey || e.ctrlKey || e.metaKey) return;
    const k = { ArrowLeft: "left", ArrowRight: "right", ArrowUp: "up", ArrowDown: "down" }[e.key];
    if (k) {
      e.preventDefault();
      if ($("#viewer").open && (k === "left" || k === "right")) return stepViewer(k === "left" ? -1 : 1);
      return move(k);
    }
    if (e.key === "Escape" || (e.key === "Backspace" && !/input|textarea/i.test(e.target.tagName))) {
      e.preventDefault();
      back();
    }
  });

  // Gamepad API (D-pad 12–15, A = 0, B = 1); the launcher also forwards XInput as "gamepad" events.
  const pressed = new Set();
  function pad(button) {
    ({ up: () => move("up"), down: () => move("down"), left: () => move("left"), right: () => move("right"),
       a: () => document.activeElement?.click(), b: back })[button]?.();
  }
  // disc.remote=false (builder: Kumanda ile gezinme off) leaves navigation to keyboard and mouse.
  const remote = m.disc.remote !== false;
  (function poll() {
    if (!remote) return;
    for (const g of navigator.getGamepads?.() ?? []) {
      if (!g) continue;
      const map = { 12: "up", 13: "down", 14: "left", 15: "right", 0: "a", 1: "b" };
      for (const [i, name] of Object.entries(map)) {
        const key = g.index + ":" + i;
        if (g.buttons[i]?.pressed) { if (!pressed.has(key)) { pressed.add(key); pad(name); } }
        else pressed.delete(key);
      }
    }
    requestAnimationFrame(poll);
  })();
  // The same pad also reaches the Gamepad API above once the page has seen it: then the launcher's copy is a duplicate.
  disc.on("gamepad", (d) => remote && !(navigator.getGamepads?.() ?? []).some(Boolean) && pad(d.button));

  // ---------------- feedback ----------------
  let toastTimer;
  function toast(text) {
    const t = $("#toast");
    t.textContent = text;
    t.classList.add("on");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.remove("on"), 2800);
  }
  const errorText = (code) => ({ cancelled: T("ui.cancelled"), "reboot-required": T("ui.rebootNeeded") }[code] ?? T("ui.failed"));

  // ---------------- sheets and tabs ----------------
  function openSheet(id) {
    const s = document.getElementById(id);
    if (!s) return;
    s.classList.add("open");
    s._opener = document.activeElement;
    focusable(s)[0]?.focus({ preventScroll: true });
  }
  function closeSheet(s) {
    s.classList.remove("open");
    s._opener?.focus({ preventScroll: true });
  }

  // ---------------- actions ----------------
  async function run(button, id) {
    const a = actionsById[id];
    if (!a) return;
    const busyText = { install: T("ui.installing"), run: T("ui.starting"), open: T("ui.opening") }[a.kind];
    if (busyText && disc.hosted) toast(busyText);
    try {
      const r = await disc.run(id);
      const canRunInstalled = r?.canRunInstalled && button?.dataset.installs;
      if (a.kind === "install" && (canRunInstalled || (r?.exitCode === 0 && button?.dataset.then))) {
        // §7.0: after installing, the primary action becomes "Oyna": the installed copy when the launcher
        // found it, otherwise the disc's portable run action.
        if (canRunInstalled) {
          delete button.dataset.action;
          button.dataset.runInstalled = button.dataset.installs;
        } else button.dataset.action = button.dataset.then;
        button.querySelector("span").textContent = T("ui.playGame");
        button.querySelector("use")?.setAttribute("href", "#i-play");
        toast(T("ui.installed"));
      }
    } catch (e) {
      if (e?.code !== "busy") toast(errorText(e?.code));
    }
  }

  document.addEventListener("click", (e) => {
    const t = e.target.closest("button");
    if (!t) return;
    const d = t.dataset;
    if (d.back !== undefined) return back();
    if (d.open) return show("v-" + d.open);
    if (d.sheet) return openSheet(d.sheet);
    if (d.tab) {
      const list = t.parentElement;
      $$(".tab", list).forEach((x) => x.setAttribute("aria-selected", String(x === t)));
      $$("[role=tabpanel]", list.parentElement).forEach((p) => (p.hidden = p.id !== d.tab));
      return;
    }
    if (d.action) return run(t, d.action);
    if (d.runInstalled) {
      toast(T("ui.starting"));
      return disc.runInstalled(d.runInstalled).catch((err) => toast(errorText(err?.code)));
    }
    if (d.play) return disc.play(d.play, { startSec: d.start ? +d.start : undefined, fullscreen: true }).catch((err) => toast(errorText(err?.code)));
    if (d.playFile) return disc.play(d.playFile, { fullscreen: true }).catch((err) => toast(errorText(err?.code)));
    if (d.track) return disc.playlist(d.album, d.track).catch((err) => toast(errorText(err?.code)));
    if (d.shuffle) return disc.playlist(d.shuffle, undefined, { shuffle: true }).catch((err) => toast(errorText(err?.code)));
    if (d.shuffleAll !== undefined) {
      const albums = m.items.filter((i) => i.type === "album");
      return disc.playlist(albums[Math.floor(Math.random() * albums.length)].id, undefined, { shuffle: true }).catch(() => {});
    }
    if (d.photo) return openViewer(d.gallery, +d.photo, false);
    if (d.shot) return openViewer(d.app, +d.shot, false);
    if (d.pkg) return togglePackage(t);
  });

  // ---------------- menu and collections: the hero and background follow the focused cover ----------------
  function bindRow(view) {
    if (!view) return;
    const a = $("[data-bg-a]", view), b = $("[data-bg-b]", view);
    let front = a, back_ = b, timer;
    const hero = (card) => {
      $("[data-hero-title]", view).textContent = card.dataset.title;
      $("[data-hero-meta]", view).textContent = card.dataset.meta ?? "";
      const s = card.dataset.summary ?? "";
      $("[data-hero-summary]", view).textContent = s && !/[.!?…]$/.test(s) ? s + "." : s;
      clearTimeout(timer);
      timer = setTimeout(() => {
        const src = card.dataset.bg;
        if (!src || front.getAttribute("src") === src) return;
        back_.onload = () => {
          back_.classList.remove("fade");
          front.classList.add("fade");
          [front, back_] = [back_, front];
        };
        back_.src = src;
      }, reduced ? 0 : 140);
    };
    view.addEventListener("focusin", (e) => { const c = e.target.closest(".card"); if (c) hero(c); });
    const first = $(".card", view);
    if (first) hero(first);
  }
  bindRow(viewEl("v-menu"));
  bindRow(viewEl("v-collection"));

  // ---------------- music bar (§7.0: keeps playing after leaving the album page) ----------------
  const bar = $("#playerbar");
  disc.on("player", (s) => {
    const album = items[s.itemId];
    const isTrack = album?.type === "album";
    bar.classList.toggle("on", isTrack && s.status !== "ended");
    $$(".track.now").forEach((x) => x.classList.remove("now"));
    if (!isTrack) return;
    const track = album.tracks.find((t) => t.id === s.trackId);
    $("#pb-title").textContent = track?.title ?? album.title;
    $("#pb-sub").textContent = [track?.artist ?? album.artist, album.title].filter(Boolean).join(" · ");
    const cover = viewEl("v-" + album.id)?.dataset.cover;
    if (cover) $("#pb-cover").src = cover;
    $("#pb-seek").style.width = s.durationSec ? (100 * s.positionSec) / s.durationSec + "%" : "0";
    const playing = s.status === "playing" || s.status === "loading";
    $("#pb-toggle use").setAttribute("href", playing ? "#i-pause" : "#i-play");
    $("#pb-toggle").setAttribute("aria-label", playing ? T("ui.pause") : T("ui.play"));
    $(`.track[data-track="${CSS.escape(s.trackId ?? "")}"]`)?.classList.add("now");
  });
  $("#pb-toggle").onclick = () => (disc.player.state()?.status === "playing" ? disc.player.pause() : disc.player.resume());
  $("#pb-next").onclick = () => disc.player.next();
  $("#pb-prev").onclick = () => disc.player.prev();
  $("#pb-stop").onclick = () => { disc.player.stop(); bar.classList.remove("on"); };
  disc.on("media-key", (d) => ({ "play-pause": $("#pb-toggle").onclick, next: () => disc.player.next(), prev: () => disc.player.prev(), stop: () => disc.player.stop() })[d.key]?.());

  // ---------------- gallery viewer and slideshow (§4.4: Ken Burns, optional music) ----------------
  const viewer = $("#viewer");
  let gal = null, idx = 0, auto = null;
  function openViewer(galleryId, start, slideshow) {
    gal = JSON.parse(document.getElementById("g-" + galleryId).textContent);
    idx = start;
    viewer.showModal();
    paint(slideshow);
    clearInterval(auto);
    if (slideshow) {
      auto = setInterval(() => stepViewer(1, true), gal.interval ?? 5000);
      if (gal.music) disc.play(gal.music).catch(() => {});
    }
  }
  function paint(kb) {
    const p = gal.photos[idx];
    const frame = $("#viewer-frame");
    frame.textContent = "";
    if (p.kind === "video" && p.media) {
      const v = document.createElement("video");
      v.controls = true;
      frame.append(v);
      clearInterval(auto);
      disc.play(p.media.file, { element: v }).catch(() => {});
    } else {
      const img = new Image();
      img.src = disc.hosted ? "https://disc.local/" + p.file.split("/").map(encodeURIComponent).join("/") : "./" + p.file.split("/").map(encodeURIComponent).join("/");
      img.alt = p.caption ?? "";
      if (kb && gal.transition !== "none" && !reduced) {
        img.className = "kb";
        img.style.setProperty("--kb-ms", (gal.interval ?? 5000) + "ms");
      }
      frame.append(img);
    }
    const when = p.takenAt ? new Date(p.takenAt).toLocaleDateString(document.documentElement.lang, { day: "numeric", month: "long", year: "numeric" }) : "";
    $("#viewer-caption").textContent = [p.caption, when, `${idx + 1} / ${gal.photos.length}`].filter(Boolean).join(" · ");
  }
  function stepViewer(d, kb = false) {
    if (!gal) return;
    idx = (idx + d + gal.photos.length) % gal.photos.length;
    paint(kb || auto !== null);
  }
  function closeViewer() {
    clearInterval(auto);
    auto = null;
    viewer.close();
  }
  viewer.addEventListener("close", () => { clearInterval(auto); auto = null; $("#viewer-frame").textContent = ""; });
  $("#viewer-prev").onclick = () => stepViewer(-1);
  $("#viewer-next").onclick = () => stepViewer(1);
  $("#viewer-close").onclick = closeViewer;
  document.addEventListener("disc:slideshow", (e) => {
    const i = Math.max(0, items[e.detail.galleryId]?.photos.findIndex((p) => p.id === e.detail.startPhotoId) ?? 0);
    openViewer(e.detail.galleryId, i, true);
  });

  // ---------------- compatibility line (§7.2 Oyun): only Steam's structured requirements, never guesses ----------------
  const gb = (bytes) => (bytes / 1073741824).toLocaleString(document.documentElement.lang, { maximumFractionDigits: 1 }) + " GB";
  async function compat() {
    const lines = $$("[data-compat]");
    if (!lines.length) return;
    let sys = null;
    try { sys = disc.hosted ? await disc.system() : null; } catch { /* unhosted or old launcher */ }
    for (const el of lines) {
      const ram = parseFloat(el.dataset.ram);
      const need = parseFloat(el.dataset.disk) || parseFloat(el.dataset.diskGb) * 1073741824 || 0;
      if (!need && !ram) continue;
      const free = sys ? Math.max(0, ...Object.values(sys.freeSpaceBytes ?? {})) : null;
      const lowRam = sys && ram && sys.memoryBytes && sys.memoryBytes < ram * 1073741824 * 0.95;
      const lowDisk = sys && need && free !== null && free < need;
      el.classList.remove("muted", "ok", "warn");
      if (lowDisk) { el.classList.add("warn"); el.innerHTML = `<svg><use href="#i-warn"/></svg>`; el.append(T("ui.notEnoughSpace", gb(need))); }
      else if (lowRam) { el.classList.add("warn"); el.innerHTML = `<svg><use href="#i-warn"/></svg>`; el.append(T("ui.needsMemory", ram + " GB")); }
      else if (sys && ram) { el.classList.add("ok"); el.innerHTML = `<svg><use href="#i-check"/></svg>`; el.append(T("ui.runsHere", gb(need))); }
      else { el.classList.add("muted"); el.textContent = T("ui.needsSpace", gb(need)); }
    }
  }

  // ---------------- bundle (§7.0) ----------------
  const bundle = viewEl("v-bundle");
  function togglePackage(row) {
    if (row.classList.contains("locked") || bundle?.dataset.busy) return;
    row.setAttribute("aria-checked", String(row.getAttribute("aria-checked") !== "true"));
    sumBundle();
  }
  function sumBundle() {
    const chosen = $$(".pkg[aria-checked=true]", bundle);
    const bytes = chosen.reduce((n, r) => n + (+r.dataset.size || 0), 0);
    $("#bundle-sum").innerHTML = "";
    const b = document.createElement("b");
    const count = chosen.filter((r) => !r.classList.contains("locked")).length;
    b.textContent = bytes > 0 ? T("ui.selected", count, gb(bytes)) : T("ui.selected", count, "").replace(/\s*·\s*$/, "");
    $("#bundle-sum").append(b);
  }
  if (bundle) {
    const list = $(".list", bundle);
    $$(".pkg", list).sort((x, y) => +x.dataset.order - +y.dataset.order).forEach((r) => list.append(r));
    sumBundle();
    disc.on("bundle-progress", (p) => {
      const row = $(`.pkg[data-pkg="${CSS.escape(p.id)}"]`, bundle);
      const st = row && $("[data-state]", row);
      if (!st) return;
      st.className = "state" + (p.state === "done" ? " ok" : p.state === "failed" ? " bad" : "");
      st.textContent = { waiting: T("ui.waiting"), installing: T("ui.installing"), done: "✓ " + T("ui.installed"), failed: T("ui.installFailed") + " · " + T("ui.retry") }[p.state];
    });
    $("#bundle-install").onclick = async () => {
      const ids = $$(".pkg[aria-checked=true]", bundle).map((r) => r.dataset.pkg);
      if (!ids.length) return;
      bundle.dataset.busy = "1";
      $$(".pkg[aria-checked=true] [data-state]", bundle).forEach((s) => (s.textContent = T("ui.waiting")));
      try {
        const r = await disc.installBundle(ids);
        if (r.failed.length === 0) toast(T("ui.installed"));
      } catch (e) {
        toast(errorText(e?.code));
      } finally {
        delete bundle.dataset.busy;
      }
    };
  }

  // ---------------- unhosted Menu.html: installers become "show the file" (§9.2) ----------------
  if (!disc.hosted) {
    $$("[data-action]").forEach((b) => {
      const a = actionsById[b.dataset.action];
      if (!a || (a.kind !== "install" && a.kind !== "run")) return;
      // One "show the file" is enough: the second program button would only repeat it.
      if (!b.classList.contains("primary")) return void (b.hidden = true);
      (b.querySelector("span") ?? b).textContent = T("ui.showFile");
    });
  }

  // ---------------- intro (≤ 2.5 s, skippable, §7.2) ----------------
  const intro = $("#intro");
  if (intro) {
    const done = () => { intro.classList.add("out"); setTimeout(() => intro.remove(), 600); };
    if (reduced) done();
    else {
      setTimeout(done, Math.min(+intro.dataset.ms || 2500, 2500));
      $("#intro-skip")?.addEventListener("click", done);
      addEventListener("keydown", done, { once: true });
    }
  }


  // ---------------- resume (§7.0): "Kaldığın yerden devam et", next episode, progress bars ----------------
  const pct = (p) => (p && p.durationSec ? Math.min(100, (100 * p.positionSec) / p.durationSec) : 0);
  function refreshProgress() {
    if (!disc.progress) return;
    for (const b of $$("[data-resume-for]")) {
      const id = b.dataset.resumeFor;
      const p = disc.progress(id);
      const label = b.querySelector("span");
      const view = b.closest(".view");
      const secondary = b.nextElementSibling;
      $$("[data-restart]", view).forEach((r) => r.remove());
      if (p && p.positionSec > 0) {
        b.dataset.play = id;
        b.dataset.start = String(p.positionSec);
        delete b.dataset.action;
        label.textContent = T("ui.resume");
        // At most two buttons (§7.2): "Baştan izle" takes the secondary slot, or opens the chapters panel.
        const restart = document.createElement("button");
        restart.dataset.play = id;
        restart.dataset.start = "0";
        restart.dataset.restart = "";
        restart.textContent = T("ui.restart");
        if (secondary?.dataset.sheet) {
          restart.className = "chapter";
          restart.innerHTML = '<div class="thumb-ph"></div><span></span>';
          restart.querySelector("span").textContent = T("ui.restart");
          document.getElementById(secondary.dataset.sheet)?.querySelector(".strip")?.prepend(restart);
        } else {
          restart.className = "btn";
          b.after(restart);
        }
      } else {
        const item = items[id];
        if (item?.actions?.[0]) b.dataset.action = item.actions[0];
        delete b.dataset.play;
        delete b.dataset.start;
        label.textContent = T("ui.watch");
      }
    }
    for (const b of $$("[data-next-episode]")) {
      const series = items[b.dataset.nextEpisode];
      const eps = series.seasons.flatMap((s) => s.episodes.map((e) => ({ s: s.no, e })));
      const started = eps.find((x) => disc.progress(x.e.id)?.positionSec > 0);
      const next = started ?? eps.find((x) => !disc.progress(x.e.id)?.watched) ?? eps[0];
      b.dataset.play = next.e.id;
      b.dataset.start = String(started ? disc.progress(next.e.id).positionSec : 0);
      b.querySelector("span").textContent = T(started ? "ui.continueEpisode" : "ui.watchEpisode", next.s, next.e.no);
      for (const x of eps) {
        const row = document.querySelector(`.episode[data-play="${CSS.escape(x.e.id)}"]`);
        if (!row) continue;
        const p = disc.progress(x.e.id);
        row.querySelector(".bar i").style.width = (p?.watched ? 100 : pct(p)) + "%";
        let done = row.querySelector(".done");
        if (p?.watched && !done) {
          done = document.createElement("small");
          done.className = "done";
          done.textContent = " ✓ " + T("ui.watched");
          row.querySelector("span").append(done);
        }
      }
    }
    for (const bar of $$("[data-progress-for]")) {
      const p = disc.progress(bar.dataset.progressFor);
      bar.hidden = !(p && p.positionSec > 0);
      bar.querySelector("i").style.width = pct(p) + "%";
    }
    const latest = $("[data-resume-latest]");
    if (latest) {
      const all = disc.progressAll();
      const movie = m.items.filter((i) => i.type === "movie" && all[i.id]?.positionSec > 0).sort((a, b) => all[b.id].updatedAt - all[a.id].updatedAt)[0];
      latest.hidden = !movie;
      if (movie) {
        latest.dataset.play = movie.id;
        latest.dataset.start = String(all[movie.id].positionSec);
        latest.querySelector("span").textContent = T("ui.continue", movie.title);
      }
    }
  }
  disc.on("player", (st) => { if (st.status === "ended" || st.status === "paused") refreshProgress(); });
  root.addEventListener("ds:shown", refreshProgress, true);

  // ---------------- start ----------------
  addEventListener("hashchange", () => {
    const id = "v-" + location.hash.slice(1);
    if (viewEl(id) && id !== current()) show(id);
  });
  const deep = location.hash.length > 1 && viewEl("v-" + decodeURIComponent(location.hash.slice(1)));
  show(homeId());
  if (deep && deep.id !== homeId()) show(deep.id);
  compat();
  refreshProgress();
  // disc.autoplay (builder: Disk takılınca › Hemen oynat): the featured item's primary action, as if chosen.
  if (m.disc.autoplay && !deep) {
    const id = m.disc.featured ? "v-" + m.disc.featured : homeId();
    if (viewEl(id)) {
      if (id !== current()) show(id);
      $(".btn.primary", viewEl(id))?.click();
    }
  }
})();
