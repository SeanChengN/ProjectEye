using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace ProjectEye.MediaControl
{
    public sealed class GsmtcMediaSessionBackend : IMediaSessionBackend
    {
        private GlobalSystemMediaTransportControlsSessionManager manager;
        public async Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(MediaControlScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (manager == null) manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (scope == MediaControlScope.CurrentSession)
            {
                var current = manager.GetCurrentSession();
                return current == null ? new IMediaSession[0] : new IMediaSession[] { new Session(current) };
            }
            if (scope != MediaControlScope.AllSessions) throw new ArgumentOutOfRangeException("scope");
            return manager.GetSessions().Select(session => (IMediaSession)new Session(session)).ToArray();
        }
        private sealed class Session : IResumableMediaSession
        {
            private readonly GlobalSystemMediaTransportControlsSession session;
            public Session(GlobalSystemMediaTransportControlsSession session) { this.session = session; }
            public object Identity { get { return session; } }
            public Task<MediaPlaybackState> GetPlaybackStateAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = session.GetPlaybackInfo();
                return Task.FromResult(new MediaPlaybackState((MediaPlaybackStatus)(int)state.PlaybackStatus, state.Controls.IsPauseEnabled));
            }
            public async Task<MediaMetadata> GetMetadataAsync(CancellationToken cancellationToken)
            {
                var metadata = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
                return new MediaMetadata(session.SourceAppUserModelId, metadata.Title, metadata.Artist, metadata.AlbumTitle);
            }
            public Task<bool> TryPlayAsync(CancellationToken cancellationToken) { return session.TryPlayAsync().AsTask(cancellationToken); }
            public IDisposable Watch(Action<MediaPlaybackStatus> playbackChanged, Action mediaChanged)
            { return new Subscription(session, playbackChanged, mediaChanged); }
            private sealed class Subscription : IDisposable
            {
                private readonly GlobalSystemMediaTransportControlsSession target;
                private readonly Action<MediaPlaybackStatus> playback;
                private readonly Action media;
                public Subscription(GlobalSystemMediaTransportControlsSession target, Action<MediaPlaybackStatus> playback, Action media)
                {
                    this.target=target;this.playback=playback;this.media=media;
                    target.PlaybackInfoChanged += Playback;
                    try { target.MediaPropertiesChanged += Media; } catch { target.PlaybackInfoChanged -= Playback; throw; }
                }
                private void Playback(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
                { try { playback((MediaPlaybackStatus)(int)sender.GetPlaybackInfo().PlaybackStatus); } catch { media(); } }
                private void Media(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) { media(); }
                public void Dispose() { try { target.PlaybackInfoChanged -= Playback; } finally { target.MediaPropertiesChanged -= Media; } }
            }
            public Task<bool> TryPauseAsync(CancellationToken cancellationToken) { return session.TryPauseAsync().AsTask(cancellationToken); }
        }
    }
}
