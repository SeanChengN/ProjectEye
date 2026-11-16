using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEye.MediaControl
{
    public enum MediaPlaybackStatus { Unknown = -1, Closed = 0, Opened = 1, Changing = 2, Stopped = 3, Playing = 4, Paused = 5 }
    public enum MediaControlScope { AllSessions, CurrentSession }

    public sealed class MediaPlaybackState
    {
        public MediaPlaybackStatus Status { get; private set; }
        public bool CanPause { get; private set; }
        public MediaPlaybackState(MediaPlaybackStatus status, bool canPause) { Status = status; CanPause = canPause; }
    }

    public sealed class MediaMetadata
    {
        public string SourceAppUserModelId { get; private set; }
        public string Title { get; private set; }
        public string Artist { get; private set; }
        public string Album { get; private set; }
        public MediaMetadata(string app, string title, string artist = null, string album = null)
        { SourceAppUserModelId = app ?? ""; Title = title ?? ""; Artist = artist ?? ""; Album = album ?? ""; }
        internal string Fingerprint()
        {
            // Length framing prevents ambiguous metadata concatenations. Never persist this data.
            if (string.IsNullOrWhiteSpace(SourceAppUserModelId) || string.IsNullOrWhiteSpace(Title)) return null;
            var value = new StringBuilder();
            foreach (var part in new[] { SourceAppUserModelId, Title, Artist, Album })
                value.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part);
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value.ToString())));
        }
    }

    public interface IMediaSession
    {
        object Identity { get; }
        Task<MediaPlaybackState> GetPlaybackStateAsync(CancellationToken cancellationToken);
        Task<MediaMetadata> GetMetadataAsync(CancellationToken cancellationToken);
        Task<bool> TryPauseAsync(CancellationToken cancellationToken);
    }

    public interface IResumableMediaSession : IMediaSession
    {
        // A watcher must cover both playback and media changes; unknown observations invalidate it.
        IDisposable Watch(Action<MediaPlaybackStatus> playbackChanged, Action mediaChanged);
        Task<bool> TryPlayAsync(CancellationToken cancellationToken);
    }

    public interface IMediaSessionBackend
    {
        Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(MediaControlScope scope, CancellationToken cancellationToken);
    }

    public sealed class MediaSessionSnapshot
    {
        internal readonly Guid Owner;
        internal readonly long Epoch;
        public object SessionReference { get; private set; }
        public string SourceAppUserModelId { get; private set; }
        public string Title { get; private set; }
        public string Artist { get; private set; }
        public string Album { get; private set; }
        public string Fingerprint { get; private set; }
        public MediaPlaybackStatus PlaybackStatus { get; private set; }
        public bool PauseSucceeded { get; private set; }
        internal ResumeGuard Guard;
        public bool CanAutoResume { get { return PauseSucceeded && Guard != null && Guard.Eligible; } }
        internal MediaSessionSnapshot(Guid owner, long epoch, object identity, MediaMetadata metadata, MediaPlaybackStatus status, bool paused)
        {
            Owner = owner; Epoch = epoch; SessionReference = identity;
            SourceAppUserModelId = metadata == null ? "" : metadata.SourceAppUserModelId;
            Title = metadata == null ? "" : metadata.Title; Artist = metadata == null ? "" : metadata.Artist;
            Album = metadata == null ? "" : metadata.Album; Fingerprint = metadata == null ? null : metadata.Fingerprint();
            PlaybackStatus = status; PauseSucceeded = paused;
        }
    }

    public sealed class MediaDiagnostic
    {
        public string Code { get; private set; }
        public string ExceptionType { get; private set; }
        public int HResult { get; private set; }
        internal MediaDiagnostic(string code, Exception error)
        { Code = code; ExceptionType = error == null ? null : error.GetType().Name; HResult = error == null ? 0 : error.HResult; }
    }

    public sealed class MediaControlService : IDisposable
    {
        private readonly IMediaSessionBackend backend;
        private readonly Action<MediaDiagnostic> diagnostic;
        private readonly TimeSpan timeout;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Guid owner = Guid.NewGuid();
        private readonly List<MediaSessionSnapshot> paused = new List<MediaSessionSnapshot>();
        private readonly HashSet<object> commanded = new HashSet<object>(new ReferenceComparer());
        private Task outstanding;
        private int disposed;
        private long epoch;

        public MediaControlService(IMediaSessionBackend backend, Action<MediaDiagnostic> diagnostic = null, TimeSpan? operationTimeout = null)
        {
            if (backend == null) throw new ArgumentNullException("backend");
            this.backend = backend; this.diagnostic = diagnostic; timeout = operationTimeout ?? TimeSpan.FromSeconds(5);
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException("operationTimeout");
        }

        public Task<IReadOnlyList<MediaSessionSnapshot>> GetMediaSessionsAsync(MediaControlScope scope = MediaControlScope.AllSessions, CancellationToken cancellationToken = default(CancellationToken))
        { return ExecuteAsync(false, scope, cancellationToken); }

        // Successful records accumulate for this service's current batch; repeats do not re-pause them.
        public Task<IReadOnlyList<MediaSessionSnapshot>> PausePlayingMediaAsync(MediaControlScope scope = MediaControlScope.AllSessions, CancellationToken cancellationToken = default(CancellationToken))
        { return ExecuteAsync(true, scope, cancellationToken); }

        private async Task<IReadOnlyList<MediaSessionSnapshot>> ExecuteAsync(bool pause, MediaControlScope scope, CancellationToken token)
        {
            ValidateScope(scope);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    if (!Ready()) return pause ? Copy(paused) : Empty();
                    var views = new List<MediaSessionSnapshot>();
                    var sessions = await Bounded(t => backend.GetSessionsAsync(scope, t), linked.Token).ConfigureAwait(false);
                    foreach (var session in sessions ?? new IMediaSession[0])
                    {
                        if (linked.IsCancellationRequested) break;
                        if (session == null) continue;
                        try
                        {
                            var state = await Bounded(session.GetPlaybackStateAsync, linked.Token).ConfigureAwait(false);
                            MediaMetadata metadata = null;
                            try { metadata = await Bounded(session.GetMetadataAsync, linked.Token).ConfigureAwait(false); }
                            catch (Exception error) when (!(error is OperationCanceledException) && !(error is TimeoutException)) { Log("MetadataUnavailable", error); }
                            var identity = session.Identity;
                            if (!pause) { views.Add(new MediaSessionSnapshot(owner, epoch, identity, metadata, state.Status, false)); continue; }
                            if (state.Status != MediaPlaybackStatus.Playing) continue;
                            if (!state.CanPause) { Log("PauseUnsupported"); continue; }
                            var commandKey = identity ?? session;
                            if (commanded.Contains(commandKey)) continue;
                            var current = await Bounded(session.GetPlaybackStateAsync, linked.Token).ConfigureAwait(false);
                            if (current.Status != MediaPlaybackStatus.Playing || !current.CanPause) continue;
                            // Mark before issuing: a cancelled/timed-out operation might still execute later.
                            commanded.Add(commandKey);
                            ResumeGuard guard = null;
                            var resumable = session as IResumableMediaSession;
                            if (resumable != null && identity != null && metadata != null && metadata.Fingerprint() != null)
                            {
                                try { guard = new ResumeGuard(resumable); }
                                catch (Exception error) { Log("WatchUnavailable", error); }
                            }
                            bool success;
                            try { success = await Bounded(session.TryPauseAsync, linked.Token).ConfigureAwait(false); }
                            catch { if (guard != null) guard.Dispose(); throw; }
                            if (!success) { if (guard != null) guard.Dispose(); commanded.Remove(commandKey); Log("PauseRejected"); continue; }
                            var snapshot = new MediaSessionSnapshot(owner, epoch, identity, metadata, state.Status, true) { Guard = guard };
                            paused.Add(snapshot);
                            if (guard != null)
                            {
                                // State polling cannot consume the initial event: a late own Paused event
                                // would otherwise look like a second, user-originated notification.
                                for (int retry = 0; retry < 20 && !guard.Confirmed && !guard.Invalid; retry++)
                                    await Task.Delay(100, linked.Token).ConfigureAwait(false);
                                if (!guard.Confirmed) guard.Invalidate();
                                if (!guard.Eligible) Log("ResumeGuard:" + guard.Reason);
                            }
                            Log("PauseSucceeded");
                        }
                        catch (OperationCanceledException) { Log("Cancelled"); break; }
                        catch (TimeoutException error) { Log("Timeout", error); break; }
                        catch (Exception error) { Log("SessionFailed", error); }
                    }
                    return pause ? Copy(paused) : Copy(views);
                }
                catch (OperationCanceledException) { Log("Cancelled"); return pause ? Copy(paused) : Empty(); }
                catch (Exception error) { Log(error is TimeoutException ? "Timeout" : "BackendFailed", error); return pause ? Copy(paused) : Empty(); }
                finally { gate.Release(); }
            }
        }

        // This verifies identity and metadata only; it deliberately does not issue play commands.
        public async Task<bool> IsSameSessionAsync(MediaSessionSnapshot snapshot, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (snapshot == null) return false;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    if (!Ready() || snapshot.Owner != owner || snapshot.Epoch != epoch || !snapshot.PauseSucceeded || snapshot.SessionReference == null || snapshot.Fingerprint == null) return false;
                    var sessions = await Bounded(t => backend.GetSessionsAsync(MediaControlScope.AllSessions, t), linked.Token).ConfigureAwait(false);
                    foreach (var session in sessions ?? new IMediaSession[0])
                        if (session != null && ReferenceEquals(snapshot.SessionReference, session.Identity))
                        {
                            var metadata = await Bounded(session.GetMetadataAsync, linked.Token).ConfigureAwait(false);
                            return metadata != null && metadata.Fingerprint() == snapshot.Fingerprint;
                        }
                    return false;
                }
                catch (Exception error) { Log("IdentityUnverified", error); return false; }
                finally { gate.Release(); }
            }
        }

        public async Task<int> ResumePausedMediaAsync(IReadOnlyList<MediaSessionSnapshot> snapshots, bool autoResumeEnabled = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!autoResumeEnabled || snapshots == null) return 0;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                int resumed = 0;
                try
                {
                    if (!Ready()) return 0;
                    var sessions = await Bounded(t => backend.GetSessionsAsync(MediaControlScope.AllSessions, t), linked.Token).ConfigureAwait(false);
                    foreach (var snapshot in snapshots)
                    {
                        if (snapshot == null || snapshot.Owner != owner || snapshot.Epoch != epoch || !paused.Contains(snapshot)) continue;
                        if (!snapshot.CanAutoResume) { Log("ResumeIneligible:" + (snapshot.Guard == null ? "NoWatch" : snapshot.Guard.Reason)); continue; }
                        var session = (sessions ?? new IMediaSession[0]).FirstOrDefault(s => s != null && ReferenceEquals(s.Identity, snapshot.SessionReference)) as IResumableMediaSession;
                        if (session == null) { snapshot.Guard.Invalidate(); continue; }
                        try
                        {
                            var metadata = await Bounded(session.GetMetadataAsync, linked.Token).ConfigureAwait(false);
                            var state = await Bounded(session.GetPlaybackStateAsync, linked.Token).ConfigureAwait(false);
                            if (metadata == null || metadata.Fingerprint() != snapshot.Fingerprint || state.Status != MediaPlaybackStatus.Paused) { snapshot.Guard.Invalidate(); continue; }
                            // Recheck the guard inside the command factory after any thread-pool delay.
                            var succeeded = await Bounded(t => snapshot.Guard.Consume() ? session.TryPlayAsync(t) : Task.FromResult(false), linked.Token).ConfigureAwait(false);
                            if (succeeded) { resumed++; Log("ResumeSucceeded"); } else Log("ResumeRejected");
                        }
                        catch (OperationCanceledException) { snapshot.Guard.Invalidate(); break; }
                        catch (TimeoutException error) { snapshot.Guard.Invalidate(); Log("ResumeTimeout", error); break; }
                        catch (Exception error) { snapshot.Guard.Invalidate(); Log("ResumeFailed", error); }
                    }
                    return resumed;
                }
                catch (Exception error) { Log("ResumeUnavailable", error); return resumed; }
                finally { gate.Release(); }
            }
        }
        // Phase 3 calls this only after closing a rest batch. Never races an uncompleted API call.
        public async Task<bool> ReleasePausedMediaAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                try { if (!Ready()) return false; foreach (var snapshot in paused) if (snapshot.Guard != null) snapshot.Guard.Dispose(); paused.Clear(); commanded.Clear(); epoch++; return true; }
                finally { gate.Release(); }
            }
        }

        private bool Ready()
        {
            if (Volatile.Read(ref disposed) != 0) return false;
            if (outstanding != null && !outstanding.IsCompleted) { Log("OperationStillPending"); return false; }
            outstanding = null; return true;
        }

        private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            // The factory itself is off the WPF dispatcher and is included in the timeout.
            var task = Task.Run(() => { cancel.Token.ThrowIfCancellationRequested(); return operation(cancel.Token); });
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var delay = Task.Delay(timeout, timer.Token);
                await Task.WhenAny(task, delay).ConfigureAwait(false);
                if (task.IsCompleted)
                {
                    timer.Cancel(); cancel.Dispose(); return await task.ConfigureAwait(false);
                }
                outstanding = task;
                _ = task.ContinueWith(t => { if (t.IsFaulted) { var ignored = t.Exception; } cancel.Dispose(); }, TaskScheduler.Default);
                try { cancel.Cancel(); } catch (Exception error) { Log("CancelFailed", error); }
                token.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }
        }

        private void Log(string code, Exception error = null)
        { try { if (diagnostic != null) diagnostic(new MediaDiagnostic(code, error)); } catch { /* Diagnostics must not break eye care. */ } }
        private static void ValidateScope(MediaControlScope scope)
        { if (scope != MediaControlScope.AllSessions && scope != MediaControlScope.CurrentSession) throw new ArgumentOutOfRangeException("scope"); }
        private static IReadOnlyList<MediaSessionSnapshot> Empty() { return Copy(new MediaSessionSnapshot[0]); }
        private static IReadOnlyList<MediaSessionSnapshot> Copy(IEnumerable<MediaSessionSnapshot> values)
        { return new ReadOnlyCollection<MediaSessionSnapshot>(values.ToList()); }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { lifetime.Cancel(); } catch { }
            _ = Task.Run(async () => { await gate.WaitAsync().ConfigureAwait(false); try { foreach (var snapshot in paused) if (snapshot.Guard != null) snapshot.Guard.Dispose(); paused.Clear(); commanded.Clear(); } finally { gate.Release(); } });
        }
        private sealed class ReferenceComparer : IEqualityComparer<object>
        { public new bool Equals(object a, object b) { return ReferenceEquals(a, b); } public int GetHashCode(object value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); } }
    }
}
