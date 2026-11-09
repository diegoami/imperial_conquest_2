import struct, sys, collections, os
sys.path.insert(0, os.path.dirname(__file__))
import pe_census as pecensus

path = sys.argv[1]
d, res = pecensus.census(path)
tot = collections.Counter()
rows = []
for nm, o, s in res[10]:
    blob = d[o:o + s]
    n = 0
    shapes = []
    i = 0
    while True:
        i = blob.find(b"BM", i)
        if i < 0:
            break
        if i + 54 <= len(blob):
            size, _, off, hs = struct.unpack_from("<IIII", blob, i + 2)
            if hs in (40, 12) and 0 < size <= len(blob) - i and off < size:
                w, h, pl, bpp = struct.unpack_from("<iiHH", blob, i + 18)
                if 0 < w < 4096 and 0 < abs(h) < 4096 and bpp in (1, 4, 8, 16, 24, 32):
                    n += 1
                    shapes.append((w, abs(h), bpp))
                    i += size
                    continue
        i += 2
    # TPF0 signature: Delphi form stream
    rows.append((nm, s, blob[:4] == b"TPF0", n, collections.Counter(shapes).most_common(3)))
    tot.update(shapes)
for r in rows:
    print(r[0], r[1], "TPF0" if r[2] else "?", "bitmaps", r[3], r[4])
print("total embedded BMP-like images:", sum(tot.values()), "distinct sizes:", len(tot))
print("most common:", tot.most_common(12))
