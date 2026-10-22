// Builder UI helpers: the capacity ruler's scale and the sizes it prints (design/screens/02 numbers).
import { describe, expect, it } from "vitest";
import { gb, NOMINAL, remaining, rulerPos, size } from "../ui/builder/src/format";
import tr from "../i18n/tr-TR.json";
import en from "../i18n/en-US.json";

describe("capacity ruler", () => {
  it("puts the marks where the design does", () => {
    expect((rulerPos(NOMINAL.cd700) * 100).toFixed(1)).toBe("8.2");
    expect((rulerPos(NOMINAL.dvd5) * 100).toFixed(1)).toBe("55.3");
    expect((rulerPos(4.4e9) * 100).toFixed(1)).toBe("51.8");
    expect(rulerPos(20e9)).toBe(1);
  });

  it("prints decimal units the Turkish way", () => {
    expect(size(4.4e9)).toBe("4,40 GB");
    expect(size(86e6)).toBe("86 MB");
    expect(size(84e3)).toBe("84 KB");
    expect(gb(0.6e9)).toBe("0,6 GB");
  });

  it("says how long is left", () => {
    expect(remaining(30)).toBe("bir dakikadan az kaldı");
    expect(remaining(230)).toBe("yaklaşık 4 dakika kaldı");
    expect(remaining(NaN)).toBe("");
  });
});

describe("builder text", () => {
  it("has every key in both languages", () => {
    const keys = (t: Record<string, string>) => Object.keys(t).filter((k) => k.startsWith("builder.")).sort();
    expect(keys(en)).toEqual(keys(tr));
    expect(keys(tr).length).toBeGreaterThan(150);
  });
});
