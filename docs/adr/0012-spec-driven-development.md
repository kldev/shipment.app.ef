# 0012. Spec-driven development

Status: accepted (2026-10-29)

## Decision

- The product lives in the local DC wiki as an SDD project "Shipment" (goals with "Done when", non-goals, decisions,
  stages Shape → MVP → Features → Retro, gates approved by the author in its UI at http://127.0.0.1:9494/wiki/sdd).
  `PRODUCT.md` in the repository mirrors it.
- Each feature is a spec there (numbered, with acceptance criteria and a verify command) and an agent prompt in
  `.instructions/NNN-slug.md` (skill agent-prompt) that a session implements.
- Done = the spec's criteria are Passed with evidence (`./verify <group>`, screenshots in `docs/screens/`).
