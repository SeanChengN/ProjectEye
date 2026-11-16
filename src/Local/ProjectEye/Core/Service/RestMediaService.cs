using ProjectEye.MediaControl;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProjectEye.Core.Service
{
    public sealed class RestMediaService : IService
    {
        private readonly RestService rest;
        private readonly ConfigService config;
        private readonly MainService main;
        private readonly App app;
        private readonly RestMediaCoordinator coordinator;
        private readonly object logLock = new object();
        private volatile bool logEnabled;
        public RestMediaService(RestService rest, ConfigService config, MainService main, App app)
        {
            this.rest=rest;this.config=config;this.main=main;this.app=app;
            coordinator=new RestMediaCoordinator(new MediaControlService(new GsmtcMediaSessionBackend(),Log));
        }
        public void Init()
        {
            rest.RestStart+=Start;rest.RestCompleted+=Complete;rest.RestCancelled+=Cancel;
            main.OnLeaveEvent+=Leave;app.Exit+=Exit;
        }
        private void Start(object sender,int time)
        {
            var options=config.options.Media ?? new ProjectEye.Core.Models.Options.MediaModel();
            logEnabled=options.Logging;
            Observe(coordinator.BeginAsync(rest.CycleId,new RestMediaOptions(options.AutoPause,options.AutoResume,options.Scope==1?MediaControlScope.CurrentSession:MediaControlScope.AllSessions)));
        }
        private void Complete(object sender,int time) { Observe(coordinator.CompleteAsync(rest.CycleId,true)); }
        private void Cancel(object sender,int time) { Observe(coordinator.CompleteAsync(rest.CycleId,false)); }
        private void Leave(object sender,int time) { rest.Cancel(); }
        private void Exit(object sender,System.Windows.ExitEventArgs args)
        {
            rest.Shutdown();coordinator.Dispose();
            rest.RestStart-=Start;rest.RestCompleted-=Complete;rest.RestCancelled-=Cancel;main.OnLeaveEvent-=Leave;app.Exit-=Exit;
        }
        private async void Observe(Task operation) { try { await operation; } catch { } }
        private void Log(MediaDiagnostic diagnostic)
        {
            if(!logEnabled)return;
            lock(logLock)
            {
                try
                {
                    var directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Log");Directory.CreateDirectory(directory);
                    var path=Path.Combine(directory,"Media_"+DateTime.UtcNow.ToString("yyyyMMdd")+".log");
                    if(File.Exists(path)&&new FileInfo(path).Length>=1024*1024)return;
                    File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" "+diagnostic.Code+" "+diagnostic.ExceptionType+" "+diagnostic.HResult+Environment.NewLine,new UTF8Encoding(false));
                }
                catch { }
            }
        }
    }
}
