using System;
using System.Collections.Generic;
using System.Net.Sockets;
using MyExtensions.Logging;
using System.Threading;

namespace MyWebSocket
{
   public class WebSocketServer
   {
      public readonly int MaxShutdownSeconds;

      private List<WebSocketSpinner> connectionSpinners;
      private Logger logger = Logger.DefaultLogger;
      private bool shouldStop = false;
      private Thread spinner = null;
      private SpinStatus spinnerStatus = SpinStatus.Initial;

      public WebSocketServer(Logger logger = null, int maxSecondsToShutdown = 5)
      {
         connectionSpinners = new List<WebSocketSpinner>();
         MaxShutdownSeconds = maxSecondsToShutdown;

         if (logger != null)
            this.logger = logger;
         else
            this.logger.StartInstantConsole();
      }

      public void Log(string message, LogLevel level = LogLevel.Normal, string tag = "WebSocketServer")
      {
         logger.LogGeneral(message, level, tag);
      }

      /// <summary>
      /// Attempt to start the websocket server. Will return false if something went wrong.
      /// </summary>
      /// <param name="port">Port.</param>
      public bool Start(int port)
      {
         //You're already running, you dingus!
         if (Running)
         {
            Log("Server already running!", LogLevel.Warning);
            return true;
         }

         //Initialize the spin thread and start it up.
         spinner = new Thread(() => AcceptanceSpin(port));
         spinner.Start();

         //Now make sure everything is running smoothly for the spinner!
         while (spinnerStatus != SpinStatus.Spinning)
         {
            if (spinnerStatus == SpinStatus.Error)
               return false;
            
            Thread.Sleep(100);
         }

         //It must've gone smoothly because the spinner is spinning.
         return true;
      }

      /// <summary>
      /// A reliable way to determine if our websocket server is running.
      /// </summary>
      /// <value><c>true</c> if running; otherwise, <c>false</c>.</value>
      public bool Running
      {
         get { return spinner != null && spinner.IsAlive; }
      }

      /// <summary>
      /// Attempt to stop the server. If it's hanging, it'll kill the thread forcefully
      /// </summary>
      public bool Stop()
      {
         //Oh, we've already stopped
         if (!Running)
            return true;

         //Signal a stop
         shouldStop = true;

         //Now see if the spinner stopped itself
         if (!WaitOnSpinStop())
         {
            //Oh jeez, it won't stop! Let's keep trying...
            Log("Accept spinner didn't stop when asked. Aborting now...", LogLevel.Error);

            try
            {
               spinner.Abort();

               //Oh god, it just won't stop!
               if(!WaitOnSpinStop())
                  Log("Accept spinner couldn't be aborted!", LogLevel.Error);
            }
            catch (Exception e)
            {
               Log("Accept spinner failed while aborting: " + e.Message);
            }
         }

         //If we were fine, get rid of the thread. Otherwise tell the caller the bad news...
         if (!Running)
         {
            spinner = null;
            return true;
         }
         else
         {
            return false;
         }
      }

      /// <summary>
      /// Waits for the spinner to shutdown for the maximum amount of time given in the server constructor.
      /// </summary>
      /// <returns><c>true</c>, if spinner stopped, <c>false</c> otherwise.</returns>
      private bool WaitOnSpinStop()
      {
         DateTime start = DateTime.Now;

         while (Running && (DateTime.Now - start) < TimeSpan.FromSeconds(MaxShutdownSeconds))
            Thread.Sleep(100);

         return !Running;
      }

      /// <summary>
      /// The worker function which should be run on a thread. It accepts connections
      /// </summary>
      /// <param name="port">Port.</param>
      private void AcceptanceSpin(int port)
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
            
            System.Threading.Thread.Sleep(100);
         }

         server.Stop();

         Log("Accept spinner shut down", LogLevel.Debug);
         spinnerStatus = SpinStatus.Complete;
      }
   }
}

