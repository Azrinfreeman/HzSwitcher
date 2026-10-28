import {Easing, interpolate} from 'remotion';

export const T = {
  fps:30, total:1650, bg:'#17181B', deep:'#111214', surface:'#292B2F', raised:'#35383D',
  text:'#F1F3F4', muted:'#B5BAC1', accent:'#4F8CFF', green:'#72D491', border:'#50555D',
  font:'"Segoe UI Variable Text", "Segoe UI", sans-serif',
  camera:Easing.bezier(0.2,0,0.25,1), fly:Easing.bezier(0.55,0,0.18,1),
  out:(p:number)=>1-Math.pow(1-p,5), smooth:(p:number)=>p*p*(3-2*p),
};
export const ramp=(frame:number,start:number,duration:number,ease=T.out)=>interpolate(frame,[start,start+duration],[0,1],{extrapolateLeft:'clamp',extrapolateRight:'clamp',easing:ease});
export const mix=(a:number,b:number,p:number)=>a+(b-a)*p;
export const beatF=(n:number)=>Math.round(n*15);
