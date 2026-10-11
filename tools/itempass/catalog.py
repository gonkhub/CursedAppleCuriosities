import json, base64, struct, sys
C=r"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\StreamingAssets\aa\catalog.json"
j=json.load(open(C,encoding="utf-8"))
print([k for k in j.keys()])
kd=base64.b64decode(j["m_KeyDataString"]); bd=base64.b64decode(j["m_BucketDataString"]); ed=base64.b64decode(j["m_EntryDataString"])
ids=j["m_InternalIds"]
def key(off):
    t=kd[off]; off+=1
    if t in (0,1):
        n=struct.unpack_from("<i",kd,off)[0]; off+=4
        return kd[off:off+n].decode("ascii" if t==0 else "utf-16-le")
    if t==4: return struct.unpack_from("<i",kd,off)[0]
    return None
n=struct.unpack_from("<i",bd,0)[0]; p=4; buckets=[]
for i in range(n):
    off,cnt=struct.unpack_from("<ii",bd,p); p+=8
    es=struct.unpack_from("<%di"%cnt,bd,p); p+=4*cnt
    buckets.append((key(off),es))
ne=struct.unpack_from("<i",ed,0)[0]
entries=[struct.unpack_from("<7i",ed,4+i*28) for i in range(ne)]
guid={}
for k,es in buckets:
    if isinstance(k,str) and len(k)==32 and all(c in "0123456789abcdef" for c in k):
        for e in es:
            iid=ids[entries[e][0]]
            if not isinstance(iid,str): continue
            guid.setdefault(k,iid)
json.dump(guid,open("guid.json","w"),indent=0)
print(len(guid)); print(guid.get("9b5b06503e9cfdf4b9e29649b8f3947b"))
