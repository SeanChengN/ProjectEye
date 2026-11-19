using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;

internal sealed class Entry { public string path; public long bytes; public string sha256; }
internal sealed class Marker { public string product; public string version; }
internal sealed class RegValue { public string Name; public object Value; public string Kind; }
internal sealed class RegSnapshot { public string View; public string Name; public bool Exists; public RegValue[] Values; }
internal sealed class LinkSnapshot { public string Path; public string Backup; public bool Existed; public string BeforeHash; public string AfterHash; public bool Changed; }
// Read-only compatibility shape for recycling historical backups, never written.
internal sealed class LegacyTransaction
{
    public string Root; public string Backup; public string Id;
    public List<Entry> Before; public List<Entry> Written;
}
internal sealed class InstallState
{
    public string Product; public string Root; public string CandidateHash;
    public string PreviousManifest; public string StageName;
}
internal static class Engine
{
#if MAINTENANCE_TESTS
    private const string KeyRoot="Software\\ProjectEyeEnhanced-MaintenanceTests\\Uninstall\\";
#else
    private const string KeyRoot="Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\";
#endif
    private static readonly string[] Keys={"ProjectEyeEnhanced-Test","ProjectEyeEnhanced-beta1","ProjectEyeEnhanced-beta2"};
    private static readonly UTF8Encoding Utf8=new UTF8Encoding(false);
    private static readonly string Self=Assembly.GetExecutingAssembly().Location;
    internal static string RecoveryRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ProjectEyeEnhanced-Recovery"); } }
    internal static string Quote(string text) { if(text==null||text.Contains("\""))throw new ArgumentException("Invalid argument.");return "\""+text+"\""; }
    internal static T Read<T>(string path) { return new JavaScriptSerializer {MaxJsonLength=64*1024*1024}.Deserialize<T>(File.ReadAllText(path,Utf8)); }
    internal static void Write(string path,object value)
    { File.WriteAllText(path,new JavaScriptSerializer {MaxJsonLength=64*1024*1024}.Serialize(value),Utf8); }
    internal static void Result(string path,bool success,string message,string recovery,string code="OK")
    {
        if(string.IsNullOrEmpty(path))return;
        Write(path,new {success=success,code=code,message=message,recovery=recovery,utc=DateTime.UtcNow.ToString("o")});
        File.WriteAllText(path+".txt",message,Utf8);
    }
    internal static string Hash(string path)
    { using(var input=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-",""); }
    internal static string Normalize(string path)
    { if(string.IsNullOrWhiteSpace(path))throw new ArgumentException("Directory missing.");return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
    private static bool Same(string a,string b) { return string.Equals(Normalize(a),Normalize(b),StringComparison.OrdinalIgnoreCase); }
    private static bool Under(string path,string root) { return Normalize(path).StartsWith(Normalize(root)+"\\",StringComparison.OrdinalIgnoreCase); }
    internal static void NoLinks(string path)
    {
        string current=Path.GetFullPath(path);
        while(!string.IsNullOrEmpty(current))
        {
            if((Directory.Exists(current)||File.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse point refused: "+current);
            current=Path.GetDirectoryName(current);
        }
    }
    private static void SafeRoot(string root)
    {
        root=Normalize(root);NoLinks(root);
        if(root.StartsWith("\\\\",StringComparison.Ordinal))throw new IOException("Network installation directory refused.");
        if(root.Length<8||Same(root,Path.GetPathRoot(root)))throw new IOException("Unsafe installation directory.");
        foreach(string blocked in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.System),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RecoveryRoot})
            if(!string.IsNullOrEmpty(blocked)&&(Same(root,blocked)||Under(root,blocked)&&!Same(blocked,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))&&!Same(blocked,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))))throw new IOException("Protected directory refused: "+root);
    }
    private static string Child(string root,string relative)
    {
        if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Contains(":")||relative.Split('\\','/').Any(p=>p==".."||p=="."||p.Length==0))throw new IOException("Invalid manifest path.");
        string path=Path.GetFullPath(Path.Combine(root,relative));if(!Under(path,root))throw new IOException("Manifest path escapes root.");NoLinks(path);return path;
    }
    private static IEnumerable<string> Files(string root)
    {
        foreach(string path in Directory.GetFileSystemEntries(root))
        { NoLinks(path);if(Directory.Exists(path)){foreach(string file in Files(path))yield return file;}else yield return path; }
    }
    private static string Relative(string root,string file) { return file.Substring(Normalize(root).Length+1); }
    private static List<Entry> Snapshot(string root)
    { return Files(root).Select(p=>new Entry {path=Relative(root,p),bytes=new FileInfo(p).Length,sha256=Hash(p)}).ToList(); }
    private static Entry[] Trusted()
    { return new JavaScriptSerializer().Deserialize<Entry[]>(BuildIdentity.TrustedJson); }
    private static bool DataPath(string relative)
    { string first=relative.Split('\\','/')[0];return first.Equals("Data",StringComparison.OrdinalIgnoreCase)||first.Equals("Log",StringComparison.OrdinalIgnoreCase); }
    internal static Entry[] VerifyInstalled(string root,bool checkRoot=true)
    {
        if(checkRoot)SafeRoot(root);else NoLinks(root);var marker=Read<Marker>(Child(root,"test-package.json"));
        if(marker.product!="ProjectEye Enhanced"||marker.version!=BuildIdentity.Version)throw new IOException("Wrong product/version marker.");
        var manifest=Read<Entry[]>(Child(root,"payload-manifest.json"));
        var expected=Trusted().Concat(new[]{new Entry {path="ProjectEye.Maintenance.exe",sha256=Hash(Self),bytes=new FileInfo(Self).Length}}).ToArray();
        if(manifest.Length!=expected.Length||manifest.Select(e=>e.path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=manifest.Length)throw new IOException("Manifest shape mismatch.");
        foreach(var entry in manifest)
        {
            var trusted=expected.SingleOrDefault(e=>e.path.Equals(entry.path,StringComparison.OrdinalIgnoreCase));
            if(trusted==null||DataPath(entry.path)||trusted.sha256!=entry.sha256||trusted.bytes!=entry.bytes)throw new IOException("Untrusted payload manifest.");
            string path=Child(root,entry.path);
            if(!File.Exists(path)||new FileInfo(path).Length!=entry.bytes||Hash(path)!=entry.sha256)throw new IOException("Payload changed: "+entry.path);
        }
        return manifest;
    }
    internal static void VerifyExisting(string root,bool recovery=false)
    {
        if(!recovery)SafeRoot(root);
        var marker=Read<Marker>(Child(root,"test-package.json"));
        if(marker.product!="ProjectEye Enhanced")throw new IOException("Unrecognized installation.");
        if(marker.version==BuildIdentity.Version&&!BuildIdentity.LegacyManifestHashes.Contains(Hash(Child(root,"payload-manifest.json")))){VerifyInstalled(root,!recovery);return;}
        if(marker.version!=BuildIdentity.Version&&marker.version!="1.0.0-beta.1"&&marker.version!="1.0.0-beta.2"&&marker.version!="1.0.0-beta.3"&&marker.version!="1.0.0-beta.4"&&marker.version!="1.0.0-beta.5"&&marker.version!="1.0.0-beta.6")throw new IOException("Unsupported installed version.");
        string hash=Hash(Child(root,"payload-manifest.json"));
        if(!BuildIdentity.LegacyManifestHashes.Contains(hash))throw new IOException("Legacy manifest is not a known local release.");
        var manifest=Read<Entry[]>(Child(root,"payload-manifest.json"));
        foreach(var entry in manifest)
        {string p=Child(root,entry.path);if(DataPath(entry.path))throw new IOException("Unexpected legacy data manifest.");if(!File.Exists(p)||Hash(p)!=entry.sha256)throw new IOException("Legacy payload changed: "+entry.path);}
    }
    internal static void RequireStopped(string root)
    {
        using(var mutex=new System.Threading.Mutex(false,"projecteye-enhanced"))
        {
            bool acquired=false;
            try { try {acquired=mutex.WaitOne(0);}catch(System.Threading.AbandonedMutexException){acquired=true;} if(!acquired)throw new IOException("Exit ProjectEye Enhanced normally from its tray before continuing."); }
            finally {if(acquired)mutex.ReleaseMutex();}
        }
        foreach(var process in Process.GetProcessesByName("ProjectEye"))using(process)
        {try{if(Same(process.MainModule.FileName,Path.Combine(root,"ProjectEye.exe")))throw new IOException("Application is still running.");}catch(System.ComponentModel.Win32Exception){throw new IOException("Cannot verify running application. Close it normally.");}}
    }
    private static List<RegSnapshot> RegistryBefore()
    {
        var snapshots=new List<RegSnapshot>();
        foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})using(var baseKey=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view))foreach(var name in Keys)using(var key=baseKey.OpenSubKey(KeyRoot+name))
            snapshots.Add(new RegSnapshot {View=view.ToString(),Name=name,Exists=key!=null,Values=key==null?new RegValue[0]:key.GetValueNames().Select(n=>new RegValue{Name=n,Kind=key.GetValueKind(n).ToString(),Value=key.GetValue(n,null,RegistryValueOptions.DoNotExpandEnvironmentNames)}).ToArray()});
        return snapshots;
    }
    private static bool RegisteredHere(RegSnapshot item,string root)
    {var location=item.Values.FirstOrDefault(v=>v.Name=="InstallLocation");return item.Exists&&location!=null&&location.Value is string&&Same((string)location.Value,root);}
    private static void Register(string root,List<RegSnapshot> before)
    {
        foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})using(var key=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view))
        {
            using(var existing=key.OpenSubKey(KeyRoot+Keys[0]))if(existing!=null && !Same(existing.GetValue("InstallLocation","").ToString(),root))throw new IOException("Test channel is registered at another location.");
        }
        using(var baseKey=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var key=baseKey.CreateSubKey(KeyRoot+Keys[0]))
        {
            string nativeUninstaller=Path.Combine(root,"uninstall","unins000.exe");
            string command=File.Exists(nativeUninstaller)?Quote(nativeUninstaller):Quote(Path.Combine(root,"ProjectEye.Maintenance.exe"))+" --uninstall";
            key.SetValue("DisplayName","ProjectEye Enhanced "+BuildIdentity.Version);key.SetValue("DisplayVersion",BuildIdentity.Version);
            key.SetValue("InstallLocation",root);key.SetValue("DisplayIcon",Path.Combine(root,"ProjectEye.exe"));
            key.SetValue("UninstallString",command);key.SetValue("QuietUninstallString",command+(File.Exists(nativeUninstaller)?" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART":" --quiet"));key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
        }
        // HKCU views can alias: never delete the stable key after creating it.
        foreach(var item in before.Where(s=>RegisteredHere(s,root)&&s.Name!=Keys[0]))using(var baseKey=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,(RegistryView)Enum.Parse(typeof(RegistryView),item.View)))baseKey.DeleteSubKeyTree(KeyRoot+item.Name,false);
    }
    private static string[] LinkPaths()
    {
#if MAINTENANCE_TESTS
        return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ProjectEyeEnhanced-MaintenanceTests","ProjectEye Enhanced Test.lnk")};
#else
        return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ProjectEye Enhanced.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ProjectEye Enhanced Test.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ProjectEye Enhanced beta.1.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ProjectEye Enhanced beta.2.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"ProjectEye Enhanced.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"ProjectEye Enhanced.lnk")};
#endif
    }
    private static string LinkTarget(string path)
    {dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic link=shell.CreateShortcut(path);return (string)link.TargetPath;}
    private static List<LinkSnapshot> BackupLinks(string recovery)
    {
        var result=new List<LinkSnapshot>();int count=0;
        foreach(string path in LinkPaths())
        {NoLinks(path);bool exists=File.Exists(path);string copy=Path.Combine(recovery,"link-"+(count++)+".lnk");if(exists){File.Copy(path,copy);if(Hash(copy)!=Hash(path))throw new IOException("Shortcut backup mismatch.");}result.Add(new LinkSnapshot{Path=path,Existed=exists,Backup=exists?copy:null,BeforeHash=exists?Hash(copy):null});}
        return result;
    }
    private static void ChangeLinks(string root,List<LinkSnapshot> links,bool uninstall,Action checkpoint)
    {
        string target=Path.Combine(root,"ProjectEye.exe");string main=LinkPaths()[0];
        foreach(var item in links)
        {
            if(File.Exists(item.Path)&&!Same(LinkTarget(item.Path),target)) {if(item.Path==main&&!uninstall)throw new IOException("Shortcut belongs to another installation.");continue;}
            item.Changed=true;checkpoint();
            if(File.Exists(item.Path)&&((item.Path!=main&&!item.Path.Contains("Startup")&&!item.Path.Contains("Desktop"))||uninstall))Recycle(item.Path);
            else if(!uninstall&&(item.Path==main||File.Exists(item.Path)))
            {Directory.CreateDirectory(Path.GetDirectoryName(item.Path));dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic link=shell.CreateShortcut(item.Path);link.TargetPath=target;link.WorkingDirectory=root;link.IconLocation=target+",0";link.Save();}
            item.AfterHash=File.Exists(item.Path)?Hash(item.Path):null;
            checkpoint();
        }
    }
    private static void Recycle(string path)
    {
        NoLinks(path);
        if(File.Exists(path))FileSystem.DeleteFile(path,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);
        else if(Directory.Exists(path))FileSystem.DeleteDirectory(path,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);
    }
    private const string StateName=".projecteye-install-state.json";
    private static string HashText(string value)
    {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(value))).Replace("-","");}
    private static Entry[] ManifestEntries(string json)
    {
        var entries=new JavaScriptSerializer {MaxJsonLength=64*1024*1024}.Deserialize<Entry[]>(json);
        if(entries==null||entries.Length==0||entries.Select(e=>e.path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=entries.Length)throw new IOException("Untrusted previous manifest.");
        foreach(var entry in entries)if(DataPath(entry.path))throw new IOException("Untrusted data manifest.");
        return entries;
    }
    private static Entry[] OldEntries(string text,string candidateHash)
    {
        if(string.IsNullOrEmpty(text))return new Entry[0];
        string hash=HashText(text);
        if(hash!=candidateHash&&!BuildIdentity.LegacyManifestHashes.Contains(hash))throw new IOException("Untrusted previous manifest.");
        return ManifestEntries(text).Concat(new[]{new Entry{path="payload-manifest.json",sha256=hash,bytes=Utf8.GetByteCount(text)}}).ToArray();
    }
    private static bool Matches(string file,Entry entry)
    {return File.Exists(file)&&new FileInfo(file).Length==entry.bytes&&Hash(file)==entry.sha256;}
    private static bool CandidatePrefix(string partial,string complete)
    {
        using(var left=File.OpenRead(partial))using(var right=File.OpenRead(complete))
        {
            if(left.Length>right.Length)return false;
            var a=new byte[65536];var b=new byte[65536];int read;
            while((read=left.Read(a,0,a.Length))>0)
            {int count=0;while(count<read){int n=right.Read(b,count,read-count);if(n==0)return false;count+=n;}for(int i=0;i<read;i++)if(a[i]!=b[i])return false;}
            return true;
        }
    }
    private static void ValidatePending(string root,InstallState state,string candidateHash,Entry[] incoming,string source)
    {
        if(state.Product!="ProjectEye Enhanced"||!Same(state.Root,root)||state.CandidateHash!=candidateHash)throw new IOException("安装尚未完成，请重新运行同一个安装包；不要覆盖其他版本或目录。");
        Guid id;
        if(state.StageName==null||!state.StageName.StartsWith(".projecteye-stage-",StringComparison.Ordinal)||!Guid.TryParseExact(state.StageName.Substring(18),"N",out id))throw new IOException("Untrusted install stage.");
        var previous=OldEntries(state.PreviousManifest,candidateHash);
        foreach(var entry in previous.Concat(incoming).GroupBy(e=>e.path,StringComparer.OrdinalIgnoreCase))
        {
            string file=Child(root,entry.Key);
            if(Directory.Exists(file))throw new IOException("File path is a directory.");
            if(File.Exists(file)&&!entry.Any(e=>Matches(file,e)))throw new IOException("Interrupted payload changed: "+entry.Key);
        }
        string stage=Child(root,state.StageName);
        if(Directory.Exists(stage))
        {
            foreach(string file in Files(stage))
            {
                string relative=Relative(stage,file);
                if(!incoming.Any(e=>e.path.Equals(relative,StringComparison.OrdinalIgnoreCase)&&CandidatePrefix(file,Child(source,e.path))))throw new IOException("Untrusted staged file.");
            }
        }
    }
    internal static string Install(string source,string target,string failAt,bool deferFinalize=false)
    {
        source=Normalize(source);target=Normalize(target);SafeRoot(target);VerifyInstalled(source);RequireStopped(target);
        if(Same(source,target)||Under(source,target)||Under(target,source))throw new IOException("Source/target overlap.");
        string manifestText=File.ReadAllText(Child(source,"payload-manifest.json"),Utf8);
        string candidateHash=HashText(manifestText);
        var incoming=ManifestEntries(manifestText).Concat(new[]{new Entry{path="payload-manifest.json",sha256=candidateHash,bytes=Utf8.GetByteCount(manifestText)}}).ToArray();
        string stateFile=Child(target,StateName);InstallState state;
        if(File.Exists(stateFile))
        {
            state=Read<InstallState>(stateFile);ValidatePending(target,state,candidateHash,incoming,source);
        }
        else
        {
            string previous=null;
            if(Directory.Exists(target)&&Files(target).Any())
            {VerifyExisting(target);previous=File.ReadAllText(Child(target,"payload-manifest.json"),Utf8);}
            state=new InstallState{Product="ProjectEye Enhanced",Root=target,CandidateHash=candidateHash,PreviousManifest=previous,StageName=".projecteye-stage-"+Guid.NewGuid().ToString("N")};
        }
        var owned=OldEntries(state.PreviousManifest,candidateHash);
        foreach(var entry in incoming)
        {
            string file=Child(target,entry.path);
            if(Directory.Exists(file))throw new IOException("File path is a directory.");
            if(File.Exists(file)&&!owned.Any(e=>e.path.Equals(entry.path,StringComparison.OrdinalIgnoreCase))&&!File.Exists(stateFile))throw new IOException("Unknown file would be overwritten: "+entry.path);
        }
        var before=RegistryBefore();
        foreach(var item in before.Where(r=>r.Name==Keys[0]&&r.Exists))if(!RegisteredHere(item,target))throw new IOException("Existing test channel belongs to another directory.");
        foreach(string path in LinkPaths())
        {NoLinks(path);if(path==LinkPaths()[0]&&File.Exists(path)&&!Same(LinkTarget(path),Path.Combine(target,"ProjectEye.exe")))throw new IOException("Shortcut belongs to another installation.");}
        if(Directory.Exists(target))
        {
            // Enumerate for containment/reparse safety, but do not read or duplicate
            // user databases/logs: their bytes are outside the replacement set.
            foreach(string file in Files(target)){}
            foreach(var entry in owned.Concat(incoming).GroupBy(e=>e.path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()))
            {string file=Child(target,entry.path);if(File.Exists(file))using(File.Open(file,FileMode.Open,FileAccess.Read,FileShare.None)){} }
        }
        Directory.CreateDirectory(target);
        if(!File.Exists(stateFile))Write(stateFile,state);
        string stage=Child(target,state.StageName);
        if(Directory.Exists(stage))Recycle(stage);
        Directory.CreateDirectory(stage);
        try
        {
            foreach(var entry in incoming)
            {
                string file=Child(stage,entry.path);Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.Copy(Child(source,entry.path),file);if(!Matches(file,entry))throw new IOException("Candidate hash mismatch.");
                if(failAt=="copy")throw new IOException("Injected preparation copy failure.");
            }
            // All candidate files have been staged and verified before replacing any.
            foreach(var entry in incoming)
            {
                string destination=Child(target,entry.path);
                if(File.Exists(destination))
                {
                    var old=owned.FirstOrDefault(e=>e.path.Equals(entry.path,StringComparison.OrdinalIgnoreCase));
                    if(!Matches(destination,entry)&&(old==null||!Matches(destination,old)))throw new IOException("Payload changed during installation.");
                    Recycle(destination);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Move(Child(stage,entry.path),destination);
                if(failAt=="replace")throw new IOException("Injected replacement failure.");
#if MAINTENANCE_TESTS
                if(failAt=="interrupt")Environment.Exit(87);
#endif
            }
            foreach(string retired in BuildIdentity.RetiredFiles)
            {
                string file=Child(target,retired);var old=owned.FirstOrDefault(e=>e.path.Equals(retired,StringComparison.OrdinalIgnoreCase));
                if(File.Exists(file)&&old!=null&&Matches(file,old))Recycle(file);
            }
            VerifyInstalled(target);Register(target,before);
            var links=LinkPaths().Select(p=>new LinkSnapshot{Path=p}).ToList();ChangeLinks(target,links,false,()=>{});
            if(failAt=="registration")throw new IOException("Injected registration failure.");
            if(!deferFinalize)return FinishInstall(target);
            return "程序文件已安装，正在完成卸载入口登记。";
        }
        catch(Exception error){throw new IOException("安装未完成。请重新运行同一个安装包修复；配置和统计仍保留。",error);}
        finally {if(Directory.Exists(stage))Recycle(stage);}
    }
    private static string FinishInstall(string root)
    {
        VerifyInstalled(root);
        string state=Child(root,StateName);if(File.Exists(state))Recycle(state);
        int warnings=CleanupHistory(root);
        return warnings==0?"安装完成。":"安装完成。部分旧恢复记录无法确认归属或清理，已保留，请手动核对。";
    }
    private static void RestoreRegistry(List<RegSnapshot> snapshots,string target)
    {
        foreach(var saved in snapshots.Where(r=>RegisteredHere(r,target)||r.Name==Keys[0]&&!r.Exists))
        {
            using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,(RegistryView)Enum.Parse(typeof(RegistryView),saved.View)))
            {
                using(var now=root.OpenSubKey(KeyRoot+saved.Name))if(now!=null&&!Same(now.GetValue("InstallLocation","").ToString(),target))continue;
                if(saved.Exists){using(var key=root.CreateSubKey(KeyRoot+saved.Name)){foreach(string n in key.GetValueNames())key.DeleteValue(n);foreach(var value in saved.Values){var kind=(RegistryValueKind)Enum.Parse(typeof(RegistryValueKind),value.Kind);key.SetValue(value.Name,ConvertValue(value.Value,kind),kind);}}}
                else root.DeleteSubKeyTree(KeyRoot+saved.Name,false);
            }
        }
    }
    private static void RestoreLinks(List<LinkSnapshot> links,string target)
    {
        foreach(var link in links)
        {
            if(!link.Changed)continue;
            if(File.Exists(link.Path))
            {if(link.AfterHash==null){if(!Same(LinkTarget(link.Path),Path.Combine(target,"ProjectEye.exe")))continue;}else if(Hash(link.Path)!=link.AfterHash)continue;Recycle(link.Path);}
            if(link.Existed&&!File.Exists(link.Path)){Directory.CreateDirectory(Path.GetDirectoryName(link.Path));File.Copy(link.Backup,link.Path);}
        }
    }
    private static object ConvertValue(object value,RegistryValueKind kind)
    {
        if(kind==RegistryValueKind.DWord)return Convert.ToInt32(value);if(kind==RegistryValueKind.QWord)return Convert.ToInt64(value);
        if(kind==RegistryValueKind.MultiString)return ((System.Collections.IEnumerable)value).Cast<object>().Select(Convert.ToString).ToArray();
        if(kind==RegistryValueKind.Binary)return ((System.Collections.IEnumerable)value).Cast<object>().Select(Convert.ToByte).ToArray();return Convert.ToString(value);
    }
    internal static string RefreshRegistration(string root)
    { VerifyInstalled(root);Register(root,RegistryBefore());return FinishInstall(root); }

    private static bool VerifiedHistory(string folder,string root)
    {
        NoLinks(folder);
        foreach(string file in Files(folder)){} // Reject reparse points in any descendant before recycling.
        string transaction=Path.Combine(folder,"transaction.json");
        if(File.Exists(transaction))
        {
            var tx=Read<LegacyTransaction>(transaction);
            if(!Same(tx.Root,root)||!Same(tx.Backup,folder)||string.IsNullOrEmpty(tx.Id))return false;
            string files=Path.Combine(folder,"files");
            if(tx.Before.Any(e=>e.path=="test-package.json"))VerifyExisting(files,true);
            else if(tx.Before.Count!=0||!tx.Written.Any(e=>Trusted().Any(t=>t.path=="test-package.json"&&t.path==e.path&&t.sha256==e.sha256)))return false;
            foreach(var entry in tx.Before)if(Hash(Child(files,entry.path))!=entry.sha256)return false;
            return true;
        }
        string ticketPath=Path.Combine(folder,"ticket.json");
        string helper=Path.Combine(folder,"ProjectEye.Maintenance.exe");
        if(File.Exists(ticketPath)&&File.Exists(helper))
        {
            var ticket=Read<Dictionary<string,object>>(ticketPath);
            if(!ticket.ContainsKey("Root")||!Same(Convert.ToString(ticket["Root"]),root))return false;
            return ticket.ContainsKey("HelperHash")&&Convert.ToString(ticket["HelperHash"])==Hash(helper)&&BuildIdentity.LegacyMaintenanceHashes.Contains(Hash(helper));
        }
        string location=Path.Combine(folder,"original-location.json");
        string original=Path.Combine(folder,"original-files.json");
        string recovered=Path.Combine(folder,"recovered-files.json");
        if(File.Exists(location)&&File.Exists(original)&&File.Exists(recovered))
        {
            var data=Read<Dictionary<string,object>>(location);
            if(!data.ContainsKey("path")||!Same(Convert.ToString(data["path"]),root))return false;
            var entries=Read<Entry[]>(original);
            var current=Read<Entry[]>(recovered);
            foreach(var entry in entries.Where(e=>DataPath(e.path)))
                if(!current.Any(c=>c.path==entry.path&&c.sha256==entry.sha256)||Hash(Child(folder,entry.path))!=entry.sha256)return false;
            // Original beta.3 manifest is bound to its known release hash in the
            // preserved recovered-files journal, even after program files were recycled.
            return current.Any(e=>e.path.Equals("program-files\\payload-manifest.json",StringComparison.OrdinalIgnoreCase)&&BuildIdentity.LegacyManifestHashes.Contains(e.sha256));
        }
        return false;
    }
    internal static int CleanupHistory(string root)
    {
        SafeRoot(root);NoLinks(RecoveryRoot);int warnings=0;
        if(!Directory.Exists(RecoveryRoot))return 0;
        foreach(string folder in Directory.GetDirectories(RecoveryRoot))
        {try{if(VerifiedHistory(folder,root))Recycle(folder);else warnings++;}catch(Exception){warnings++;}}
        if(!Directory.EnumerateFileSystemEntries(RecoveryRoot).Any())Recycle(RecoveryRoot);
        return warnings;
    }
    internal static void CheckUninstall(string root)
    {
        VerifyInstalled(root);RequireStopped(root);
        // Probe exclusive access before registry or shortcut changes. Do not rely on
        // shell recycling to discover a locked user database after partial removal.
        foreach(string file in Files(root))
        {
            string relative=Relative(root,file);
            if(relative.Equals("uninstall\\unins000.dat",StringComparison.OrdinalIgnoreCase)||relative.Equals("uninstall\\unins000.exe",StringComparison.OrdinalIgnoreCase))continue;
            using(File.Open(file,FileMode.Open,FileAccess.Read,FileShare.None)){}
        }
    }
    internal static string Uninstall(string root)
    {
        root=Normalize(root);CheckUninstall(root);
        int uncertain=0;
        string stage=Path.Combine(Path.GetTempPath(),"ProjectEye-uninstall-"+Guid.NewGuid().ToString("N"));
        NoLinks(stage);Directory.CreateDirectory(stage);
        var registry=RegistryBefore();var links=BackupLinks(stage);
        try
        {
            foreach(var item in registry.Where(r=>RegisteredHere(r,root)))using(var key=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,(RegistryView)Enum.Parse(typeof(RegistryView),item.View)))key.DeleteSubKeyTree(KeyRoot+item.Name,false);
            ChangeLinks(root,links,true,()=>{});
            string native=Path.Combine(root,"uninstall","unins000.dat");
            if(File.Exists(native))
            {
                // Inno owns its open log until usPostUninstall. Move everything
                // else to a same-volume sibling before a single recoverable recycle.
                string payloadStage=root+".uninstall-"+Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(payloadStage);
                var moved=new List<string>();
                try
                {
                    foreach(string entry in Directory.GetFileSystemEntries(root).Where(p=>!Same(p,Path.Combine(root,"uninstall"))))
                    {string destination=Path.Combine(payloadStage,Path.GetFileName(entry));if(Directory.Exists(entry))Directory.Move(entry,destination);else File.Move(entry,destination);moved.Add(destination);}
                    Recycle(payloadStage);
                }
                catch
                {
                    foreach(string entry in moved.Where(p=>File.Exists(p)||Directory.Exists(p)))
                    {string destination=Path.Combine(root,Path.GetFileName(entry));if(Directory.Exists(entry))Directory.Move(entry,destination);else File.Move(entry,destination);}
                    if(Directory.Exists(payloadStage)&&!Directory.EnumerateFileSystemEntries(payloadStage).Any())Recycle(payloadStage);
                    throw;
                }
            }
            else {Recycle(root);if(Directory.Exists(root))throw new IOException("安装目录未能完整移入回收站，请关闭占用它的程序后重试。");}
        }
        catch{RestoreRegistry(registry,root);RestoreLinks(links,root);Recycle(stage);throw;}
        uncertain+=CleanupHistory(root);
        Recycle(stage);
        return uncertain==0?"卸载完成，程序、配置、统计、日志及本安装恢复副本已移入系统回收站。":"卸载完成。部分恢复记录无法可靠确认或回收，已保留，请手动核对；程序和本次配置已移入系统回收站。";
    }
}
