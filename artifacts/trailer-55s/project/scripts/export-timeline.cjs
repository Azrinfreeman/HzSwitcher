const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),ts=require('typescript');
const root=path.resolve(__dirname,'..');
const js=ts.transpileModule(fs.readFileSync(path.join(root,'src/timeline.ts'),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
const context={exports:{},require:()=>({beatF:n=>Math.round(n*15)})};vm.runInNewContext(js,context);
fs.writeFileSync(path.join(root,'public/timeline.json'),JSON.stringify({fps:30,bpm:120,total:context.exports.TOTAL,shots:context.exports.SHOTS,sfx:context.exports.SFX_CUES},null,2));
console.log('Exported single-source timeline:',context.exports.TOTAL,'frames.');
