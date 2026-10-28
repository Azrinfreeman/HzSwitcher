const {bundle}=require('@remotion/bundler');
const {selectComposition,renderStill,openBrowser}=require('@remotion/renderer');
const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..');const out=path.resolve(root,'../qa');fs.mkdirSync(out,{recursive:true});
(async()=>{
 const url=await bundle({entryPoint:path.join(root,'src/index.ts')});
 const browser=await openBrowser('chrome');
 const frames=[100,270,410,560,700,870,1000,1150,1300,1440,1620];
 for(const id of ['Landscape','Vertical']){
  const comp=await selectComposition({serveUrl:url,id,puppeteerInstance:browser});
  for(const frame of frames){
   await renderStill({serveUrl:url,composition:comp,frame,output:path.join(out,`${id}-${frame}.png`),scale:.6,puppeteerInstance:browser,logLevel:'error'});
   console.log(id,frame);
  }
 }
 await browser.close({silent:true});
})().catch(e=>{console.error(e);process.exit(1)});
