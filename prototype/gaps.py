import sys, fitparse
for p in sys.argv[1:]:
    recs=[{d.name:d.value for d in m} for m in fitparse.FitFile(p).get_messages('record')]
    print('==',p,len(recs))
    prev=None; big=[]
    from collections import Counter; c=Counter()
    for i,r in enumerate(recs):
        if prev:
            dt=(r['timestamp']-prev['timestamp']).total_seconds(); c[dt]+=1
            if dt>5 or dt<=0: big.append((i,str(prev['timestamp'].time()),str(r['timestamp'].time()),dt,prev.get('distance'),r.get('distance')))
        prev=r
    print(sorted(c.items())[:20]); print('big',len(big)); [print(b) for b in big[:40]]
