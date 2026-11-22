import sys
lines = open(sys.argv[1]).read().split('\n')
for i, l in enumerate(lines):
    if l.strip().startswith('expected') and i + 1 < len(lines) and lines[i+1].strip().startswith('written'):
        name = next(x for x in reversed(lines[:i]) if not x.startswith(' ')).split(':')[0]
        e = bytes.fromhex(l.split()[1]); w = bytes.fromhex(lines[i+1].split()[1])
        diffs = [k for k in range(min(len(e), len(w))) if e[k] != w[k]]
        print(f"{name}: expected {len(e)} written {len(w)}, {len(diffs)} differing bytes, first at {diffs[:8]}")
        if diffs:
            k = diffs[0]; print("   e", e[max(0,k-24):k+24].hex(), "\n   w", w[max(0,k-24):k+24].hex())
    elif l.strip().startswith('expected') and 'read' in lines[i+1]:
        pass
