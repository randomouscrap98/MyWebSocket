using System;
using System.Collections.Generic;
using System.Net.Sockets;
using MyExtensions.Logging;
using System.Threading;
using System.Linq;

namespace MyWebSocket
{
   public class WebSocketServer : BasicSpinner, IDisposable
   {
      private int port;
      private List<WebSocketSpinner> connectionSpinners;
      private readonly object spinnerLock = new object();
      private Logger logger = Logger.DefaultLogger;
      private System.Timers.Timer cleanupTimer = new System.Timers.Timer();

      public readonly string Service;
      public readonly Func<WebSocketUser> GenerateWebSocketUser;

      public WebSocketServer(int port, string service, Func<WebSocketUser> howToCreate,
         Logger logger = null, int maxSecondsToShutdown = 5) : base("WebSocket Server", maxSecondsToShutdown)
      {
         this.port = port;
         connectionSpinners = new List<WebSocketSpinner>();
         ReportsSpinStatus = true;
         Service = service;
         GenerateWebSocketUser = howToCreate;

         //MaxShutdownSeconds = maxSecondsToShutdown;
         cleanupTimer.Interval = TimeSpan.FromSeconds(5).TotalMilliseconds;

         if (logger != null)
            this.logger = logger;
      }

      public void Dispose()
      {
         cleanupTimer.Dispose();
      }

      public List<WebSocketUser> ConnectedUsers()
      {
         lock(spinnerLock)
         {
            return connectionSpinners.Select(x => x.User).ToList();
         }
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
      /// Completely obliterate a spinner. It will just no longer exist. Use this when
      /// you need absolute destruction.
      /// </summary>
      /// <param name="spinner">Spinner.</param>
      private void ObliterateSpinner(WebSocketSpinner spinner)
      {
         Log("Forcefully obliterating spinner: " + spinner.ID, LogLevel.Debug);
         spinner.Stop();
         RemoveSpinner(spinner);
      }

      /// <summary>
      /// Remove and cleanup the given spinner
      /// </summary>
      /// <param name="spinner">Spinner.</param>
      private void RemoveSpinner(BasicSpinner baseSpinner)
      {
         WebSocketSpinner spinner = (WebSocketSpinner)baseSpinner;

         lock (spinnerLock)
         {
            if(connectionSpinners.Remove(spinner))
               Log("Removed spinner: " + spinner.ID, LogLevel.Debug);
         }

         spinner.OnComplete -= RemoveSpinner;
         spinner.Dispose();
      }

      /// <summary>
      /// The worker function which should be run on a thread. It accepts connections
      /// </summary>
      protected override void Spin()
      {
         spinnerStatus = SpinStatus.Starting;
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
               Log("Accepting pending connection", LogLevel.Debug);

               //Accept the client and set it up
               TcpClient client = server.AcceptTcpClient();
               client.ReceiveBufferSize = 2048;
               client.SendBufferSize = 16384;
               client.SendTimeout = client.ReceiveTimeout = 10000;
               WebSocketClient webClient = new WebSocketClient(client);

               //Start up a spinner to handle this new connection. The spinner will take care of headers and all that,
               //we're just here to intercept new connections.
               WebSocketSpinner newSpinner = new WebSocketSpinner(this, webClient);
               newSpinner.OnComplete += RemoveSpinner;

               if (!newSpinner.Start())
               {
                  Log("Couldn't startup client spinner!", LogLevel.Error);
                  ObliterateSpinner(newSpinner);
               }
               else
               {
                  lock (spinnerLock)
                  {
                     connectionSpinners.Add(newSpinner);
                  }

                  Log("Accepted connection from " + client.Client.RemoteEndPoint);
               }

            }

            System.Threading.Thread.Sleep(100);
         }

         Log("Attempting to stop server", LogLevel.Debug);

         server.Stop();

         foreach (WebSocketSpinner spinner in new List<WebSocketSpinner>(connectionSpinners))
            ObliterateSpinner(spinner);

         Log("Server shut down");
         spinnerStatus = SpinStatus.Complete;
      }
   }
}

