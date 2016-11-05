using System;
using System.Collections.Generic;
using System.Net.Sockets;
using MyExtensions.Logging;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using MyExtensions;

namespace MyWebSocket
{
   public class WebSocketConnectionAsync : WebSocketConnection
   {
      public Task Awaitable;
      public readonly List<Task<DataStatus>> PendingWrites = new List<Task<DataStatus>>();
      public readonly Object writeLock = new object();

      private DateTime CloseRequestedTimestamp = new DateTime(0);
      private Task<DataStatus> CloseTask = null;

      public WebSocketConnectionAsync(WebSocketClient client, WebSocketUser user, Logger logger) 
         : base(client, user, logger)
      {
         
      }

      /// <summary>
      /// Retrieve data statuses for completed writes AND remove them from the list.
      /// </summary>
      /// <returns>The completed writes.</returns>
      public List<DataStatus> PullCompletedWrites()
      {
         //Locks are PROBABLY unnecessary, but since I'm using a timer I just want to be sure...
         lock (writeLock)
         {
            List<DataStatus> completed = PendingWrites.Where(x => x.IsCompleted).Select(x => x.Result).ToList();
            PendingWrites.RemoveAll(x => x.IsCompleted);
            return completed;
         }
      }

      public void AddWrite(Task<DataStatus> write)
      {
         lock (writeLock)
         {
            PendingWrites.Add(write);
         }
      }

        public void AddClose(Task<DataStatus> closeTask)
        {
            lock (writeLock)
            {
                if (CloseRequested)
                    return;

                AddWrite(closeTask);
                CloseTask = closeTask;
                CloseRequestedTimestamp = DateTime.Now;
            }
        }

        public bool CloseRequested
        {
            get { return CloseTask != null; }
        }

        public bool IsCloseCompleted
        {
            get { return CloseRequested && CloseTask.IsCompleted; }
        }

        public TimeSpan TimeSinceClosed
        {
            get { return DateTime.Now - CloseRequestedTimestamp;  }
        }

   }

   public class WebSocketServerAsync : IDisposable
   {
      public const string Version = "RA_1.0.8";

      private WebSocketSettings settings;
      private List<WebSocketConnectionAsync> connections;
      private readonly object connectionLock = new object();
      private TcpListener server = null;
      private System.Timers.Timer updateTimer;
      private bool running = false;

      public WebSocketSettings Settings
      {
         get { return settings; }
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="MyWebSocket.WebSocketServer"/> class using a preconstructed
      /// settings object.
      /// </summary>
      /// <param name="settings">Settings.</param>
      public WebSocketServerAsync(WebSocketSettings settings)
      {
         this.settings = settings;
         connections = new List<WebSocketConnectionAsync>();
         updateTimer = new System.Timers.Timer();
         updateTimer.Elapsed += ServerUpdate;
         updateTimer.Interval = WebSocketHelper.GCD((long)settings.HandshakeTimeout.TotalMilliseconds,
            (long)settings.PingInterval.TotalMilliseconds);
         updateTimer.Start();

         Log("Server will check connections every " + 
            StringExtensions.LargestTime(TimeSpan.FromMilliseconds(updateTimer.Interval)), 
            LogLevel.Debug);
      }

      private void ServerUpdate(Object sender, System.Timers.ElapsedEventArgs e)
      {
         List<WebSocketConnectionAsync> removals = new List<WebSocketConnectionAsync>();

         lock (connectionLock)
         {
            // connections.Where(x => x.PullCompletedWrites().Any(y => y != DataStatus.Complete)).ToList();

            foreach (WebSocketConnectionAsync connection in connections)
            {
               if (connection.PullCompletedWrites().Any(x => x != DataStatus.Complete) ||
                   (connection.CloseRequested && (connection.IsCloseCompleted || connection.TimeSinceClosed > settings.ReadWriteTimeout)))
               {
                  removals.Add(connection);
               }
               //Oof, you're taking too long to finish the handshake!
               else if (connection.State == WebSocketState.Startup &&
                        (DateTime.Now - connection.LastTest) > settings.HandshakeTimeout)
               {
                  connection.Log("Handshake timeout", LogLevel.Warning);
                  removals.Add(connection);
               }
               else if (connection.State == WebSocketState.Connected &&
                        (DateTime.Now - connection.LastTest) > settings.PingInterval)
               {
                  connection.Log("Sending heartbeat", LogLevel.SuperDebug);
                  connection.AddWrite(connection.Client.WriteRawAsync(WebSocketFrame.GetPongFrame().GetRawBytes()));
                  connection.LastTest = DateTime.Now;
               }
            }
         }

         foreach (WebSocketConnectionAsync connection in removals.Distinct())
            RemoveConnection(connection);

         Log("Server update timer complete", LogLevel.SuperDebug);
      }

      public void Dispose()
      {
         if (updateTimer != null)
         {
            updateTimer.Stop();
            updateTimer.Dispose();
         }

         if (connections != null)
         {
            foreach (WebSocketConnectionAsync client in new List<WebSocketConnectionAsync>(connections))
               RemoveConnection(client);

            connections.Clear();
            connections = null;
         }
      }

      public WebSocketUser GenerateNewUser()
      {
         WebSocketUser User = settings.Generator();

         User.SetGetAllUsersPlaceholder(() =>
         {
            return ConnectedUsers();
         });
         User.SetBroadcastPlaceholder((message) =>
         {
            GeneralBroadcast(message);
         });

         return User;
      }

      public List<WebSocketUser> ConnectedUsers()
      {
         lock(connectionLock)
         {
            return connections.Select(x => x.User).ToList();
         }
      }

      public void GeneralBroadcast(string message)
      {
         foreach(WebSocketUser user in ConnectedUsers())
            user.Send(message);
      }

      /// <summary>
      /// Log messages to the log provider given in the settings.
      /// </summary>
      /// <param name="message">Message to log</param>
      /// <param name="level">Level of message</param>
      public void Log(string message, LogLevel level = LogLevel.Normal)
      {
         settings.LogProvider.LogGeneral(message, level, "WebSocketServer");
      }
         
      /// <summary>
      /// Remove and cleanup the given spinner
      /// </summary>
      /// <param name="spinner">Spinner.</param>
      private void RemoveConnection(WebSocketConnectionAsync connection, bool wait = true)
      {
         bool removed = false;

         lock (connectionLock)
         {
            removed = connections.Remove(connection);
         }

         if (removed)
         {
            connection.Client.CancelAsyncOperations();

            if (!wait || connection.Awaitable.Wait(settings.ShutdownTimeout))
               Log("Removed connection: " + connection.ID, LogLevel.Debug);
            else
               Log("Connection " + connection.ID + " didn't shut down properly! You have a dangling connection!", LogLevel.Error);

            connection.Dispose();
         }
      }

      private async Task ProcessConnection(WebSocketConnectionAsync connection)
      {
         //Fix up the user so it does what we want.
         connection.User.SetSendPlaceholder((message) =>
         {
            if(connection.Client != null)
               connection.AddWrite(connection.Client.WriteMessageAsync(message));
         });
         connection.User.SetCloseSelfPlaceholder(() =>
         {
             if (connection.Client != null)
             {
                 connection.AddClose(connection.Client.WriteRawAsync(WebSocketFrame.GetCloseFrame().GetRawBytes()));
                 connection.Log("This connection was forced to close.", LogLevel.Warning);
             }
         });

         string error = "";
         DataStatus dataStatus;
         HTTPClientHandshake readHandshake;
         WebSocketFrame readFrame;
         byte[] tempBytes;

         connection.State = WebSocketState.Startup;

         while (true)
         {
            //In the beginning, we wait for a handshake dawg.
            if (connection.State == WebSocketState.Startup)
            {
               Tuple<DataStatus, HTTPClientHandshake, string> handshakeResult = await connection.Client.ReadHandshakeAsync();
               dataStatus = handshakeResult.Item1;
               readHandshake = handshakeResult.Item2;
               error = handshakeResult.Item3;

               //Wow, we got a real thing! Let's see if the header is what we need!
               if (dataStatus == DataStatus.Complete)
               {
                  if (readHandshake.Service != settings.Service)
                  {
                     connection.AddWrite(connection.Client.WriteHandshakeAsync(HTTPServerHandshake.GetBadRequest()));
                     break;
                  }

                  //Generate a responding handshake, but strip all extensions and protocols.
                  HTTPServerHandshake response = HTTPServerHandshake.GetResponseForClientHandshake(readHandshake);
                  response.AcceptedProtocols.Clear();
                  response.AcceptedExtensions.Clear();

                  connection.AddWrite(connection.Client.WriteHandshakeAsync(response));
                  connection.State = WebSocketState.Connected;
                  connection.LastTest = DateTime.Now;
                  connection.Log("WebSocket handshake complete", LogLevel.Debug);
               }
               //Hmm, if it's not complete and we're not waiting, it's an error. Close the connection?
               else
               {
                  connection.LogStatus(dataStatus, "Handshake");

                  if (!string.IsNullOrWhiteSpace(error))
                     connection.Log("Extra handshake error information: " + error);

                  //Oohhh it was the CLIENT trying to make us do something we don't like! OK then,
                  //let's tell them why they suck!
                  if (dataStatus == DataStatus.DataFormatError)
                     connection.AddWrite(connection.Client.WriteHandshakeAsync(HTTPServerHandshake.GetBadRequest()));

                  break;
               }
            }
            else if (connection.State == WebSocketState.Connected)
            {
               Tuple<DataStatus, WebSocketFrame> frameResult = await connection.Client.ReadFrameAsync();
               dataStatus = frameResult.Item1;
               readFrame = frameResult.Item2;

               //Ah, we got a full frame from the client! Let's see what it is
               if (dataStatus == DataStatus.Complete)
               {
                  string frameMessage = "";
                  bool continueConnection = connection.ProcessFrame(readFrame, out tempBytes, out frameMessage);

                  if (tempBytes != null)
                     connection.AddWrite(connection.Client.WriteRawAsync(tempBytes));
                  if (!continueConnection)
                     break;
                  if(!string.IsNullOrEmpty(frameMessage))
                     await Task.Run(() => connection.User.ReceivedMessage(frameMessage));
               }
               //Oh something went wrong. That's OK I guess.
               else
               {
                  connection.LogStatus(dataStatus, "Read");
                  break;
               }
            }
         }

         //Now that we're ending, try to dump out a bit of the write queue.
         connection.Log("Connection finished.", LogLevel.Debug);
         connection.User.ClosedConnection();
         RemoveConnection(connection, false);
      }

      public async Task StartAsync()
      {
         //OY! Don't call us again!
         if (server != null)
            return;

         server = new TcpListener(System.Net.IPAddress.Any, settings.Port);

         try
         {
            server.Start();
         }
         catch(Exception e)
         {
            Log("Couldn't start base server: " + e.Message, LogLevel.FatalError);
            return;
         }

         running = true;
         Log("Started server on port: " + settings.Port);

         while (running)
         {
            //Accept the client and set it up
            try
            {
               TcpClient client = await server.AcceptTcpClientAsync();
               client.ReceiveBufferSize = settings.ReceiveBufferSize;
               client.SendBufferSize = settings.SendBufferSize;
               client.SendTimeout = client.ReceiveTimeout = (int)settings.ReadWriteTimeout.TotalMilliseconds;
               WebSocketClient webClient = new WebSocketClient(client, settings.MaxReceiveSize);

               Log("Accepted connection from " + client.Client.RemoteEndPoint);

               //Start up a spinner to handle this new connection. The spinner will take care of headers and all that,
               //we're just here to intercept new connections.
               WebSocketConnectionAsync connection = 
                  new WebSocketConnectionAsync(webClient, GenerateNewUser(), settings.LogProvider);
               connection.Awaitable = ProcessConnection(connection);

               lock (connectionLock)
               {
                  connections.Add(connection);
               }
            }
            catch(Exception e)
            {
               if (e is ObjectDisposedException)
                  Log("Connection accepter has gone away it seems...", LogLevel.Debug);
               else
                  Log("Encountered exception while accepting: " + e, LogLevel.Error);
            }
         }

         running = false;
      }

      public void Stop()
      {
         Log("Attempting to stop server", LogLevel.Debug);

         running = false;
         server.Stop();

         foreach (WebSocketConnectionAsync connection in new List<WebSocketConnectionAsync> (connections))
            RemoveConnection(connection);

         Log("Server shut down");
      }
   }
}

