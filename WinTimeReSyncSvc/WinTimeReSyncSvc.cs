using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

namespace WinTimeReSyncSvc
{
    public partial class WinTimeReSyncSvc : ServiceBase
    {
        private const string EventSourceName = "WinTimeReSyncSvc";
        private const string LogName = "Application";
        private const double MinimumAdjustmentMilliseconds = 500;

        private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MaximumClockCorrection = TimeSpan.FromDays(7);

        private Timer _timer;
        private int _syncInProgress;
        private volatile bool _stopping;

        public WinTimeReSyncSvc()
        {
            InitializeComponent();
            ConfigureEventLog();
        }

        protected override void OnStart(string[] args)
        {
            _stopping = false;
            Log("Initializing service.");

            _timer = new Timer(SyncInterval.TotalMilliseconds)
            {
                AutoReset = false
            };
            _timer.Elapsed += OnTimer;

            // Synchronize immediately; subsequent runs are scheduled after completion.
            QueueSynchronization();
        }

        protected override void OnStop()
        {
            StopTimer();
            Log("Service stopped.");
        }

        protected override void OnShutdown()
        {
            StopTimer();
            Log("Service stopped because Windows is shutting down.");
        }

        internal void StartInteractive()
        {
            OnStart(new string[0]);
        }

        internal void StopInteractive()
        {
            OnStop();
        }

        internal void SynchronizeOnceInteractive()
        {
            SynchronizeTime();
        }

        private void OnTimer(object sender, ElapsedEventArgs args)
        {
            QueueSynchronization();
        }

        private void QueueSynchronization()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                SynchronizeAndScheduleNextRun();
            });
        }

        private void SynchronizeAndScheduleNextRun()
        {
            if (Interlocked.CompareExchange(ref _syncInProgress, 1, 0) != 0)
            {
                ScheduleNextRun();
                return;
            }

            try
            {
                if (!_stopping)
                {
                    SynchronizeTime();
                }
            }
            catch (Exception ex)
            {
                Log("Time synchronization failed: " + ex, EventLogEntryType.Error);
            }
            finally
            {
                Interlocked.Exchange(ref _syncInProgress, 0);
                ScheduleNextRun();
            }
        }

        private void SynchronizeTime()
        {
            Log("Querying NTP servers.");

            NetworkTimeResult result = DateTimeUtil.GetNetworkTimeUtc();
            DateTime currentUtc = DateTime.UtcNow;
            TimeSpan offset = result.UtcTime - currentUtc;

            Log(string.Format(
                "Got UTC time {0:O} from {1}; offset={2:F3}s; round-trip={3:F0}ms.",
                result.UtcTime,
                result.Server,
                offset.TotalSeconds,
                result.RoundTripTime.TotalMilliseconds));

            if (Math.Abs(offset.TotalMilliseconds) < MinimumAdjustmentMilliseconds)
            {
                Log("Clock is already within the adjustment threshold; no change required.");
                return;
            }

            if (Math.Abs(offset.TotalMilliseconds) > MaximumClockCorrection.TotalMilliseconds)
            {
                throw new InvalidOperationException(string.Format(
                    "Refusing an implausible clock correction of {0}. Maximum allowed correction is {1}.",
                    offset,
                    MaximumClockCorrection));
            }

            if (_stopping)
            {
                Log("Service is stopping; clock adjustment was skipped.");
                return;
            }

            if (!DateTimeUtil.SetSystemUtcTime(result.UtcTime))
            {
                Win32Exception exception = new Win32Exception(Marshal.GetLastWin32Error());
                throw new InvalidOperationException("Windows rejected the system clock update.", exception);
            }

            Log(string.Format(
                "System time updated successfully to {0:O} UTC using {1}.",
                result.UtcTime,
                result.Server));
        }

        private void ScheduleNextRun()
        {
            if (_stopping)
            {
                return;
            }

            Timer timer = _timer;
            if (timer == null)
            {
                return;
            }

            try
            {
                timer.Start();
            }
            catch (ObjectDisposedException)
            {
                // The service can be stopped while a synchronization is finishing.
            }
        }

        private void StopTimer()
        {
            _stopping = true;

            Timer timer = Interlocked.Exchange(ref _timer, null);
            if (timer == null)
            {
                return;
            }

            timer.Stop();
            timer.Elapsed -= OnTimer;
            timer.Dispose();
        }

        private void ConfigureEventLog()
        {
            try
            {
                if (!EventLog.SourceExists(EventSourceName))
                {
                    EventLog.CreateEventSource(EventSourceName, LogName);
                }

                _eventLog.Source = EventSourceName;
                _eventLog.Log = LogName;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Unable to configure the Windows Event Log source: " + ex);

                if (Environment.UserInteractive)
                {
                    Console.Error.WriteLine("Unable to configure the Windows Event Log source: " + ex);
                }

                _eventLog = null;
            }
        }

        private void Log(string message, EventLogEntryType type = EventLogEntryType.Information)
        {
            Trace.WriteLine(message);

            if (Environment.UserInteractive)
            {
                Console.WriteLine("[{0:O}] {1}: {2}", DateTime.UtcNow, type, message);
            }

            EventLog eventLog = _eventLog;
            if (eventLog == null)
            {
                return;
            }

            try
            {
                eventLog.WriteEntry(message, type);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Unable to write to the Windows Event Log: " + ex);

                if (Environment.UserInteractive)
                {
                    Console.Error.WriteLine("Unable to write to the Windows Event Log: " + ex);
                }
            }
        }
    }
}
