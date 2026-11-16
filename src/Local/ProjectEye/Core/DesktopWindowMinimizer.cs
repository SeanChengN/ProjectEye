using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEye.Core
{
    public interface IDesktopWindowBackend
    {
        IReadOnlyList<IntPtr> Enumerate(CancellationToken token);
        bool Minimize(IntPtr window);
        bool IsMinimizedOrGone(IntPtr window);
    }
    public sealed class DesktopResult
    {
        public int Attempted { get; internal set; }
        public int Failed { get; internal set; }
        public string Code { get; internal set; }
    }
    public sealed class DesktopWindowMinimizer
    {
        private readonly IDesktopWindowBackend backend;
        private Task<DesktopResult> pending;
        private CancellationTokenSource active;
        private volatile bool stopped;
        public DesktopWindowMinimizer(IDesktopWindowBackend backend) { this.backend = backend; }
        public async Task<DesktopResult> MinimizeAsync()
        {
            if (stopped) return new DesktopResult { Code = "Shutdown" };
            if (pending != null && !pending.IsCompleted) return new DesktopResult { Failed = 1, Code = "Pending" };
            var cancel = new CancellationTokenSource(); active = cancel;
            var clock = Stopwatch.StartNew();
            pending = Task.Run(() => Run(cancel.Token, clock));
            var operation = pending;
            await Task.WhenAny(operation, Task.Delay(200));
            if (!operation.IsCompleted)
            {
                cancel.Cancel();
                if (active == cancel) active = null;
                _ = operation.ContinueWith(done => cancel.Dispose());
                return new DesktopResult { Failed = 1, Code = "Timeout" };
            }
            try { return await operation; }
            finally { if (active == cancel) active = null; cancel.Dispose(); }
        }
        private DesktopResult Run(CancellationToken token, Stopwatch clock)
        {
            var result = new DesktopResult { Code = "Completed" };
            try
            {
                var windows = backend.Enumerate(token).Distinct().ToArray();
                var check = new List<IntPtr>();
                foreach (var window in windows)
                {
                    if (stopped || token.IsCancellationRequested || clock.ElapsedMilliseconds >= 190) { result.Failed++; result.Code = "Timeout"; break; }
                    result.Attempted++;
                    if (backend.Minimize(window)) check.Add(window); else result.Failed++;
                }
                while (check.Count != 0 && !token.IsCancellationRequested && clock.ElapsedMilliseconds < 190)
                {
                    check.RemoveAll(window => backend.IsMinimizedOrGone(window));
                    if (check.Count != 0) Thread.Sleep(5);
                }
                result.Failed += check.Count;
            }
            catch (OperationCanceledException) { result.Failed++; result.Code = "Cancelled"; }
            catch (Exception error) { result.Failed++; result.Code = "Error:" + error.HResult; }
            return result;
        }
        public void Shutdown() { stopped = true; if (active != null) active.Cancel(); }
    }
    public sealed class NativeDesktopWindowBackend : IDesktopWindowBackend
    {
        private readonly HashSet<uint> excludedProcesses;
        private readonly HashSet<IntPtr> onlyWindows;
        private volatile HashSet<IntPtr> excludedWindows;
        public void ExcludeWindows(IEnumerable<IntPtr> windows) { excludedWindows = new HashSet<IntPtr>(windows); }
        public NativeDesktopWindowBackend(IEnumerable<uint> excludedProcesses, IEnumerable<IntPtr> onlyWindows = null, IEnumerable<IntPtr> excludedWindows = null)
        {
            this.excludedProcesses = new HashSet<uint>(excludedProcesses);
            this.onlyWindows = onlyWindows == null ? null : new HashSet<IntPtr>(onlyWindows);
            this.excludedWindows = new HashSet<IntPtr>(excludedWindows ?? new IntPtr[0]);
        }
        public IReadOnlyList<IntPtr> Enumerate(CancellationToken token)
        {
            var windows = new List<IntPtr>();
            IVirtualDesktopManager desktops = null;
            try
            {
                desktops = (IVirtualDesktopManager)new VirtualDesktopManager();
                int skipped = 0;
                bool enumerated = EnumWindows((window, value) => {
                    if (token.IsCancellationRequested) return false;
                    if (onlyWindows != null && !onlyWindows.Contains(window)) return true;
                    if (excludedWindows.Contains(window)) return true;
                    uint process; GetWindowThreadProcessId(window, out process);
                    if (excludedProcesses.Contains(process) || !IsWindowVisible(window) || IsIconic(window)) return true;
                    if ((GetWindowLong(window, -20) & 0x80) != 0) return true;
                    // Borderless topmost widgets and edge-docked panels manage their own visibility.
                    if ((GetWindowLong(window, -20) & 0x8) != 0 && (GetWindowLong(window, -16) & 0xC00000) == 0) return true;
                    var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
                    if (new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" }.Contains(name.ToString())) return true;
                    int cloaked;
                    if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) != 0) { skipped++; return true; }
                    if (cloaked != 0) return true;
                    bool current;
                    if (desktops.IsWindowOnCurrentVirtualDesktop(window, out current) < 0) { skipped++; return true; }
                    if (!current) return true;
                    windows.Add(window); return true;
                }, IntPtr.Zero);
                token.ThrowIfCancellationRequested();
                if (!enumerated || skipped != 0) throw new InvalidOperationException("Window enumeration incomplete");
                return windows;
            }
            finally { if (desktops != null) Marshal.ReleaseComObject(desktops); }
        }
        public bool Minimize(IntPtr window)
        {
            uint process; GetWindowThreadProcessId(window, out process);
            if (excludedProcesses.Contains(process) || excludedWindows.Contains(window) || (onlyWindows != null && !onlyWindows.Contains(window))) return false;
            if (!IsWindow(window) || !IsWindowVisible(window) || IsIconic(window)) return true;
            return ShowWindowAsync(window, 6);
        }
        public bool IsMinimizedOrGone(IntPtr window) { return !IsWindow(window) || !IsWindowVisible(window) || IsIconic(window); }
        private delegate bool EnumProc(IntPtr window, IntPtr value);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr value);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")] private class VirtualDesktopManager { }
        [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);
            [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid desktop);
            [PreserveSig] int MoveWindowToDesktop(IntPtr window, ref Guid desktop);
        }
    }
}
