using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--pause"))
        {
            Console.Error.WriteLine("Usage: GsmtcPoc.exe [--pause] (enumeration is read-only by default)");
            return 2;
        }
        try { return RunAsync(args.Length == 1).GetAwaiter().GetResult(); }
        catch (Exception e)
        {
            // Do not log media metadata or exception messages which can contain private data.
            Console.Error.WriteLine("GSMTC failure: type={0}; hresult=0x{1:X8}", e.GetType().Name, e.HResult);
            return 1;
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation)
    {
        using (var cancel = new CancellationTokenSource())
        {
            var task = operation(cancel.Token);
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completed != task)
            {
                cancel.Cancel();
                // Observe a late fault. An API timeout is not proof that playback stayed unchanged.
                _ = task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException();
            }
            return await task;
        }
    }

    private static async Task<int> RunAsync(bool pause)
    {
        Console.WriteLine("OS={0}; CLR={1}; mode={2}", Environment.OSVersion.Version, Environment.Version, pause ? "pause-playing" : "enumerate");
        var manager = await Bounded(token => GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token));
        var sessions = manager.GetSessions().ToArray();
        Console.WriteLine("sessions={0}", sessions.Length);
        int playing = 0, paused = 0, failed = 0, unsupported = 0;
        for (int i = 0; i < sessions.Length; i++)
        {
            try
            {
                var session = sessions[i];
                var info = session.GetPlaybackInfo();
                Console.WriteLine("session={0}; status={1}; pauseSupported={2}; app={3}", i, info.PlaybackStatus, info.Controls.IsPauseEnabled, session.SourceAppUserModelId);
                if (info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
                playing++;
                if (!pause) continue;
                if (!info.Controls.IsPauseEnabled) { unsupported++; continue; }
                // Re-read just before commanding; leave paused/stopped media alone.
                if (session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
                var succeeded = await Bounded(token => session.TryPauseAsync().AsTask(token));
                if (succeeded) paused++; else failed++;
                Console.WriteLine("session={0}; pauseSucceeded={1}; statusAfter={2}", i, succeeded, session.GetPlaybackInfo().PlaybackStatus);
            }
            catch (Exception e)
            {
                failed++;
                Console.Error.WriteLine("session={0}; failure={1}; hresult=0x{2:X8}", i, e.GetType().Name, e.HResult);
            }
        }
        Console.WriteLine("playing={0}; pauseSucceeded={1}; failed={2}; unsupported={3}", playing, paused, failed, unsupported);
        return failed == 0 ? 0 : 1;
    }
}
