using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectEye.MediaControl;

internal static class Program
{
    private static int passed, failed;
    private static int Main(string[] args)
    {
        if (args.Length == 1 && (args[0] == "--live" || args[0] == "--live-pause")) return LiveAsync(args[0] == "--live-pause").GetAwaiter().GetResult();
        if (args.Length != 0) return 2;
        RunAsync().GetAwaiter().GetResult();
        Console.WriteLine("Passed={0}; Failed={1}", passed, failed);
        return failed == 0 ? 0 : 1;
    }
    private static async Task<int> LiveAsync(bool pause)
    {
        int errors=0;
        using (var service = new MediaControlService(new GsmtcMediaSessionBackend(), d => {
            Console.WriteLine("diagnostic={0}; error={1}; hresult={2}",d.Code,d.ExceptionType,d.HResult);
            if(d.ExceptionType!=null || d.Code=="PauseRejected") errors++;
        }))
        {
            var sessions = await service.GetMediaSessionsAsync();
            Console.WriteLine("sessions={0}",sessions.Count);
            foreach (var session in sessions) Console.WriteLine("before={0}; app={1}; fingerprintAvailable={2}",session.PlaybackStatus,session.SourceAppUserModelId,session.Fingerprint != null);
            if (pause)
            {
                // Live test explicitly confines commands to the user-prepared two-browser scenario.
                if(sessions.Count!=2 || !sessions.Any(x=>x.SourceAppUserModelId=="msedge.exe" && x.PlaybackStatus==MediaPlaybackStatus.Playing) || !sessions.Any(x=>x.SourceAppUserModelId=="chrome.exe" && x.PlaybackStatus==MediaPlaybackStatus.Paused))
                { Console.Error.WriteLine("Live mixed-state precondition not satisfied; no command sent.");return 3; }
                var paused = await service.PausePlayingMediaAsync();
                Console.WriteLine("successfulRecords={0}",paused.Count);
                if(paused.Count!=1 || paused[0].SourceAppUserModelId!="msedge.exe") errors++;
                await Task.Delay(1000);
                foreach(var snapshot in paused)
                {
                    var matches=await service.IsSameSessionAsync(snapshot);
                    Console.WriteLine("identityMatches={0}; canAutoResume={1}",matches,snapshot.CanAutoResume);
                    if(!matches || snapshot.CanAutoResume) errors++;
                }
                var after=await service.GetMediaSessionsAsync();
                foreach(var snapshot in after) Console.WriteLine("after={0}; app={1}",snapshot.PlaybackStatus,snapshot.SourceAppUserModelId);
                if(after.Count!=2 || after.Any(x=>x.PlaybackStatus!=MediaPlaybackStatus.Paused)) errors++;
                var repeated=await service.PausePlayingMediaAsync();
                Console.WriteLine("retainedAfterRepeat={0}",repeated.Count);
                if(repeated.Count!=1) errors++;
            }
        }
        return errors==0?0:1;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Test(string name, Func<Task> test)
    { try { await test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
    private static MediaControlService Service(Backend backend, Action<MediaDiagnostic> log = null, int timeout = 500)
    { return new MediaControlService(backend, log, TimeSpan.FromMilliseconds(timeout)); }
    private static async Task RunAsync()
    {
        await Test("empty manager", async () => { using(var s=Service(new Backend())) Check((await s.PausePlayingMediaAsync()).Count==0,"not empty"); });
        await Test("only Playing pauses, multiple sessions", async () => {
            Session a=new Session(), b=new Session(), paused=new Session(MediaPlaybackStatus.Paused), stopped=new Session(MediaPlaybackStatus.Stopped);
            using(var s=Service(new Backend(a,b,paused,stopped))) { var result=await s.PausePlayingMediaAsync(); Check(result.Count==2 && a.Calls==1 && b.Calls==1 && paused.Calls==0 && stopped.Calls==0,"unsafe targets"); Check(result.All(x=>x.PauseSucceeded && x.PlaybackStatus==MediaPlaybackStatus.Playing && !x.CanAutoResume),"unsafe records"); }
        });
        await Test("read-only enumeration", async () => { var a=new Session(); using(var s=Service(new Backend(a))) { var result=await s.GetMediaSessionsAsync(); Check(result.Count==1 && !result[0].PauseSucceeded && a.Calls==0,"enumeration paused"); } });
        await Test("unsupported control", async () => { var a=new Session{CanPause=false}; using(var s=Service(new Backend(a))) Check((await s.PausePlayingMediaAsync()).Count==0 && a.Calls==0,"unsupported commanded"); });
        await Test("false return not recorded and can retry", async () => { var a=new Session{PauseResult=false}; using(var s=Service(new Backend(a))) { Check((await s.PausePlayingMediaAsync()).Count==0,"false recorded"); a.PauseResult=true; Check((await s.PausePlayingMediaAsync()).Count==1 && a.Calls==2,"retry lost"); } });
        await Test("state changes before command", async () => { var a=new Session{ChangeBeforeCommand=true}; using(var s=Service(new Backend(a))) Check((await s.PausePlayingMediaAsync()).Count==0 && a.Calls==0,"state not rechecked"); });
        await Test("per-session exception preserves other success", async () => { Session bad=new Session{ThrowPause=true}, good=new Session(); using(var s=Service(new Backend(bad,good))) Check((await s.PausePlayingMediaAsync()).Count==1 && good.Calls==1,"failure escaped"); });
        await Test("backend unavailable", async () => { using(var s=Service(new Backend{Throw=true})) Check((await s.GetMediaSessionsAsync()).Count==0,"backend escaped"); });
        await Test("metadata unavailable can pause but cannot identify", async () => { var a=new Session{ThrowMetadata=true}; using(var s=Service(new Backend(a))) { var result=await s.PausePlayingMediaAsync(); Check(result.Count==1 && result[0].Fingerprint==null && !await s.IsSameSessionAsync(result[0]),"unreliable identity"); } });
        await Test("empty metadata cannot identify", async () => { var a=new Session{Metadata=new MediaMetadata("app","")}; using(var s=Service(new Backend(a))) { var result=await s.PausePlayingMediaAsync(); Check(result.Count==1 && !await s.IsSameSessionAsync(result[0]),"empty identity"); } });
        await Test("same reference and fingerprint", async () => { var a=new Session(); using(var s=Service(new Backend(a))) { var result=await s.PausePlayingMediaAsync(); Check(await s.IsSameSessionAsync(result[0]),"reference lost"); } });
        await Test("changed title/artist/album/app invalidates", async () => {
            foreach(var metadata in new[]{new MediaMetadata("app","other","artist","album"),new MediaMetadata("app","title","other","album"),new MediaMetadata("app","title","artist","other"),new MediaMetadata("other","title","artist","album")}) {
                var a=new Session(); using(var s=Service(new Backend(a))) {var result=await s.PausePlayingMediaAsync(); a.Metadata=metadata; Check(!await s.IsSameSessionAsync(result[0]),"changed content accepted");}
            }
        });
        await Test("same metadata different object rejected", async () => { var a=new Session(); var backend=new Backend(a); using(var s=Service(backend)) {var result=await s.PausePlayingMediaAsync(); backend.Sessions.Clear();backend.Sessions.Add(new Session()); Check(!await s.IsSameSessionAsync(result[0]),"new object accepted");} });
        await Test("disappeared session rejected", async () => {var backend=new Backend(new Session()); using(var s=Service(backend)){var result=await s.PausePlayingMediaAsync();backend.Sessions.Clear();Check(!await s.IsSameSessionAsync(result[0]),"missing accepted");} });
        await Test("foreign and enumeration snapshots rejected", async () => {var a=new Session();var backend=new Backend(a);using(var s=Service(backend))using(var other=Service(backend)){var view=await s.GetMediaSessionsAsync();Check(!await s.IsSameSessionAsync(view[0]),"enumeration accepted");var paused=await s.PausePlayingMediaAsync();Check(!await other.IsSameSessionAsync(paused[0]),"foreign accepted");} });
        await Test("duplicate enumeration and delayed state refresh", async () => {var a=new Session{KeepPlaying=true};using(var s=Service(new Backend(a,a))){Check((await s.PausePlayingMediaAsync()).Count==1,"duplicate snapshot");Check((await s.PausePlayingMediaAsync()).Count==1 && a.Calls==1,"duplicate pause");} });
        await Test("current session scope", async () => {Session a=new Session(), b=new Session();var backend=new Backend(a,b){Current=b};using(var s=Service(backend)){Check((await s.PausePlayingMediaAsync(MediaControlScope.CurrentSession)).Count==1 && a.Calls==0 && b.Calls==1,"wrong scope");} });
        await Test("simultaneous calls serialize", async () => {var a=new Session{KeepPlaying=true,DelayMilliseconds=10};var backend=new Backend(a);using(var s=Service(backend)){var tasks=Enumerable.Range(0,12).Select(_=>s.PausePlayingMediaAsync()).ToArray();await Task.WhenAll(tasks);Check(a.Calls==1 && backend.MaxConcurrent==1,"overlapping operations");} });
        await Test("bounded timeout retains pending quarantine", async () => {
            var pending=new TaskCompletionSource<bool>(); var entered=new TaskCompletionSource<bool>();var a=new Session{PauseOverride=token=>{entered.TrySetResult(true);return pending.Task;}};
            var backend=new Backend(a);var logs=new List<string>();using(var s=Service(backend,d=>logs.Add(d.Code),60)){
                var request=s.PausePlayingMediaAsync();await entered.Task;Check((await request).Count==0,"timeout recorded");var calls=backend.Calls;Check((await s.PausePlayingMediaAsync()).Count==0 && backend.Calls==calls,"pending overlapped");Check(!await s.ReleasePausedMediaAsync(),"pending released");pending.SetResult(true);await Task.Delay(20);await s.PausePlayingMediaAsync();Check(a.Calls==1 && logs.Contains("OperationStillPending"),"unknown result retried");}
        });
        await Test("metadata timeout never sends pause", async () => {var pending=new TaskCompletionSource<MediaMetadata>();var a=new Session{MetadataOverride=token=>pending.Task};using(var s=Service(new Backend(a),null,30)){Check((await s.PausePlayingMediaAsync()).Count==0 && a.Calls==0,"timeout paused");pending.SetResult(a.Metadata);} });
        await Test("partial success survives later cancellation", async () => {var cancel=new CancellationTokenSource();Session a=new Session(),b=new Session{PauseOverride=token=>{cancel.Cancel();throw new OperationCanceledException();}};using(var s=Service(new Backend(a,b))){var result=await s.PausePlayingMediaAsync(cancellationToken:cancel.Token);Check(result.Count==1 && result[0].PauseSucceeded,"partial lost");} });
        await Test("cancel before entry sends nothing", async () => {var cancel=new CancellationTokenSource();cancel.Cancel();var a=new Session();using(var s=Service(new Backend(a))){try{await s.PausePlayingMediaAsync(cancellationToken:cancel.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){}Check(a.Calls==0,"cancel sent pause");} });
        await Test("batch release invalidates identity", async () => {var a=new Session{KeepPlaying=true};using(var s=Service(new Backend(a))){var result=await s.PausePlayingMediaAsync();Check(await s.ReleasePausedMediaAsync(),"release failed");Check(!await s.IsSameSessionAsync(result[0]),"old batch accepted");Check((await s.PausePlayingMediaAsync()).Count==1 && a.Calls==2,"next batch blocked");} });
        await Test("diagnostic callback failures isolated", async () => {using(var s=Service(new Backend(new Session()),d=>{throw new Exception("logger failed");}))Check((await s.PausePlayingMediaAsync()).Count==1,"logger escaped"); });
        await Test("diagnostics omit metadata", async () => {var logs=new List<MediaDiagnostic>();var a=new Session{ThrowMetadata=true};using(var s=Service(new Backend(a),d=>logs.Add(d))){await s.PausePlayingMediaAsync();Check(logs.Any(x=>x.Code=="MetadataUnavailable" && x.ExceptionType=="InvalidOperationException"),"diagnostic lost");Check(typeof(MediaDiagnostic).GetProperties().All(p=>p.Name!="Message" && p.Name!="Metadata"),"private log field");} });
        await Test("null identity paused conservatively", async () => {var a=new Session{IdentityValue=null};using(var s=Service(new Backend(a))){var result=await s.PausePlayingMediaAsync();Check(result.Count==1 && !await s.IsSameSessionAsync(result[0]),"null identity accepted");} });
        await Test("dispose cancels active and queued calls", async () => {var entered=new TaskCompletionSource<bool>();var a=new Session{PauseOverride=async token=>{entered.SetResult(true);await Task.Delay(10000,token);return true;}};var s=Service(new Backend(a));var request=s.PausePlayingMediaAsync();await entered.Task;var queued=s.PausePlayingMediaAsync();s.Dispose();s.Dispose();Check((await request).Count==0,"disposed pause recorded");try{await queued;}catch(OperationCanceledException){}Check(a.Calls==1,"queued executed");});
        await Test("synchronous API factory is bounded", async () => {
            var entered=new TaskCompletionSource<bool>();var unblock=new ManualResetEventSlim(false);
            var a=new Session{PauseOverride=token=>{entered.TrySetResult(true);unblock.Wait();return Task.FromResult(true);}};
            using(var s=Service(new Backend(a),null,40)){var request=s.PausePlayingMediaAsync();await entered.Task;var finished=await Task.WhenAny(request,Task.Delay(1000));Check(finished==request,"synchronous API blocked caller");Check((await request).Count==0,"unknown result recorded");unblock.Set();await Task.Delay(20);await s.PausePlayingMediaAsync();Check(a.Calls==1,"unknown result repeated");}
        });
        await Test("known success retained while later command pending", async () => {
            var pending=new TaskCompletionSource<bool>();Session a=new Session(), b=new Session{PauseOverride=token=>pending.Task};
            using(var s=Service(new Backend(a,b),null,40)){var result=await s.PausePlayingMediaAsync();Check(result.Count==1,"known success lost on timeout");Check((await s.PausePlayingMediaAsync()).Count==1,"pending guard lost snapshot");pending.SetResult(false);}
        });
        await Test("fingerprint length framing avoids collisions", async () => {
            var a=new Session{Metadata=new MediaMetadata("ab","c","d","e")};using(var s=Service(new Backend(a))){var result=await s.PausePlayingMediaAsync();a.Metadata=new MediaMetadata("a","bc","d","e");Check(!await s.IsSameSessionAsync(result[0]),"concatenation collision");}
        });
        await Test("identity error fails closed", async () => {
            var a=new Session();var backend=new Backend(a);using(var s=Service(backend)){var result=await s.PausePlayingMediaAsync();backend.Throw=true;Check(!await s.IsSameSessionAsync(result[0]),"backend failure identified");}
        });
        await Test("invalid scope rejected", async () => {using(var s=Service(new Backend())){try{await s.GetMediaSessionsAsync((MediaControlScope)99);throw new Exception("invalid accepted");}catch(ArgumentOutOfRangeException){}}});
    }
    private sealed class Backend : IMediaSessionBackend
    {
        public readonly List<IMediaSession> Sessions;
        public IMediaSession Current;
        public bool Throw;
        public int Calls, Concurrent, MaxConcurrent;
        public Backend(params IMediaSession[] sessions) {Sessions=sessions.ToList();}
        public async Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(MediaControlScope scope,CancellationToken token)
        {
            Calls++; var concurrent=Interlocked.Increment(ref Concurrent);MaxConcurrent=Math.Max(MaxConcurrent,concurrent);
            try {await Task.Delay(1,token);if(Throw)throw new InvalidOperationException("private details");return scope==MediaControlScope.CurrentSession?(Current==null?new IMediaSession[0]:new[]{Current}):Sessions.ToArray();}
            finally {Interlocked.Decrement(ref Concurrent);}
        }
    }
    private sealed class Session : IMediaSession
    {
        public object IdentityValue=new object(); public object Identity {get{return IdentityValue;}}
        public MediaPlaybackStatus Status; public bool CanPause=true,PauseResult=true,ThrowPause,ThrowMetadata,ChangeBeforeCommand,KeepPlaying;
        public int Calls,Reads,DelayMilliseconds;
        public MediaMetadata Metadata=new MediaMetadata("app","title","artist","album");
        public Func<CancellationToken,Task<bool>> PauseOverride;
        public Func<CancellationToken,Task<MediaMetadata>> MetadataOverride;
        public Session(MediaPlaybackStatus status=MediaPlaybackStatus.Playing){Status=status;}
        public Task<MediaPlaybackState> GetPlaybackStateAsync(CancellationToken token){token.ThrowIfCancellationRequested();Reads++;if(ChangeBeforeCommand&&Reads>1)Status=MediaPlaybackStatus.Paused;return Task.FromResult(new MediaPlaybackState(Status,CanPause));}
        public Task<MediaMetadata> GetMetadataAsync(CancellationToken token){if(ThrowMetadata)throw new InvalidOperationException("secret title");return MetadataOverride==null?Task.FromResult(Metadata):MetadataOverride(token);}
        public async Task<bool> TryPauseAsync(CancellationToken token){Calls++;if(PauseOverride!=null)return await PauseOverride(token);if(ThrowPause)throw new InvalidOperationException("secret title");if(DelayMilliseconds>0)await Task.Delay(DelayMilliseconds,token);if(PauseResult&&!KeepPlaying)Status=MediaPlaybackStatus.Paused;return PauseResult;}
    }
}
