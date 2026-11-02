"""Match WinKnife literals to Vorssaint English entries, allowing platform-noun rules.

Rules transform the Vorssaint English into WinKnife English. Each rule has an id used later to transform
the translations per language (glossary.py).
"""
import json, re, itertools, collections

# (rule id, regex, replacement) applied on the English source. 'BAR' has several target spellings.
RULES = [
    ("APP", r"Vorssaint", "WinKnife"),
    ("OS", r"macOS", "Windows"),
    ("MAC", r"\bMac(?=s?\b|’s|'s)", "PC"),
    ("BREW", r"Homebrew", "winget"),
    ("DOCK", r"\bDock\b", "taskbar"),
    ("DOCK", r"\bDock\b", "Taskbar"),
    ("FINDER", r"\bFinder\b", "File Explorer"),
    ("TRASH", r"\bTrash\b", "Recycle Bin"),
    ("BAR", r"\bmenu bar\b", "notification area"),
    ("BAR", r"\bmenu bar\b", "tray"),
    ("BAR", r"\bmenu bar\b", "system tray"),
    ("BAR", r"\bmenu bar\b", "taskbar"),
    ("BAR", r"\bMenu bar\b", "Notification area"),
    ("BAR", r"\bMenu bar\b", "Tray"),
    ("PAD", r"\btrackpad\b", "touchpad"),
    ("PAD", r"\bTrackpad\b", "Touchpad"),
]


def norm(s):
    return (s.replace("’", "'").replace("‘", "'").replace("“", '"').replace("”", '"')
             .replace(" ", " ").replace("…", "…"))


_FMT = re.compile(r"\{(\d+)(?:,-?\d+)?(?::[^{}]*)?\}")


def strip_fmt(s):
    """Drop alignment/format specs of composite placeholders: {0:F1} -> {0}."""
    return _FMT.sub(lambda m: "{" + m.group(1) + "}", s)


def key(s):
    return strip_fmt(norm(s))


def variants(en):
    """All (text, frozenset(rule ids used), applied) variants of an English source."""
    out = {(en, ()): None}
    # for each rule id, the set of alternatives (regex, repl); optionally applied
    by_id = collections.OrderedDict()
    for rid, rx, rp in RULES:
        by_id.setdefault(rid, []).append((rx, rp))
    cur = [(en, ())]
    for rid, alts in by_id.items():
        nxt = list(cur)
        for text, used in cur:
            for rx, rp in alts:
                if re.search(rx, text):
                    nxt.append((re.sub(rx, rp, text), used + ((rid, rp),)))
        cur = nxt
    seen = {}
    for text, used in cur:
        k = (text, used)
        seen[k] = True
    return list(seen)


def build_index(entries):
    """key(variant text) -> list of (entry, used rules)"""
    idx = collections.defaultdict(list)
    for e in entries:
        for text, used in variants(e["en"]):
            idx[key(text)].append((e, used))
    return idx
