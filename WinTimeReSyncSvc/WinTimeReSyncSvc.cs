using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace WinTimeReSyncSvc
{
    public partial class WinTimeReSyncSvc : ServiceBase
    {
        private const string eventSourceName = "WinTimeReSyncSvc";
        private const string logName = "";

        private const double ONE_SEC = 1000;

        public WinTimeReSyncSvc()
        {
            InitializeComponent();
            try
            {
                _eventLog = new EventLog();
                if (!EventLog.SourceExists(eventSourceName))
                {
                    EventLog.CreateEventSource(eventSourceName, logName);
                }

                _eventLog.Source = eventSourceName;
                _eventLog.Log = logName;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        protected override void OnStart(string[] args)
        {
            _eventLog.WriteEntry("Initializing service.");

            Timer timer = new System.Timers.Timer
            {
                Interval = 10 * 60 * ONE_SEC // 60 seconds
            };
            timer.Elapsed += new ElapsedEventHandler(this.OnTimer);
            timer.Start();
        }

        public void OnTimer(object sender, ElapsedEventArgs args)
        {
            // TODO: Insert monitoring activities here.
            _eventLog.WriteEntry("Pooling ntp server", EventLogEntryType.Information);
            var dt = DateTimeUtil.GetNetworkTime();
            _eventLog.WriteEntry($"Got time from ntp server: {dt}", EventLogEntryType.Information);
            if (!DateTimeUtil.SetSystemLocalTime(dt))
            {
                var ex = new Win32Exception(Marshal.GetLastWin32Error());
                _eventLog.WriteEntry($"Error setting system date time: {ex}", EventLogEntryType.Error);
            }
            else
            {
                _eventLog.WriteEntry($"Set system date time to: {dt} successfully", EventLogEntryType.Information);
            }
        }

        protected override void OnStop()
        {
            _eventLog.WriteEntry("Service stopped.");
        }

        protected override void OnShutdown()
        {
        }

        private void eventLog1_EntryWritten(object sender, EntryWrittenEventArgs e)
        {

        }
    }
}
