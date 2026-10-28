import {beatF} from './theme';

const DATA = [
  {id:'opening', beats:12, title:'Every hertz.\nOn your terms.', subtitle:'Windows display control.', visual:'display240'},
  {id:'rates', beats:10, title:'Change your\nrefresh rate.', subtitle:'One click. A different pace.', visual:'display240'},
  {id:'shortcuts', beats:8, title:'A preset.\nA shortcut.', subtitle:'Your settings, within reach.', visual:'shortcuts'},
  {id:'profiles', beats:10, title:'A rate for\neach app.', subtitle:'App profiles', visual:'profiles'},
  {id:'brightness', beats:10, title:'Brightness,\nwithin reach.', subtitle:'Control the selected display.', visual:'brightness70'},
  {id:'saver', beats:12, title:'Step away.\nLet it ease down.', subtitle:'Automatic power saver', visual:'automation'},
  {id:'pause', beats:8, title:'Pause on\nyour terms.', subtitle:'Automation can wait.', visual:'paused'},
  {id:'pin', beats:10, title:'Keep what matters\non top.', subtitle:'Always on top', visual:'general'},
  {id:'diagnostics', beats:10, title:"See what's\nhappening.", subtitle:'Local diagnostics. Read-only.', visual:'diagnostics'},
  {id:'ensemble', beats:8, title:'Small app.\nMore control.', subtitle:'Made for your workflow.', visual:'display240'},
  {id:'outro', beats:12, title:'HzSwitcher', subtitle:'Built for your Windows display.', visual:'icon'},
] as const;
let beat=0;
export const SHOTS=DATA.map(s=>{const shot={...s,from:beatF(beat),duration:beatF(s.beats)};beat+=s.beats;return shot;});
export type Shot=typeof SHOTS[number];
export const TOTAL=beatF(beat);
export const at=(id:Shot['id'],offset=0)=>SHOTS.find(s=>s.id===id)!.from+offset;
export const SFX_CUES=[
  {id:'reveal', from:at('opening',24), duration:48, src:'audio/transition-soft.mp3', volume:0.34},
  {id:'rateclick', from:at('rates',75), duration:12, src:'audio/switch-click-quick.mp3', volume:0.55},
  {id:'keytap', from:at('shortcuts',60), duration:12, src:'audio/switch-click-quick.mp3', volume:0.44},
  {id:'profileslide', from:at('profiles',75), duration:25, src:'audio/sweep-fast-small.mp3', volume:0.24},
  {id:'sliderstart', from:at('brightness',45), duration:12, src:'audio/switch-click-quick.mp3', volume:0.28},
  {id:'saverdrop', from:at('saver',75), duration:40, src:'audio/bass-hit-short.mp3', volume:0.17},
  {id:'saverrestore', from:at('saver',135), duration:25, src:'audio/sweep-fast-small.mp3', volume:0.21},
  {id:'pausestop', from:at('pause',30), duration:12, src:'audio/switch-click-quick.mp3', volume:0.38},
  {id:'pin', from:at('pin',60), duration:12, src:'audio/switch-click-quick.mp3', volume:0.48},
  {id:'diagnostics', from:at('diagnostics',30), duration:12, src:'audio/switch-click-quick.mp3', volume:0.32},
  {id:'ensemble', from:at('ensemble',15), duration:48, src:'audio/transition-soft.mp3', volume:0.28},
  {id:'lockup', from:at('outro',37), duration:45, src:'audio/bass-hit-short.mp3', volume:0.25},
];
export const COPY_SCHEMA=[
  {type:'textarea',key:'title',label:'Headline',default:''},
  {type:'text',key:'subtitle',label:'Supporting line',default:''},
  {type:'color',key:'accent',label:'Accent',default:'#4F8CFF'},
  {type:'number',key:'titleSize',label:'Headline size',default:100,min:60,max:140,step:2},
] as const;
