"""Render actual production WinForms views with safe, isolated state adapters."""
from pathlib import Path
import json, shutil, subprocess, tempfile

project = Path(__file__).resolve().parents[1]
root = project.parents[2]
work = Path(tempfile.mkdtemp(prefix='hz-film-capture-'))
shutil.copyfile(project/'capture/Capture.cs.txt', work/'Capture.cs')
files = [p for p in root.rglob('*.cs') if not any(x in ('bin','obj','artifacts','tests','.git') for x in p.relative_to(root).parts) and p.name not in ('Program.cs','TrayApp.cs')]
items = ''.join(f'<Compile Include="{p}" />' for p in files)
(work/'Capture.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><Version>1.8.0</Version><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ApplicationIcon>{root/'Assets/app.ico'}</ApplicationIcon></PropertyGroup><ItemGroup><Compile Include="Capture.cs"/>{items}<PackageReference Include="NAudio" Version="2.2.1"/><PackageReference Include="System.Management" Version="10.0.0"/></ItemGroup></Project>''', encoding='utf-8')
output = project/'public/screens'
output.mkdir(parents=True, exist_ok=True)
subprocess.run(['dotnet','run','--project',str(work/'Capture.csproj'),'--',str(output)], check=True)
from PIL import Image
Image.open(root/'Assets/app.ico').save(project/'public/icon.png')
print(json.dumps({'captureProject':str(work),'output':str(output)}, indent=2))
