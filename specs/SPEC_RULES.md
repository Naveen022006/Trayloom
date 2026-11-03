# Replica spec rules

Goal: Trayloom must be an exact functional and visual replica of Vorssaint
(https://vorssaint.com) on Windows — same structure, screens, sections, controls,
labels, defaults, options and behaviour — except branding (never the Vorssaint
name, saturn logo or icon; use "Trayloom").

Source of truth: Swift sources in `E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint`
(UI under `UI/`, logic under `Services/`, strings under `Core/*Strings.swift` — use the
English strings verbatim), reference images in
`E:\REA TEST 1\vorssaint-mac\docs\assets\readme\` and `Resources/Images`, `Resources/Gifs`.
Current Windows port: `E:\REA TEST 1\trayloom\src` (read it to write the gap list).

Each spec file (`E:\REA TEST 1\trayloom\specs\<area>.md`) must contain, per screen/feature:
1. **Layout** — exact structure top to bottom: sections, rows, controls (type), icons
   (SF Symbol name → suggest a Segoe Fluent Icons code point), sizes/paddings/corner radii
   and colours where the code states them.
2. **Strings** — exact English labels, descriptions, placeholders, menu items.
3. **Settings** — every option: key, type, default, allowed values, what it changes.
4. **Behaviour** — interactions, states (empty/loading/error/permission missing), timing,
   animations, persistence.
5. **Windows mapping** — how to implement each piece on Windows (Win32/WinRT/.NET API),
   and anything truly impossible on Windows (say why, propose the closest equivalent).
6. **Gap vs Trayloom** — what the current Trayloom code has, lacks, or does differently.

Be precise and complete rather than short: implementation agents will build from your
spec without reading the Swift. Do not edit any code. Only write your spec file(s).
