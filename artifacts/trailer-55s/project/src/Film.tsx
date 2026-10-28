import React from 'react';
import {AbsoluteFill,Audio,Sequence,staticFile,useCurrentFrame} from 'remotion';
import {SHOTS,SFX_CUES,TOTAL} from './timeline';
import {Scene} from './scenes';
import {T,ramp} from './theme';
import cues from './audio-cues.json';

export const TRANSITIONS=SHOTS.slice(1).filter(s=>['profiles','diagnostics','outro'].includes(s.id)).map(s=>({id:'cut-'+s.id,from:s.from-6,duration:12}));
export const Cut:React.FC<{duration?:number}>=({duration=12})=>{
 const f=useCurrentFrame();const p=f/(duration-1);const op=Math.sin(p*Math.PI)*.92;
 return <AbsoluteFill style={{background:T.deep,opacity:op}}/>;
};
export const Film:React.FC<{bgm?:boolean;sound?:boolean}>=({bgm=true,sound=true})=><AbsoluteFill style={{background:T.deep}}>
 {SHOTS.map(s=><Sequence key={s.id} from={s.from} durationInFrames={s.duration}><Scene id={s.id} title={s.title} subtitle={s.subtitle} duration={s.duration}/></Sequence>)}
 {TRANSITIONS.map(t=><Sequence key={t.id} from={t.from} durationInFrames={t.duration}><Cut duration={t.duration}/></Sequence>)}
 {sound&&bgm&&<Audio src={staticFile('audio/score.wav')} volume={0.73}/>}
 {sound&&cues.map(c=><Sequence key={c.id} from={c.from} durationInFrames={c.duration}><Audio src={staticFile(c.src)} volume={c.volume}/></Sequence>)}
</AbsoluteFill>;
