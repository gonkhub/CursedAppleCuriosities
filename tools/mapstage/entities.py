"""Every entity of a map's entity lump (Source2Viewer-CLI's text dump of maps/<map>/entities/default_ents.vents_c, -b DATA)
as a JSON list of {key: value}. Usage: entities.py <dump.txt> <entsfull.json> [classname,classname,... to print]"""
import sys,re,json
ents=[];cur=None;depth=0
for line in open(sys.argv[1],encoding='utf-8',errors='ignore'):
    s=line.strip()
    if s=='values =': cur={}; continue
    if cur is not None:
        if s=='{': depth+=1; continue
        if s=='}':
            depth-=1
            if depth==0: ents.append(cur); cur=None
            continue
        m=re.match(r'([\w.]+) = (.*)',s)
        if m and depth==1: cur[m.group(1)]=m.group(2).strip('"')
json.dump(ents,open(sys.argv[2],'w'))
want=set(sys.argv[3].split(',')) if len(sys.argv)>3 else set()
for e in ents:
    if e.get('classname') in want:
        print({k:v for k,v in e.items() if k in('classname','origin','targetname','teamnumber','m_iTeamNum','lane','m_eLane','m_iLane','district_number','target','m_nLane','parentname','TeamNum') or 'lane' in k.lower() or 'team' in k.lower()})
