"""Original 120 BPM score, sample-aligned sound cues, editable stems. No external music."""
from pathlib import Path
import json, math, subprocess, wave
import numpy as np
import imageio_ffmpeg

ROOT=Path(__file__).resolve().parents[1]; A=ROOT/'public/audio'; SR=48000; SECONDS=55; N=SR*SECONDS
data=json.loads((ROOT/'public/timeline.json').read_text()); rng=np.random.default_rng(20261029)
score=np.zeros((N,2),dtype=np.float64); sfx=score.copy()
def add(buf,signal,t,pan=0):
    i=round(t*SR);sig=np.asarray(signal); lo=max(0,-i);i=max(0,i);size=min(len(sig)-lo,N-i)
    if size<=0:return
    sig=sig[lo:lo+size]
    if sig.ndim==1: sig=np.column_stack((sig*math.sqrt((1-pan)/2),sig*math.sqrt((1+pan)/2)))
    buf[i:i+size]+=sig
def env(t,attack=.02,decay=1):return np.minimum(1,t/attack)*np.exp(-t/decay)
def tone(hz,length,gain):
    t=np.arange(round(length*SR))/SR
    return gain*(np.sin(2*np.pi*hz*t)+.16*np.sin(2*np.pi*hz*2*t)+.035*np.sin(2*np.pi*hz*3*t))*env(t,.035,length*.48)
def write(path,x):
    path.parent.mkdir(parents=True,exist_ok=True)
    x=np.asarray(x); x=np.column_stack((x,x)) if x.ndim==1 else x
    with wave.open(str(path),'wb') as w:w.setnchannels(2);w.setsampwidth(2);w.setframerate(SR);w.writeframes((np.clip(x,-.98,.98)*32767).astype('<i2').tobytes())

# Four harmonies, voiced sparsely below the typography: Am(add9), Fmaj7, Cmaj9, Gsus.
chords=[[110,164.8138,220,261.6256,329.6276],[87.3071,130.8128,174.6141,220,261.6256],[130.8128,196,261.6256,293.6648,329.6276],[97.9989,146.8324,196,261.6256,293.6648]]
for bar in range(28):
    start=bar*2
    chord=chords[(bar//4)%4]
    # Low, smooth polyphonic pad; slight fixed stereo detune, no randomized animation.
    t=np.arange(round(3.8*SR))/SR; window=np.minimum(1,t/.6)*np.minimum(1,(3.8-t)/1.2)
    for j,hz in enumerate(chord[1:]):
        left=np.sin(2*np.pi*(hz*.999)*t)+.17*np.sin(2*np.pi*hz*2*t)
        right=np.sin(2*np.pi*(hz*1.001)*t)+.17*np.sin(2*np.pi*hz*2*t+.3)
        add(score,np.column_stack((left,right))*window[:,None]*.022,start)
for beat in range(110):
    start=beat*.5
    energy=.42 if start<6 else .84 if start<25 else .65 if start<31 else .3 if start<35 else .9 if start<45 else 1.08 if start<49 else .5
    if start>=53: energy*=max(0,(55-start)/2)
    # Kick is rounded and cinematic, not a UI notification tone.
    t=np.arange(round(.36*SR))/SR
    phase=2*np.pi*(43*t+63*.023*(1-np.exp(-t/.023)))
    kick=np.sin(phase)*np.exp(-t/.085)*np.minimum(1,t/.002)*.58*energy
    if start<49 or beat%4==0:add(score,kick,start)
    root=chords[(int(start//8))%4][0]/2
    if 6<=start<49 and not 31<=start<33:
        add(score,tone(root,.31,.21*energy),start+.25)
        if beat%2:
            nt=np.arange(round(.14*SR))/SR; noise=rng.normal(0,1,len(nt));noise=noise-np.roll(noise,1)
            clap=noise*np.exp(-nt/.028)*np.minimum(1,nt/.001)*.043*energy
            add(score,clap,start,pan=.08)
    if 11<=start<49:
        for off in [0,.25]:
            nt=np.arange(round(.07*SR))/SR;noise=rng.normal(0,1,len(nt));noise=noise-np.roll(noise,1)
            add(score,noise*np.exp(-nt/.012)*.016*energy,start+off,pan=-.35 if off==0 else .35)
    if 15<=start<49 and beat%4 in [1,3]:
        chord=chords[(int(start//8))%4]
        for j,hz in enumerate(chord[1:4]):add(score,tone(hz*2,.45,.027*energy),start+.25,pan=(j-1)*.45)
# A measured final cadence rather than a commercial-track tail.
for hz in chords[0][1:]:add(score,tone(hz,5.5,.042),49)
time=np.arange(N)/SR
gain=np.minimum(1,time/.8)*np.minimum(1,np.maximum(0,(55-time)/2.0))
score*=gain[:,None]
# Soft saturation retains kick detail and headroom for physical SFX.
score=np.tanh(score*1.45)*.77
write(A/'score.wav',score)

FFMPEG=imageio_ffmpeg.get_ffmpeg_exe();cue_list=[]
for cue in data['sfx']:
    raw=subprocess.run([FFMPEG,'-v','error','-i',str(ROOT/'public'/cue['src']),'-f','f32le','-ac','2','-ar',str(SR),'-'],capture_output=True,check=True).stdout
    x=np.frombuffer(raw,dtype='<f4').reshape(-1,2).astype(np.float64)
    peak=int(np.argmax(np.max(np.abs(x),axis=1)))
    target=cue['from'];start=max(0,round(target-peak/(SR/30)))
    # Keep peak inside the cue; preserve a short release and the planned bounded duration.
    dur=max(cue['duration'],math.ceil(peak/(SR/30))+6)
    x=x[:round(dur*SR/30)]
    if len(x)>SR*.03:x[-1440:]*=np.linspace(1,0,1440)[:,None]
    source_peak=max(.001,float(np.max(np.abs(x))))
    x=x/source_peak*cue['volume']*.56
    filename='audio/cues/'+cue['id']+'.wav'
    write(ROOT/'public'/filename,x)
    add(sfx,x,start/30)
    cue_list.append({**cue,'targetFrame':target,'from':start,'duration':math.ceil(len(x)/(SR/30)),'src':filename,'volume':1,'sourcePeakSamples':peak,'predictedPeakFrame':start+peak/(SR/30)})
write(A/'sfx.wav',sfx)
write(A/'mix.wav',score*.73+sfx)
(ROOT/'src/audio-cues.json').write_text(json.dumps(cue_list,indent=2))
(A/'audio-manifest.json').write_text(json.dumps({'bpm':120,'beatZero':0,'sampleRate':SR,'duration':55,'score':'Original composition, deterministic synthesis, seed 20261029','cues':cue_list,'scorePeak':float(np.max(abs(score))),'mixPeak':float(np.max(abs(score*.73+sfx)))},indent=2))
print('Original score and',len(cue_list),'peak-aligned SFX created. Mix peak',round(float(np.max(abs(score*.73+sfx))),4))
