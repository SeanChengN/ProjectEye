namespace ProjectEye.Core.Models.Options
{
    public class MediaModel
    {
        public bool AutoPause { get; set; } = false;
        public bool AutoResume { get; set; } = false;
        // 0: all registered sessions; 1: GSMTC current session.
        public int Scope { get; set; } = 0;
        public bool Logging { get; set; } = true;
    }
}
