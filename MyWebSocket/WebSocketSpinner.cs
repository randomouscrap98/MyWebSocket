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

      //public readonly WebSocketUser User;

//      private byte[] fragmentBuffer;
//      private int fragmentBufferSize = 0;
//
//      public readonly long ID;
//      private static long NextID = 1;
//      private static readonly object idLock = new object();

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

//         Client = supportingClient;
//         Server = managingServer;
//         ID = GenerateID();
//
//         User = Server.Settings.Generator();
//         User.SetSendPlaceholder((message) =>
//         {
//            if(Client != null)
//               Client.QueueMessage(message);
//         });
//         User.SetGetAllUsersPlaceholder(() =>
//         {
//            return Server.ConnectedUsers();
//         });
//         User.SetBroadcastPlaceholder((message) =>
//         {
//            Server.GeneralBroadcast(message);
//         });
//         User.SetCloseSelfPlaceholder(() =>
//         {
//            CleanClose();
//         });
//
//         fragmentBuffer = new byte[Client.MaxReceiveSize];
      }

      public override void Log(string message, LogLevel level = LogLevel.Normal)
      {
         connection.Log(message, level);
         //Server.LogGeneral(message, level, SpinnerName + ID);
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
      /// The only unmanaged resource here is the client, so get rid of them.
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
//         if (Client != null)
//         {
//            Client.Dispose();
//            Client = null;
//         }
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
         //DateTime lastTest = DateTime.Now;
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

                  //Client.QueueHandshakeMessage(HTTPServerHandshake.GetBadRequest());
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

                  //If it's a message frame or PART of a message frame, we should add the payload to the 
                  //fragment buffer. The fragment buffer will be complete if this is a fin frame (see next statement)
//                  if (readFrame.Header.Opcode == HeaderOpcode.ContinueFrame || 
//                      readFrame.Header.Opcode == HeaderOpcode.TextFrame)
//                  {
//                     if (readFrame.Header.Opcode == HeaderOpcode.ContinueFrame)
//                        Log("Received fragmented frame.", LogLevel.SuperDebug);
//                     
//                     Array.Copy(readFrame.PayloadData, 0, fragmentBuffer, fragmentBufferSize, readFrame.Header.PayloadSize);
//                     fragmentBufferSize += readFrame.Header.PayloadSize;
//                  }
//
//                  //Only convert fragment buffer into message if this is the final frame and it's a text frame
//                  if (readFrame.Header.Fin && readFrame.Header.Opcode == HeaderOpcode.TextFrame)
//                  {
//                     string message = System.Text.Encoding.UTF8.GetString(fragmentBuffer, 0, fragmentBufferSize);
//                     fragmentBufferSize = 0;
//         
//                     Log("Received message: " + message, LogLevel.SuperDebug);
//                     User.ReceivedMessage(message);
//                  }
                  //If user is pinging us, pong them back
//                  else if (readFrame.Header.Opcode == HeaderOpcode.PingFrame)
//                  {
//                     Log("Client ping. Sending pong", LogLevel.SuperDebug);
//                     connection.Client.QueueRaw(WebSocketFrame.GetPongFrame().GetRawBytes());
//                  }
//                  //Oh they're disconnecting? OK then, we need to finish up. Do NOT send more data.
//                  else if (readFrame.Header.Opcode == HeaderOpcode.CloseConnectionFrame)
//                  {
//                     Log("Client is disconnecting: " + readFrame.CloseCode, LogLevel.Debug);
//                     readFrame.Header.Masked = false;
//                     connection.Client.QueueRaw(readFrame.GetRawBytes());
//                     break;
//                  }
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

//      private static long GenerateID()
//      {
//         lock (idLock)
//         {
//            return NextID++;
//         }
//      }
   }
}

