"""Writes a GLB (JSON + BIN chunks)."""
import struct, json

def write_glb(path, j, binc):
    js = json.dumps(j, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4); binc += b"\0" * (-len(binc) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(binc)))
        f.write(struct.pack("<II", len(js), 0x4E4F534A)); f.write(js)
        f.write(struct.pack("<II", len(binc), 0x004E4942)); f.write(binc)
