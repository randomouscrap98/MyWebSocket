using System;
using System.Threading;
using MyExtensions.Logging;
using System.Threading.Tasks;

namespace MyWebSocket
{
   public delegate void SpinnerComplete(BasicSpinner completedSpinner);

   /// <summary>
   /// A basic object which allows a task start on a separate thread. Wraps whatever
   /// implementation may be used underneath (currently threads, but could be tasks in the future)
   /// </summary>
   public class BasicSpinner
   {
      private Thread spinner = null;
      protected SpinStatus spinnerStatus = SpinStatus.None;
      protected bool shouldStop = false;
      protected bool ReportsSpinStatus = false;

      public readonly string SpinnerName = "BasicSpinner";
      public readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

      public event SpinnerComplete OnComplete;

      public BasicSpinner(string name, TimeSpan? shutdownTimeout = null)
      {
         SpinnerName = name;

         if (shutdownTimeout != null)
            ShutdownTimeout = (TimeSpan)shutdownTimeout;
      }

      public void Complete()
      {
         if (OnComplete != null)
         {
            OnComplete(this);
         }
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
      protected virtual void Spin()
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
            Log(SpinnerName + " already running!", LogLevel.Warning);
            return true;
         }

         //Initialize the spin thread and start it up.
         shouldStop = false;
         spinnerStatus = SpinStatus.None;

         ThreadStart starter = Spin;
         starter += Complete;
         spinner = new Thread(starter);
         spinner.Start();

         DateTime start = DateTime.Now;

         //Now make sure everything is running smoothly for the spinner!
         while (ReportsSpinStatus && spinnerStatus != SpinStatus.Spinning && spinnerStatus != SpinStatus.Complete)
         {
            if (spinnerStatus == SpinStatus.Error)
            {
               Stop();
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
               Log(SpinnerName + " failed while aborting: " + e);
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

         while (Running && (DateTime.Now - start) < ShutdownTimeout)
            Thread.Sleep(100);

         return !Running;
      }
   }

}

