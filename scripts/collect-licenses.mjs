import fs from 'node:fs';
import path from 'node:path';
const root=path.resolve(import.meta.dirname,'..');
const lock=JSON.parse(fs.readFileSync(path.join(root,'ui/package-lock.json'),'utf8'));
let output='SetupPulse third-party licenses\nGenerated from installed production npm packages.\n\n';
for(const [location,info] of Object.entries(lock.packages)){
 if(!location||info.dev)continue;
 const dir=path.join(root,'ui',location);if(!fs.existsSync(dir))continue;
 const pkg=JSON.parse(fs.readFileSync(path.join(dir,'package.json'),'utf8'));
 output+=`\n${'='.repeat(72)}\n${pkg.name} ${pkg.version} — ${info.license||pkg.license||'see below'}\n`;
 const files=fs.readdirSync(dir).filter(f=>/^(licen[sc]e|copying|notice)(\.|$)/i.test(f));
 if(!files.length)output+='License declaration: '+(pkg.license||info.license)+'\n';
 for(const file of files)if(fs.statSync(path.join(dir,file)).isFile())output+=fs.readFileSync(path.join(dir,file),'utf8')+'\n';
}
const additions=path.join(root,'licenses');
if(fs.existsSync(additions))for(const file of fs.readdirSync(additions))output+=`\n${'='.repeat(72)}\n${file}\n`+fs.readFileSync(path.join(additions,file),'utf8')+'\n';
fs.writeFileSync(path.join(root,'THIRD-PARTY-LICENSES.txt'),output);
