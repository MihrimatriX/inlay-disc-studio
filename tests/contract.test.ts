import { describe, expect, test } from "vitest";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { readFileSync, readdirSync } from "node:fs";
import { generate } from "../scripts/gen-types.mjs";

const load = (p: string) => JSON.parse(readFileSync(p, "utf8"));
const ajv = new Ajv2020({ allErrors: true, strict: false });
addFormats(ajv);
const validateManifest = ajv.compile(load("schemas/manifest.schema.json"));
const validateTheme = ajv.compile(load("schemas/theme.schema.json"));

describe("samples", () => {
  for (const f of readdirSync("samples")) {
    test(f, () => {
      const validate = f.startsWith("theme.") ? validateTheme : validateManifest;
      expect(validate(load(`samples/${f}`)), JSON.stringify(validate.errors)).toBe(true);
    });
  }
});

describe("manifest rejects", () => {
  const base = () => load("samples/manifest.bundle.json");

  test.each(["../x.exe", "/Setup/x.exe", "C:/x.exe", "a/../../x.exe", "http://evil/x.exe"])(
    "relPath %s",
    (target) => {
      const m = base();
      m.actions[0].target = target;
      expect(validateManifest(m)).toBe(false);
    },
  );

  test("non-https url action", () => {
    const m = base();
    m.actions.push({ id: "web", label: "Site", kind: "url", target: "http://example.com" });
    expect(validateManifest(m)).toBe(false);
  });

  test("unknown field", () => {
    const m = base();
    m.items[0].surprise = true;
    expect(validateManifest(m)).toBe(false);
  });
});

test("sdk/manifest.ts is up to date with the schema", async () => {
  expect(readFileSync("sdk/manifest.ts", "utf8")).toBe(await generate());
});
