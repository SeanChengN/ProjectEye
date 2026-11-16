using System;
using System.Windows.Threading;

namespace ProjectEye.Core.Service
{
    public class RestService : IService
    {
        /// <summary>
        /// 计时器
        /// </summary>
        private readonly DispatcherTimer timer;

        private int timed = 0;
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
            MainService main)
        {
            this.config = config;
            this.main = main;
            //初始化计时器
            timer = new DispatcherTimer();
            timer.Tick += new EventHandler(timer_Tick);
            timer.Interval = new TimeSpan(0, 0, 1);

        }



        public void Init()
        {
        }
        /// <summary>
        /// 开始休息
        /// </summary>
        public void Start()
        {
            if (IsResting) return;
            IsResting = true;
            CycleId = Guid.NewGuid();
            timed = config.options.General.RestTime;
            timer.Start();
            RestStart?.Invoke(this, timed);
        }
        /// <summary>
        /// 休息结束
        /// </summary>
        private void End()
        {
            if (!IsResting) return;
            IsResting = false;
            WindowManager.Hide("TipWindow");
            timer.Stop();
            timed = config.options.General.RestTime;
            main.ReStartWorkTimerWatch();
            OnResetCompleted();

        }
        public void Cancel()
        {
            if (!IsResting) return;
            timer.Stop();
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
