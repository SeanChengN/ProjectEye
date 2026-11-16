using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectEye.MediaControl;

internal static class ResumeTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test,Action<bool,string> check)
    {
        await test("resume disabled by default",async()=>{var a=new Session();using(var s=new MediaControlService(new Backend(a))){var r=await s.PausePlayingMediaAsync();check(r[0].CanAutoResume,"not eligible");check(await s.ResumePausedMediaAsync(r)==0&&a.Plays==0,"default resumed");}});
        await test("safe resume once",async()=>{var a=new Session();using(var s=new MediaControlService(new Backend(a))){var r=await s.PausePlayingMediaAsync();check(await s.ResumePausedMediaAsync(r,true)==1,"not resumed");check(await s.ResumePausedMediaAsync(r,true)==0&&a.Plays==1,"double resume");}});
        await test("user playback change invalidates resume",async()=>{var a=new Session();using(var s=new MediaControlService(new Backend(a))){var r=await s.PausePlayingMediaAsync();a.Change(MediaPlaybackStatus.Playing);a.Change(MediaPlaybackStatus.Paused);check(!r[0].CanAutoResume&&await s.ResumePausedMediaAsync(r,true)==0,"user overwritten");}});
        await test("duplicate Paused notification conservatively invalidates",async()=>{var a=new Session();using(var s=new MediaControlService(new Backend(a))){var r=await s.PausePlayingMediaAsync();a.Change(MediaPlaybackStatus.Paused);check(await s.ResumePausedMediaAsync(r,true)==0,"ambiguous event resumed");}});
        await test("media changed away and back invalidates",async()=>{var a=new Session();using(var s=new MediaControlService(new Backend(a))){var r=await s.PausePlayingMediaAsync();a.Media();check(await s.ResumePausedMediaAsync(r,true)==0,"changed resumed");}});
        await test("unreported metadata change still rejected",async()=>{var a=new Session();using(var s=new MediaControlService(new Backend(a))){var r=await s.PausePlayingMediaAsync();a.Title="different";check(await s.ResumePausedMediaAsync(r,true)==0,"fingerprint skipped");}});
        await test("gone session never resumed",async()=>{var a=new Session();var b=new Backend(a);using(var s=new MediaControlService(b)){var r=await s.PausePlayingMediaAsync();b.Sessions.Clear();check(await s.ResumePausedMediaAsync(r,true)==0,"gone resumed");}});
        await test("play false consumes eligibility",async()=>{var a=new Session{PlayResult=false};using(var s=new MediaControlService(new Backend(a))){var r=await s.PausePlayingMediaAsync();check(await s.ResumePausedMediaAsync(r,true)==0,"false success");await s.ResumePausedMediaAsync(r,true);check(a.Plays==1,"play retried");}});
        await test("release unsubscribes watch",async()=>{var a=new Session();using(var s=new MediaControlService(new Backend(a))){var r=await s.PausePlayingMediaAsync();await s.ReleasePausedMediaAsync();check(a.Unsubscribed&&!r[0].CanAutoResume,"watch leaked");}});
        await test("coordinator disabled settings",async()=>{var a=new Session();using(var c=new RestMediaCoordinator(new MediaControlService(new Backend(a)))){var id=Guid.NewGuid();await c.BeginAsync(id,new RestMediaOptions());await c.CompleteAsync(id,true);check(a.Pauses==0&&a.Plays==0,"disabled affected");}});
        await test("coordinator duplicate start complete",async()=>{var a=new Session();using(var c=new RestMediaCoordinator(new MediaControlService(new Backend(a)))){var id=Guid.NewGuid();await Task.WhenAll(c.BeginAsync(id,new RestMediaOptions(true,true)),c.BeginAsync(id,new RestMediaOptions(true,true)));await Task.WhenAll(c.CompleteAsync(id,true),c.CompleteAsync(id,true));await c.BeginAsync(id,new RestMediaOptions(true,true));check(a.Pauses==1&&a.Plays==1,"duplicate cycle");}});
        await test("coordinator cancelled never resumes",async()=>{var a=new Session();using(var c=new RestMediaCoordinator(new MediaControlService(new Backend(a)))){var id=Guid.NewGuid();await c.BeginAsync(id,new RestMediaOptions(true,true));await c.CompleteAsync(id,false);check(a.Pauses==1&&a.Plays==0,"cancel resumed");}});
        await test("completion waits pending pause",async()=>{var entered=new TaskCompletionSource<bool>();var release=new TaskCompletionSource<bool>();var a=new Session{BeforePause=async()=>{entered.SetResult(true);await release.Task;}};using(var c=new RestMediaCoordinator(new MediaControlService(new Backend(a)))){var id=Guid.NewGuid();var begin=c.BeginAsync(id,new RestMediaOptions(true,true));await entered.Task;var end=c.CompleteAsync(id,true);check(!end.IsCompleted,"did not wait");release.SetResult(true);await Task.WhenAll(begin,end);check(a.Pauses==1&&a.Plays==1,"pause omitted");}});
        await test("delayed own Paused event is not consumed by polling",async()=>{var a=new Session{PauseEventDelay=80};using(var service=new MediaControlService(new Backend(a))){var records=await service.PausePlayingMediaAsync();await Task.Delay(150);check(records[0].CanAutoResume,"late own event invalidated");check(await service.ResumePausedMediaAsync(records,true)==1,"late own event prevented resume");}});
        await test("no pause notification cannot auto resume",async()=>{var a=new Session{SuppressPauseEvent=true};using(var service=new MediaControlService(new Backend(a))){var records=await service.PausePlayingMediaAsync();check(!records[0].CanAutoResume&&await service.ResumePausedMediaAsync(records,true)==0,"poll-only eligibility");}});
        await test("resume exception isolated",async()=>{var a=new Session{ThrowPlay=true};using(var service=new MediaControlService(new Backend(a))){var records=await service.PausePlayingMediaAsync();check(await service.ResumePausedMediaAsync(records,true)==0,"exception success");check(!records[0].CanAutoResume,"exception retriable");}});
        await test("dispose active rest never resumes",async()=>{var a=new Session();var coordinator=new RestMediaCoordinator(new MediaControlService(new Backend(a)));var id=Guid.NewGuid();await coordinator.BeginAsync(id,new RestMediaOptions(true,true));coordinator.Dispose();await coordinator.CompleteAsync(id,true);check(a.Plays==0,"exit resumed");});
        await test("new cycle cannot replace active cycle",async()=>{var a=new Session();using(var coordinator=new RestMediaCoordinator(new MediaControlService(new Backend(a)))){var id=Guid.NewGuid();await coordinator.BeginAsync(id,new RestMediaOptions(true,false));await coordinator.BeginAsync(Guid.NewGuid(),new RestMediaOptions(true,true));await coordinator.CompleteAsync(id,true);check(a.Pauses==1&&a.Plays==0,"active replaced");}});
        await test("coordinator sequential twenty cycles",async()=>{var a=new Session();using(var c=new RestMediaCoordinator(new MediaControlService(new Backend(a)))){for(int i=0;i<20;i++){a.Change(MediaPlaybackStatus.Playing);var id=Guid.NewGuid();await c.BeginAsync(id,new RestMediaOptions(true,true));await c.CompleteAsync(id,true);}check(a.Pauses==20&&a.Plays==20,"cycles lost");}});
    }
    private sealed class Backend:IMediaSessionBackend
    {internal List<IMediaSession> Sessions;internal Backend(IMediaSession a){Sessions=new List<IMediaSession>{a};}public Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(MediaControlScope scope,CancellationToken t){return Task.FromResult((IReadOnlyList<IMediaSession>)Sessions.ToArray());}}
    private sealed class Session:IResumableMediaSession
    {
        private readonly object identity=new object();public object Identity{get{return identity;}}
        internal int Pauses,Plays,PauseEventDelay;internal bool SuppressPauseEvent;internal string Title="title";internal bool PlayResult=true,Unsubscribed,ThrowPlay;internal Func<Task> BeforePause;
        private MediaPlaybackStatus state=MediaPlaybackStatus.Playing;private Action<MediaPlaybackStatus> playback;internal Action Media=()=>{};
        public Task<MediaPlaybackState> GetPlaybackStateAsync(CancellationToken t){return Task.FromResult(new MediaPlaybackState(state,true));}
        public Task<MediaMetadata> GetMetadataAsync(CancellationToken t){return Task.FromResult(new MediaMetadata("app",Title));}
        public async Task<bool> TryPauseAsync(CancellationToken t){Pauses++;if(BeforePause!=null)await BeforePause();state=MediaPlaybackStatus.Paused;
            if(!SuppressPauseEvent){if(PauseEventDelay==0){if(playback!=null)playback(state);}else{_ = Task.Run(async()=>{await Task.Delay(PauseEventDelay);if(playback!=null)playback(MediaPlaybackStatus.Paused);});}}
            return true;}
        public Task<bool> TryPlayAsync(CancellationToken t){Plays++;if(ThrowPlay)throw new InvalidOperationException("play failure");if(PlayResult)Change(MediaPlaybackStatus.Playing);return Task.FromResult(PlayResult);}
        internal void Change(MediaPlaybackStatus value){state=value;if(playback!=null)playback(value);}
        public IDisposable Watch(Action<MediaPlaybackStatus> p,Action m){playback=p;Media=m;return new Subscription(()=>{Unsubscribed=true;playback=null;Media=()=>{};});}
        private sealed class Subscription:IDisposable{private readonly Action dispose;internal Subscription(Action d){dispose=d;}public void Dispose(){dispose();}}
    }
}
