# XG's own format sources

Both files come from GameSite 2000's page
https://www.extremegammon.com/XGformat.aspx, read 2026-10-28
(halheinrich/backgammon#419). `XgContainerLayout`'s class doc cites them
beside xgdatatools.

- **`xg_format.pas`** — committed. "XG FILE FORMAT", last modified
  12/31/2013: the steps to read a `.xg` file, its four inner files, and the
  record types (`TSaveRec`, `TRolloutContext`, the save-file versions). Its
  header reads "This information can be freely redistributed".
- **`ZLIBArchive.pas`** — not committed; keep a local copy here, which
  `.gitignore` excludes. It is the archive unit `xg_format.pas` refers the
  container to: "ZLBArchive Control. Ver. 1.52". It carries no copyright or
  licence text, and the page states no terms for it, so the repo records
  only its name, version and source. Download it from the page above to
  read it.
