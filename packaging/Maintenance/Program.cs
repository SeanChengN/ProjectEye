using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("ProjectEye Enhanced Maintenance")]
[assembly: AssemblyProduct("ProjectEye Enhanced")]
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool quiet = args.Contains("--quiet");
        string resultPath = Value(args, "--result");
        try
        {
            var self = Assembly.GetExecutingAssembly().Location;
            string mode = args.FirstOrDefault() ?? "--uninstall";
            if (mode == "--install")
            {
                string recovery = Engine.Install(Value(args,"--source"), Value(args,"--target"), Value(args,"--fail-at"));
                Engine.Result(resultPath, true, "Installed", recovery); return 0;
            }
            if (mode == "--rollback")
            {
                Engine.Rollback(Value(args,"--transaction"));
                Engine.Result(resultPath,true,"Rolled back",null); return 0;
            }
            if (mode == "--check")
            {
                Engine.VerifyInstalled(Path.GetDirectoryName(self));
                Engine.Result(resultPath,true,"Payload verified",null); return 0;
            }
            if (mode == "--finish-uninstall")
            {
                var ticket = Engine.Read<Ticket>(Value(args,"--ticket"));
                quiet=ticket.Quiet;
                resultPath=ticket.ResultPath;
                Engine.ValidateTicket(self, Value(args,"--ticket"),ticket);
                try { using(var parent = Process.GetProcessById(ticket.ParentId))
                    if(!parent.WaitForExit(15000)) throw new InvalidOperationException("Original uninstaller is still running."); }
                catch(ArgumentException) { /* Parent already exited. */ }
                string recovery = Engine.Uninstall(ticket.Root, self, ticket);
                Engine.Result(ticket.ResultPath,true,"Uninstalled; data preserved",recovery);
                if(!ticket.Quiet) MessageBox.Show("ProjectEye Enhanced removed. Data and recovery details: " + recovery,"ProjectEye Enhanced");
                // Preserve helper for crash diagnostics. It never registers as an installed app.
                return 0;
            }
            if (mode != "--uninstall" && mode != "--quiet") throw new ArgumentException("Unknown maintenance action.");
            string root = Path.GetDirectoryName(self);
            Engine.VerifyInstalled(root);
            Engine.RequireStopped(root);
            if (!quiet && MessageBox.Show("Uninstall ProjectEye Enhanced? Data and logs will be preserved in your local recovery folder.","ProjectEye Enhanced",MessageBoxButtons.OKCancel)!=DialogResult.OK) return 2;
            string stage = Engine.NewRecovery("uninstall-helper");
            string helper = Path.Combine(stage,"ProjectEye.Maintenance.exe");
            File.Copy(self,helper);
            if(Engine.Hash(helper)!=Engine.Hash(self)) throw new IOException("Helper hash mismatch.");
            var ticketOut = new Ticket {Root=root,ParentId=Process.GetCurrentProcess().Id,HelperHash=Engine.Hash(self),ResultPath=resultPath,Quiet=quiet,Nonce=Guid.NewGuid().ToString("N")};
            string ticketFile=Path.Combine(stage,"ticket.json");Engine.Write(ticketFile,ticketOut);
            Process.Start(new ProcessStartInfo(helper,"--finish-uninstall --ticket "+Engine.Quote(ticketFile)) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage});
            return 0;
        }
        catch(Exception error)
        {
            try { Engine.Result(resultPath,false,error.Message,error.Data["Recovery"] as string); } catch { }
            if(!quiet)MessageBox.Show(error.Message,"ProjectEye Enhanced maintenance",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return 1;
        }
    }
    private static string Value(string[] args,string key)
    { int index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:null; }
}
