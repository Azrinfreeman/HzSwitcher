import React from 'react';
import {AbsoluteFill,Img,staticFile,useCurrentFrame,useVideoConfig} from 'remotion';
import {Stage,Chrome,Copy,Panel,PageCam,Cursor,Headline,CamKey} from './components';
import {T,mix,ramp} from './theme';
import {Shot} from './timeline';

export type SceneProps={id:Shot['id'];title:string;subtitle:string;duration:number;accent?:string;titleSize?:number};
const box=(v:boolean)=>v?{x:64,y:760,w:952,h:760}:{x:850,y:200,w:974,h:720};
const crop=(w:number,h:number,x:number,y:number,cw:number,ch:number,frame=0):CamKey=>({frame,cx:x+cw/2,cy:y+ch/2,zoom:Math.min(w/cw,h/ch)});
const image=<T,>(array:readonly T[],n:number)=>array[Math.min(array.length-1,Math.max(0,n))];

const Opening:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;const b=box(v);
 // Original crane timing retained: 24f hold, 102f move, then settled brand hold.
 const far=crop(b.w,b.h,0,0,1560,1120,126);
 const close=crop(b.w,b.h,1100,300,440,300,0);
 const breath={...close,frame:24,zoom:close.zoom*1.015};
 return <Stage><Panel {...b} rot={mix(-8,0,ramp(f,24,102,T.camera))}><PageCam src="display240" w={b.w} h={b.h} keys={[close,breath,far]}/></Panel><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize}/><Chrome/></Stage>;
};

const Rates:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;
 const b=v?{x:64,y:765,w:952,h:650}:{x:96,y:518,w:1728,h:410};
 const z=b.w/1180;
 const p=ramp(f,20,20,T.fly); const cx=mix(870,932,p),cy=420;
 const cur=ramp(f,18,16,T.out);const target={x:657,y:474};
 const wx=mix(1040,target.x,cur),wy=mix(620,target.y,cur)+Math.sin(Math.PI*cur)*28;
 const x=b.w/2+(wx-cx)*z,y=b.h/2+(wy-cy)*z;
 return <Stage><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize} top={v?235:155}/><Panel {...b}><PageCam src={f>=77?'display120':'display240'} w={b.w} h={b.h} keys={[{frame:0,cx,cy,zoom:z}]} frame={0}/><Cursor x={x} y={y} clickAt={75}/></Panel><Chrome/></Stage>;
};

const Shortcuts:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;
 const b=box(v);const phase=f<42;const p=ramp(f,42,16,T.smooth);
 return <Stage><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize}/><Panel {...b}>
 <div style={{position:'absolute',inset:0,opacity:1-p}}><PageCam src="display240" w={b.w} h={b.h} keys={[crop(b.w,b.h,340,620,1200,220)]}/></div>
 <div style={{position:'absolute',inset:0,opacity:p}}><PageCam src="shortcuts" w={b.w} h={b.h} keys={[crop(b.w,b.h,330,120,1200,460)]}/></div>
 </Panel><div style={{position:'absolute',left:v?72:98,top:v?1580:710,display:'flex',gap:v?16:18,opacity:ramp(f,34,18)}}>{['Ctrl','Alt','2'].map((key,i)=>{const press=1-ramp(Math.abs(f-60),0,6);return <div key={key} style={{fontSize:v?54:48,fontWeight:600,borderRadius:14,padding:'18px 25px',background:T.raised,border:'1px solid #ffffff2b',boxShadow:`0 ${6-press*5}px 0 #090a0b`,transform:`translateY(${press*5+(1-ramp(f,34+i*4,18))*30}px)`}}>{key}</div>;})}</div><Chrome/></Stage>;
};

const Profiles:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;
 const b=v?{x:64,y:740,w:952,h:680}:{x:96,y:520,w:1728,h:360};
 const slide=ramp(f,75,26,T.fly);
 return <Stage><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize} top={v?235:155}/><Panel {...b}>
 <div style={{position:'absolute',inset:0,transform:`translateX(${-slide*b.w}px)`}}><PageCam src="profiles" pageW={1360} pageH={840} w={b.w} h={b.h} keys={[crop(b.w,b.h,35,210,1260,290)]}/></div>
 <div style={{position:'absolute',inset:0,transform:`translateX(${(1-slide)*b.w}px)`}}><PageCam src="profileactive" w={b.w} h={b.h} keys={[crop(b.w,b.h,345,300,1170,310)]}/></div>
 </Panel><div style={{position:'absolute',left:v?72:100,top:v?1490:930,fontSize:40,color:T.muted,opacity:ramp(f,98,16)}}>Notepad → 120 Hz</div><Chrome/></Stage>;
};

const Brightness:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;
 const b=v?{x:64,y:800,w:952,h:230}:{x:96,y:515,w:1728,h:390};
 const p=ramp(f,45,45,T.smooth);const level=70-Math.round(p*15)*2;
 const key=crop(b.w,b.h,350,790,1170,265);
 const dragX=488+888*mix(.7,.4,p),dragY=961;
 return <Stage><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize} top={v?235:155}/><Panel {...b}><PageCam src={`brightness${level}`} w={b.w} h={b.h} keys={[key]}/><Cursor x={b.w/2+(dragX-key.cx)*key.zoom} y={b.h/2+(dragY-key.cy)*key.zoom} clickAt={45}/></Panel><Chrome/></Stage>;
};

const Saver:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;const b=box(v);
 const settings=f<75;const src=settings?'automation':f<135?'saver':'restored';
 const key=settings?crop(b.w,b.h,335,270,1180,440):crop(b.w,b.h,350,300,1170,330);
 return <Stage><Copy title={title} subtitle={subtitle} accent={f>=75&&f<135?T.green:accent} titleSize={titleSize}/><Panel {...b}><PageCam src={src} w={b.w} h={b.h} keys={[key]}/></Panel><div style={{position:'absolute',left:v?72:98,top:v?1570:760,color:f>=75&&f<135?T.green:T.muted,fontSize:42,opacity:ramp(f,62,13)}}>{f<135?'Saver active · 60 Hz':'Previous rate restored · 240 Hz'}</div><Chrome annotation="Time compressed"/></Stage>;
};

const Pause:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;const b=v?{x:64,y:800,w:952,h:210}:{x:96,y:550,w:1728,h:250};
 const key=crop(b.w,b.h,335,145,1180,170);
 return <Stage light={false}><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize} top={v?260:170}/><Panel {...b} opacity={ramp(f,0,30)}><PageCam src="paused" w={b.w} h={b.h} keys={[key]}/></Panel><div style={{position:'absolute',left:v?72:96,top:v?1060:830,fontSize:40,color:T.muted,opacity:ramp(f,40,20)}}>15 minutes · 1 hour · Until restart</div><Chrome/></Stage>;
};

const Pin:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;
 const x=v?72:900,y=v?790:265,w=v?900:900,h=600;
 const rear=ramp(f,48,65,T.smooth);
 return <Stage><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize??(v?94:88)}/>
 <div style={{position:'absolute',left:x-80+200*rear,top:y-50,width:w,height:h,borderRadius:18,background:'#35383d',boxShadow:'0 20px 60px #0007',transform:`perspective(1800px) rotateY(${-8+8*rear}deg)`}}><div style={{padding:36,fontSize:40,color:T.muted}}>Workspace</div></div>
 <div style={{position:'absolute',left:x,top:y,width:w,height:h,borderRadius:16,overflow:'hidden',boxShadow:`0 0 0 ${f>=60?4:1}px ${f>=60?(accent??T.accent):'#50555d'},0 28px 80px #0009`,transform:`translateY(${-16*ramp(f,60,24)}px)`}}><Img src={staticFile('screens/notes.png')} style={{width:'100%',height:'100%'}}/></div>
 <div style={{position:'absolute',left:v?72:98,top:v?1480:740,width:v?900:650,height:v?190:180,borderRadius:14,overflow:'hidden',opacity:ramp(f,12,24)}}><PageCam src="general" w={v?900:650} h={180} keys={[crop(v?900:650,180,335,495,1180,200)]}/></div><Chrome/></Stage>;
};

const Diagnostics:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;const b=box(v);
 const a=crop(b.w,b.h,45,170,1240,820,0),z=crop(b.w,b.h,45,210,1240,820,75);
 return <Stage><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize}/><Panel {...b}><PageCam src="diagnostics" pageW={1320} pageH={1200} w={b.w} h={b.h} keys={[a,z]}/></Panel><Chrome/></Stage>;
};

const Ensemble:React.FC<SceneProps>=({title,subtitle,accent,titleSize})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;
 const items=v?[{x:80,y:790,w:920,h:260,src:'display240',c:[350,300,1170,290]},{x:80,y:1080,w:920,h:230,src:'brightness40',c:[350,790,1170,265]},{x:80,y:1340,w:920,h:220,src:'paused',c:[335,145,1180,235]}]:[{x:875,y:170,w:925,h:270,src:'display240',c:[350,300,1170,290]},{x:875,y:470,w:925,h:230,src:'brightness40',c:[350,790,1170,265]},{x:875,y:730,w:925,h:220,src:'paused',c:[335,145,1180,235]}];
 return <Stage><Copy title={title} subtitle={subtitle} accent={accent} titleSize={titleSize}/>{items.map((it,i)=>{const p=ramp(f,8+i*7,40,T.fly);return <Panel key={it.src} x={it.x+(1-p)*(i%2?150:-150)} y={it.y+(1-p)*70} w={it.w} h={it.h} opacity={p}><PageCam src={it.src} w={it.w} h={it.h} keys={[crop(it.w,it.h,it.c[0],it.c[1],it.c[2],it.c[3])]}/></Panel>;})}<Chrome/></Stage>;
};

const Outro:React.FC<SceneProps>=({subtitle,accent})=>{
 const f=useCurrentFrame();const{width,height}=useVideoConfig();const v=height>width;
 const t=Math.min(1,f/131); const shrink=ramp(t,.02,.26,T.smooth); const brake=Math.sin(ramp(t,.26,.11,T.smooth)*Math.PI)*.06;
 const scale=mix(5.4,1,shrink)*(1+brake);const shift=ramp(t,.34,.13,T.smooth);
 const icon=v?132:142; const wordWidth=v?655:760;const gap=30;const total=icon+gap+wordWidth;
 const left=(width-total)/2;const top=v?760:440;
 return <Stage><div style={{position:'absolute',left:mix(width/2-icon/2,left,shift),top,width:icon,height:icon,transform:`scale(${scale})`,transformOrigin:'50% 50%'}}><Img src={staticFile('icon.png')} style={{width:'100%',height:'100%'}}/></div>
 <div style={{position:'absolute',left:left+icon+gap,top:top+3,height:icon,display:'flex',alignItems:'center',fontSize:v?110:128,fontWeight:600,letterSpacing:'-.05em'}}>{[...'HzSwitcher'].map((ch,i)=>{const p=ramp(t,.46+i*(.24/10),.10,T.out);return <span key={i} style={{opacity:p,transform:`translateX(${(1-p)*34}px)`,filter:p<.999?`blur(${(1-p)*10}px)`:'none'}}>{ch}</span>;})}</div>
 <div style={{position:'absolute',left:72,right:72,top:top+icon+56,textAlign:'center',fontSize:v?42:44,color:accent??T.accent,opacity:ramp(t,.72,.12,T.smooth)}}>{subtitle}</div>
 <div style={{position:'absolute',left:72,right:72,top:top+icon+148,textAlign:'center',fontSize:v?40:40,color:T.muted,opacity:ramp(f,108,18)}}>Explore HzSwitcher on GitHub</div>
 <div style={{position:'absolute',left:72,right:72,top:top+icon+214,textAlign:'center',fontSize:v?36:38,color:T.text,opacity:ramp(f,108,18)}}>github.com/Azrinfreeman/HzSwitcher</div>
 <div style={{position:'absolute',bottom:v?260:106,left:72,right:72,textAlign:'center',fontSize:34,color:T.muted,opacity:ramp(f,108,18)}}>Windows 10 &amp; 11 · x64</div></Stage>;
};

const COMPONENTS={opening:Opening,rates:Rates,shortcuts:Shortcuts,profiles:Profiles,brightness:Brightness,saver:Saver,pause:Pause,pin:Pin,diagnostics:Diagnostics,ensemble:Ensemble,outro:Outro};
export const Scene:React.FC<SceneProps>=(props)=>{const Component=COMPONENTS[props.id];return <Component {...props}/>;};
