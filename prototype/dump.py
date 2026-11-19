import sys, fitparse, collections
f = fitparse.FitFile(sys.argv[1])
cnt = collections.Counter()
recs=[]
for m in f.get_messages():
    cnt[m.name]+=1
    if m.name!='record':
        print(m.name, {d.name:d.value for d in m})
    else:
        recs.append({d.name:d.value for d in m})
print(cnt)
print('first', recs[:3]); print('last', recs[-2:])
ts=[r.get('timestamp') for r in recs]
print('n',len(recs), ts[0], ts[-1])
