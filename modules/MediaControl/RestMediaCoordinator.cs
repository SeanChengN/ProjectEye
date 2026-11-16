using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEye.MediaControl
{
    public sealed class RestMediaOptions
    {
        public bool AutoPause { get; private set; }
        public bool AutoResume { get; private set; }
        public MediaControlScope Scope { get; private set; }
        public RestMediaOptions(bool pause = false, bool resume = false, MediaControlScope scope = MediaControlScope.AllSessions)
        { AutoPause = pause; AutoResume = pause && resume; Scope = scope; }
    }

    public sealed class RestMediaCoordinator : IDisposable
    {
        private readonly object sync = new object();
        private readonly MediaControlService media;
        private Batch current;
        private Guid last;
        private bool disposed, unavailable;
        private sealed class Batch
        {
            internal Guid Id;
            internal RestMediaOptions Options;
            internal CancellationTokenSource Cancel = new CancellationTokenSource();
            internal Task<IReadOnlyList<MediaSessionSnapshot>> Pause;
            internal Task Finish;
        }
        public RestMediaCoordinator(MediaControlService media) { if(media == null) throw new ArgumentNullException("media"); this.media=media; }
        public Task BeginAsync(Guid id, RestMediaOptions options)
        {
            if(options == null) throw new ArgumentNullException("options");
            lock(sync)
            {
                if(disposed || unavailable || id == Guid.Empty || id == last) return Task.FromResult(0);
                if(current != null) return current.Id == id ? (Task)current.Pause : Task.FromResult(0);
                var batch = new Batch {Id=id,Options=options}; current=batch;
                batch.Pause=Task.Run(async () => options.AutoPause ? await media.PausePlayingMediaAsync(options.Scope,batch.Cancel.Token).ConfigureAwait(false) : (IReadOnlyList<MediaSessionSnapshot>)new MediaSessionSnapshot[0]);
                return batch.Pause;
            }
        }
        public Task CompleteAsync(Guid id, bool completedNormally)
        {
            lock(sync)
            {
                var batch=current;
                if(batch==null || batch.Id!=id) return Task.FromResult(0);
                if(!completedNormally) batch.Cancel.Cancel();
                if(batch.Finish==null) batch.Finish=Task.Run(()=>FinishAsync(batch,completedNormally));
                return batch.Finish;
            }
        }
        private async Task FinishAsync(Batch batch,bool normal)
        {
            try
            {
                var snapshots=await batch.Pause.ConfigureAwait(false);
                if(normal && batch.Options.AutoResume && !batch.Cancel.IsCancellationRequested)
                    await media.ResumePausedMediaAsync(snapshots,true,batch.Cancel.Token).ConfigureAwait(false);
            }
            catch { /* Failure is isolated from the rest UI; the service reports diagnostics. */ }
            finally
            {
                bool released=false;
                try { released=await media.ReleasePausedMediaAsync().ConfigureAwait(false); } catch { }
                lock(sync) { if(current==batch) { unavailable=!released; last=batch.Id; current=null; } }
                batch.Cancel.Dispose();
            }
        }
        public void Dispose()
        {
            lock(sync) { if(disposed)return;disposed=true;if(current!=null)current.Cancel.Cancel(); }
            media.Dispose();
        }
    }
}
