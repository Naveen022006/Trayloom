"""Per-language noun substitution for Windows-adapted strings.

apply(locale, text, used_rules) -> new text, or None when the substitution cannot be done reliably
(the string then falls back to English and is listed in specs/L10N_TODO.md).

used_rules: tuple of (rule id, English replacement) as produced by match.variants().
"""
import re

U = "’"  # typographic apostrophe


def S(pattern, repl, flags=0):
    return (re.compile(pattern, flags), repl)


# ---------------------------------------------------------------------------------------------------------
# Language independent
# ---------------------------------------------------------------------------------------------------------
def _generic(rid, text, loc):
    if rid == "APP":
        return text.replace("Vorssaint", "WinKnife")
    if rid == "OS":
        return text.replace("macOS", "Windows")
    if rid == "BREW":
        return text.replace("Homebrew", "winget")
    if rid == "MAC":
        t = re.sub(r"\bMac(?=s\b)", "PC", text)  # "Macs" -> "PCs"
        t = re.sub(r"\bMac\b", "PC", t)
        return t
    return None


# ---------------------------------------------------------------------------------------------------------
# Language specific: each entry is a list of (compiled regex, replacement) tried in order, then a "leftover"
# regex: if it still matches afterwards the substitution is rejected.
# ---------------------------------------------------------------------------------------------------------
G = {}


def add(rule, loc, subs, leftover):
    G.setdefault(rule, {})[loc] = (subs, re.compile(leftover))


# ----- TRASH -> Recycle Bin
add("TRASH", "de", [], r"(?!)")  # Papierkorb is the Windows word too
add("TRASH", "fr", [], r"(?!)")
add("TRASH", "es", [S(r"\bPapelera\b(?! de reciclaje)", "Papelera de reciclaje")], r"(?!)")
add("TRASH", "it", [], r"(?!)")
add("TRASH", "pt-BR", [S(r"\bo Lixo\b", "a Lixeira"), S(r"\bpara Lixo\b", "para a Lixeira")], r"\b(?:o|no|do|para o|ao|pelo|para) Lixo\b")
add("TRASH", "tr", [
    S(r"Çöp Sepeti[’'](ne|na)", "Geri Dönüşüm Kutusu’na"), S(r"Çöp Sepeti[’']ni", "Geri Dönüşüm Kutusu’nu"),
    S(r"Çöp Sepeti[’']ndeki", "Geri Dönüşüm Kutusu’ndaki"), S(r"Çöp Sepetine", "Geri Dönüşüm Kutusuna"),
    S(r"Çöp Sepetini", "Geri Dönüşüm Kutusunu"), S(r"Çöp Sepeti(?![’'\w])", "Geri Dönüşüm Kutusu")], r"Çöp Sepet")
add("TRASH", "ru", [], r"(?!)")
add("TRASH", "uk", [S("Смітник", "Кошик")], r"Смітн")
add("TRASH", "sk", [], r"(?!)")
add("TRASH", "ja", [], r"(?!)")
add("TRASH", "ko", [], r"(?!)")
add("TRASH", "zh-Hans", [S("废纸篓", "回收站")], "废纸篓")
add("TRASH", "zh-TW", [S("垃圾桶", "資源回收筒")], "垃圾桶")
add("TRASH", "zh-HK", [S("垃圾桶", "資源回收筒")], "垃圾桶")

# ----- FINDER -> File Explorer
add("FINDER", "de", [S(r"\bFinder\b", "Datei-Explorer")], r"Finder")
add("FINDER", "es", [S(r"\bFinder\b", "Explorador de archivos")], r"Finder")
add("FINDER", "pt-BR", [S(r"\bFinder\b", "Explorador de Arquivos")], r"Finder")
add("FINDER", "fr", [
    S(r"\b[Ll]e Finder\b", "l’Explorateur de fichiers"), S(r"\bdu Finder\b", "de l’Explorateur de fichiers"),
    S(r"\bau Finder\b", "à l’Explorateur de fichiers"), S(r"\bFinder\b", "Explorateur de fichiers")], r"Finder")
add("FINDER", "it", [
    S(r"\bnel Finder\b", "in Esplora file"), S(r"\bdel Finder\b", "di Esplora file"), S(r"\bal Finder\b", "a Esplora file"),
    S(r"\bdal Finder\b", "da Esplora file"), S(r"\b[Ii]l Finder\b", "Esplora file"), S(r"\bFinder\b", "Esplora file")], r"Finder")
add("FINDER", "tr", [
    S(r"Finder[’']da\b", "Dosya Gezgini’nde"), S(r"Finder[’']dan\b", "Dosya Gezgini’nden"),
    S(r"Finder[’']ı\b", "Dosya Gezgini’ni"), S(r"\bFinder(?![’'\w])", "Dosya Gezgini")], r"Finder")
add("FINDER", "ru", [
    S(r"\b(в|В|во) Finder\b", r"\1 Проводнике"), S(r"\b(из|Из) Finder\b", r"\1 Проводника"),
    S(r"^Finder (?=[а-я])", "Проводник "),
    S(r"\b(Автоматизация|Сочетания|Только) Finder\b", lambda m: m.group(1) + (" Проводник" if m.group(1) == "Только" else " Проводника"))], r"Finder")
add("FINDER", "uk", [
    S(r"\b([Уу]|[Вв]) Finder\b", r"\1 Провіднику"), S(r"^Finder (?=[а-яіїє])", "Провідник "),
    S(r"\b(Автоматизація|Клавіатурні скорочення|Лише) Finder\b", lambda m: m.group(1) + (" Провідник" if m.group(1) == "Лише" else " Провідника"))], r"Finder")
add("FINDER", "sk", [
    S(r"\bvo Finderi\b", "v Prieskumníkovi"), S(r"\bFindera\b", "Prieskumníka"), S(r"\bFinderu\b", "Prieskumníka"),
    S(r"\bFinder\b", "Prieskumník")], r"Finder")
add("FINDER", "ja", [S(r" ?Finder ?", "エクスプローラー")], r"Finder")
add("FINDER", "ko", [S(r"Finder", "파일 탐색기")], r"Finder")
add("FINDER", "zh-Hans", [S(r" ?(Finder|访达) ?", "文件资源管理器")], r"Finder|访达")
add("FINDER", "zh-TW", [S(r" ?Finder ?", "檔案總管")], r"Finder")
add("FINDER", "zh-HK", [S(r" ?Finder ?", "檔案總管")], r"Finder")

# ----- DOCK -> taskbar
add("DOCK", "de", [
    S(r"\baufs Dock-", "auf das Taskleisten-"), S(r"\bim Dock\b", "in der Taskleiste"), S(r"\baufs Dock\b", "auf die Taskleiste"), S(r"\bauf dem Dock\b", "auf der Taskleiste"),
    S(r"^Dock$", "Taskleiste"), S(r"Dock-(?=[A-ZÄÖÜ])", "Taskleisten-"), S(r"(?<=[Ff]enster und )Dock\b", "Taskleiste"), S(r"\bDock\b(?= öffnen)", "Taskleiste")], r"\bDock")
add("DOCK", "fr", [S(r"^Dock$", "Barre des tâches"), 
    S(r"\b[Ll]e Dock\b", "la barre des tâches"), S(r"\bdu Dock\b", "de la barre des tâches"), S(r"\bau Dock\b", "à la barre des tâches"),
    S(r"(?<=et )Dock\b", "barre des tâches")], r"\bDock")
add("DOCK", "es", [S(r"^Dock$", "Barra de tareas"), 
    S(r"\b[Ee]l Dock\b", "la barra de tareas"), S(r"\bdel Dock\b", "de la barra de tareas"), S(r"\bal Dock\b", "a la barra de tareas"),
    S(r"(?<= y )Dock\b", "barra de tareas")], r"\bDock")
add("DOCK", "it", [S(r"^Dock$", "Barra delle applicazioni"), 
    S(r"\bil Dock\b", "la barra delle applicazioni"), S(r"\bIl Dock\b", "La barra delle applicazioni"),
    S(r"\bsul Dock\b", "sulla barra delle applicazioni"), S(r"\bnel Dock\b", "nella barra delle applicazioni"),
    S(r"\bdal Dock\b", "dalla barra delle applicazioni"), S(r"\bdel Dock\b", "della barra delle applicazioni"),
    S(r"\bal Dock\b", "alla barra delle applicazioni"), S(r"(?<= e )Dock\b", "barra delle applicazioni")], r"\bDock")
add("DOCK", "pt-BR", [S(r"^Dock$", "Barra de tarefas"), 
    S(r"\bo Dock\b", "a barra de tarefas"), S(r"\bno Dock\b", "na barra de tarefas"), S(r"\bdo Dock\b", "da barra de tarefas"),
    S(r"\bao Dock\b", "à barra de tarefas"), S(r"\bpelo Dock\b", "pela barra de tarefas"), S(r"(?<= e )Dock\b", "barra de tarefas")], r"\bDock")
add("DOCK", "tr", [S(r"^Dock$", "Görev çubuğu"), 
    S(r"Dock[’']tan\b", "Görev çubuğundan"), S(r"Dock[’']ta\b", "Görev çubuğunda"), S(r"Dock[’']u\b", "Görev çubuğunu"),
    S(r"Dock[’']a\b", "Görev çubuğuna"), S(r"\bDock(?=\s)", "Görev çubuğu"), S(r"\bve Dock\b", "ve Görev çubuğu")], r"Dock")
add("DOCK", "ru", [S(r"^Dock$", "Панель задач"), S(r"\bпо Dock\b", "по панели задач"), S(r"\bна Dock\b", "на панель задач"),
                   S(r"\b[вВ] Dock\b", "на панели задач"), S(r"\bиз Dock\b", "с панели задач")], r"Dock")
add("DOCK", "uk", [S(r"^Dock$", "Панель завдань"), S(r"\bпо Dock\b", "по панелі завдань"), S(r"\bна Dock\b", "на панель завдань"),
                   S(r"\b[вВ] Dock\b", "на панелі завдань"), S(r"\bз Dock\b", "з панелі завдань")], r"Dock")
add("DOCK", "sk", [S(r"^Dock$", "Panel úloh"), S(r"\bv Docku\b", "na paneli úloh"), S(r"\bna Dock\b", "na panel úloh")], r"Dock")
add("DOCK", "ja", [S(r" ?Dock ?", "タスクバー")], r"Dock")
add("DOCK", "ko", [S(r"Dock으로", "작업 표시줄로"), S(r"Dock", "작업 표시줄")], r"Dock")
add("DOCK", "zh-Hans", [S("程序坞", "任务栏")], "程序坞|Dock")
add("DOCK", "zh-TW", [S(r" ?Dock ?", "工作列")], r"Dock")
add("DOCK", "zh-HK", [S(r" ?Dock ?", "工作列")], r"Dock")

# ----- BAR (menu bar) -> notification area / tray ; target "taskbar" is handled with the DOCK table of the language
_BAR = {
    "de": ("Infobereich", [
        S(r"\b[Ii]n der Menüleiste\b", "im Infobereich"), S(r"\bauf der Menüleiste\b", "im Infobereich"),
        S(r"Menüleistensymbol", "Infobereichssymbol"), S(r"Unter dem Menüleistensymbol", "Unter dem Infobereichssymbol")], r"Menüleiste"),
    "fr": ("zone de notification", [
        S(r"\bdans la barre des menus\b", "dans la zone de notification"), S(r"\bde la barre des menus\b", "de la zone de notification"),
        S(r"\bsur la barre des menus\b", "dans la zone de notification"), S(r"\bla barre des menus\b", "la zone de notification"),
        S(r"\bBarre des menus\b", "Zone de notification"), S(r"\bbarre des menus\b", "zone de notification")], r"barre des menus"),
    "es": ("área de notificación", [
        S(r"\ben la barra de menús\b", "en el área de notificación"), S(r"\bde la barra de menús\b", "del área de notificación"),
        S(r"\ben la barra\b", "en el área de notificación"), S(r"\bla barra de menús\b", "el área de notificación"),
        S(r"\bBarra de menús\b", "Área de notificación"), S(r"\bbarra de menús\b", "área de notificación")], r"barra de menús"),
    "it": ("area di notifica", [
        S(r"\bnella barra dei menu\b", "nell’area di notifica"), S(r"\bsulla barra dei menu\b", "nell’area di notifica"),
        S(r"\bdella barra dei menu\b", "dell’area di notifica"), S(r"\bla barra dei menu\b", "l’area di notifica"),
        S(r"\bBarra dei menu\b", "Area di notifica"), S(r"\bbarra dei menu\b", "area di notifica")], r"barra dei menu"),
    "pt-BR": ("área de notificação", [
        S(r"\bna barra de menus\b", "na área de notificação"), S(r"\bda barra de menus\b", "da área de notificação"),
        S(r"\ba barra de menus\b", "a área de notificação"), S(r"\bBarra de menus\b", "Área de notificação"),
        S(r"\bbarra de menus\b", "área de notificação")], r"barra de menus"),
    "tr": ("bildirim alanı", [
        S(r"[Mm]enü çubuğunda\b", "Bildirim alanında"), S(r"[Mm]enü çubuğu simgesin(in|e|i)", lambda m: "Bildirim alanı simgesi" + {"in": "nin", "e": "ne", "i": "ni"}[m.group(1)]),
        S(r"\bmenü çubuğu\b", "bildirim alanı"), S(r"\bMenü çubuğu\b", "Bildirim alanı")], r"[Mm]enü çubuğ"),
    "ru": ("область уведомлений", [
        S(r"\bстроке меню\b", "области уведомлений"), S(r"\bстроку меню\b", "область уведомлений"), S(r"\bстрока меню\b", "область уведомлений"),
        S(r"\bстроки меню\b", "области уведомлений"), S(r"\bСтрока меню\b", "Область уведомлений")], r"[Сс]трок\w+ меню"),
    "uk": ("область сповіщень", [
        S(r"\bсмузі меню\b", "області сповіщень"), S(r"\bсмугу меню\b", "область сповіщень"), S(r"\bсмуга меню\b", "область сповіщень"),
        S(r"\bсмуги меню\b", "області сповіщень"), S(r"\bСмуга меню\b", "Область сповіщень")], r"[Сс]муг\w+ меню"),
    "sk": ("oblasť oznámení", [
        S(r"\bv lište s ponukami\b", "v oblasti oznámení"), S(r"\bna lištu s ponukami\b", "do oblasti oznámení"),
        S(r"\bLišta s ponukami\b", "Oblasť oznámení")], r"[Ll]išt\w* s ponukami|\bv lište\b"),
    "ja": ("通知領域", [S(r"メニューバー", "通知領域")], r"メニューバー"),
    "ko": ("알림 영역", [S(r"메뉴 막대", "알림 영역")], r"메뉴 막대"),
    "zh-Hans": ("通知区域", [S(r"菜单栏", "通知区域")], r"菜单栏"),
    "zh-TW": ("通知區域", [S(r"選單列", "通知區域")], r"選單列"),
    "zh-HK": ("通知區域", [S(r"選單列", "通知區域")], r"選單列"),
}
for _l, (_t, _s, _lo) in _BAR.items():
    add("BAR", _l, _s, _lo)

# ----- PAD (trackpad) -> touchpad
_PAD = {
    "de": [S(r"\bTrackpad\b", "Touchpad")], "fr": [S(r"\btrackpad\b", "pavé tactile"), S(r"\bTrackpad\b", "Pavé tactile")],
    "es": [S(r"\btrackpad\b", "panel táctil")], "it": [S(r"\btrackpad\b", "touchpad")], "pt-BR": [S(r"\bTrackpad\b", "Touchpad"), S(r"\btrackpad\b", "touchpad")],
    "tr": [S(r"izleme dörtgeniyle", "dokunmatik yüzeyle"), S(r"izleme dörtgeni", "dokunmatik yüzey")],
    "ru": [S(r"\bтрекпаде\b", "тачпаде"), S(r"\bтрекпад\b", "тачпад")], "uk": [S(r"\bтрекпеді\b", "тачпаді"), S(r"\bтрекпед\b", "тачпад")],
    "sk": [S(r"\btrackpade\b", "touchpade"), S(r"\btrackpad\b", "touchpad")], "ja": [S("トラックパッド", "タッチパッド")],
    "ko": [S("트랙패드", "터치패드")], "zh-Hans": [S("触控板", "触摸板")], "zh-TW": [S("觸控式軌跡板", "觸控板")], "zh-HK": [S("觸控式軌跡板", "觸控板")],
}
_PADLEFT = {"de": "[Tt]rackpad", "fr": "[Tt]rackpad", "es": "trackpad", "it": "trackpad", "pt-BR": "[Tt]rackpad", "tr": "izleme dörtgen",
            "ru": "трекпад", "uk": "трекпед", "sk": "trackpad", "ja": "トラックパッド", "ko": "트랙패드", "zh-Hans": "(?!)", "zh-TW": "(?!)", "zh-HK": "(?!)"}
for _l, _s in _PAD.items():
    add("PAD", _l, _s, _PADLEFT[_l])


def apply(loc, text, used):
    """Transform a Vorssaint translation into its WinKnife counterpart. Returns None when not reliable."""
    for rid, repl in used:
        g = _generic(rid, text, loc)
        if g is not None:
            text = g
            continue
        if rid == "BAR" and repl == "taskbar":
            rid = "DOCK"  # "menu bar" became "taskbar": the wording of the taskbar table needs an article-aware phrase; unsupported
            return None
        table = G.get(rid, {}).get(loc)
        if table is None:
            return None
        subs, leftover = table
        for rx, rp in subs:
            text = rx.sub(rp, text)
        if leftover.search(text):
            return None
        if rid == "BAR" and repl == "tray":
            pass  # "tray" and "notification area" share the term
    return text
