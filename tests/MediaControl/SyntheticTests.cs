using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using ProjectEye.MediaControl;

internal static class SyntheticTests
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    internal static async Task<int> Run()
    {
        SetCurrentProcessExplicitAppUserModelID("ProjectEye.MediaControl.TestFixture");
        string title="ProjectEye synthetic "+Guid.NewGuid().ToString("N");
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"synthetic.wav");
        using(var file=new BinaryWriter(File.Create(path)))
        {
            int bytes=8000*2*10;
            file.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));file.Write(bytes+36);file.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));file.Write(16);file.Write((short)1);file.Write((short)1);file.Write(8000);file.Write(16000);file.Write((short)2);file.Write((short)16);file.Write(System.Text.Encoding.ASCII.GetBytes("data"));file.Write(bytes);file.Write(new byte[bytes]);
        }
        using(var player=new MediaPlayer())
        {
            player.Volume=0;player.IsLoopingEnabled=true;
            player.CommandManager.IsEnabled=true;
            player.SystemMediaTransportControls.IsEnabled=true;
            player.SystemMediaTransportControls.IsPlayEnabled=true;
            player.SystemMediaTransportControls.IsPauseEnabled=true;
            var display=player.SystemMediaTransportControls.DisplayUpdater;display.Type=MediaPlaybackType.Music;display.MusicProperties.Title=title;display.Update();
            player.Source=MediaSource.CreateFromUri(new Uri(path));player.Play();
            var backend=new FilteredBackend(title);
            IReadOnlyList<IMediaSession> owned=new IMediaSession[0];
            for(int retry=0;retry<30;retry++){owned=await backend.GetSessionsAsync(MediaControlScope.AllSessions,CancellationToken.None);if(owned.Count>0&&(await owned[0].GetPlaybackStateAsync(CancellationToken.None)).Status==ProjectEye.MediaControl.MediaPlaybackStatus.Playing)break;await Task.Delay(200);}
            if(owned.Count==0){Console.WriteLine("SKIP: synthetic player did not publish a discoverable GSMTC session");return 3;}
            using(var service=new MediaControlService(backend,d=>Console.WriteLine("event="+d.Code)))
            {
                var snapshots=await service.PausePlayingMediaAsync();Console.WriteLine("ownedPaused={0}",snapshots.Count);
                if(snapshots.Count!=1) return 1;
                await Task.Delay(1000);
                Console.WriteLine("resumeEligible={0}",snapshots[0].CanAutoResume);
                int resumed=await service.ResumePausedMediaAsync(snapshots,true);Console.WriteLine("ownedResumed={0}",resumed);
                if(resumed!=1)return 1;
                await Task.Delay(500);
                var state=await owned[0].GetPlaybackStateAsync(CancellationToken.None);
                Console.WriteLine("ownedFinalStatus={0}",state.Status);
                return state.Status==ProjectEye.MediaControl.MediaPlaybackStatus.Playing?0:1;
            }
        }
    }
    private sealed class FilteredBackend:IMediaSessionBackend
    {
        private readonly string title;private readonly GsmtcMediaSessionBackend real=new GsmtcMediaSessionBackend();
        internal FilteredBackend(string title){this.title=title;}
        public async Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(MediaControlScope scope,CancellationToken token)
        {
            var found=new List<IMediaSession>();foreach(var session in await real.GetSessionsAsync(scope,token))
            {var metadata=await session.GetMetadataAsync(token);if(metadata.Title==title&&(metadata.SourceAppUserModelId=="ProjectEye.MediaControl.TestFixture"||metadata.SourceAppUserModelId.IndexOf("MediaControl.Tests",StringComparison.OrdinalIgnoreCase)>=0))found.Add(session);}return found.ToArray();
        }
    }
}
