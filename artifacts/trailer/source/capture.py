import pathlib, tempfile, subprocess, shutil
root=pathlib.Path(__file__).resolve().parents[3]
work=pathlib.Path(tempfile.mkdtemp(prefix='hz-trailer-capture-'))
source=root/'artifacts/trailer/source'
shutil.copyfile(source/'Capture.cs.txt',work/'Capture.cs')
files=[p for p in root.rglob('*.cs') if not any(x in ('bin','obj','artifacts','tests','.git') for x in p.relative_to(root).parts) and p.name not in ('Program.cs','TrayApp.cs')]
items=''.join(f'<Compile Include="{p}" />' for p in files)
(work/'Capture.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><Version>1.7.2</Version><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ApplicationIcon>{root/'Assets/app.ico'}</ApplicationIcon></PropertyGroup><ItemGroup><Compile Include="Capture.cs"/>{items}<PackageReference Include="NAudio" Version="2.2.1"/><PackageReference Include="System.Management" Version="10.0.0"/></ItemGroup></Project>''')
subprocess.run(['dotnet','run','--project',str(work/'Capture.csproj'),'--',str(root/'artifacts/trailer/assets')],check=True)
