"""MapConverter.ShapeHash (FNV-1a-ish over the float bits of Outline, Origin, NoWalk) from MapSectionDef.cs text."""
import re, struct, sys
src = open(sys.argv[1], encoding='utf-8').read()
def arr(name):
    m = re.search(r'(?:float|int)\[\] ' + name + r' = \{(.*?)\};', src, re.S)
    body = re.sub(r'//[^\n]*', '', m.group(1))
    return [float(x) for x in re.findall(r'-?\d+(?:\.\d+)?', body)]
origin = [float(x) for x in re.findall(r'-?\d+(?:\.\d+)?', re.search(r'Origin = new\[\] \{([^}]*)\}', src).group(1))]
h = 2166136261
for v in arr('Outline') + origin + arr('VentTunnels'):
    h ^= struct.unpack('<I', struct.pack('<f', v))[0]
    h = (h * 16777619) & 0xFFFFFFFF
print(f'{h:08x}')
