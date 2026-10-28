from pathlib import Path
import json, shutil, urllib.request

project=Path(__file__).resolve().parents[1]
skill=Path(r'C:/Users/Melon/.agents/skills/video-shotcraft')
refs=project/'references'; refs.mkdir(exist_ok=True)
data=json.loads((skill/'gallery/api/library.json').read_text(encoding='utf-8'))
names=['crane-rise-reveal','cursor-flyover','blur-slide','logo-shrink-wordmark-lockup']
selected=[]
frontmatter=[]
for file in (skill/'references/shots').rglob('*.md'):
    text=file.read_text(encoding='utf-8')
    if text.startswith('---'): frontmatter.append({'source':str(file.relative_to(skill)), 'metadata':text.split('---',2)[1]})
(refs/'frontmatter-scan.json').write_text(json.dumps(frontmatter,ensure_ascii=False,indent=2),encoding='utf-8')
for name in names:
    card=next(c for c in data['cards'] if c['name']==name)
    assert card['styles'][0]['key']==name
    shutil.copy2(skill/card['source'],refs/(name+'.md'))
    demos=list((skill/'demos').rglob(name+'/*.tsx'))
    if not demos: demos=[p for p in (skill/'demos').rglob('*.tsx') if p.parent.name==name]
    assert len(demos)==1, demos
    shutil.copy2(demos[0],refs/demos[0].name)
    media=card['styles'][0]['media']['url'].split('?')[0].replace('./','')
    target=refs/(name+'.mp4')
    url='https://vincentwei1021.github.io/video-shotcraft/'+media
    try:
        if not target.exists(): urllib.request.urlretrieve(url,target)
        status='downloaded'
    except Exception as error: status='unavailable: '+str(error)
    selected.append({'name':name,'style':name,'card':card['source'],'demo':str(demos[0].relative_to(skill)), 'gallery':url,'referenceStatus':status})
(refs/'selection.json').write_text(json.dumps(selected,indent=2),encoding='utf-8')
for name in ['aesthetic-rules.md','final-review.md']:
    shutil.copy2(skill/'references'/name,refs/name)
lib=project/'src/lib'; lib.mkdir(parents=True,exist_ok=True)
shutil.copy2(skill/'assets/lib/PageCam.tsx',lib/'PageCam.reference.tsx.txt')
audio=project/'public/audio';audio.mkdir(parents=True,exist_ok=True)
for path in ['sfx/transition/sweep-fast-small.mp3','sfx/transition/transition-soft.mp3','sfx/ui/switch-click-quick.mp3','sfx/impact/bass-hit-short.mp3']:
    shutil.copy2(skill/'assets/audio'/path,audio/Path(path).name)
shutil.copy2(skill/'assets/audio/ATTRIBUTION.md',refs/'AUDIO-ATTRIBUTION.md')
print('Scanned',len(frontmatter),'shot recipes; prepared',len(selected),'validated reference variants and 4 attributed SFX.')
print(json.dumps(selected,indent=2))
