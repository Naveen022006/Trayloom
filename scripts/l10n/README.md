# scripts/l10n

Regenerates `src/Trayloom.Core/Localization/Strings.<locale>.json` from Vorssaint's Swift string tables.

1. `harvest.py` reads `Sources/Vorssaint/Core/*Strings*.swift` (set `SRC`), builds English -> per-locale translations
   (printf placeholders become .NET `{0}`) and writes `harvest_raw.json`.
2. `build_tables.py` matches the English literals found in the Trayloom sources (`sites.json`: a list of
   `value, kind, ctx` objects extracted with Roslyn) against the harvest, applies the Mac -> Windows noun substitutions
   (`match.py` rules, `glossary.py` per language) and merges `extra.json` (hand-written translations of Trayloom-only text).

Python 3.10, standard library only. The literal extraction and the one-off source rewrite that introduced `Loc.T` were done with
a scratch Roslyn tool and are not part of the build.
