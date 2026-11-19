import pickle, datetime
from fit_tool.fit_file_builder import FitFileBuilder
from fit_tool.profile.messages.file_id_message import FileIdMessage
from fit_tool.profile.messages.record_message import RecordMessage
from fit_tool.profile.messages.event_message import EventMessage
from fit_tool.profile.messages.lap_message import LapMessage
from fit_tool.profile.messages.session_message import SessionMessage
from fit_tool.profile.messages.activity_message import ActivityMessage
from fit_tool.profile.profile_type import *
out=pickle.load(open('out.pkl','rb'))
for r in out:
    if (r['ts']-out[0]['ts']).total_seconds()>1300 and r['pw']==0: r['pw']=None
ms=lambda d: int(d.replace(tzinfo=datetime.timezone.utc).timestamp()*1000)
t0,t1=out[0]['ts'],out[-1]['ts']; el=(t1-t0).total_seconds(); D=out[-1]['dist']
b=FitFileBuilder(auto_define=True,min_string_size=50)
m=FileIdMessage(); m.type=FileType.ACTIVITY; m.manufacturer=Manufacturer.DEVELOPMENT.value; m.product=0; m.serial_number=0x12345678; m.time_created=ms(t1); b.add(m)
e=EventMessage(); e.timestamp=ms(t0); e.event=Event.TIMER; e.event_type=EventType.START; b.add(e)
for r in out:
    x=RecordMessage(); x.timestamp=ms(r['ts']); x.position_lat=r['lat']; x.position_long=r['lon']
    x.altitude=r['alt']; x.distance=r['dist']; x.speed=r['spd']
    if r['hr'] is not None: x.heart_rate=r['hr']
    if r['pw'] is not None: x.power=r['pw']
    b.add(x)
e=EventMessage(); e.timestamp=ms(t1); e.event=Event.TIMER; e.event_type=EventType.STOP_ALL; b.add(e)
hrs=[r['hr'] for r in out if r['hr']]; pws=[r['pw'] for r in out if r['pw'] is not None]
def fill(x):
    x.timestamp=ms(t1); x.start_time=ms(t0); x.total_elapsed_time=el; x.total_timer_time=el; x.total_distance=D
    x.sport=Sport.RUNNING; x.avg_speed=D/el; x.avg_heart_rate=round(sum(hrs)/len(hrs)); x.max_heart_rate=max(hrs)
    x.avg_power=round(sum(pws)/len(pws)); x.max_power=max(pws)
    x.start_position_lat=out[0]['lat']; x.start_position_long=out[0]['lon']
l=LapMessage(); fill(l); l.event=Event.LAP; l.event_type=EventType.STOP; l.end_position_lat=out[-1]['lat']; l.end_position_long=out[-1]['lon']; b.add(l)
s=SessionMessage(); fill(s); s.event=Event.SESSION; s.event_type=EventType.STOP; s.first_lap_index=0; s.num_laps=1; s.sub_sport=SubSport.GENERIC; b.add(s)
a=ActivityMessage(); a.timestamp=ms(t1); a.total_timer_time=el; a.num_sessions=1; a.type=Activity.MANUAL; a.event=Event.ACTIVITY; a.event_type=EventType.STOP
a.local_timestamp=ms(t1)//1000-631065600+7200; b.add(a)
b.build().to_file('Run_4k_Chabany_fixed.fit')
