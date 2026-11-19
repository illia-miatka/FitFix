import fitparse, math
o=[{d.name:d.value for d in m} for m in fitparse.FitFile('../data/2f575b64-Run_4k_Chabany.fit').get_messages('record')]
f=[{d.name:d.value for d in m} for m in fitparse.FitFile('../data/0de30628-FFF.fit').get_messages('record')]
S=180/2**31
def xy(r): return (r['position_lat']*S, r['position_long']*S)
def dist(a,b):
    la=math.radians((a[0]+b[0])/2); return math.hypot((a[0]-b[0])*111320,(a[1]-b[1])*111320*math.cos(la))
fp=[xy(r) for r in f]
print('FFF start',fp[0],'end',fp[-1],'loop?',dist(fp[0],fp[-1]))
for i in range(0,len(o),60):
    p=xy(o[i]); j=min(range(len(fp)),key=lambda k:dist(p,fp[k]))
    print(i,o[i]['distance'],'-> nearest FFF',j,f[j]['distance'],round(dist(p,fp[j])),p)
