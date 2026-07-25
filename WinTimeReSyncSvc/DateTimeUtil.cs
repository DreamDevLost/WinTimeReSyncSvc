using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace WinTimeReSyncSvc
{
    public static class DateTimeUtil
    {
        private const int NtpPacketLength = 48;
        private const int NtpPort = 123;
        private const int SocketTimeoutMilliseconds = 5000;
        private const double NtpEraSeconds = 4294967296.0;

        private static readonly DateTime NtpEpoch =
            new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly string[] NtpServers =
        {
            "time.windows.com",
            "time.cloudflare.com",
            "pool.ntp.org"
        };

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemTime(ref SYSTEMTIME systemTime);

        internal static bool SetSystemUtcTime(DateTime utcTime)
        {
            DateTime normalizedUtc = utcTime.Kind == DateTimeKind.Utc
                ? utcTime
                : utcTime.ToUniversalTime();

            SYSTEMTIME systemTime = SystemTimeFromDateTime(normalizedUtc);
            return SetSystemTime(ref systemTime);
        }

        internal static NetworkTimeResult GetNetworkTimeUtc()
        {
            List<Exception> failures = new List<Exception>();

            foreach (string server in NtpServers)
            {
                IPAddress[] addresses;

                try
                {
                    addresses = Dns.GetHostAddresses(server);
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException(
                        "Unable to resolve NTP server " + server + ".",
                        ex));
                    continue;
                }

                bool addressAttempted = false;
                AddressFamily[] preferredFamilies =
                {
                    AddressFamily.InterNetwork,
                    AddressFamily.InterNetworkV6
                };

                foreach (AddressFamily family in preferredFamilies)
                {
                    foreach (IPAddress address in addresses)
                    {
                        if (address.AddressFamily != family)
                        {
                            continue;
                        }

                        addressAttempted = true;

                        try
                        {
                            return QueryServer(server, address);
                        }
                        catch (Exception ex)
                        {
                            failures.Add(new InvalidOperationException(
                                string.Format("NTP query to {0} ({1}) failed.", server, address),
                                ex));
                        }
                    }
                }

                if (!addressAttempted)
                {
                    failures.Add(new InvalidOperationException(
                        "NTP server " + server + " did not resolve to a supported IP address."));
                }
            }

            throw new InvalidOperationException(
                "All configured NTP servers failed.",
                new AggregateException(failures));
        }

        private static NetworkTimeResult QueryServer(string server, IPAddress address)
        {
            byte[] request = new byte[NtpPacketLength];
            byte[] response = new byte[NtpPacketLength];

            // LI = 0, VN = 4, Mode = 3 (client).
            request[0] = 0x23;

            DateTime clientSendUtc = DateTime.UtcNow;
            WriteNtpTimestamp(request, 40, clientSendUtc);

            IPEndPoint endpoint = new IPEndPoint(address, NtpPort);
            int received;
            DateTime clientReceiveUtc;

            using (Socket socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.SendTimeout = SocketTimeoutMilliseconds;
                socket.ReceiveTimeout = SocketTimeoutMilliseconds;
                socket.Connect(endpoint);

                int sent = socket.Send(request);
                if (sent != NtpPacketLength)
                {
                    throw new InvalidOperationException(
                        string.Format("Only {0} of {1} NTP request bytes were sent.", sent, NtpPacketLength));
                }

                received = socket.Receive(response);
                clientReceiveUtc = DateTime.UtcNow;
            }

            if (received < NtpPacketLength)
            {
                throw new InvalidOperationException(
                    string.Format("NTP response was only {0} bytes; expected at least {1}.", received, NtpPacketLength));
            }

            ValidateResponse(request, response);

            DateTime serverReceiveUtc = ReadNtpTimestamp(response, 32, clientReceiveUtc);
            DateTime serverTransmitUtc = ReadNtpTimestamp(response, 40, clientReceiveUtc);

            TimeSpan offset = TimeSpan.FromTicks(
                ((serverReceiveUtc - clientSendUtc).Ticks +
                 (serverTransmitUtc - clientReceiveUtc).Ticks) / 2);

            TimeSpan roundTripTime =
                (clientReceiveUtc - clientSendUtc) -
                (serverTransmitUtc - serverReceiveUtc);

            if (roundTripTime < TimeSpan.Zero)
            {
                roundTripTime = TimeSpan.Zero;
            }

            return new NetworkTimeResult(
                clientReceiveUtc + offset,
                server + " (" + address + ")",
                roundTripTime);
        }

        private static void ValidateResponse(byte[] request, byte[] response)
        {
            int leapIndicator = (response[0] >> 6) & 0x03;
            int version = (response[0] >> 3) & 0x07;
            int mode = response[0] & 0x07;
            int stratum = response[1];

            if (leapIndicator == 3)
            {
                throw new InvalidOperationException("NTP server reports an unsynchronized clock.");
            }

            if (version < 3 || version > 4)
            {
                throw new InvalidOperationException("Unsupported NTP protocol version: " + version + ".");
            }

            if (mode != 4)
            {
                throw new InvalidOperationException("Unexpected NTP response mode: " + mode + ".");
            }

            if (stratum < 1 || stratum > 15)
            {
                throw new InvalidOperationException("Invalid NTP stratum: " + stratum + ".");
            }

            for (int index = 0; index < 8; index++)
            {
                if (response[24 + index] != request[40 + index])
                {
                    throw new InvalidOperationException(
                        "NTP response does not match the request originate timestamp.");
                }
            }

            if (IsZeroTimestamp(response, 32) || IsZeroTimestamp(response, 40))
            {
                throw new InvalidOperationException("NTP response contains an empty timestamp.");
            }
        }

        private static bool IsZeroTimestamp(byte[] buffer, int offset)
        {
            for (int index = 0; index < 8; index++)
            {
                if (buffer[offset + index] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static DateTime ReadNtpTimestamp(byte[] buffer, int offset, DateTime referenceUtc)
        {
            uint seconds = ReadUInt32NetworkOrder(buffer, offset);
            uint fraction = ReadUInt32NetworkOrder(buffer, offset + 4);

            double secondsWithinEra = seconds + (fraction / NtpEraSeconds);
            double referenceSeconds = (referenceUtc - NtpEpoch).TotalSeconds;
            double era = Math.Round((referenceSeconds - secondsWithinEra) / NtpEraSeconds);

            return NtpEpoch.AddSeconds(secondsWithinEra + (era * NtpEraSeconds));
        }

        private static void WriteNtpTimestamp(byte[] buffer, int offset, DateTime utcTime)
        {
            DateTime normalizedUtc = utcTime.Kind == DateTimeKind.Utc
                ? utcTime
                : utcTime.ToUniversalTime();

            double totalSeconds = (normalizedUtc - NtpEpoch).TotalSeconds;
            double secondsWithinEra = totalSeconds % NtpEraSeconds;

            if (secondsWithinEra < 0)
            {
                secondsWithinEra += NtpEraSeconds;
            }

            uint seconds = (uint)Math.Floor(secondsWithinEra);
            uint fraction = (uint)Math.Floor(
                (secondsWithinEra - Math.Floor(secondsWithinEra)) * NtpEraSeconds);

            WriteUInt32NetworkOrder(buffer, offset, seconds);
            WriteUInt32NetworkOrder(buffer, offset + 4, fraction);
        }

        private static uint ReadUInt32NetworkOrder(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                   buffer[offset + 3];
        }

        private static void WriteUInt32NetworkOrder(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static SYSTEMTIME SystemTimeFromDateTime(DateTime dateTime)
        {
            return new SYSTEMTIME
            {
                wYear = (ushort)dateTime.Year,
                wMonth = (ushort)dateTime.Month,
                wDay = (ushort)dateTime.Day,
                wHour = (ushort)dateTime.Hour,
                wMinute = (ushort)dateTime.Minute,
                wSecond = (ushort)dateTime.Second,
                wMilliseconds = (ushort)dateTime.Millisecond
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }
    }

    internal sealed class NetworkTimeResult
    {
        internal NetworkTimeResult(DateTime utcTime, string server, TimeSpan roundTripTime)
        {
            UtcTime = utcTime;
            Server = server;
            RoundTripTime = roundTripTime;
        }

        internal DateTime UtcTime { get; private set; }

        internal string Server { get; private set; }

        internal TimeSpan RoundTripTime { get; private set; }
    }
}
