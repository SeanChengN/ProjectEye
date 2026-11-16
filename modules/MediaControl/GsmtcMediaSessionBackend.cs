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
        private sealed class Session : IMediaSession
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
            public Task<bool> TryPauseAsync(CancellationToken cancellationToken) { return session.TryPauseAsync().AsTask(cancellationToken); }
        }
    }
}
