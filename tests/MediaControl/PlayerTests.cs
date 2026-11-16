using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectEye.MediaControl;

internal static class PlayerTests
{
    internal static async Task<int> Run(string[] ids,bool resume)
    {
        if(ids.Length==0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)return 2;
        var real=new GsmtcMediaSessionBackend();
        IReadOnlyList<MediaSessionSnapshot> before;
        using(var probe=new MediaControlService(real))before=await probe.GetMediaSessionsAsync();
        foreach(var view in before)Console.WriteLine("before={0}; app={1}",view.PlaybackStatus,view.SourceAppUserModelId);
        var selected=before.Where(s=>ids.Contains(s.SourceAppUserModelId,StringComparer.Ordinal)).ToArray();
        if(ids.Any(id=>selected.Count(s=>s.SourceAppUserModelId==id)!=1))
        {Console.Error.WriteLine("Expected one registered session per exact target app id; no command sent.");return 3;}
        if(selected.Any(s=>s.PlaybackStatus!=MediaPlaybackStatus.Playing && s.PlaybackStatus!=MediaPlaybackStatus.Paused && s.PlaybackStatus!=MediaPlaybackStatus.Stopped))
        {Console.Error.WriteLine("Target state is not testable; no command sent.");return 3;}
        int errors=0;
        using(var service=new MediaControlService(new Filter(real,selected.Select(s=>s.SessionReference).ToArray()),d=>{
            Console.WriteLine("diagnostic={0}; error={1}; hresult={2}",d.Code,d.ExceptionType,d.HResult);
            if(d.ExceptionType!=null || d.Code=="PauseRejected" || d.Code=="ResumeRejected")errors++;
        }))
        {
            var expected=selected.Count(s=>s.PlaybackStatus==MediaPlaybackStatus.Playing);
            var paused=await service.PausePlayingMediaAsync();
            Console.WriteLine("expectedPlaying={0}; successfulRecords={1}",expected,paused.Count);
            if(paused.Count!=expected)errors++;
            await Task.Delay(1000);
            foreach(var snapshot in paused)
            {
                var matches=await service.IsSameSessionAsync(snapshot);
                Console.WriteLine("app={0}; identityMatches={1}; canAutoResume={2}",snapshot.SourceAppUserModelId,matches,snapshot.CanAutoResume);
                if(!matches)errors++;
            }
            var afterPause=await service.GetMediaSessionsAsync();
            foreach(var snapshot in afterPause)Console.WriteLine("afterPause={0}; app={1}",snapshot.PlaybackStatus,snapshot.SourceAppUserModelId);
            if(afterPause.Count!=selected.Length || afterPause.Any(s=>s.PlaybackStatus==MediaPlaybackStatus.Playing))errors++;
            var repeated=await service.PausePlayingMediaAsync();
            if(repeated.Count!=paused.Count)errors++;
            if(resume)
            {
                foreach(var snapshot in paused)
                {
                    bool eligible=snapshot.CanAutoResume;
                    int resumed=await service.ResumePausedMediaAsync(new[]{snapshot},true);
                    if(eligible && resumed!=1)errors++;
                    Console.WriteLine("app={0}; resumeSucceeded={1}; outcome={2}",snapshot.SourceAppUserModelId,resumed,resumed==1?"Resumed":eligible?"ResumeRejectedOrEligibilityChanged":"PauseOnly_StrictGuard");
                }
                await Task.Delay(1000);
            }
            foreach(var snapshot in await service.GetMediaSessionsAsync())Console.WriteLine("final={0}; app={1}",snapshot.PlaybackStatus,snapshot.SourceAppUserModelId);
        }
        using(var probe=new MediaControlService(real))
        {
            var final=await probe.GetMediaSessionsAsync();
            foreach(var view in before.Where(s=>!ids.Contains(s.SourceAppUserModelId,StringComparer.Ordinal)))
            {
                var same=final.FirstOrDefault(s=>ReferenceEquals(s.SessionReference,view.SessionReference));
                Console.WriteLine("nonTarget={0}; unchanged={1}",view.SourceAppUserModelId,same!=null&&same.PlaybackStatus==view.PlaybackStatus);
                if(same==null||same.PlaybackStatus!=view.PlaybackStatus)errors++;
            }
        }
        return errors==0?0:1;
    }
    private sealed class Filter:IMediaSessionBackend
    {
        private readonly IMediaSessionBackend real;private readonly object[] identities;
        internal Filter(IMediaSessionBackend real,object[] identities){this.real=real;this.identities=identities;}
        public async Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(MediaControlScope scope,CancellationToken token)
        {return (await real.GetSessionsAsync(scope,token)).Where(s=>identities.Any(id=>ReferenceEquals(id,s.Identity))).ToArray();}
    }
}
