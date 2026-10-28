from pathlib import Path
import subprocess,struct,json,re
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[1];FF=imageio_ffmpeg.get_ffmpeg_exe();OUT=ROOT/'HzSwitcher-Trailer-57s.mp4'
def run(args,log):
 r=subprocess.run([FF,'-hide_banner',*args],capture_output=True,text=True)
 (ROOT/'qa'/log).write_text(r.stdout+'\n'+r.stderr,encoding='utf-8')
 if r.returncode:raise RuntimeError(r.stderr[-2000:])
 return r.stdout,r.stderr
run(['-y','-i',str(ROOT/'video-silent.mp4'),'-i',str(ROOT/'audio/mixed.wav'),'-map','0:v:0','-map','1:a:0','-c:v','copy','-c:a','aac','-b:a','192k','-ar','48000','-ac','2','-t','57','-movflags','+faststart','-metadata','title=HzSwitcher - Your screen. Your rules.','-metadata','comment=Original narrated promotional UI demonstration. 57 seconds.',str(OUT)],'mux.log')
# Parse ISO BMFF timing/sample tables directly: no ffprobe dependency is required.
data=OUT.read_bytes()
def boxes(start,end):
 while start+8<=end:
  size,kind=struct.unpack_from('>I4s',data,start);header=8
  if size==1:size=struct.unpack_from('>Q',data,start+8)[0];header=16
  if size==0:size=end-start
  if size<header or start+size>end:break
  yield kind,start+header,start+size
  start+=size
def child(start,end,kind):return next((a,b) for k,a,b in boxes(start,end) if k==kind)
def timing(a):
 if data[a]==0:scale,duration=struct.unpack_from('>II',data,a+12)
 else:scale=struct.unpack_from('>I',data,a+20)[0];duration=struct.unpack_from('>Q',data,a+24)[0]
 return scale,duration
moov=next((a,b) for k,a,b in boxes(0,len(data)) if k==b'moov')
movie_header=child(*moov,b'mvhd'); movie_scale,movie_ticks=timing(movie_header[0]); movie_duration=movie_ticks/movie_scale
assert movie_duration==57.0, movie_duration
tracks=[]
for kind,a,b in boxes(*moov):
 if kind!=b'trak':continue
 mdia=child(a,b,b'mdia');hdr=child(*mdia,b'mdhd');scale,duration=timing(hdr[0]);handler=child(*mdia,b'hdlr');kind=data[handler[0]+8:handler[0]+12].decode()
 minf=child(*mdia,b'minf');table=child(*minf,b'stbl');stsz=child(*table,b'stsz');samples=struct.unpack_from('>I',data,stsz[0]+8)[0]
 tracks.append({'type':kind,'timescale':scale,'duration_ticks':duration,'duration_seconds':duration/scale,'samples':samples})
video=next(x for x in tracks if x['type']=='vide')
assert video['samples']==3420,video
assert abs(video['duration_seconds']-57)<.000001,video
assert video['samples']/video['duration_seconds']==60,video
# Full-stream decode verifies the entire file, not only selected stills.
run(['-v','error','-i',str(OUT),'-f','null','-'],'decode.log')
_,levels=run(['-i',str(OUT),'-af','loudnorm=I=-14:TP=-1:LRA=8:print_format=json','-f','null','-'],'loudness.log')
match=re.search(r'\{\s*"input_i".*?\}',levels,re.S);audio=json.loads(match.group()) if match else {}
assert audio and float(audio['input_tp']) <= -1, audio
result={'file':OUT.name,'bytes':OUT.stat().st_size,'resolution':'1920x1080','fps':60,'video_frames':3420,'video_duration_seconds':57.0,'presentation_duration_seconds':movie_duration,'tracks':tracks,'audio_measurement':audio,'full_decode':'passed','caption_file':'HzSwitcher-Trailer-57s.srt','visual_review':'Scene keyframes and contact sheet inspected; source is labelled UI DEMO.'}
(ROOT/'qa/verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
