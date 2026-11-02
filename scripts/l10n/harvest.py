#!/usr/bin/env python3
"""Harvest Vorssaint's Swift string tables into  english -> {locale: translation}.

Reads every `static let <lang> = <Type>( label: "..." , ... )` in Core/*.swift.
Output: harvest.json  {
   "entries": [ {"type": T, "label": L, "en": "...", "tr": {locale: text}} ... ],
   "stats": {...}
}
Placeholders are converted from Swift printf form to .NET composite form.
"""
import json, re, sys, os, collections

SRC = r"E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint"
OUT = os.path.dirname(os.path.abspath(__file__))
LANGS = {  # swift member -> locale tag used by WinKnife (Vorssaint raw value)
    "enUS": "en-US", "ptBR": "pt-BR", "tr": "tr", "ru": "ru", "es": "es", "sk": "sk", "de": "de",
    "fr": "fr", "it": "it", "ja": "ja", "ko": "ko", "uk": "uk", "zhHans": "zh-Hans", "zhTW": "zh-TW", "zhHK": "zh-HK",
}

DECL = re.compile(r"static\s+let\s+(%s)\s*=\s*(\w+)\s*\(" % "|".join(LANGS))

ESC = {"n": "\n", "t": "\t", "r": "\r", "0": "\0", "\\": "\\", '"': '"', "'": "'"}


def parse_string(s, i):
    """s[i] == '"'. Returns (value, next_index, interp_count). Interpolations become {§k§}."""
    multi = s.startswith('"""', i)
    q = '"""' if multi else '"'
    i += len(q)
    out = []
    interp = 0
    while True:
        if s.startswith(q, i):
            i += len(q)
            break
        c = s[i]
        if c == "\\":
            n = s[i + 1]
            if n == "(":
                depth = 1
                j = i + 2
                while depth:
                    if s[j] == "(":
                        depth += 1
                    elif s[j] == ")":
                        depth -= 1
                    elif s[j] == '"':
                        _, j, _ = parse_string(s, j)
                        continue
                    j += 1
                out.append("{§%d§}" % interp)
                interp += 1
                i = j
                continue
            if n == "u":
                m = re.match(r"\\u\{([0-9a-fA-F]+)\}", s[i:])
                out.append(chr(int(m.group(1), 16)))
                i += m.end()
                continue
            out.append(ESC.get(n, n))
            i += 2
            continue
        out.append(c)
        i += 1
    v = "".join(out)
    if multi:
        # drop leading newline and trailing indentation line
        lines = v.split("\n")
        if lines and lines[0] == "":
            lines = lines[1:]
        if lines:
            indent = re.match(r"\s*", lines[-1]).group(0) if lines[-1].strip() == "" else ""
            lines = lines[:-1] if lines[-1].strip() == "" else lines
            lines = [l[len(indent):] if l.startswith(indent) else l for l in lines]
        v = "\n".join(lines)
    return v, i, interp


def skip_ws(s, i):
    while i < len(s):
        if s[i] in " \t\r\n":
            i += 1
        elif s.startswith("//", i):
            while i < len(s) and s[i] != "\n":
                i += 1
        elif s.startswith("/*", i):
            i = s.index("*/", i) + 2
        else:
            break
    return i


def parse_args(s, i):
    """s[i] just after '('. Returns (dict label->(value|None), next_index)."""
    args = collections.OrderedDict()
    while True:
        i = skip_ws(s, i)
        if s[i] == ")":
            return args, i + 1
        m = re.compile(r"(\w+)\s*:\s*").match(s, i)
        if not m:
            raise ValueError("bad arg at %r" % s[i:i + 60])
        label = m.group(1)
        i = m.end()
        parts = []
        ok = True
        while True:
            i = skip_ws(s, i)
            if s[i] == '"':
                v, i, interp = parse_string(s, i)
                parts.append(v)
            else:
                ok = False
                # skip a non-literal expression until top-level comma/paren
                depth = 0
                while True:
                    c = s[i]
                    if c == '"':
                        _, i, _ = parse_string(s, i)
                        continue
                    if c in "([{":
                        depth += 1
                    elif c in ")]}":
                        if depth == 0:
                            break
                        depth -= 1
                    elif c == "," and depth == 0:
                        break
                    i += 1
                break
            i = skip_ws(s, i)
            if s[i] == "+":
                i += 1
                continue
            break
        i = skip_ws(s, i)
        args[label] = "".join(parts) if ok else None
        if s[i] == ",":
            i += 1
        elif s[i] == ")":
            continue
        else:
            raise ValueError("expected , or ) at %r" % s[i:i + 60])


PCT = re.compile(r"%(?:(\d+)\$)?([-+ 0#]*)(\d*)(?:\.(\d+))?(l{0,2}|h{0,2}|q|z)?([@dDiuUfFeEgGsScCxXoOp%])")


def to_net(text):
    """Swift/printf placeholders -> .NET composite. Returns (text, n_placeholders)."""
    # escape braces that are literal
    text = text.replace("{", "{{").replace("}", "}}")
    text = re.sub(r"\{\{§(\d+)§\}\}", lambda m: "{" + m.group(1) + "}", text)
    n = [0]
    maxidx = [-1]

    def rep(m):
        pos, flags, width, prec, length, conv = m.groups()
        if conv == "%":
            return "%"
        if pos:
            idx = int(pos) - 1
        else:
            idx = n[0]
            n[0] += 1
        maxidx[0] = max(maxidx[0], idx)
        fmt = ""
        if conv in "fFeEgG":
            fmt = ":F%s" % (prec if prec is not None else "6")
        elif conv in "dDiuU" and width and "0" in (flags or "") + width[:1]:
            fmt = ":D%d" % int(width)
        return "{%d%s}" % (idx, fmt)

    text = PCT.sub(rep, text)
    return text, maxidx[0] + 1


def harvest():
    tables = collections.defaultdict(lambda: collections.defaultdict(dict))  # (type) -> lang -> {label: val}
    nonliteral = collections.Counter()
    files = []
    for root, _, fs in os.walk(SRC):
        for f in fs:
            if f.endswith(".swift"):
                files.append(os.path.join(root, f))
    for path in files:
        s = open(path, encoding="utf-8").read()
        for m in DECL.finditer(s):
            lang, typ = m.group(1), m.group(2)
            try:
                args, _ = parse_args(s, m.end())
            except Exception as ex:  # report and continue
                print("PARSE FAIL", path, typ, lang, ex, file=sys.stderr)
                continue
            for label, v in args.items():
                if v is None:
                    nonliteral[(typ, label, lang)] += 1
                    continue
                tables[typ][lang][label] = v
        # switch form:  case .de, .fr: return Type(label: "...", ...)   (inside static func name(_ language:))
        for m in re.finditer(r"case\s+((?:\.\w+\s*,\s*)*\.\w+)\s*:\s*return\s+(\.?\w+)\(", s):
            langs = re.findall(r"\.(\w+)", m.group(1))
            if not all(l in LANGS for l in langs):
                continue
            typ = m.group(2)
            if typ in ("OperationalStrings",) or (typ[0].islower() and typ != ".init"):
                continue
            if typ in (".init", "Self"):
                typ = "anon"
            fn = None
            for fm in re.finditer(r"func\s+(\w+)\s*\(", s[:m.start()]):
                fn = fm.group(1)
            try:
                args, _ = parse_args(s, m.end())
            except Exception as ex:
                print("PARSE FAIL switch", path, typ, langs, ex, file=sys.stderr)
                continue
            key = typ + "@" + (fn or "")
            for lang in langs:
                for label, v in args.items():
                    if v is None:
                        nonliteral[(key, label, lang)] += 1
                        continue
                    tables[key][lang][label] = v
        if path.endswith("WhatsAppDownloadStrings.swift"):
            SHORT = {"hub": "hubDescription", "types": "fileTypes", "all": "allTypes", "days": "daysFormat",
                     "manage": "manageAgain", "never": "neverRun", "future": "futureOnly",
                     "existing": "includeExisting", "trash": "trashNote"}
            for m in re.finditer(r"static\s+let\s+(\w+)\s*=\s*translated\(", s):
                lang = m.group(1)
                args, _ = parse_args(s, m.end())
                for label, v in args.items():
                    if v is not None:
                        tables["WhatsAppDownloadStrings"][lang][SHORT.get(label, label)] = v
            for m in re.finditer(r"case\s+\.(\w+)(?:,\s*\.\w+)*:\s*return\s+OperationalStrings\(", s):
                lang = m.group(1)
                args, _ = parse_args(s, m.end())
                for label, v in args.items():
                    if v is not None:
                        tables["WhatsAppDownloadStrings"][lang][label] = v
    return tables, nonliteral


def main():
    tables, nonliteral = harvest()
    entries = []
    missing = collections.Counter()
    for typ, langs in sorted(tables.items()):
        en = langs.get("enUS")
        if not en:
            print("no enUS for", typ, file=sys.stderr)
            continue
        for label, en_text in en.items():
            tr = {}
            en_net, en_n = to_net(en_text)
            for lang, loc in LANGS.items():
                if lang == "enUS":
                    continue
                v = langs.get(lang, {}).get(label)
                if v is None and lang == "zhHK":
                    v = langs.get("zhTW", {}).get(label)
                if v is None:
                    missing[loc] += 1
                    continue
                t, n = to_net(v)
                tr[loc] = {"t": t, "n": n}
            entries.append({"type": typ, "label": label, "en": en_net, "en_n": en_n, "tr": tr})
    json.dump({"entries": entries}, open(os.path.join(OUT, "harvest_raw.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    print("types", len(tables), "entries", len(entries), "non-literal", sum(nonliteral.values()))
    print("missing per locale", dict(missing))
    cnt = collections.Counter()
    for e in entries:
        for loc in e["tr"]:
            cnt[loc] += 1
    print({k: v for k, v in cnt.items()})


if __name__ == "__main__":
    main()
