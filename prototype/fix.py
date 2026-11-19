import fitparse, math, bisect, sys
from datetime import timezone
o=[{d.name:d.value for d in m} for m in fitparse.FitFile(sys.argv[1]).get_messages('record')]
f=[{d.name:d.value for d in m} for m in fitparse.FitFile(sys.argv[2]).get_messages('record')]
S=180/2**31
def ll(r): return (r['position_lat']*S, r['position_long']*S)
def dist(a,b):
    la=math.radians((a[0]+b[0])/2); return math.hypot((a[0]-b[0])*111320,(a[1]-b[1])*111320*math.cos(la))
# FFF track with own cumulative distance (geodesic), dedupe
pts=[ll(f[0])]; alts=[f[0]['altitude']]
for r in f[1:]:
    p=ll(r)
    if dist(p,pts[-1])>0.3: pts.append(p); alts.append(r['altitude'])
cum=[0.0]
for a,b in zip(pts,pts[1:]): cum.append(cum[-1]+dist(a,b))
L=cum[-1]; print('track length',L)
def proj(p,lo,hi):
    best=(1e9,None)
    for k in range(lo,min(hi,len(pts)-1)):
        a,b=pts[k],pts[k+1]; seg=cum[k+1]-cum[k]
        best_t=0; 
        if seg>0:
            # local planar projection
            cl=math.cos(math.radians(a[0]))
            ax,ay=0,0; bx,by=(b[1]-a[1])*111320*cl,(b[0]-a[0])*111320
            px,py=(p[1]-a[1])*111320*cl,(p[0]-a[0])*111320
            best_t=max(0,min(1,(px*bx+py*by)/(bx*bx+by*by)))
            d=math.hypot(px-best_t*bx,py-best_t*by)
        else: d=dist(p,a)
        if d<best[0]: best=(d,cum[k]+best_t*seg,k)
    return best
t0=o[0]['timestamp']; T=(o[-1]['timestamp']-t0).total_seconds()
anchors=[(0.0,0.0)]; k=0
for r in o:
    t=(r['timestamp']-t0).total_seconds()
    if t>1720 or r.get('position_lat') is None: continue
    d,s,kk=proj(ll(r),max(0,k-3),k+40)
    ta,sa=anchors[-1]; dt=t-ta; ds=s-sa
    if d<20 and dt>0 and ds>=0 and ds<=dt*4.5+3 and (dt<30 or ds/dt>=1.8):
        anchors.append((t,s)); k=kk
print('last good anchor',anchors[-1], 'of', T)
anchors.append((T,L))
# shift FFF altitude to match original start altitude
off=o[0]['altitude']-alts[0]; print('alt offset',off)
at=[a[0] for a in anchors]
out=[]
for r in o:
    t=(r['timestamp']-t0).total_seconds()
    i=min(bisect.bisect_right(at,t)-1,len(at)-2)
    (ta,sa),(tb,sb)=anchors[i],anchors[i+1]
    s=sa+(sb-sa)*((t-ta)/(tb-ta) if tb>ta else 0)
    j=min(bisect.bisect_right(cum,s)-1,len(pts)-2)
    u=(s-cum[j])/(cum[j+1]-cum[j]) if cum[j+1]>cum[j] else 0
    lat=pts[j][0]+u*(pts[j+1][0]-pts[j][0]); lon=pts[j][1]+u*(pts[j+1][1]-pts[j][1])
    alt=alts[j]+u*(alts[j+1]-alts[j])+off
    out.append(dict(ts=r['timestamp'],lat=lat,lon=lon,alt=alt,dist=s,hr=r.get('heart_rate'),pw=r.get('power')))
# smooth distance-vs-time (monotone moving average), heavier where original GPS was noisy
raw=[r['dist'] for r in out]; n=len(raw)
for i,r in enumerate(out):
    t=(r['ts']-t0).total_seconds(); w=15 if t<1250 else 60
    lo,hi=max(0,i-w),min(n-1,i+w); w2=min(i-lo,hi-i)
    seg=raw[i-w2:i+w2+1]; s=sum(seg)/len(seg)
    j=min(bisect.bisect_right(cum,s)-1,len(pts)-2)
    u=(s-cum[j])/(cum[j+1]-cum[j]) if cum[j+1]>cum[j] else 0
    r.update(dist=s,lat=pts[j][0]+u*(pts[j+1][0]-pts[j][0]),lon=pts[j][1]+u*(pts[j+1][1]-pts[j][1]),alt=alts[j]+u*(alts[j+1]-alts[j])+off)
# speed
for i,r in enumerate(out):
    a=out[max(0,i-3)]; b=out[min(len(out)-1,i+3)]
    dt=(b['ts']-a['ts']).total_seconds(); r['spd']=(b['dist']-a['dist'])/dt if dt else 0
import json,pickle; pickle.dump(out,open('out.pkl','wb'))
print(len(out), out[0], out[-1])
for i in range(0,len(out),120): print(i,round(out[i]['dist']),round(out[i]['spd'],2),out[i]['hr'])
