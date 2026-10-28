from PIL import Image, ImageDraw, ImageFont, ImageFilter
from pathlib import Path
import math,json,subprocess,sys,wave,textwrap
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[1]; AS=ROOT/'assets'; QA=ROOT/'qa'
W,H,FPS=1920,1080,60
SCENES=json.loads((ROOT/'source/timeline.json').read_text())
FFMPEG=imageio_ffmpeg.get_ffmpeg_exe()
BG=(9,13,23); WHITE=(239,244,255); MUTED=(145,161,186); BLUE=(78,150,255); CYAN=(107,226,231)
FONTS={}
def font(size,bold=False):
 key=(size,bold)
 if key not in FONTS:FONTS[key]=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf' if bold else 'C:/Windows/Fonts/segoeui.ttf',size)
 return FONTS[key]
def text(d,xy,s,size=30,fill=WHITE,bold=False):d.text(xy,s,font=font(size,bold),fill=fill,spacing=5)
def ease(x):return 1-(1-max(0,min(1,x)))**3
base=Image.new('RGB',(W,H),BG);d=ImageDraw.Draw(base)
for y in range(H):
 shade=int(5*(1-y/H));d.line((0,y,W,y),fill=(9+shade,13+shade,23+shade))
# restrained atmospheric glow, baked once
light=Image.new('RGB',(480,270),BG);ld=ImageDraw.Draw(light)
for r in range(250,0,-2):
 k=(1-r/250); ld.ellipse((325-r,110-r,325+r,110+r),fill=(int(9+10*k),int(13+20*k),int(23+42*k)))
base=Image.blend(base,light.resize((W,H),Image.Resampling.BILINEAR),.7)
for x in range(0,W,80):ImageDraw.Draw(base).line((x,0,x,H),fill=(18,25,39),width=1)
for y in range(0,H,80):ImageDraw.Draw(base).line((0,y,W,y),fill=(18,25,39),width=1)
base=base.convert('RGBA')
icon=Image.open(AS/'app.ico').convert('RGBA')
# Cache source capture images and prepared rounded panels.
imgs={p.stem:Image.open(p).convert('RGBA') for p in AS.glob('*.png')}
for name in ['display144','display60','display120','brightness40','automation','profileactive','saver','restored','shortcuts','general']:
 im=imgs[name];imgs[name]=im.crop((8,31,788,591))
im=imgs['profiles'];imgs['profiles']=im.crop((8,31,im.width-8,im.height-8))
CACHE={}
def panel(canvas,name,x,y,width,stroke=None):
 key=(name,width,stroke)
 if key not in CACHE:
  src=imgs[name];h=round(src.height*width/src.width);im=src.resize((width,h),Image.Resampling.LANCZOS)
  mask=Image.new('L',im.size);ImageDraw.Draw(mask).rounded_rectangle((0,0,width-1,h-1),radius=18,fill=255);im.putalpha(mask)
  outline=ImageDraw.Draw(im);outline.rounded_rectangle((0,0,width-1,h-1),radius=18,outline=stroke or (59,74,98),width=2)
  shadow=Image.new('RGBA',(width+100,h+100));ImageDraw.Draw(shadow).rounded_rectangle((50,50,width+50,h+50),radius=22,fill=(0,0,0,150));shadow=shadow.filter(ImageFilter.GaussianBlur(22))
  CACHE[key]=(im,shadow)
 im,shadow=CACHE[key];canvas.alpha_composite(shadow,(int(x-50),int(y-35)));canvas.alpha_composite(im,(int(x),int(y)))
 return im.size

def cursor(c,x,y,t,click=False):
 d=ImageDraw.Draw(c)
 if click:
  r=18+28*(t%1);d.ellipse((x-r,y-r,x+r,y+r),outline=CYAN,width=3)
 d.polygon([(x,y),(x+3,y+35),(x+12,y+26),(x+22,y+44),(x+29,y+40),(x+19,y+22),(x+34,y+21)],fill=WHITE,outline=(10,18,33),width=3)

def pill(d,xy,label,size=25,tone=BLUE):
 x,y=xy;b=d.textbbox((0,0),label,font=font(size,True));width=b[2]+38
 d.rounded_rectangle((x,y,x+width,y+size+26),radius=12,fill=(23,38,64),outline=(50,81,128),width=1)
 text(d,(x+19,y+8),label,size,tone,True)

def wrap(d,s,maxw,size):
 lines=[];line=''
 for word in s.split():
  new=(line+' '+word).strip()
  if d.textlength(new,font=font(size))>maxw and line:lines.append(line);line=word
  else:line=new
 if line:lines.append(line)
 return lines
# Captions timed to each generated voice file. Voice is pronounced "Hertz Switcher"; display spelling is the product name.
captions=[]
for i,s in enumerate(SCENES):
 with wave.open(str(ROOT/'audio'/f'voice-{i}.wav')) as w: duration=w.getnframes()/w.getframerate()
 wording=s['voice'].replace('Hertz Switcher','HzSwitcher')
 phrases=wording.split('. '); phrases=[p+('.' if not p.endswith('.') else '') for p in phrases]
 weights=[len(p.split()) for p in phrases];total=sum(weights);start=s['start']+.25
 for phrase,weight in zip(phrases,weights):
  end=start+duration*weight/total
  captions.append((start,min(end,s['end']-.08),phrase));start=end

def ts(t):
 n=round(t*1000);return f'{n//3600000:02}:{n//60000%60:02}:{n//1000%60:02},{n%1000:03}'
(ROOT/'HzSwitcher-Trailer-57s.srt').write_text('\n\n'.join(f'{i+1}\n{ts(a)} --> {ts(b)}\n{s}' for i,(a,b,s) in enumerate(captions))+'\n',encoding='utf-8')
LABELS=['DISPLAY CONTROL, REIMAGINED','01 / ONE-CLICK CONTROL','02 / PRESETS + SHORTCUTS','03 / APP PROFILES','04 / BRIGHTNESS','05 / AUTOMATIC POWER SAVER','06 / ALWAYS ON TOP','TAKE CONTROL']

def frame(t):
 idx=next((i for i,s in enumerate(SCENES) if s['start']<=t<s['end']),7);s=SCENES[idx];u=t-s['start'];dur=s['end']-s['start']
 c=base.copy();d=ImageDraw.Draw(c)
 # Product navigation rail: quiet, consistent across the entire film.
 c.alpha_composite(icon.resize((38,38),Image.Resampling.LANCZOS),(108,68));text(d,(161,61),'HzSwitcher',31,WHITE,True)
 text(d,(110,1005),'WINDOWS DISPLAY CONTROL',17,MUTED)
 text(d,(1660,1005),f'{idx+1:02} / 08',19,MUTED)
 if 0<idx<7:
  pill(d,(1660,67),'UI DEMO',18,MUTED)
  y=int(267+28*(1-ease(u/.75)))
  text(d,(110,y-65),LABELS[idx],20,CYAN,True)
  text(d,(105,y),s['title'],80,WHITE,True)
  x=int(850+75*(1-ease(u/.8))); py=206
  if idx==1:
   name='display144' if u<3.1 else 'display60';panel(c,name,x,py,940)
   if u<1.4:
    # Tray reveal begins at a large, readable scale.
    panel(c,'traydemo',1230,320,430)
    cursor(c,1390,470,u,click=u>1)
   else:
    px=x+248/780*940;pyc=206+346/560*675
    cursor(c,int(x+730-(730-(px-x))*ease((u-1.4)/1.5)),int(270+(pyc-270)*ease((u-1.4)/1.5)),u,2.9<u<3.7)
   pill(ImageDraw.Draw(c),(110,600),'TRAY  →  SELECT A RATE',22)
  elif idx==2:
   name='display60' if u<2 else 'display120' if u<4 else 'display144';panel(c,name,x,py,940)
   pill(ImageDraw.Draw(c),(110,600),'Ctrl  +  Alt  +  3',30)
   target=(x+(417 if u<4 else 280)/780*940,py+224/560*675)
   cursor(c,*map(int,target),u,1.8<u<2.6 or 3.8<u<4.6)
  elif idx==3:
   panel(c,'automation' if u<1.7 else 'profileactive',x,py,940)
   if 1.7<u<5.4:panel(c,'profiles',x-20,290,960)
   pill(ImageDraw.Draw(c),(110,600),'NOTEPAD  →  60 Hz',25)
   if 1.7<u<5.4:cursor(c,x+710,485,u,3<u<3.7)
  elif idx==4:
   panel(c,'display144' if u<3 else 'brightness40',x,py,940)
   # Lower control enlargement: actual production brightness card crop.
   name='display144' if u<3 else 'brightness40'
   src=imgs[name];imgs['brightnesscard']=src.crop((200,425,760,550))
   # cache states independently
   imgs['bright70card']=imgs['display144'].crop((200,425,760,550));imgs['bright40card']=imgs['brightness40'].crop((200,425,760,550))
   panel(c,'bright70card' if u<3 else 'bright40card',x-20,650,980)
   progress=ease((u-1)/2);cursor(c,int(x+620-210*progress),765,u,2.8<u<3.6)
   pill(ImageDraw.Draw(c),(110,600),'YOUR DISPLAY. YOUR LEVEL.',22)
  elif idx==5:
   name='automation' if u<1.8 else 'saver' if u<5 else 'restored';panel(c,name,x,py,940)
   pill(ImageDraw.Draw(c),(110,600),'144 Hz  →  60 Hz  →  144 Hz',23)
   pill(ImageDraw.Draw(c),(x+530,py+585),'TIME COMPRESSED',20,CYAN)
  elif idx==6:
   if u<2:
    panel(c,'general',x,py,940);cursor(c,x+270,475,u,1<u<1.8)
   else:
    # Simulated desktop composition, labelled UI DEMO, using a real native menu capture.
    if u>4.7:
     bd=ImageDraw.Draw(c);bx=int(x+10+100*ease((u-4.7)/2));by=py+28
     bd.rounded_rectangle((bx,by,bx+760,by+500),radius=18,fill=(28,39,57),outline=(72,88,111),width=2)
     text(bd,(bx+28,by+17),'Workspace',22,MUTED)
    panel(c,'notes',x+80,py+80,780,BLUE if u>4.7 else None)
    if u<4.7:
     panel(c,'pinmenu',x+490,py+125,280)
     cursor(c,x+570,py+350,u,4<u<4.7)
    else:
     nd=ImageDraw.Draw(c);nd.rounded_rectangle((x+76,py+76,x+864,py+80+round(379*780/536)+4),radius=20,outline=BLUE,width=5)
     pill(nd,(x+105,py+640),'PINNED',20,CYAN)
   pill(ImageDraw.Draw(c),(110,600),'RIGHT-CLICK  →  ALWAYS ON TOP',21)
 elif idx==0:
  # Original typography and display-ring opener.
  center=(1400,490);d=ImageDraw.Draw(c)
  for k in range(5):
   r=170+k*45+int(12*math.sin(u*1.6-k*.3));d.ellipse((center[0]-r,center[1]-r,center[0]+r,center[1]+r),outline=(25+k*5,51+k*5,86+k*6),width=2)
  c.alpha_composite(icon.resize((180,180),Image.Resampling.LANCZOS),(1310,400))
  text(d,(110,253),LABELS[0],22,CYAN,True);text(d,(102,310),'Your screen.\nYour rules.',122,WHITE,True)
  text(d,(110,662),'Refresh rate. Brightness. Focus.',32,MUTED)
 else:
  # Final card settles completely by 53 seconds; URL remains static for the final four seconds.
  d=ImageDraw.Draw(c);c.alpha_composite(icon.resize((106,106),Image.Resampling.LANCZOS),(398,225))
  text(d,(535,215),'HzSwitcher',96,WHITE,True)
  text(d,(400,380),'Your screen. Your rules.',74,WHITE,True)
  text(d,(402,505),'Refresh rate  /  Brightness  /  Always on top',31,MUTED)
  d.rounded_rectangle((400,600,1515,701),radius=20,fill=BLUE)
  text(d,(448,620),'Get HzSwitcher on GitHub',42,(5,16,35),True)
  text(d,(402,738),'github.com/Azrinfreeman/HzSwitcher',39,WHITE)
  text(d,(402,806),'Windows 10 & 11  ·  x64',25,MUTED)
 # Narration subtitles, clear of the UI demonstration.
 active=next((s for a,b,s in captions if a<=t<b),None)
 if active:
  d=ImageDraw.Draw(c);lines=wrap(d,active,1610,32)
  d.rounded_rectangle((105,925,1815,925+len(lines)*42+20),radius=16,fill=(8,13,23,244),outline=(38,53,76),width=1)
  for j,line in enumerate(lines):
   tw=d.textlength(line,font=font(32));text(d,((W-tw)/2,933+j*42),line,32,WHITE)
 # Scene position line; frozen during the final 4-second end-card hold.
 d=ImageDraw.Draw(c);d.line((110,1046,1810,1046),fill=(35,46,64),width=3)
 position=min(t/53,1);d.line((110,1046,110+1700*position,1046),fill=BLUE,width=3)
 # Eight-frame dissolve-in from the background at every cut, no black flash.
 if idx>0 and u<.16:
  c=Image.blend(base,c,ease(u/.16))
 return c.convert('RGB')

def render():
 output=ROOT/'video-silent.mp4'
 command=[FFMPEG,'-hide_banner','-loglevel','error','-y','-f','rawvideo','-vcodec','rawvideo','-pix_fmt','rgb24','-s','1920x1080','-r','60','-i','-','-an','-c:v','libx264','-preset','fast','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(output)]
 p=subprocess.Popen(command,stdin=subprocess.PIPE)
 for i in range(3420):
  im=frame(i/60)
  if i in [120,420,840,1260,1740,2220,2700,3300]:im.save(QA/f'frame-{i:04}.jpg',quality=94)
  p.stdin.write(im.tobytes())
  if i%300==0:print(f'Rendered {i}/3420 frames',flush=True)
 p.stdin.close()
 if p.wait()!=0:raise RuntimeError('Video encode failed')
 print('Silent video complete',flush=True)
if __name__=='__main__':
 if '--stills' in sys.argv:
  for t in [2,7,14,21,29,37,45,55]:frame(t).save(QA/f'preview-{t:02}.jpg',quality=94)
 else:render()
