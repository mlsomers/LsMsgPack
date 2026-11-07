#!/usr/bin/env python3
"""Colors the numbers of the benchmark tables in a Markdown file (docs/Comparison.md) by how good they are.

Per column (per row for a table whose first header cell is "Target": its libraries are the columns), the best value
(the lowest: sizes and times) gets rgb(0,150,0) and bold, the worst rgb(150,0,0), the others their place on the straight line
between them: t = (value - best) / (worst - best), rgb(150t, 150(1-t), 0), so the middle is (75,75,0) (black in the middle
was unreadable on GitHub's dark theme, a path through yellow on the light one).
A cell with two values ("117 / 188 ns") is two columns. Rows starting with "System.Text.Json" (the baseline) and section rows
("**Arrays**") are left as they are, and are not part of the scale.

GitHub removes style attributes from Markdown, so the colors are math: $\\color{#009600}\\text{5.05 ms}$ (GitHub and the
VS Code preview render it), \textbf for the best: \textsf{\textbf{}} needs MathJax's textmacros, which GitHub may not load.
Rerun it after changing numbers: it removes its own colors first.

Usage: python3 docs/color-tables.py docs/Comparison.md
"""
import re
import sys

COLORED = re.compile(r'\$\\color\{#[0-9A-Fa-f]{6}\}\\text(?:sf|bf)?\{(.*?)\}\$')
NUMBER = re.compile(r'\d[\d,]*(?:\.\d+)?')
BASELINES = ('System.Text.Json',)


def plain(text):
    return COLORED.sub(lambda m: m.group(1).replace('\\%', '%'), text)


def colored(text, value, best, worst):
    red = 0 if worst == best else round(150 * (value - best) / (worst - best))
    macro = 'textbf' if value == best else 'text'
    return '$\\color{#%02X%02X00}\\%s{%s}$' % (red, 150 - red, macro, text.replace('%', '\\%'))


def number(text):
    match = NUMBER.search(text)
    return float(match.group(0).replace(',', '')) if match else None


def split_row(line):
    return [cell.strip() for cell in line.strip().strip('|').split('|')]


def parts(cell):
    """The values of a cell: "117 / 188 ns" is two."""
    return [part.strip() for part in cell.split(' / ')] if cell else []


def scales(rows, slots):
    """(best, worst) per slot (a key of the values compared with each other)."""
    values = {}
    for slot, value in slots(rows):
        values.setdefault(slot, []).append(value)
    return {slot: (min(found), max(found)) for slot, found in values.items()}


def color_table(lines):
    header = split_row(lines[0])
    by_row = header[0] == 'Target'
    rows = [split_row(line) for line in lines[2:]]

    def measured(row):
        return not (row[0].startswith(BASELINES) or (row[0].startswith('**') and all(not cell for cell in row[1:])))

    def slots(rows):
        for r, row in enumerate(rows):
            if not measured(row):
                continue
            for c in range(1, len(row)):
                for p, part in enumerate(parts(row[c])):
                    value = number(part)
                    if value is not None:
                        yield ((r, p) if by_row else (c, p)), value

    limits = scales(rows, slots)
    out = lines[:2]
    for r, row in enumerate(rows):
        if measured(row):
            for c in range(1, len(row)):
                texts = []
                for p, part in enumerate(parts(row[c])):
                    value = number(part)
                    slot = (r, p) if by_row else (c, p)
                    texts.append(part if value is None else colored(part, value, *limits[slot]))
                row[c] = ' / '.join(texts)
        out.append('| ' + ' | '.join(row) + ' |')
    return out


def main(path):
    with open(path, encoding='utf-8-sig') as f:
        lines = [plain(line.rstrip('\n')).replace('**', '') if line.startswith('|') and not line.startswith('| **') else plain(line.rstrip('\n')) for line in f]
    result, table = [], []
    for line in lines + ['']:
        if line.startswith('|'):
            table.append(line)
            continue
        if table:
            result.extend(color_table(table) if len(table) > 2 and set(table[1].replace('|', '').strip()) <= set('-: ') else table)
            table = []
        result.append(line)
    with open(path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(result[:-1]) + '\n')


if __name__ == '__main__':
    main(sys.argv[1])
