using System;
using System.Collections.Generic;
using System.Net.Sockets;
using MyExtensions.Logging;
using System.Threading;

namespace MyWebSocket
{
   public class WebSocketServer : BasicSpinner
   {
      //public readonly int MaxShutdownSeconds;

      private int port;
      private List<WebSocketSpinner> connectionSpinners;
      private Logger logger = Logger.DefaultLogger;
      //private bool shouldStop = false;
      //private Thread spinner = null;
      //private SpinStatus spinnerStatus = SpinStatus.Initial;

      public WebSocketServer(int port, Logger logger = null, int maxSecondsToShutdown = 5) : base("WebSocket Server", maxSecondsToShutdown)
      {
         this.port = port;
         connectionSpinners = new List<WebSocketSpinner>();
         //MaxShutdownSeconds = maxSecondsToShutdown;

         if (logger != null)
            this.logger = logger;
      }

      /// <summary>
      /// Our logs need to be written to the user provided log, so we override the default log function
      /// from BasicSpinner
      /// </summary>
      /// <param name="message">Message to log</param>
      /// <param name="level">Level of message</param>
      public override void Log(string message, LogLevel level = LogLevel.Normal)
      {
         LogGeneral(message, level);
      }

      /// <summary>
      /// Generic logging for anybody!
      /// </summary>
      /// <param name="message">Message.</param>
      /// <param name="level">Level.</param>
      /// <param name="tag">Tag.</param>
      public void LogGeneral(string message, LogLevel level = LogLevel.Normal, string tag = "WebSocketServer")
      {
         logger.LogGeneral(message, level, tag);
      }

      /// <summary>
      /// The worker function which should be run on a thread. It accepts connections
      /// </summary>
      public override void Spin()
      {
         spinnerStatus = SpinStatus.Initial;
         shouldStop = false;
         TcpListener server = new TcpListener(System.Net.IPAddress.Any, port);

         try
         {
            server.Start();
         }
         catch(Exception e)
         {
            Log("Couldn't start accept spinner: " + e.Message, LogLevel.FatalError);
            spinnerStatus = SpinStatus.Error;
            return;
         }

         Log("Started server on port: " + port);
         spinnerStatus = SpinStatus.Spinning;

         while (!shouldStop)
         {
            //NO! NO BLOCKING! This is basically nonblocking... kind of.
            if (server.Pending())
            {
               //Accept the client and set it up
               TcpClient client = server.AcceptTcpClient();
               client.ReceiveBufferSize = 2048;
               client.SendBufferSize = 16384;
               client.SendTimeout = client.ReceiveTimeout = 20000;
               WebSocketClient webClient = new WebSocketClient(client);

               //Start up a spinner to handle this new connection. The spinner will take care of headers and all that,
               //we're just here to intercept new connections.
               WebSocketSpinner newSpinner = new WebSocketSpinner(this, webClient);
               connectionSpinners.Add(newSpinner);
               newSpinner.Start();

            }

            System.Threading.Thread.Sleep(100);
         }

         server.Stop();

         Log("Server shut down");
         spinnerStatus = SpinStatus.Complete;
      }
   }
}

