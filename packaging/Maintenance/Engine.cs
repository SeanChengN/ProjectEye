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
internal sealed class Ticket { public string Root; public int ParentId; public string HelperHash; public string ResultPath; public bool Quiet; public string Nonce; }
internal sealed class RegValue { public string Name; public object Value; public string Kind; }
internal sealed class RegSnapshot { public string View; public string Name; public bool Exists; public RegValue[] Values; }
internal sealed class LinkSnapshot { public string Path; public string Backup; public bool Existed; public string BeforeHash; public string AfterHash; public bool Changed; }
internal sealed class Transaction
{
    public string Root; public string Backup; public string Id; public string State;
    public bool Existed; public List<Entry> Before = new List<Entry>();
    public List<Entry> Written = new List<Entry>(); public List<string> NewDirectories = new List<string>();
    public List<Entry> Retired = new List<Entry>();
    public List<RegSnapshot> Registry; public List<LinkSnapshot> Links;
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
    internal static void Result(string path,bool success,string message,string recovery)
    { if(!string.IsNullOrEmpty(path))Write(path,new {success=success,message=message,recovery=recovery,utc=DateTime.UtcNow.ToString("o")}); }
    internal static string Hash(string path)
    { using(var input=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-",""); }
    internal static string NewRecovery(string purpose)
    {
        NoLinks(RecoveryRoot);Directory.CreateDirectory(RecoveryRoot);
        string path=Path.Combine(RecoveryRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+purpose+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);return path;
    }
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
    private static void VerifyExisting(string root,bool recovery=false)
    {
        var marker=Read<Marker>(Child(root,"test-package.json"));
        if(marker.product!="ProjectEye Enhanced")throw new IOException("Unrecognized installation.");
        if(marker.version==BuildIdentity.Version){VerifyInstalled(root,!recovery);return;}
        if(marker.version!="1.0.0-beta.1"&&marker.version!="1.0.0-beta.2")throw new IOException("Unsupported installed version.");
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
            string command=Quote(Path.Combine(root,"ProjectEye.Maintenance.exe"))+" --uninstall";
            key.SetValue("DisplayName","ProjectEye Enhanced "+BuildIdentity.Version);key.SetValue("DisplayVersion",BuildIdentity.Version);
            key.SetValue("InstallLocation",root);key.SetValue("DisplayIcon",Path.Combine(root,"ProjectEye.exe"));
            key.SetValue("UninstallString",command);key.SetValue("QuietUninstallString",command+" --quiet");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
        }
        // HKCU views can alias: never delete the stable key after creating it.
        foreach(var item in before.Where(s=>RegisteredHere(s,root)&&s.Name!=Keys[0]))using(var baseKey=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,(RegistryView)Enum.Parse(typeof(RegistryView),item.View)))baseKey.DeleteSubKeyTree(KeyRoot+item.Name,false);
    }
    private static string[] LinkPaths()
    {
#if MAINTENANCE_TESTS
        return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ProjectEyeEnhanced-MaintenanceTests","ProjectEye Enhanced Test.lnk")};
#else
        return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ProjectEye Enhanced Test.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ProjectEye Enhanced beta.1.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ProjectEye Enhanced beta.2.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"ProjectEye Enhanced.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"ProjectEye Enhanced.lnk")};
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
    private static void CopyTree(string from,string to,List<Entry> files)
    {
        foreach(var entry in files){string source=Child(from,entry.path);string target=Child(to,entry.path);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target);if(Hash(target)!=entry.sha256)throw new IOException("Recovery backup verification failed.");}
    }
    private static void MoveDirectory(string from,string to)
    {
        if(Same(Path.GetPathRoot(from),Path.GetPathRoot(to))){Directory.Move(from,to);return;}
        Directory.CreateDirectory(to);CopyTree(from,to,Snapshot(from));Recycle(from);
    }
    private static void MoveFile(string from,string to)
    {
        if(Same(Path.GetPathRoot(from),Path.GetPathRoot(to))){File.Move(from,to);return;}
        File.Copy(from,to);if(Hash(from)!=Hash(to))throw new IOException("Cross-volume move verification failed.");Recycle(from);
    }
    internal static string Install(string source,string target,string failAt)
    {
        source=Normalize(source);target=Normalize(target);SafeRoot(target);VerifyInstalled(source);RequireStopped(target);
        if(Same(source,target)||Under(source,target)||Under(target,source))throw new IOException("Source/target overlap.");
        bool existed=Directory.Exists(target);
        if(existed&&Directory.EnumerateFileSystemEntries(target).Any())VerifyExisting(target);
        var incomingEntries=Read<Entry[]>(Child(source,"payload-manifest.json"));
        var existingManifest=Path.Combine(target,"payload-manifest.json");
        var owned=File.Exists(existingManifest)?Read<Entry[]>(existingManifest):new Entry[0];
        foreach(var entry in incomingEntries)
            if(File.Exists(Child(target,entry.path))&&!owned.Any(e=>e.path.Equals(entry.path,StringComparison.OrdinalIgnoreCase)))throw new IOException("Unknown file would be overwritten: "+entry.path);
        var before=RegistryBefore();
        foreach(var item in before.Where(s=>s.Name==Keys[0]&&s.Exists))if(!RegisteredHere(item,target))throw new IOException("Existing test channel belongs to another directory.");
        var tx=new Transaction {Id=Guid.NewGuid().ToString("N"),Root=target,Backup=NewRecovery("upgrade"),State="Prepared",Existed=existed,Before=existed?Snapshot(target):new List<Entry>(),Registry=before};
        string backupFiles=Path.Combine(tx.Backup,"files");Directory.CreateDirectory(backupFiles);CopyTree(target,backupFiles,tx.Before);
        tx.Links=BackupLinks(tx.Backup);Write(Path.Combine(tx.Backup,"transaction.json"),tx);
        try
        {
            Directory.CreateDirectory(target);
            var incoming=Read<Entry[]>(Child(source,"payload-manifest.json")).Concat(new[]{new Entry {path="payload-manifest.json",sha256=Hash(Child(source,"payload-manifest.json")),bytes=new FileInfo(Child(source,"payload-manifest.json")).Length}}).ToArray();
            foreach(var entry in incoming)
            {
                string destination=Child(target,entry.path);
                if(DataPath(entry.path))throw new IOException("Payload would overwrite user data.");
                tx.Written.Add(entry);Write(Path.Combine(tx.Backup,"transaction.json"),tx);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                // Verify a sibling candidate before touching the old file. Rename is on
                // the same volume; a failed copy leaves the original file intact.
                string staged=destination+".candidate-"+tx.Id;
                File.Copy(Child(source,entry.path),staged);
                if(Hash(staged)!=entry.sha256)throw new IOException("Candidate hash mismatch.");
                if(File.Exists(destination))Recycle(destination);
                File.Move(staged,destination);
                if(Hash(destination)!=entry.sha256)throw new IOException("Installed hash mismatch.");
                if(failAt=="copy")throw new IOException("Injected copy failure for recovery acceptance.");
            }
            VerifyInstalled(target);
            var oldScript=Path.Combine(target,"Remove-TestVersion.ps1");
            var previousManifest=Path.Combine(backupFiles,"payload-manifest.json");
            if(File.Exists(oldScript)&&File.Exists(previousManifest)&&Read<Entry[]>(previousManifest).Any(e=>e.path.Equals("Remove-TestVersion.ps1",StringComparison.OrdinalIgnoreCase)&&Hash(oldScript)==e.sha256))
            {tx.Retired.Add(tx.Before.Single(e=>e.path.Equals("Remove-TestVersion.ps1",StringComparison.OrdinalIgnoreCase)));Write(Path.Combine(tx.Backup,"transaction.json"),tx);Recycle(oldScript);}
            Register(target,before);ChangeLinks(target,tx.Links,false,()=>Write(Path.Combine(tx.Backup,"transaction.json"),tx));
            if(failAt=="registration")throw new IOException("Injected registration failure for recovery acceptance.");
            tx.State="Committed";Write(Path.Combine(tx.Backup,"transaction.json"),tx);
            return tx.Backup;
        }
        catch(Exception error) {tx.State="Failed";Write(Path.Combine(tx.Backup,"transaction.json"),tx);error.Data["Recovery"]=tx.Backup;Rollback(tx.Backup);throw;}
    }
    internal static void Rollback(string directory)
    {
        directory=Normalize(directory);NoLinks(directory);if(!Under(directory,RecoveryRoot))throw new IOException("Not a product recovery directory.");
        var tx=Read<Transaction>(Path.Combine(directory,"transaction.json"));if(!Same(tx.Backup,directory)||string.IsNullOrEmpty(tx.Id))throw new IOException("Invalid transaction.");SafeRoot(tx.Root);RequireStopped(tx.Root);
        if(tx.State=="RolledBack")return;
        if(tx.Registry.Any(r=>!Keys.Contains(r.Name)||r.View!="Registry32"&&r.View!="Registry64"))throw new IOException("Invalid registry rollback scope.");
        foreach(var link in tx.Links)
        {
            if(!LinkPaths().Any(p=>Same(p,link.Path)))throw new IOException("Invalid shortcut rollback scope.");
            if(link.Existed&&(!Under(link.Backup,directory)||Hash(link.Backup)!=link.BeforeHash))throw new IOException("Shortcut backup changed.");
        }
        var allowed=Trusted().Concat(new[]{new Entry{path="ProjectEye.Maintenance.exe",sha256=Hash(Self),bytes=new FileInfo(Self).Length}}).ToList();
        foreach(var entry in tx.Written.Where(e=>e.path!="payload-manifest.json"))
            if(!allowed.Any(a=>a.path==entry.path&&a.sha256==entry.sha256&&a.bytes==entry.bytes))throw new IOException("Untrusted rollback payload.");
        string savedFiles=Path.Combine(directory,"files");NoLinks(savedFiles);
        if(tx.Before.Count>0)VerifyExisting(savedFiles,true);
        if(tx.Retired.Any(e=>e.path!="Remove-TestVersion.ps1"||!tx.Before.Any(b=>b.path==e.path&&b.sha256==e.sha256)))throw new IOException("Untrusted retired payload.");
        foreach(var entry in tx.Before)if(Hash(Child(savedFiles,entry.path))!=entry.sha256)throw new IOException("Backup changed.");
        foreach(var entry in tx.Written)
        {
            string path=Child(tx.Root,entry.path);var old=tx.Before.FirstOrDefault(e=>e.path==entry.path);
            if(File.Exists(path)&&Hash(path)!=entry.sha256&&(old==null||Hash(path)!=old.sha256))throw new IOException("Rollback target changed; preserve it for manual review: "+entry.path);
        }
        foreach(var entry in tx.Written){string path=Child(tx.Root,entry.path);if(File.Exists(path)&&Hash(path)==entry.sha256)Recycle(path);}
        foreach(var entry in tx.Before.Where(e=>!DataPath(e.path)&&(tx.Written.Any(w=>w.path==e.path)||tx.Retired.Any(r=>r.path==e.path))))
        {string path=Child(tx.Root,entry.path);string copy=Child(Path.Combine(directory,"files"),entry.path);if(Hash(copy)!=entry.sha256)throw new IOException("Backup changed.");if(!File.Exists(path)){Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(copy,path);}}
        RestoreRegistry(tx.Registry,tx.Root);
        RestoreLinks(tx.Links,tx.Root);
        tx.State="RolledBack";Write(Path.Combine(directory,"transaction.json"),tx);
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
    internal static void ValidateTicket(string self,string ticketPath,Ticket ticket)
    {
        NoLinks(ticketPath);if(!Under(ticketPath,RecoveryRoot)||!Same(Path.GetDirectoryName(self),Path.GetDirectoryName(ticketPath))||Hash(self)!=ticket.HelperHash||string.IsNullOrEmpty(ticket.Nonce))throw new IOException("Invalid uninstall handoff.");
        VerifyInstalled(ticket.Root);if(Hash(Path.Combine(ticket.Root,"ProjectEye.Maintenance.exe"))!=Hash(self))throw new IOException("Helper identity mismatch.");
    }
    internal static string Uninstall(string root,string helper,Ticket ticket)
    {
        var manifest=VerifyInstalled(root);RequireStopped(root);string recovery=NewRecovery("uninstalled-data");
        var before=Snapshot(root);Write(Path.Combine(recovery,"original-files.json"),before);Write(Path.Combine(recovery,"original-location.json"),new {path=root,utc=DateTime.UtcNow.ToString("o")});
        var registry=RegistryBefore();Write(Path.Combine(recovery,"registry-before.json"),registry);var links=BackupLinks(recovery);Write(Path.Combine(recovery,"links-before.json"),links);
        string bin=Path.Combine(recovery,"program-files");Directory.CreateDirectory(bin);
        var moved=new List<KeyValuePair<string,string>>();
        try
        {
        foreach(string name in new[]{"Data","Log"}){string directory=Child(root,name);if(Directory.Exists(directory)){string to=Path.Combine(recovery,name);MoveDirectory(directory,to);moved.Add(new KeyValuePair<string,string>(directory,to));}}
        foreach(var entry in manifest.Concat(new[]{new Entry{path="payload-manifest.json"}}))
        {string path=Child(root,entry.path);string to=Child(bin,entry.path);Directory.CreateDirectory(Path.GetDirectoryName(to));MoveFile(path,to);moved.Add(new KeyValuePair<string,string>(path,to));}
        foreach(var entry in before.Where(e=>DataPath(e.path)))if(Hash(Child(recovery,entry.path))!=entry.sha256)throw new IOException("Retained data verification failed.");
        foreach(var entry in manifest)if(Hash(Child(bin,entry.path))!=entry.sha256)throw new IOException("Removed payload verification failed.");
        foreach(var item in registry.Where(r=>RegisteredHere(r,root)))using(var key=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,(RegistryView)Enum.Parse(typeof(RegistryView),item.View)))key.DeleteSubKeyTree(KeyRoot+item.Name,false);
        ChangeLinks(root,links,true,()=>Write(Path.Combine(recovery,"links-after.json"),links));
        }
        catch
        {
            foreach(var pair in moved.AsEnumerable().Reverse()){Directory.CreateDirectory(Path.GetDirectoryName(pair.Key));if(Directory.Exists(pair.Value))MoveDirectory(pair.Value,pair.Key);else if(File.Exists(pair.Value))MoveFile(pair.Value,pair.Key);}
            RestoreRegistry(registry,root);RestoreLinks(links,root);
            throw;
        }
        Write(Path.Combine(recovery,"recovered-files.json"),Snapshot(recovery));
        var unknown=Snapshot(root);Write(Path.Combine(recovery,"unknown-files-preserved.json"),unknown);
        Recycle(bin);return recovery;
    }
}
