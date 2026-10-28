# 0001 — Contract models are hand-written records, verified against the schema

**Status:** accepted (M0)

C# models in `Inlay.Core` are hand-written `record`s instead of NJsonSchema output. NJsonSchema targets
draft-04/07 and does not model `$defs` + `unevaluatedProperties` + `oneOf` discriminators cleanly, and its
output is not System.Text.Json source-generator (NativeAOT) friendly without heavy post-editing.

Drift is prevented by tests, not by generation: every `samples/*.json` must deserialize (unknown fields,
missing required fields and unknown enum values throw), serialize back to a JSON-equal document, and the
output must validate against the schema (`ContractTests`). TS types stay generated (`npm run gen:types`)
and a test fails if `sdk/manifest.ts` is stale.

Optional fields are nullable with no defaults so round-trips are lossless; schema defaults are applied by consumers.
