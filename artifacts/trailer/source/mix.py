from pathlib import Path
import subprocess,json,wave
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[1]; FFMPEG=imageio_ffmpeg.get_ffmpeg_exe(); scenes=json.loads((ROOT/'source/timeline.json').read_text())
cmd=[FFMPEG,'-hide_banner','-y','-i',str(ROOT/'audio/original-music.wav')]
for i in range(8):cmd+=['-i',str(ROOT/'audio'/f'voice-{i}.wav')]
filters=[]
for i,s in enumerate(scenes):
 delay=round((s['start']+.25)*1000)
 filters.append(f'[{i+1}:a]aresample=48000,highpass=f=75,loudnorm=I=-17:TP=-2:LRA=7,adelay={delay}:all=1[v{i}]')
filters.append(''.join(f'[v{i}]' for i in range(8))+'amix=inputs=8:normalize=0,apad,atrim=duration=57,asplit=2[voice][detector]')
filters.append('[0:a]volume=0.65[bed]')
filters.append('[bed][detector]sidechaincompress=threshold=0.025:ratio=6:attack=15:release=350[ducked]')
filters.append('[ducked][voice]amix=inputs=2:normalize=0,loudnorm=I=-14:TP=-1.5:LRA=8,aresample=48000,atrim=duration=57[out]')
cmd+=['-filter_complex',';'.join(filters),'-map','[out]','-ac','2','-c:a','pcm_s16le',str(ROOT/'audio/mixed.wav')]
subprocess.run(cmd,check=True,stdout=subprocess.DEVNULL,stderr=(ROOT/'qa/audio-mix.log').open('w'))
print('Narration, original soundtrack and ducking mixed to 57 seconds.')
