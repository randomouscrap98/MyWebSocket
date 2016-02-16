using System;
using System.Collections.Generic;
using System.Net.Sockets;
using MyExtensions.Logging;
using System.Threading;
using System.Linq;

namespace MyWebSocket
{
   /// <summary>
   /// An internal class which allows users to create a settings object. This helps 
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

   public class WebSocketServer : BasicSpinner, IDisposable
   {
      public const string Version = "R_1.1.5";

      private WebSocketSettings settings;
      private List<WebSocketSpinner> connectionSpinners;
      private readonly object spinnerLock = new object();

      public WebSocketSettings Settings
      {
         get { return settings; }
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="MyWebSocket.WebSocketServer"/> class using a preconstructed
      /// settings object.
      /// </summary>
      /// <param name="settings">Settings.</param>
      public WebSocketServer(WebSocketSettings settings) : base("WebSocketServer", settings.ShutdownTimeout)
      {
         this.settings = settings;
         connectionSpinners = new List<WebSocketSpinner>();
         ReportsSpinStatus = true;
      }

      public void Dispose()
      {
         if (connectionSpinners != null)
         {
            foreach (WebSocketSpinner spinner in new List<WebSocketSpinner>(connectionSpinners))
               ObliterateSpinner(spinner);

            connectionSpinners.Clear();
            connectionSpinners = null;
         }
      }

      public List<WebSocketUser> ConnectedUsers()
      {
         lock(spinnerLock)
         {
            return connectionSpinners.Select(x => x.User).ToList();
         }
      }

      public void GeneralBroadcast(string message)
      {
         foreach(WebSocketUser user in ConnectedUsers())
            user.Send(message);
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
         settings.LogProvider.LogGeneral(message, level, tag);
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
         TcpListener server = new TcpListener(System.Net.IPAddress.Any, settings.Port);

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

         Log("Started server on port: " + settings.Port);
         spinnerStatus = SpinStatus.Spinning;

         while (!shouldStop)
         {
            //NO! NO BLOCKING! This is basically nonblocking... kind of.
            if (server.Pending())
            {
               //Accept the client and set it up
               try
               {
                  TcpClient client = server.AcceptTcpClient();
                  client.ReceiveBufferSize = settings.ReceiveBufferSize;
                  client.SendBufferSize = settings.SendBufferSize;
                  client.SendTimeout = client.ReceiveTimeout = (int)settings.ReadWriteTimeout.TotalMilliseconds;
                  WebSocketClient webClient = new WebSocketClient(client, settings.MaxReceiveSize);

                  //Start up a spinner to handle this new connection. The spinner will take care of headers and all that,
                  //we're just here to intercept new connections.
                  WebSocketSpinner newSpinner = new WebSocketSpinner(this, webClient);
                  newSpinner.OnComplete += RemoveSpinner;

                  Log("Accepted connection from " + client.Client.RemoteEndPoint);
                  lock (spinnerLock)
                  {
                     connectionSpinners.Add(newSpinner);
                  }

                  if (!newSpinner.Start())
                  {
                     Log("Couldn't startup client spinner!", LogLevel.Error);
                     ObliterateSpinner(newSpinner);
                  }
               }
               catch(Exception e)
               {
                  Log("Encountered exception while accepting: " + e, LogLevel.Error);
               }
            }

            System.Threading.Thread.Sleep((int)settings.AcceptPollInterval.TotalMilliseconds);
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

