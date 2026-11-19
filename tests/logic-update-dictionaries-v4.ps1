$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Execute exact updater extraction/validation/planning methods with real ZIPs and
# file transactions, without network, registry, GUI or a Windows OCR dependency.
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $root 'src/ApplicationUpdater.cs'))).GetRoot()
$class = @($tree.DescendantNodes() | Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'ApplicationUpdater' })[0]
$names = @('IsPackageFile','ExtractRequiredFiles','ValidateManifest','IsChildPath','RejectReparsePoints','ComputeSha256','BuildInstallPlan')
$body = ''
foreach ($member in $class.Members) {
    if ($member -is [Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax] -and @($member.Declaration.Variables | Where-Object { $_.Identifier.ValueText -in @('PackageFiles','OptionalPackageFiles') }).Count) { $body += $member.ToFullString() }
    elseif ($member -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $member.Identifier.ValueText -in $names) { $body += $member.ToFullString() }
}
$manifest = @($tree.DescendantNodes() | Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'ApplicationUpdateManifest' })[0].ToFullString()
$source = 'using System;using System.IO;using System.IO.Compression;using System.Collections.Generic;using System.Security.Cryptography;using System.Text;using System.Text.RegularExpressions; namespace MapleOverlay {' + $manifest + 'public static partial class ApplicationUpdater {' + $body + '}}'
$source += [IO.File]::ReadAllText((Join-Path $root 'src/UpdateFileTransaction.cs')) -replace '(?m)^using .*;\r?\n',''
$source += [IO.File]::ReadAllText((Join-Path $root 'src/StrongOcrComponent.cs')) -replace '(?m)^using .*;\r?\n',''
$source += @'
namespace MapleOverlay {
 public static partial class ApplicationUpdater {
 public static class DictionaryUpdateProbe {
  static int checks;
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS: "+text);}
  static void Zip(string path,string[] names){using(var z=ZipFile.Open(path,ZipArchiveMode.Create)){foreach(var name in names){using(var writer=new StreamWriter(z.CreateEntry(name).Open()))writer.Write("new:"+name);}}}
  static bool Reject(Action run){try{run();return false;}catch(InvalidDataException){return true;}}
  public static int Run(string root){
   string[] legacy={"枫语幕.exe","枫语幕词库.tsv","使用说明.txt"};
   string[] current={"枫语幕.exe","枫语幕词库.tsv","使用说明.txt","基础补充词库.tsv","任务正文.tsv"};
   Directory.CreateDirectory(root);
   string zip=Path.Combine(root,"current.zip"),install=Path.Combine(root,"install"),stage=Path.Combine(install,"stage"),backup=Path.Combine(install,"backup");
   Directory.CreateDirectory(stage);Zip(zip,current);ApplicationUpdater.ExtractRequiredFiles(zip,stage);
   Check(File.Exists(Path.Combine(stage,current[3]))&&File.Exists(Path.Combine(stage,current[4])),"both supplemental dictionaries are extracted");
   var m=new ApplicationUpdateManifest{InstallDirectory=install,StageDirectory=stage,BackupDirectory=backup,Tag="v4.0",Hashes=new Dictionary<string,string>()};
   foreach(string name in current)m.Hashes.Add(name,ApplicationUpdater.ComputeSha256(Path.Combine(stage,name)));
   string manifest=Path.Combine(install,"update","manifest.json");ApplicationUpdater.ValidateManifest(m,manifest);
   Check(true,"five-file manifest passes actual path/hash validation");
   File.WriteAllText(Path.Combine(install,legacy[1]),"custom");File.WriteAllText(Path.Combine(install,current[3]),"old supplement");
   var plan=ApplicationUpdater.BuildInstallPlan(m);UpdateFileTransaction.Execute(install,stage,backup,plan);
   Check(File.ReadAllText(Path.Combine(install,legacy[1]))=="custom","active user dictionary is preserved");
   Check(File.ReadAllText(Path.Combine(install,"枫语幕官方词库-v4.0.tsv"))=="new:"+legacy[1],"new official main dictionary is saved separately");
   Check(File.ReadAllText(Path.Combine(install,current[3]))=="new:"+current[3]&&File.ReadAllText(Path.Combine(backup,current[3]))=="old supplement","supplement is replaced with backup");
   Check(File.ReadAllText(Path.Combine(install,current[4]))=="new:"+current[4],"missing task dictionary is installed");
   File.WriteAllText(Path.Combine(stage,current[4]),"tampered");Check(Reject(()=>ApplicationUpdater.ValidateManifest(m,manifest)),"tampered task dictionary fails before installation");
   string oldZip=Path.Combine(root,"legacy.zip"),oldStage=Path.Combine(root,"legacy");Directory.CreateDirectory(oldStage);Zip(oldZip,legacy);ApplicationUpdater.ExtractRequiredFiles(oldZip,oldStage);
   Check(Directory.GetFiles(oldStage).Length==3,"legacy three-file packages remain compatible");
   string dupe=Path.Combine(root,"dupe.zip"),dupeStage=Path.Combine(root,"dupe");Directory.CreateDirectory(dupeStage);Zip(dupe,new[]{legacy[0],legacy[1],legacy[2],current[3],current[3]});
   Check(Reject(()=>ApplicationUpdater.ExtractRequiredFiles(dupe,dupeStage)),"duplicate supplemental entry is rejected");
   string missing=Path.Combine(root,"missing.zip"),missingStage=Path.Combine(root,"missing");Directory.CreateDirectory(missingStage);Zip(missing,new[]{legacy[0],current[3],current[4]});
   Check(Reject(()=>ApplicationUpdater.ExtractRequiredFiles(missing,missingStage)),"supplements cannot substitute for required main files");
   return checks;
  }
 }
 }
}
'@
Add-Type -TypeDefinition $source
$temporary = Join-Path $root ('.bench/update-dictionaries-' + [Guid]::NewGuid().ToString('N'))
try {
    $count = [MapleOverlay.ApplicationUpdater+DictionaryUpdateProbe]::Run($temporary)
    if ($count -ne 10) { throw "Unexpected check count: $count" }
    Write-Output 'Update dictionaries: 10/10 passed; real filesystem/ZIP/production validation, no network or GUI.'
} finally { if (Test-Path $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
