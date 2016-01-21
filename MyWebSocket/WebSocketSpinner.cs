using System;
using MyExtensions.Logging;
using System.Threading;

namespace MyWebSocket
{
   /// <summary>
   /// A basic object which allows a task start on a separate thread. Wraps whatever
   /// implementation may be used underneath (currently threads, but could be tasks in the future)
   /// </summary>
   public class BasicSpinner
   {
      private Thread spinner = null;
      protected SpinStatus spinnerStatus = SpinStatus.Initial;
      protected bool shouldStop = false;

      public readonly string SpinnerName = "Spinner";
      public readonly int MaxShutdownSeconds;

      public BasicSpinner(string name, int maxSecondsToShutdown = 5)
      {
         SpinnerName = name;
         MaxShutdownSeconds = maxSecondsToShutdown;
      }

      /// <summary>
      /// Write a message to the screen using the log system.
      /// Logging only dumps to the default logger! Override this to dump to your own!
      /// </summary>
      /// <param name="message">Message to log</param>
      /// <param name="level">Level of message</param>
      public virtual void Log(string message, LogLevel level = LogLevel.Normal)
      {
         Logger.DefaultLogger.LogGeneral(message, level, SpinnerName);
      }

      /// <summary>
      /// The work which the spinner will do. Override this to do actual work.
      /// </summary>
      public virtual void Spin()
      {
         Log("Did some fake work (override Spin to do real work)");
      }

      /// <summary>
      /// Attempt to start the spinner. Will return false if something went wrong.
      /// </summary>
      public bool Start()
      {
         //You're already running, you dingus!
         if (Running)
         {
            Log(SpinnerName + " spinner already running!", LogLevel.Warning);
            return true;
         }

         //Initialize the spin thread and start it up.
         spinner = new Thread(Spin);
         spinner.Start();

         //Now make sure everything is running smoothly for the spinner!
         while (spinnerStatus != SpinStatus.Spinning)
         {
            if (spinnerStatus == SpinStatus.Error)
            {
               Log(SpinnerName + " failed while starting", LogLevel.Error);
               return false;
            }

            Thread.Sleep(100);
         }

         Log(SpinnerName + " started successfully", LogLevel.Debug);

         //It must've gone smoothly because the spinner is spinning.
         return true;
      }

      /// <summary>
      /// A reliable way to determine if the spinner is running.
      /// </summary>
      /// <value><c>true</c> if running; otherwise, <c>false</c>.</value>
      public bool Running
      {
         get { return spinner != null && spinner.IsAlive; }
      }

      /// <summary>
      /// Attempt to stop the spinner. If it's hanging, it'll kill the thread forcefully
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
            Log(SpinnerName + " didn't stop when asked. Aborting now...", LogLevel.Error);

            try
            {
               spinner.Abort();

               //Oh god, it just won't stop!
               if(!WaitOnSpinStop())
                  Log(SpinnerName + " couldn't be aborted!", LogLevel.Error);
            }
            catch (Exception e)
            {
               Log(SpinnerName + " failed while aborting: " + e.Message);
            }
         }

         //If we were fine, get rid of the thread. Otherwise tell the caller the bad news...
         if (!Running)
         {
            Log(SpinnerName + " stopped successfully", LogLevel.Debug);
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
   }

   /// <summary>
   /// A spinner which handles user connections. This is the main class! It's what lets people
   /// talk to each other!
   /// </summary>
   public class WebSocketSpinner : BasicSpinner
   {
      public readonly WebSocketServer Server;
      public readonly WebSocketClient Client;
      public readonly WebSocketUser User;
      public readonly long ID;

      //Shhhh, don't look at them!
      private static long NextID = 1;
      private static readonly object idLock = new object();

      public WebSocketSpinner(WebSocketServer managingServer, WebSocketClient supportingClient) : base("WebsocketSpinner")
      {
         Client = supportingClient;
         Server = managingServer;
         User = new WebSocketUser();
         ID = GenerateID();
      }

      public override void Log(string message, LogLevel level = LogLevel.Normal)
      {
         Server.LogGeneral(message, level, SpinnerName + ID);
      }

      private static long GenerateID()
      {
         lock (idLock)
         {
            return NextID++;
         }
      }
   }
}

