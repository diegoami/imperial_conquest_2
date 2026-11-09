"""T292a: census of the PE resource tree of the user's own original installation.

Reads headers only and prints counts; writes nothing, so no original bytes reach the repository.
Usage: python pe_census.py "<install>/Imperial Conquest 2.exe" [<install>/WAVS]
"""
import struct, sys, collections, os, wave

TYPES = {1: "CURSOR", 2: "BITMAP", 3: "ICON", 4: "MENU", 5: "DIALOG", 6: "STRING", 8: "FONT", 9: "ACCELERATOR", 10: "RCDATA",
         12: "GROUP_CURSOR", 14: "GROUP_ICON", 16: "VERSION"}


def census(path):
    d = open(path, "rb").read()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", d, opt)[0]
    ddir = opt + (96 if magic == 0x10B else 112)
    rva, size = struct.unpack_from("<II", d, ddir + 16)
    sects = []
    so = opt + struct.unpack_from("<H", d, pe + 20)[0]
    for i in range(nsec):
        n, vs, va, rs, rp = struct.unpack_from("<8sIIII", d, so + i * 40)
        sects.append((va, vs, rp, rs))

    def off(r):
        for va, vs, rp, rs in sects:
            if va <= r < va + max(vs, rs):
                return rp + r - va
        raise ValueError(hex(r))

    base = off(rva)

    def entries(o):
        nn, ni = struct.unpack_from("<HH", d, o + 12)
        out = []
        for i in range(nn + ni):
            nm, tgt = struct.unpack_from("<II", d, o + 16 + i * 8)
            out.append((nm, tgt))
        return out

    def name(nm):
        if nm & 0x80000000:
            o = base + (nm & 0x7FFFFFFF)
            n = struct.unpack_from("<H", d, o)[0]
            return d[o + 2:o + 2 + n * 2].decode("utf-16le")
        return nm

    res = collections.defaultdict(list)
    for tn, tt in entries(base):
        t = name(tn)
        if not tt & 0x80000000:
            continue
        for nn_, nt in entries(base + (tt & 0x7FFFFFFF)):
            nm = name(nn_)
            if not nt & 0x80000000:
                continue
            for ln, lt in entries(base + (nt & 0x7FFFFFFF)):
                dr, ds, _, _ = struct.unpack_from("<IIII", d, base + lt)
                res[t].append((nm, off(dr), ds))
    return d, res


def main(path):
    d, res = census(path)
    print(os.path.basename(path))
    for t, items in sorted(res.items(), key=lambda kv: str(kv[0])):
        print(" ", TYPES.get(t, t), len(items), "total bytes", sum(i[2] for i in items))
    bmps = res.get(2, [])
    shapes = collections.Counter()
    for nm, o, s in bmps:
        w, h, planes, bpp = struct.unpack_from("<iiHH", d, o + 4)
        shapes[(w, abs(h), bpp)] += 1
    print("  bitmap shapes (w,h,bpp) -> count:", dict(sorted(shapes.items(), key=lambda kv: -kv[1])[:20]))
    print("  distinct bitmap sizes:", len(shapes), "; bpp:", collections.Counter(k[2] for k in shapes.elements()))
    rc = res.get(10, [])
    print("  RCDATA names:", [str(n) for n, _, _ in rc][:40])
    for nm, o, s in rc[:0]:
        pass
    return res


if __name__ == "__main__":
    main(sys.argv[1])
    if len(sys.argv) > 2:
        wd = sys.argv[2]
        fmts = collections.Counter()
        n = 0
        for f in os.listdir(wd):
            if f.lower().endswith(".wav"):
                n += 1
                try:
                    with wave.open(os.path.join(wd, f)) as w:
                        fmts[(w.getnchannels(), w.getframerate(), w.getsampwidth() * 8)] += 1
                except Exception as e:
                    fmts[("unreadable", str(e)[:30])] += 1
        print("WAVS:", n, dict(fmts))
