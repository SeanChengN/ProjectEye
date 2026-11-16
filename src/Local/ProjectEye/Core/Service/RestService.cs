using System;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace ProjectEye.Core.Service
{
    public class RestService : IService
    {
        /// <summary>
        /// 计时器
        /// </summary>
        private readonly DispatcherTimer timer;

        private int timed = 0;
        private readonly DesktopControlService desktop;
        private bool minimizeThisCycle;
        private bool shuttingDown;
        private bool cancelled;
        private Task finishTask;
        public bool IsResting { get; private set; }
        public Guid CycleId { get; private set; }
        public event RestEventHandler RestCancelled;

        /// <summary>
        /// 倒计时更改时发生
        /// </summary>
        public event RestEventHandler TimeChanged;
        /// <summary>
        /// 休息结束时发生
        /// </summary>
        public event RestEventHandler RestCompleted;
        /// <summary>
        /// 进入休息状态时发生
        /// </summary>
        public event RestEventHandler RestStart;
        private readonly ConfigService config;
        private readonly MainService main;
        public RestService(
            ConfigService config,
            MainService main,
            DesktopControlService desktop)
        {
            this.desktop = desktop;
            this.config = config;
            this.main = main;
            //初始化计时器
            timer = new DispatcherTimer();
            timer.Tick += new EventHandler(timer_Tick);
            timer.Interval = new TimeSpan(0, 0, 1);

        }



        public void Init()
        {
            main.BeforeLeave = CancelAsync;
            System.Windows.Application.Current.Exit += (sender, args) => Shutdown();
        }
        /// <summary>
        /// 开始休息
        /// </summary>
        public void Start()
        {
            if (IsResting || shuttingDown || (finishTask != null && !finishTask.IsCompleted)) return;
            cancelled = false;
            finishTask = null;
            minimizeThisCycle = config.options.Behavior.MinimizeWindowsAfterRest;
            IsResting = true;
            CycleId = Guid.NewGuid();
            timed = config.options.General.RestTime;
            timer.Start();
            RestStart?.Invoke(this, timed);
        }
        /// <summary>
        /// 休息结束
        /// </summary>
        private async void End() { await FinishAsync(true); }
        public void Cancel() { ObserveCancel(); }
        private async void ObserveCancel() { await CancelAsync(); }
        public Task CancelAsync()
        {
            cancelled = true;
            return FinishAsync(false);
        }
        private Task FinishAsync(bool normal)
        {
            if (shuttingDown) return Task.FromResult(0);
            if (!IsResting) { WindowManager.Hide("TipWindow"); return Task.FromResult(0); }
            if (finishTask != null) return finishTask;
            timer.Stop();
            finishTask = FinishCoreAsync(normal);
            return finishTask;
        }
        private async Task FinishCoreAsync(bool normal)
        {
            // Keep masks visible until this one bounded operation has completed.
            if (minimizeThisCycle)
            {
                try { await desktop.MinimizeAsync(); } catch { /* Eye care must still finish. */ }
            }
            if (shuttingDown) return;
            IsResting = false;
            WindowManager.Hide("TipWindow");
            timed = config.options.General.RestTime;
            if (normal && !cancelled)
            {
                main.ReStartWorkTimerWatch();
                OnResetCompleted();
            }
            else RestCancelled?.Invoke(this, 0);
        }
        public void Shutdown()
        {
            shuttingDown = true;
            timer.Stop();
            desktop.Shutdown();
            if (!IsResting) return;
            IsResting = false;
            RestCancelled?.Invoke(this, 0);
        }
        private void timer_Tick(object sender, EventArgs e)
        {
            if (timed <= 1)
            {
                End();
            }
            else
            {
                timed--;
                OnTimeChanged();
            }
        }
        private void OnTimeChanged()
        {
            TimeChanged?.Invoke(this, timed);
        }
        private void OnResetCompleted()
        {
            RestCompleted?.Invoke(this, 0);
        }


    }
}
