using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("ProjectEye Enhanced Maintenance")]
[assembly: AssemblyProduct("ProjectEye Enhanced")]
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool quiet=args.Contains("--quiet");string result=Value(args,"--result");
        try
        {
            string root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            switch(args.FirstOrDefault()??"--uninstall")
            {
                case "--install":
                    string installed=Engine.Install(Value(args,"--source"),Value(args,"--target"),Value(args,"--fail-at"),args.Contains("--defer-finalize"));
                    Engine.Result(result,true,installed,null);return 0;
                case "--cleanup-history":
                    string legacy=Value(args,"--target");Engine.VerifyExisting(legacy);
                    int warnings=Engine.CleanupHistory(legacy);
                    Engine.Result(result,true,warnings==0?"已核验并回收属于本安装的旧恢复记录。":"部分旧恢复记录无法确认或回收，已保留。",null);return 0;
                case "--check":
                    Engine.VerifyInstalled(root);Engine.Result(result,true,"程序文件校验通过。",null);return 0;
                case "--register":
                    string registered=Engine.RefreshRegistration(Value(args,"--target"));Engine.Result(result,true,registered,null);return 0;
                case "--check-uninstall":
                    Engine.CheckUninstall(Value(args,"--target"));Engine.Result(result,true,"可以卸载。",null);return 0;
                case "--remove":
                    string message=Engine.Uninstall(Value(args,"--target"));Engine.Result(result,true,message,null);return 0;
                case "--uninstall":
                    Engine.VerifyInstalled(root);Engine.RequireStopped(root);
                    string native=Path.Combine(root,"uninstall","unins000.exe");
                    if(!File.Exists(native))throw new InvalidOperationException("便携版请退出后将整个专用目录移入系统回收站；安装版请重新安装以修复卸载入口。");
                    Process.Start(new ProcessStartInfo(native,quiet?"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART":""){UseShellExecute=false});return 0;
                default:throw new ArgumentException("不支持的维护操作。");
            }
        }
        catch(Exception error)
        {
            string code;string message=Friendly(error,out code);
            try{Engine.Result(result,false,message,error.Data["Recovery"] as string,code);}catch{}
            if(!quiet)MessageBox.Show(message,"ProjectEye Enhanced 维护",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return 1;
        }
    }
    private static string Friendly(Exception error,out string code)
    {
        string detail=error.Message;code="E_VALIDATION";
        if(detail.Contains("tray")||detail.Contains("still running")||detail.Contains("running application")){code="E_RUNNING";return "请先从系统托盘正常退出 ProjectEye Enhanced，再重试。";}
        if(error is UnauthorizedAccessException){code="E_ACCESS";return "没有访问所选目录的权限，请选择当前用户可写的专用目录。";}
        if(error is IOException&&((error.HResult&0xffff)==32||(error.HResult&0xffff)==33)){code="E_LOCKED";return "文件正在被其他程序占用，请关闭相关程序后重试。";}
        if(detail.Contains("Protected")||detail.Contains("Unsafe")||detail.Contains("Network")){code="E_PATH";return "该目录不适合安装或卸载，请选择本应用的专用目录。";}
        if(detail.Contains("Reparse")){code="E_REPARSE";return "检测到目录链接或重解析点，为避免影响其他位置，已停止操作。";}
        if(detail.Contains("changed")||detail.Contains("mismatch")||detail.Contains("Untrusted")){code="E_INTEGRITY";return "程序文件或备份已被修改，校验未通过，已停止操作。请保留现有文件并核对安装包。";}
        if(error is FileNotFoundException||error is DirectoryNotFoundException){code="E_MISSING";return "所选目录缺少必要的产品文件；只有空目录或完整的受支持旧版本可以安装。";}
        if(detail.Contains("another")||detail.Contains("Unknown file")){code="E_CONFLICT";return "所选目录或快捷方式与另一安装冲突，请核对目录，避免覆盖其他文件。";}
        if(detail.Contains("Injected")){code="E_TEST_FAILURE";return "测试故障已触发，请重新运行同一个安装包修复。";}
        if(detail.Any(c=>c>0x3000&&c<0xa000))return detail;
        return "操作未完成，已停止后续处理。请检查目录、文件占用及安装包完整性。错误码："+code;
    }
    private static string Value(string[] args,string key)
    {int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
}
