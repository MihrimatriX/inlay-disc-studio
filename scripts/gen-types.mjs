// Generates sdk/manifest.ts from the manifest schema. tests/contract.test.ts fails if the committed file is stale.
import { compileFromFile } from "json-schema-to-typescript";
import { writeFileSync } from "node:fs";

const aliases = `
/** Kısa ad */
export type DiscManifest = DiscStudioManifestV1;
export type DiscItem = Movie | Series | Album | Gallery | App | File;
`;

export async function generate() {
  return (await compileFromFile("schemas/manifest.schema.json")) + aliases;
}

if (process.argv[2] === "write") writeFileSync("sdk/manifest.ts", await generate());
