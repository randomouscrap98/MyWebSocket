using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MyExtensions.Logging;

namespace MyWebSocket
{
   public enum CloseStatus
   {
      Normal = 1000,
      GoingAway = 1001,
      ProtocolError = 1002,
      UnsupportedDataType = 1003,
      Reserved1 = 1004,
      Reserved2 = 1005,
      Reserved3 = 1006,
      InconsistentData = 1007,
      PolicyViolation = 1008,
      MessageTooBig = 1009,
      ExpectedExtension = 1010,
      UnexpectedError = 1011,
      Reserved4 = 1015,
      NoStatus = 4000,
      BadStatus = 4001
   }

   public enum SpinStatus
   {
      Spinning,
      Complete,
      None,
      Starting,
      Error
   }

   /// <summary>
   /// A class which allows users to create a settings object. This helps 
   /// when you inherit from the WebSocketServer class.
   /// </summary>
   public class WebSocketSettings
   {
      public readonly int Port;
      public readonly string Service;
      public readonly Func<WebSocketUser> Generator;
      public readonly Logger LogProvider = Logger.DefaultLogger;
      public TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
      public TimeSpan PingInterval = TimeSpan.FromSeconds(10);
      public TimeSpan ReadWriteTimeout = TimeSpan.FromSeconds(10);
      public TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
      public TimeSpan AcceptPollInterval = TimeSpan.FromMilliseconds(100);
      public TimeSpan DataPollInterval = TimeSpan.FromMilliseconds(100);
      public int ReceiveBufferSize = 2048;
      public int SendBufferSize = 16384;
      public int MaxReceiveSize = 16384;

      public WebSocketSettings(int port, string service, Func<WebSocketUser> generator, Logger logger = null)
      {
         Port = port;
         Service = service;
         Generator = generator;

         if(logger != null)
            LogProvider = logger;
      }
   }

   public static class WebSocketHelper
   {
      public static List<string> Explode(string text, bool trim = true)
      {
         return text.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Select(
            x => trim ? x.Trim() : x).ToList();
      }

      public static void TruncateBeginning<T>(this T[] array, int start, int length = 0)
      {
         if (length <= 0)
            length = array.Count();
         
         for (int i = 0; i < length - start; i++)
            array[i] = array[start + i];
      }

      public static int ParseInt(this IEnumerable<byte> array)
      {
         int result = 0;
         int multiplier = 1;

         for (int i = Math.Min(array.Count(), sizeof(int)) - 1; i >= 0; i--)
         {
            result += multiplier * array.ElementAt(i);
            multiplier *= 256;
         }

         return result;
      }

      public static long GCD(long a, long b)
      {
         long t = 0;

         while (b != 0)
         {
            t = b;
            b = a % b;
            a = t;
         }

         return a;
      }

      public static TcpState GetState(this TcpClient tcpClient)
      {
         var foo = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpConnections()
            .SingleOrDefault(x => x.LocalEndPoint.Equals(tcpClient.Client.LocalEndPoint));
         return foo != null ? foo.State : TcpState.Unknown;
      }
   }
}

