# Docs

- Describe the current behavior only, without the history of earlier versions.
- The `README.md` next to each packable project is its nuget.org page (absolute links, no mermaid): keep their examples in sync with these docs when the public API changes.
- `Comparison.md`: the numbers of its tables are colored by `color-tables.py` (math markup, GitHub strips `style`): edit the numbers, then run `python3 docs/color-tables.py docs/Comparison.md` (it removes its own colors first). The rows are the medians of three runs of the interop benchmarks (see the page).
- `Compatibility.md` documents the settings per library from `LsMsgPackInteropTests`: keep both in sync.
- `Wizard/` is a decision tree for beginners (questions `Files*.md`/`Network*.md`, setups `Setup-*.md`, `Extras.md`): its setups repeat packages, settings and code of the other docs, so update them too when those change.
