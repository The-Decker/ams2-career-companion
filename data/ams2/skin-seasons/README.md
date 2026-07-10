# Skin-season pointer library

Per season key, the ACTIVE override pointer XMLs (`<model>.xml`) extracted from that season's
community skin pack. Two season packs for the same car model collide ONLY on this one file —
each pack keeps its textures in its own subfolder (`TAMS2SP\`, `F1_1985\`, `SMGP\`, …), so all
seasons' textures coexist on disk and swapping seasons is swapping these small pointer files.
The in-app Skin Season Manager (`SkinSeasonManager`) writes them backup-first; a pack opts in
with `pack.json` `skinSeason: "<key>"`.

Conflicting families covered:

| Family (car models)                       | Seasons              |
| ----------------------------------------- | -------------------- |
| formula_retro, _v12, _v8, lotus_72e, mclaren_m23 | f1-1974 · f1-1975 |
| formula_retro_g3, _te, mclaren_mp4_1c (+brabham_bt52 1983-only) | f1-1983 · f1-1985 |
| formula_classic_g3m1–m4 (+mclaren_mp45b 1990-only) | f1-1990 · smgp |
| formula_v10_g1, mclaren_mp4_12            | f1-1996 · f1-1997    |
| formula_reiza, mercedes_amg_sc            | f1-2010 · f1-2012    |

Provenance: extracted from the community skin packs in Mike's `Z:\SKINS 4 AI MUCH LOVE`
directory (2026-07-10) — 1974/1975 F-Retro-G1 packs, TAMS2SP 1983 V2-2, Klukkluk F1_1985,
IMG 1990 v1.4, SMGP SKINS V1 (rafaelcsanti), 1996HC/1997HC, 2010HC, IMG 2012 v2.08. Pointer
XMLs only (small text files); textures are never committed. `*_dist.xml` distribution templates
are deliberately not included (the game never reads them).
