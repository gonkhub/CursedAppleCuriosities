"""Parse a v3 section cache in the mod's MapCache order and sanity-check it."""
import struct, sys
import numpy as np
f = open(sys.argv[1], 'rb')
def i32(): return struct.unpack('<i', f.read(4))[0]
def f32(): return struct.unpack('<f', f.read(4))[0]
def u8(): return f.read(1)[0]
def s7():
    n, sh = 0, 0
    while True:
        b = u8(); n |= (b & 0x7f) << sh; sh += 7
        if b < 0x80: break
    return f.read(n).decode('utf-8')
magic, ver = struct.unpack('<Ii', f.read(8)); fp = s7(); origin = struct.unpack('<3f', f.read(12))
lmfile = s7(); lmsize = i32()
print('ver', ver, 'fp', fp, 'lightmap', lmfile, lmsize)
mats = []
for _ in range(i32()):
    m = dict(name=s7(), tex=s7(), flags=u8(), cutoff=f32(), tint=tuple(f.read(3)), tex2=s7(), reveal=s7(), roff=f32(), rsoft=f32(),
             rinv=u8(), tint2=tuple(f.read(3)), overlay=s7(), tiling=f32(), dark=f32(), bright=f32(), s1=f32(), s2=f32(), m1=u8(), m2=u8())
    mats.append(m)
print(len(mats), 'materials;', sum(1 for m in mats if m['tex2']), 'with layer 2;', sum(1 for m in mats if m['overlay']), 'with overlay')
for m in [m for m in mats if m['tex2'] or m['overlay']][:4]: print('  ', m)
lit = probe = none = 0; blendA = []
for c in range(i32()):
    back = u8(); f.read(24)
    for _ in range(i32()):
        mi = i32()
        n = i32(); f.read(n * 12)
        n2 = i32(); f.read(n2 * 12)
        n3 = i32(); f.read(n3 * 8)
        n4 = i32(); lm = np.frombuffer(f.read(n4 * 8), np.float32).reshape(-1, 2)
        n5 = i32(); col = np.frombuffer(f.read(n5 * 4), np.uint8).reshape(-1, 4)
        n6 = i32(); f.read(n6 * 4)
        assert n == n2 == n3 == n4 == n5, (n, n2, n3, n4, n5)
        neg = lm[:, 0] < 0
        none += int(neg.sum()); lit += int((~neg).sum())
        if (~neg).any(): assert lm[~neg].max() <= 1.001 and lm[~neg].min() >= -0.001, lm[~neg].max()
        if mats[mi]['tex2']: blendA.append(col[:, 3])
cp = i32(); f.read(cp * 12); ct = i32(); f.read(ct * 4)
print('verts with lightmap', lit, 'without (fallback)', none, '| layer-2 blend alpha mean', float(np.concatenate(blendA).mean()) if blendA else None)
print('trailing bytes', len(f.read()))
