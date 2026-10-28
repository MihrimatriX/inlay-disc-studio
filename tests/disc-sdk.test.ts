import { describe, expect, test } from "vitest";
import { readFileSync } from "node:fs";
import { createSdk, DiscError, findMedia, relUrl, type Transport } from "../ui/disc-sdk/src/sdk";
import type { DiscManifest } from "../sdk/manifest";

const manifest = (name: string): DiscManifest => JSON.parse(readFileSync(`samples/manifest.${name}.json`, "utf8"));

function fakeHost() {
  const sent: any[] = [];
  let deliver: (m: any) => void = () => {};
  const transport: Transport = { send: (m) => sent.push(m), listen: (h) => (deliver = h) };
  return { sent, transport, reply: (m: any) => deliver(m) };
}

describe("disc sdk", () => {
  test("run rejects ids outside the allow-list without asking the launcher", async () => {
    const host = fakeHost();
    const sdk = createSdk({ manifest: manifest("bundle"), hosted: true }, host.transport);
    await expect(sdk.run("format-c")).rejects.toMatchObject({ code: "unknown-action" });
    expect(host.sent).toEqual([]);
  });

  test("run sends only the action id and resolves with the launcher reply", async () => {
    const host = fakeHost();
    const sdk = createSdk({ manifest: manifest("bundle"), hosted: true }, host.transport);
    const done = sdk.run("inst-game");
    expect(host.sent).toEqual([{ v: 1, req: 1, cmd: "run", id: "inst-game", opts: undefined }]);
    host.reply({ v: 1, res: 1, ok: true, data: { exitCode: 0 } });
    await expect(done).resolves.toEqual({ exitCode: 0 });
  });

  test("runInstalled sends only the app id; unhosted menus have no installed copy to start", async () => {
    const host = fakeHost();
    const sdk = createSdk({ manifest: manifest("bundle"), hosted: true }, host.transport);
    const done = sdk.runInstalled("ap-game");
    expect(host.sent).toEqual([{ v: 1, req: 1, cmd: "runInstalled", id: "ap-game", opts: undefined }]);
    host.reply({ v: 1, res: 1, ok: true, data: {} });
    await expect(done).resolves.toEqual({});
    await expect(createSdk({ manifest: manifest("bundle"), hosted: false }, null).runInstalled("ap-game")).rejects.toMatchObject({ code: "not-found" });
  });

  test("launcher errors become DiscError codes and replies are matched by req", async () => {
    const host = fakeHost();
    const sdk = createSdk({ manifest: manifest("bundle"), hosted: true }, host.transport);
    const a = sdk.run("inst-game");
    const b = sdk.run("inst-editor");
    host.reply({ v: 1, res: 2, ok: false, error: "cancelled" });
    host.reply({ v: 1, res: 1, ok: false, error: "busy" });
    await expect(b).rejects.toBeInstanceOf(DiscError);
    await expect(b).rejects.toMatchObject({ code: "cancelled" });
    await expect(a).rejects.toMatchObject({ code: "busy" });
  });

  test("events reach subscribers until they unsubscribe", () => {
    const host = fakeHost();
    const sdk = createSdk({ manifest: manifest("bundle"), hosted: true }, host.transport);
    const seen: any[] = [];
    const off = sdk.on("action-exited", (d) => seen.push(d));
    host.reply({ v: 1, event: "action-exited", data: { id: "inst-game", exitCode: 0 } });
    off();
    host.reply({ v: 1, event: "action-exited", data: { id: "inst-game", exitCode: 1 } });
    expect(seen).toEqual([{ id: "inst-game", exitCode: 0 }]);
  });

  test("messages from other protocol versions are ignored", () => {
    const host = fakeHost();
    const sdk = createSdk({ manifest: manifest("bundle"), hosted: true }, host.transport);
    const seen: any[] = [];
    sdk.on("visibility", (d) => seen.push(d));
    host.reply({ v: 2, event: "visibility", data: {} });
    expect(seen).toEqual([]);
  });

  test("libvlc media is played by the launcher", async () => {
    const m = manifest("mixed");
    const movie = m.items.find((i) => i.type === "movie")!;
    if (movie.type !== "movie") throw new Error();
    movie.media.playback.engine = "libvlc";
    const host = fakeHost();
    const sdk = createSdk({ manifest: m, hosted: true }, host.transport);
    const p = sdk.play(movie.id, { startSec: 30 });
    expect(host.sent[0]).toMatchObject({ cmd: "play", id: movie.id, opts: { startSec: 30 } });
    host.reply({ v: 1, res: 1, ok: true, data: { itemId: movie.id, engine: "libvlc", status: "playing" } });
    await expect(p).resolves.toMatchObject({ status: "playing" });
    expect(sdk.player.state()).toMatchObject({ engine: "libvlc" });
  });

  test("unhosted menu has no launcher to ask", async () => {
    const sdk = createSdk({ manifest: manifest("bundle"), hosted: false }, null);
    await expect(sdk.system()).rejects.toMatchObject({ code: "internal" });
  });
});

test("relUrl encodes each segment and keeps separators", () => {
  expect(relUrl("Müzik/01 Açılış #1.flac", true)).toBe("https://disc.local/M%C3%BCzik/01%20A%C3%A7%C4%B1l%C4%B1%C5%9F%20%231.flac");
  expect(relUrl("Film/a.mp4", false)).toBe("./Film/a.mp4");
});

test("findMedia resolves items, tracks and episodes", () => {
  const series = manifest("series");
  expect(findMedia(series, "sr-ofis")?.trackId).toBe("s1e1");
  expect(findMedia(series, "s1e1")?.itemId).toBe("sr-ofis");
  expect(findMedia(series, "nope")).toBeNull();
});
