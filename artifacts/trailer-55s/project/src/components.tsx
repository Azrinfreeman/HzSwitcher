import React from 'react';
import {AbsoluteFill,Img,staticFile,useCurrentFrame,useVideoConfig} from 'remotion';
import {T,ramp,mix} from './theme';

export type CamKey={frame:number;cx:number;cy:number;zoom:number;rotY?:number;rotX?:number;rotZ?:number};
// Adapted from Shotcraft assets/lib/PageCam.tsx. Layout zoom retains native texture detail.
export const PageCam:React.FC<{src:string;pageW?:number;pageH?:number;w:number;h:number;keys:CamKey[];frame?:number;children?:React.ReactNode}>=({src,pageW=1560,pageH=1120,w,h,keys,frame:given,children})=>{
  const local=useCurrentFrame(); const f=given??local;
  let a=keys[0],b=keys[keys.length-1];
  if(f>=b.frame)a=b;
  else for(let i=0;i<keys.length-1;i++)if(f>=keys[i].frame&&f<keys[i+1].frame){a=keys[i];b=keys[i+1];break;}
  const p=a.frame===b.frame?1:ramp(f,a.frame,b.frame-a.frame,T.camera);
  const cx=mix(a.cx,b.cx,p),cy=mix(a.cy,b.cy,p),z=mix(a.zoom,b.zoom,p);
  const ry=mix(a.rotY??0,b.rotY??0,p),rx=mix(a.rotX??0,b.rotX??0,p),rz=mix(a.rotZ??0,b.rotZ??0,p);
  return <AbsoluteFill style={{overflow:'hidden',background:T.bg,perspective:2400,perspectiveOrigin:`${w/2}px ${h/2}px`}}>
    <div style={{position:'absolute',width:pageW,height:pageH,zoom:z,transformOrigin:`${cx}px ${cy}px`,transform:`translate(${w/2/z-cx}px,${h/2/z-cy}px) rotateY(${ry}deg) rotateX(${rx}deg) rotateZ(${rz}deg)`}}>
      <Img src={staticFile(`screens/${src}.png`)} style={{position:'absolute',width:pageW,height:pageH}}/>{children}
    </div>
  </AbsoluteFill>;
};

// Shared progress is the essential blur-slide relationship; see references/BlurSlide.tsx.
export const Headline:React.FC<{text:string;size?:number;accent?:string;start?:number;lastAccent?:boolean}>=({text,size=100,accent=T.accent,start=6,lastAccent=true})=>{
 const f=useCurrentFrame();let index=0;
 return <div style={{fontSize:size,lineHeight:1.06,letterSpacing:'-.045em',fontWeight:600}}>{text.split('\n').map((line,lineIndex)=><div key={lineIndex} style={{display:'flex',gap:'.23em',whiteSpace:'nowrap',color:lastAccent&&lineIndex===text.split('\n').length-1?accent:T.text}}>{line.split(' ').map(word=>{const i=index++;const p=ramp(f,start+i*4,word.length>7?30:22);return <span key={i} style={{display:'inline-block',opacity:p,transform:`translateY(${(1-p)*size*.38}px)`,filter:p>.999?'none':`blur(${(1-p)*Math.min(18,size*.12)}px)`}}>{word}</span>;})}</div>)}</div>;
};
export const Stage:React.FC<{children:React.ReactNode;light?:boolean}>=({children,light=true})=><AbsoluteFill style={{background:T.deep,color:T.text,fontFamily:T.font,overflow:'hidden'}}>
 <AbsoluteFill style={{background:light?'radial-gradient(ellipse at 70% 15%,#292b2f 0%,#17181b 48%,#111214 95%)':T.bg}}/>
 {children}
</AbsoluteFill>;

export const Chrome:React.FC<{demo?:boolean;annotation?:string}>=({demo=true,annotation})=>{
 const{width,height}=useVideoConfig(); const v=height>width;const inset=v?72:96;
 return <><div style={{position:'absolute',left:inset,top:v?110:65,display:'flex',alignItems:'center',gap:14,fontSize:42,fontWeight:600,letterSpacing:'-.02em'}}><Img src={staticFile('icon.png')} style={{width:46,height:46}}/>HzSwitcher</div>
 {demo&&<div style={{position:'absolute',left:inset,bottom:v?140:54,fontSize:v?38:40,color:T.muted,letterSpacing:'-.01em'}}>UI DEMO · simulated device state</div>}
 {annotation&&<div style={{position:'absolute',left:inset,bottom:v?194:102,fontSize:36,color:T.text}}>{annotation}</div>}</>;
};
export const Cursor:React.FC<{x:number;y:number;clickAt:number;frame?:number}>=({x,y,clickAt,frame:given})=>{
 const local=useCurrentFrame();const f=given??local;const age=f-clickAt;
 const press=age>=-2&&age<1?ramp(age,-2,3):age>=1&&age<8?1-ramp(age,1,6):0;
 const ripple=age>=0&&age<18?age/18:null;
 return <>{ripple!==null&&<div style={{position:'absolute',left:x-35,top:y-35,width:70,height:70,borderRadius:'50%',border:`2px solid ${T.accent}`,opacity:1-ripple,transform:`scale(${.3+1.2*T.out(ripple)})`}}/>}<svg width={44} height={52} viewBox="0 0 24 28" style={{position:'absolute',left:x-5,top:y-4,overflow:'visible',filter:'drop-shadow(0px 3px 4px #0008)',transform:`scale(${1-.16*press})`,transformOrigin:'5px 4px'}}><path d="M4.5 2.5 L4.5 21 L9 16 L12.2 24 L16 22.5 L12.6 15 L20 15 Z" fill="#f1f3f4" stroke="#17181b" strokeWidth="1.3" strokeLinejoin="round"/></svg></>;
};
export const Panel:React.FC<{x:number;y:number;w:number;h:number;children:React.ReactNode;rot?:number;opacity?:number}>=({x,y,w,h,children,rot=0,opacity=1})=><div style={{position:'absolute',left:x,top:y,width:w,height:h,borderRadius:22,overflow:'hidden',background:T.bg,boxShadow:'0 36px 90px #0007,0 0 0 1px #ffffff18',transform:`perspective(2500px) rotateY(${rot}deg)`,opacity}}>{children}</div>;

export const Copy:React.FC<{title:string;subtitle:string;accent?:string;titleSize?:number;top?:number}>=({title,subtitle,accent=T.accent,titleSize,top})=>{
 const f=useCurrentFrame(); const{width,height}=useVideoConfig();const v=height>width;const p=ramp(f,34,24);
 return <div style={{position:'absolute',left:v?72:96,top:top??(v?235:265),width:v?936:760}}><Headline text={title} size={titleSize??(v?104:100)} accent={accent}/><div style={{fontSize:v?40:38,lineHeight:1.3,color:T.muted,marginTop:36,opacity:p,transform:`translateY(${(1-p)*16}px)`}}>{subtitle}</div></div>;
};
