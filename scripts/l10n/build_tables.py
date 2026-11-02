"""Build per-locale string tables keyed by the exact English text WinKnife uses.

Inputs : sites.json (literals of the WinKnife source), harvest_raw.json (Vorssaint tables)
Outputs: out/Strings.<locale>.json, out/matched.json, out/dropped.json
"""
import json, collections, re, os, sys
from match import build_index, key, strip_fmt
import glossary

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
os.makedirs(OUT, exist_ok=True)
LOCALES = ["pt-BR", "tr", "ru", "es", "sk", "de", "fr", "it", "ja", "ko", "uk", "zh-Hans", "zh-TW", "zh-HK"]

S = json.load(open(os.path.join(HERE, "sites.json"), encoding="utf-8"))
H = json.load(open(os.path.join(HERE, "harvest_raw.json"), encoding="utf-8"))["entries"]
extra = os.path.join(HERE, "extra_literals.json")  # additional keys (manual) {text: [..]}
idx = build_index(H)

PH = re.compile(r"\{(\d+)(?:,-?\d+)?(?::[^{}]*)?\}")


def placeholders(t):
    return set(int(m.group(1)) for m in PH.finditer(t))


def candidate_keys():
    out = {}
    for s in S:
        if s["kind"] == "interp":
            out[strip_fmt(s["value"])] = 1
        else:
            out[s["value"]] = 1
    return list(out)


PLATFORM_EN = re.compile(r"Finder|Dock|menu bar|Trash|trackpad|Vorssaint|macOS|\bMac", re.I)


def scan_platform_words(loc, key, tr):
    """Translations of texts that did not name a Mac thing in English may still do so (e.g. "the Mac lid"). Substitute or drop."""
    used = []
    if "Vorssaint" in tr:
        used.append(("APP", "WinKnife"))
    if "macOS" in tr:
        used.append(("OS", "Windows"))
    if re.search(r"\bMacs?\b", tr):
        used.append(("MAC", "PC"))
    if "Homebrew" in tr:
        used.append(("BREW", "winget"))
    for rid, repl in (("FINDER", "File Explorer"), ("DOCK", "taskbar"), ("BAR", "notification area"), ("TRASH", "Recycle Bin"), ("PAD", "touchpad")):
        g = glossary.G.get(rid, {}).get(loc)
        if g and g[1].search(tr) and not re.search(r"(?!)", ""):
            # the English key must not already be handled by the rule path (it then would not contain the macOS term)
            used.append((rid, repl))
    if not used:
        return tr
    return glossary.apply(loc, tr, tuple(used))


def main():
    keys = candidate_keys()
    tables = {l: {} for l in LOCALES}
    matched = {}
    dropped = collections.defaultdict(list)  # key -> [locales dropped by glossary]
    ambiguous = {}
    for k in keys:
        nk = key(k)
        if nk not in idx or len(k.strip()) < 2:
            continue
        cands = idx[nk]
        minrules = min(len(u) for _, u in cands)
        best = [(e, u) for e, u in cands if len(u) == minrules]
        # group by identical rule set
        used = best[0][1]
        entries = [e for e, u in best if u == used]
        matched[k] = {"rules": [r for r, _ in used], "types": sorted(set(e["type"] for e in entries)), "labels": sorted(set(e["label"] for e in entries))[:4]}
        kph = placeholders(k)
        for loc in LOCALES:
            cnt = collections.Counter()
            for e in entries:
                t = e["tr"].get(loc)
                if t:
                    cnt[t["t"]] += 1
            if not cnt:
                continue
            tr, c = cnt.most_common(1)[0]
            if len(cnt) > 1:
                ambiguous.setdefault(k, set()).add(loc)
            if used:
                tr2 = glossary.apply(loc, tr, used)
                if tr2 is None:
                    dropped[k].append(loc)
                    continue
                tr = tr2
            if not placeholders(tr) <= kph:
                dropped[k].append(loc + ":ph")
                continue
            # when the WinKnife English has no placeholders but the translation does -> drop
            tr3 = scan_platform_words(loc, k, tr)
            if tr3 is None:
                dropped[k].append(loc + ":scan")
                continue
            tr = tr3
            if tr == k:
                continue
            tables[loc][k] = tr
    ex = os.path.join(HERE, "extra.json")
    if os.path.exists(ex):
        for en, trs in json.load(open(ex, encoding="utf-8")).items():
            for loc, t in trs.items():
                tables[loc][en] = t
    for loc in LOCALES:
        with open(os.path.join(OUT, f"Strings.{loc}.json"), "w", encoding="utf-8") as f:
            json.dump(dict(sorted(tables[loc].items())), f, ensure_ascii=False, indent=0)
    json.dump(matched, open(os.path.join(OUT, "matched.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    json.dump(dropped, open(os.path.join(OUT, "dropped.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    print("keys", len(keys), "matched", len(matched), "ambiguous", len(ambiguous), "dropped keys", len(dropped))
    for loc in LOCALES:
        print(loc, len(tables[loc]))


if __name__ == "__main__":
    main()
