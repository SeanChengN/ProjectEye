using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace ProjectEye.Core.Service
{
    public sealed class DesktopControlService : IService
    {
        private readonly TrayService tray;
        private DesktopWindowMinimizer nativeMinimizer;
        private NativeDesktopWindowBackend nativeBackend;
        public DesktopWindowMinimizer Minimizer { get; set; }
        public DesktopControlService(TrayService tray)
        {
            this.tray = tray;
        }
        public void Init() { }
        public async Task MinimizeAsync()
        {
            if (Minimizer == null)
            {
                // Snapshot mask handles on the dispatcher. Other application windows remain eligible.
                var masks = Application.Current.Windows.Cast<Window>().Where(w => w.Uid == "TipWindow")
                    .Select(w => new WindowInteropHelper(w).Handle).ToArray();
                if (nativeMinimizer == null)
                {
                    nativeBackend = new NativeDesktopWindowBackend(new uint[0], excludedWindows: masks);
                    nativeMinimizer = new DesktopWindowMinimizer(nativeBackend);
                }
                else nativeBackend.ExcludeWindows(masks);
            }
            var result = await (Minimizer ?? nativeMinimizer).MinimizeAsync();
            _ = Task.Run(() => {
                try
                {
                    var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "Desktop_" + DateTime.UtcNow.ToString("yyyyMMdd") + ".log");
                    if (!File.Exists(path) || new FileInfo(path).Length < 1024 * 1024)
                        File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " attempted=" + result.Attempted + " failed=" + result.Failed + " code=" + result.Code + Environment.NewLine, new UTF8Encoding(false));
                }
                catch { }
            });
            if (result.Failed != 0)
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => tray.BalloonTipIcon("ProjectEye Enhanced", (string)Application.Current.Resources["Lang_DesktopMinimizeFailed"])));
        }
        public void Shutdown() { if (Minimizer != null) Minimizer.Shutdown(); if (nativeMinimizer != null) nativeMinimizer.Shutdown(); }
    }
}
