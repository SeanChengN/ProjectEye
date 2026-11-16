using System;

namespace ProjectEye.MediaControl
{
    internal sealed class ResumeGuard : IDisposable
    {
        private readonly object sync = new object();
        private IDisposable watch;
        private bool confirmed, invalid, consumed;
        internal ResumeGuard(IResumableMediaSession session) { watch = session.Watch(Playback, Invalidate); if (watch == null) invalid = true; }
        internal bool Confirmed { get { lock(sync) return confirmed; } }
        internal bool Eligible { get { lock(sync) return confirmed && !invalid && !consumed; } }
        internal void Confirm() { lock(sync) { if (!invalid) confirmed = true; } }
        private void Playback(MediaPlaybackStatus state)
        {
            lock(sync)
            {
                // Allow exactly the initial Paused transition. Any later notification is ambiguous.
                if (!confirmed && !invalid && state == MediaPlaybackStatus.Paused) confirmed = true;
                else invalid = true;
            }
        }
        internal void Invalidate() { lock(sync) invalid = true; }
        internal bool Consume() { lock(sync) { if (!confirmed || invalid || consumed) return false; consumed = true; return true; } }
        public void Dispose() { Invalidate(); var subscription = watch; watch = null; if(subscription != null) { try { subscription.Dispose(); } catch { } } }
    }
}
