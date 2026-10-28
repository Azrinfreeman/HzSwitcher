import math, wave, array, pathlib, random
out=pathlib.Path(__file__).resolve().parents[1]/'audio'
sr=48000; N=57*sr; mix=array.array('f',[0])*N
# Original ambient electronic bed. A minor / F major / C major / G major.
chords=[(45,52,60,64),(41,48,57,60),(48,55,60,64),(43,50,59,62)]
def hz(n): return 440*2**((n-69)/12)
for block in range(15):
 start=block*4; chord=chords[block%4]
 for midi in chord:
  f=hz(midi); end=min(N,int((start+4.6)*sr)); base=int(start*sr)
  for j in range(base,end):
   t=(j-base)/sr; env=min(1,t/.55)*min(1,(4.6-t)/.8)
   mix[j]+=.023*env*(math.sin(2*math.pi*f*t)+.25*math.sin(2*math.pi*f*1.002*t))
for beat in range(114):
 start=beat*.5; base=int(start*sr); dur=.23; chord=chords[int(start//4)%4]; f=hz(chord[beat%4]+24)
 for k in range(min(int(dur*sr),N-base)):
  t=k/sr; mix[base+k]+=.05*math.exp(-t*20)*math.sin(2*math.pi*f*t)
 # A restrained kick on the whole-second pulse.
 if beat%2==0:
  for k in range(min(int(.22*sr),N-base)):
   t=k/sr; mix[base+k]+=.085*math.exp(-t*24)*math.sin(2*math.pi*(48*t+5*(1-math.exp(-t*30))))
# Soft original sweep at each scene transition.
for start in [4,11,18,26,33,41,49]:
 base=int((start-.22)*sr)
 for k in range(int(.45*sr)):
  t=k/sr; mix[base+k]+=.019*math.sin(math.pi*t/.45)**2*math.sin(2*math.pi*(220*t+1100*t*t))
samples=array.array('h')
for i,x in enumerate(mix):
 t=i/sr; envelope=min(1,t/1.2,max(0,(57-t)/1.7))
 # Subtle stereo width from a short, low-level delay.
 left=x*envelope; right=(x*.92+(mix[i-613] if i>613 else 0)*.08)*envelope
 samples.extend((int(max(-1,min(1,left))*32767),int(max(-1,min(1,right))*32767)))
with wave.open(str(out/'original-music.wav'),'wb') as w:
 w.setparams((2,2,sr,0,'NONE','not compressed'));w.writeframes(samples.tobytes())
print('Original 57-second soundtrack composed.')
