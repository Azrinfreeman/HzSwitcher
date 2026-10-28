import {Scene} from './scenes';
import {SHOTS,TOTAL,COPY_SCHEMA} from './timeline';
import {Film,Cut,TRANSITIONS} from './Film';
import {T} from './theme';
import cues from './audio-cues.json';

export const WORKBENCH={
 name:'HzSwitcher · Every hertz',fps:30,width:1920,height:1080,total:TOTAL,background:T.deep,
 revision:'55s-v1',
 shots:SHOTS.map(s=>({id:s.id,label:s.title.replace('\n',' '),from:s.from,duration:s.duration,component:Scene,
  props:{id:s.id,title:s.title,subtitle:s.subtitle,duration:s.duration,accent:T.accent,titleSize:s.id==='pin'?88:100},
  schema:COPY_SCHEMA,durationProp:'duration',cardId:s.id,cardName:s.id})),
 transitions:TRANSITIONS.map(t=>({...t,label:'Soft match cut',component:Cut,props:{duration:t.duration},durationProp:'duration'})),
 captions:[],overlays:[],
 sfx:cues.map(c=>({from:c.from,duration:c.duration,src:c.src,volume:c.volume})),
 bgm:[{from:0,duration:TOTAL,src:'audio/score.wav',volume:.73}],
 order:['transitions','captions','overlays'],original:Film,
};
