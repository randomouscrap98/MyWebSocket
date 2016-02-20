using System;
using MyExtensions.Logging;
using System.Threading;
using System.Collections.Generic;

namespace MyWebSocket
{
   /// <summary>
   /// A spinner which handles user connections. This is the main class! It's what lets people
   /// talk to each other!
   /// </summary>
   public class WebSocketSpinner : BasicSpinner, IDisposable
   {
      //Shhhh, don't look at them!
      //private WebSocketServer Server;
      //private WebSocketClient Client;
      //private WebSocketState internalState;
      private WebSocketConnection connection = null;
      private WebSocketSettings settings = null;

      public WebSocketSpinner(WebSocketClient supportingClient, WebSocketUser newUser, WebSocketSettings settings) 
         : base("WebsocketSpinner", settings.ShutdownTimeout)
      {
         connection = new WebSocketConnection(supportingClient, newUser, settings.LogProvider);
         this.settings = settings;

         //Fix up the user so it does what we want.
         User.SetSendPlaceholder((message) =>
         {
            if(connection.Client != null)
               connection.Client.QueueMessage(message);
         });
         User.SetCloseSelfPlaceholder(() =>
         {
            connection.Client.QueueRaw(WebSocketFrame.GetCloseFrame().GetRawBytes());
         });
      }

      public override void Log(string message, LogLevel level = LogLevel.Normal)
      {
         connection.Log(message, level);
      }

      public WebSocketUser User
      {
         get { return connection.User; }
      }

      public long ID
      {
         get { return connection.ID; }
      }

      /// <summary>
      /// The only unmanaged resource here is the connection, so get rid of them.
      /// </summary>
      /// <remarks>Call <see cref="Dispose"/> when you are finished using the <see cref="MyWebSocket.WebSocketSpinner"/>. The
      /// <see cref="Dispose"/> method leaves the <see cref="MyWebSocket.WebSocketSpinner"/> in an unusable state. After
      /// calling <see cref="Dispose"/>, you must release all references to the
      /// <see cref="MyWebSocket.WebSocketSpinner"/> so the garbage collector can reclaim the memory that the
      /// <see cref="MyWebSocket.WebSocketSpinner"/> was occupying.</remarks>
      public void Dispose()
      {
         if (connection != null)
         {
            connection.Dispose();
            connection = null;
         }
      }

      public void LogStatus(DataStatus status, string caller)
      {
         connection.LogStatus(status, caller);
      }

      public void CleanClose()
      {
         connection.Client.QueueRaw(WebSocketFrame.GetCloseFrame().GetRawBytes());
      }

      protected override void Spin()
      {
         string error = "";
         string message = "";
         DataStatus dataStatus;
         WebSocketFrame readFrame;
         connection.State = WebSocketState.Startup;
         byte[] tempBytes;

         while (!shouldStop)
         {
            //In the beginning, we wait for a handshake dawg.
            if (connection.State == WebSocketState.Startup)
            {
               //Oof, you're taking too long!
               if ((DateTime.Now - connection.LastTest) > settings.HandshakeTimeout)
               {
                  Log("Handshake timeout", LogLevel.Warning);
                  break;
               }

               HTTPClientHandshake readHandshake;
               dataStatus = connection.Client.TryReadHandshake(out readHandshake, out error);

               //Wow, we got a real thing! Let's see if the header is what we need!
               if (dataStatus == DataStatus.Complete)
               {
                  if (readHandshake.Service != settings.Service)
                  {
                     connection.Client.QueueHandshakeMessage(HTTPServerHandshake.GetBadRequest());
                     break;
                  }

                  //Generate a responding handshake, but strip all extensions and protocols.
                  HTTPServerHandshake response = HTTPServerHandshake.GetResponseForClientHandshake(readHandshake);
                  response.AcceptedProtocols.Clear();
                  response.AcceptedExtensions.Clear();

                  connection.Client.QueueHandshakeMessage(response);
                  connection.State = WebSocketState.Connected;
                  connection.LastTest = DateTime.Now;
                  Log("WebSocket handshake complete", LogLevel.Debug);
               }
               //Hmm, if it's not complete and we're not waiting, it's an error. Close the connection?
               else if (dataStatus != DataStatus.WaitingOnData)
               {
                  LogStatus(dataStatus, "Handshake");

                  if (!string.IsNullOrWhiteSpace(error))
                     Log("Extra handshake error information: " + error);

                  //Oohhh it was the CLIENT trying to make us do something we don't like! OK then,
                  //let's tell them why they suck!
                  if (dataStatus == DataStatus.DataFormatError)
                     connection.Client.QueueHandshakeMessage(HTTPServerHandshake.GetBadRequest());

                  break;
               }
            }
            else if (connection.State == WebSocketState.Connected)
            {
               //Ping if we're already in a connected state
               if ((DateTime.Now - connection.LastTest) > settings.PingInterval)
               {
                  Log("Sending heartbeat", LogLevel.SuperDebug);
                  connection.Client.QueueRaw(WebSocketFrame.GetPongFrame().GetRawBytes());
                  connection.LastTest = DateTime.Now;
               }

               dataStatus = connection.Client.TryReadFrame(out readFrame);

               //Ah, we got a full frame from the client! Let's see what it is
               if (dataStatus == DataStatus.Complete)
               {
                  bool continueConnection = connection.ProcessFrame(readFrame, out tempBytes, out message);

                  if(tempBytes != null)
                     connection.Client.QueueRaw(tempBytes);
                  if (!continueConnection)
                     break;
                  if(!string.IsNullOrEmpty(message))
                     User.ReceivedMessage(message);
               }
               //Oh something went wrong. That's OK I guess.
               else if (dataStatus != DataStatus.WaitingOnData)
               {
                  LogStatus(dataStatus, "Read");
                  break;
               }
            }

            dataStatus = connection.Client.DequeueWrite();

            if (dataStatus != DataStatus.Complete && dataStatus != DataStatus.WaitingOnData)
            {
               LogStatus(dataStatus, "Write");
               break;
            }

            Thread.Sleep(100);
         }
         
         //Now that we're ending, try to dump out a bit of the write queue.
         Log("Connection spinner finished. Dumping write queue", LogLevel.Debug);
         connection.Client.DumpWriteQueue(settings.ShutdownTimeout);

         connection.User.ClosedConnection();
      }
   }
}

