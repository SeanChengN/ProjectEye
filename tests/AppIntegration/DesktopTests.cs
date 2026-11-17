using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using ProjectEye.Core;
using ProjectEye.Core.Service;

internal static class DesktopTests
{
    internal static async Task Run(ServiceCollection collection, Action<bool,string> check)
    {
        var config = (ConfigService)collection.GetInstance(typeof(ConfigService).FullName);
        var rest = (RestService)collection.GetInstance(typeof(RestService).FullName);
        var desktop = (DesktopControlService)collection.GetInstance(typeof(DesktopControlService).FullName);
        var main = (MainService)collection.GetInstance(typeof(MainService).FullName);
        check(!config.options.Behavior.MinimizeWindowsAfterRest, "legacy desktop default disabled");
        var fake = new Fake(); desktop.Minimizer = new DesktopWindowMinimizer(fake);
        await rest.CancelAsync(); check(fake.Enumerations == 0, "skip initial reminder does not minimize");
        config.options.General.RestTime = 1;
        rest.Start(); config.options.Behavior.MinimizeWindowsAfterRest = true;
        await Task.Delay(1250); check(fake.Enumerations == 0, "mid-cycle enabling applies next time");
        rest.Start(); config.options.Behavior.MinimizeWindowsAfterRest = false;
        check(fake.Enumerations == 0, "start retains other windows");
        await Task.Delay(1250); check(fake.Enumerations == 1 && !rest.IsResting, "normal finish uses enabled snapshot");
        config.options.Behavior.MinimizeWindowsAfterRest = true;
        fake.Delay = 35;
        rest.Start(); var id = rest.CycleId;
        var a = rest.CancelAsync(); var b = rest.CancelAsync(); rest.Start();
        check(rest.CycleId == id && ReferenceEquals(a,b), "duplicate cancel and start share one finishing cycle");
        await a; check(fake.Enumerations == 2 && !rest.IsResting, "cancel minimizes once");
        rest.Start(); main.OnLeave(); main.OnLeave();
        await Task.Delay(150); check(fake.Enumerations == 3 && !rest.IsResting, "leave path minimizes once before hiding");
        main.Start();
        fake.Throw = true; rest.Start(); await rest.CancelAsync();
        check(!rest.IsResting, "desktop failure does not block cancellation"); fake.Throw = false;
        var slow = new Fake { Delay = 450 };
        var bounded = new DesktopWindowMinimizer(slow);
        var clock = Stopwatch.StartNew(); var result = await bounded.MinimizeAsync();
        check(clock.ElapsedMilliseconds < 350 && result.Code == "Timeout" && result.Failed != 0, "200ms operation returns on blocked backend");
        var second = await bounded.MinimizeAsync(); check(second.Code == "Pending", "pending native work is not overlapped");
        bounded.Shutdown(); await Task.Delay(320); check(slow.Commands == 0, "timeout sends no delayed minimize commands");
        var rejected = new Fake { Reject = true };
        result = await new DesktopWindowMinimizer(rejected).MinimizeAsync();
        check(result.Failed == 1, "rejected window reported");
        await Native(check);
        ReadOnlyMonitor(check);
        await HungWindow(check);
        config.options.Behavior.MinimizeWindowsAfterRest = true;
        check(config.Save(), "desktop setting saves"); config.Init();
        check(config.options.Behavior.MinimizeWindowsAfterRest, "desktop setting reloads");
        var beforeExit = fake.Enumerations;
        desktop.Minimizer = new DesktopWindowMinimizer(fake);
        rest.Start(); rest.Shutdown();
        check(!rest.IsResting && fake.Enumerations == beforeExit, "exit does not enumerate windows");
        config.options.Behavior.MinimizeWindowsAfterRest = false; config.Save();
    }
    private static void ReadOnlyMonitor(Action<bool,string> check)
    {
        var processes=Process.GetProcessesByName("opencodex-monitor");
        var ids=new HashSet<uint>(processes.Select(p=>(uint)p.Id));
        foreach(var process in processes)process.Dispose();
        var handles=new List<IntPtr>();
        EnumWindows((window,value)=>{uint pid;GetWindowThreadProcessId(window,out pid);if(ids.Contains(pid))handles.Add(window);return true;},IntPtr.Zero);
        if(handles.Count==0){Console.WriteLine("SKIP real Monitor read-only eligibility: no windows");return;}
        var backend=new NativeDesktopWindowBackend(new uint[0],handles);
        // This test enumerates only; never call Minimize on a user's window.
        check(backend.Enumerate(CancellationToken.None).Count==0,"actual Monitor windows excluded by read-only enumeration");
        Console.WriteLine("Monitor read-only handles={0}; commands=0",handles.Count);
    }
    private static async Task Native(Action<bool,string> check)
    {
        var windows = new List<Window>();
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            foreach (var screen in screens)
            {
                var normal = new Window { Title="ProjectEye desktop fixture", Width=240, Height=120, Left=screen.Bounds.Left+30, Top=screen.Bounds.Top+30 };
                normal.Show(); windows.Add(normal);
            }
            var maximized = new Window { Title="ProjectEye maximized fixture", Width=240, Height=120 }; maximized.Show(); maximized.WindowState=WindowState.Maximized; windows.Add(maximized);
            var minimized = new Window { Title="ProjectEye minimized fixture", Width=240, Height=120 }; minimized.Show(); minimized.WindowState=WindowState.Minimized; windows.Add(minimized);
            var hidden = new Window { Title="ProjectEye hidden fixture", Width=240, Height=120 }; hidden.Show(); hidden.Hide(); windows.Add(hidden);
            var mask = new Window { Title="ProjectEye mask fixture", Width=240, Height=120, Topmost=true }; mask.Show(); windows.Add(mask);
            var dock = new Window { Title="ProjectEye dock fixture", Width=240, Height=120, Topmost=true, WindowStyle=WindowStyle.None, ShowInTaskbar=false }; dock.Show(); windows.Add(dock);
            // Real Tauri windows can retain WS_CAPTION while visually borderless.
            var captionedTopmost = new Window { Title="ProjectEye caption topmost fixture", Width=240, Height=120, Topmost=true }; captionedTopmost.Show(); windows.Add(captionedTopmost);
            var noTaskbar = new Window { Title="ProjectEye no taskbar fixture", Width=240, Height=120, ShowInTaskbar=false }; noTaskbar.Show(); windows.Add(noTaskbar);
            var noActivate = new Window { Title="ProjectEye no activate fixture", Width=240, Height=120, ShowActivated=false }; noActivate.Show(); windows.Add(noActivate);
            var noActivateHandle=new WindowInteropHelper(noActivate).Handle;
            SetWindowLong(noActivateHandle,-20,GetWindowLong(noActivateHandle,-20)|0x08000000);
            var race = new Window { Title="ProjectEye state race fixture", Width=240, Height=120 }; race.Show(); windows.Add(race);
            await Task.Delay(80);
            var candidates = windows.Where(w=>w!=mask).Select(w=>new WindowInteropHelper(w).Handle).ToArray();
            var backend = new NativeDesktopWindowBackend(new uint[0],candidates,new[]{new WindowInteropHelper(mask).Handle});
            var found = backend.Enumerate(CancellationToken.None);
            check(found.Count == screens.Length+2, "native enumeration excludes protected fixtures on all screens");
            var raceHandle=new WindowInteropHelper(race).Handle;
            check(found.Contains(raceHandle),"ordinary fixture initially eligible");
            race.Topmost=true;await Task.Delay(20);
            check(backend.Minimize(raceHandle),"changed topmost state safely skipped before command");
            var result = await new DesktopWindowMinimizer(backend).MinimizeAsync();
            await Task.Delay(50);
            Console.WriteLine("Native result={0} attempted={1} failed={2}; states={3}",result.Code,result.Attempted,result.Failed,string.Join(",",windows.Select(w=>w.Title+":"+w.WindowState)));
            check(result.Failed==0 && windows.Where(w=>w!=mask&&w!=hidden&&w!=dock&&w!=captionedTopmost&&w!=noTaskbar&&w!=noActivate&&w!=race).All(w=>w.WindowState==WindowState.Minimized), "native normal and maximized fixtures minimized");
            check(dock.IsVisible && dock.WindowState==WindowState.Normal,"borderless topmost dock retains its visibility state");
            check(captionedTopmost.IsVisible&&captionedTopmost.WindowState==WindowState.Normal,"captioned topmost window never minimized");
            check(noTaskbar.IsVisible&&noTaskbar.WindowState==WindowState.Normal&&!noTaskbar.ShowInTaskbar,"non-taskbar window remains absent from taskbar");
            check(noActivate.IsVisible&&noActivate.WindowState==WindowState.Normal,"no-activate window remains untouched");
            check(race.WindowState==WindowState.Normal&&race.Topmost,"late topmost transition preserved");
            hidden.Show();await Task.Delay(10);hidden.Hide();
            check(backend.Minimize(new WindowInteropHelper(hidden).Handle)&&!hidden.IsVisible,"late hidden state never shown by minimize");
            check(mask.WindowState==WindowState.Normal && mask.IsVisible, "mask fixture remains visible");
            await Task.Delay(100);
            check(maximized.WindowState==WindowState.Minimized, "native windows not automatically restored");
            var excluded = new NativeDesktopWindowBackend(new[]{(uint)Process.GetCurrentProcess().Id},new[]{new WindowInteropHelper(mask).Handle});
            check(excluded.Enumerate(CancellationToken.None).Count==0, "application process is excluded");
            check(!backend.Minimize(FindWindow("Shell_TrayWnd",null)), "taskbar outside exact fixture set never commanded");
            Console.WriteLine("Native desktop fixture screens={0}; attempted={1}; failed={2}",screens.Length,result.Attempted,result.Failed);
        }
        finally { foreach(var window in windows) window.Close(); }
    }
    private static async Task HungWindow(Action<bool,string> check)
    {
        var ready = new TaskCompletionSource<IntPtr>();
        var block = new ManualResetEvent(false);
        var thread = new Thread(() => {
            var window = new Window {Title="ProjectEye nonresponsive fixture",Width=220,Height=100};
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            window.Dispatcher.BeginInvoke(new Action(() => { ready.SetResult(handle); block.WaitOne(); window.Close(); System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background); }),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
        var target = await ready.Task;
        try
        {
            var backend = new NativeDesktopWindowBackend(new uint[0],new[]{target});
            var clock = Stopwatch.StartNew();
            var result = await new DesktopWindowMinimizer(backend).MinimizeAsync();
            Console.WriteLine("Blocked fixture elapsedMs={0}; attempted={1}; failed={2}; code={3}",clock.ElapsedMilliseconds,result.Attempted,result.Failed,result.Code);
            check(clock.ElapsedMilliseconds<350 && (result.Failed!=0 || backend.IsMinimizedOrGone(target)),"real blocked window returns bounded and reports its observed state");
        }
        finally { block.Set(); }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string className,string title);
    private delegate bool WindowCallback(IntPtr window,IntPtr value);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback,IntPtr value);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window,int index,int value);
    private sealed class Fake : IDesktopWindowBackend
    {
        public int Enumerations, Commands, Delay; public bool Throw, Reject;
        public IReadOnlyList<IntPtr> Enumerate(CancellationToken token)
        { Interlocked.Increment(ref Enumerations); if(Delay!=0)Thread.Sleep(Delay); token.ThrowIfCancellationRequested(); if(Throw)throw new InvalidOperationException(); return new[]{new IntPtr(123)}; }
        public bool Minimize(IntPtr window) { Interlocked.Increment(ref Commands); return !Reject; }
        public bool IsMinimizedOrGone(IntPtr window) { return true; }
    }
}
