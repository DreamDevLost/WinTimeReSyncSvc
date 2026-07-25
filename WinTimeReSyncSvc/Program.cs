using System;
using System.ServiceProcess;

namespace WinTimeReSyncSvc
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                RunInteractive(args);
                return;
            }

            ServiceBase.Run(new ServiceBase[]
            {
                new WinTimeReSyncSvc()
            });
        }

        private static void RunInteractive(string[] args)
        {
            using (WinTimeReSyncSvc service = new WinTimeReSyncSvc())
            {
                try
                {
                    if (HasArgument(args, "--once"))
                    {
                        Console.WriteLine("Running a single elevated time synchronization.");
                        service.SynchronizeOnceInteractive();
                        return;
                    }

                    service.StartInteractive();
                    Console.WriteLine("WinTimeReSyncSvc is running interactively.");
                    Console.WriteLine("Press Enter to stop.");
                    Console.ReadLine();
                    service.StopInteractive();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Environment.ExitCode = 1;
                }
            }
        }

        private static bool HasArgument(string[] args, string expected)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string argument in args)
            {
                if (string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
