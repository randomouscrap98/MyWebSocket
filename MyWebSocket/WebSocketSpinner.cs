using System;
using MyExtensions.Logging;
using System.Threading;
using System.Collections.Generic;

namespace MyWebSocket
{
   public enum WebSocketState
   {
      Startup,
      Connected
   }

   /// <summary>
   /// A spinner which handles user connections. This is the main class! It's what lets people
   /// talk to each other!
   /// </summary>
   public class WebSocketSpinner : BasicSpinner, IDisposable
   {
      //Shhhh, don't look at them!
      private WebSocketServer Server;
      private WebSocketClient Client;
      private WebSocketState internalState;

      public readonly WebSocketUser User;

      private byte[] fragmentBuffer;
      private int fragmentBufferSize = 0;

      public readonly long ID;
      private static long NextID = 1;
      private static readonly object idLock = new object();

      public WebSocketSpinner(WebSocketServer managingServer, WebSocketClient supportingClient) : base("WebsocketSpinner")
      {
         Client = supportingClient;
         Server = managingServer;
         ID = GenerateID();

         User = Server.GenerateWebSocketUser();
         User.SetSendPlaceholder((message) =>
         {
            Client.QueueMessage(message);
         });
         User.SetGetAllUsersPlaceholder(() =>
         {
            return Server.ConnectedUsers();
         });
         User.SetBroadcastPlaceholder((message) =>
         {
            foreach(WebSocketUser user in Server.ConnectedUsers())
               user.Send(message);
         });

         fragmentBuffer = new byte[Client.MaxReceiveSize];
      }

      public override void Log(string message, LogLevel level = LogLevel.Normal)
      {
         Server.LogGeneral(message, level, SpinnerName + ID);
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
         if (Client != null)
         {
            Client.Dispose();
            Client = null;
         }
      }

      public void LogStatus(DataStatus status, string caller)
      {
         Action<string, LogLevel> Slog = (message, level) =>
         {
            Log(caller + ": " + message, level);
         };

         if (status == DataStatus.ClosedSocketError)
            Slog("Client endpoint closed socket", LogLevel.Warning);
         else if (status == DataStatus.ClosedStreamError)
            Slog("Stream closed by itself", LogLevel.Warning);
         else if (status == DataStatus.DataFormatError)
            Slog("Data was in an unrecognized format!", LogLevel.Warning);
         else if (status == DataStatus.EndOfStream)
            Slog("Somehow, the end of the stream data was reached", LogLevel.Warning);
         else if (status == DataStatus.InternalError)
            Slog("Something broke within the WebSocket library!", LogLevel.Error);
         else if (status == DataStatus.OversizeError)
            Slog("Data too large; not accepting", LogLevel.Warning);
         else if (status == DataStatus.SocketExceptionError)
            Slog("The socket encountered an exception", LogLevel.Error);
         else if (status == DataStatus.UnknownError)
            Slog("An unknown error occurred in the WebSocket library!", LogLevel.Error);
         else if (status == DataStatus.UnsupportedError)
            Slog("Tried to use an unsupported WebSocket feature!", LogLevel.Warning);
      }

      protected override void Spin()
      {
         string error = "";
         DataStatus dataStatus;
         WebSocketFrame readFrame;
         internalState = WebSocketState.Startup;
         DateTime lastTest = DateTime.Now;

         while (!shouldStop)
         {
            if ((DateTime.Now - lastTest) > Server.PingInterval)
            {
               Client.QueueRaw(WebSocketFrame.GetPongFrame().GetRawBytes());
               lastTest = DateTime.Now;
            }

            //In the beginning, we wait for a handshake dawg.
            if (internalState == WebSocketState.Startup)
            {
               HTTPClientHandshake readHandshake;
               dataStatus = Client.TryReadHandshake(out readHandshake, out error);

               //Wow, we got a real thing! Let's see if the header is what we need!
               if (dataStatus == DataStatus.Complete)
               {
                  if (readHandshake.Service != Server.Service)
                  {
                     Client.QueueHandshakeMessage(HTTPServerHandshake.GetBadRequest());
                     break;
                  }

                  //Generate a responding handshake, but strip all extensions and protocols.
                  HTTPServerHandshake response = HTTPServerHandshake.GetResponseForClientHandshake(readHandshake);
                  response.AcceptedProtocols.Clear();
                  response.AcceptedExtensions.Clear();

                  Client.QueueHandshakeMessage(response);
                  internalState = WebSocketState.Connected;
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
                     Client.QueueHandshakeMessage(HTTPServerHandshake.GetBadRequest());

                  break;
               }
            }
            else if (internalState == WebSocketState.Connected)
            {
               dataStatus = Client.TryReadFrame(out readFrame);

               //Ah, we got a full frame from the client! Let's see what it is
               if (dataStatus == DataStatus.Complete)
               {
                  //If it's a message frame or PART of a message frame, we should add the payload to the 
                  //fragment buffer. The fragment buffer will be complete if this is a fin frame (see next statement)
                  if (readFrame.Header.Opcode == HeaderOpcode.ContinueFrame || 
                      readFrame.Header.Opcode == HeaderOpcode.TextFrame)
                  {
                     if (readFrame.Header.Opcode == HeaderOpcode.ContinueFrame)
                        Log("Received fragmented frame.", LogLevel.SuperDebug);
                     
                     Array.Copy(readFrame.PayloadData, 0, fragmentBuffer, fragmentBufferSize, readFrame.Header.PayloadSize);
                     fragmentBufferSize += readFrame.Header.PayloadSize;
                  }

                  //Only convert fragment buffer into message if this is the final frame and it's a text frame
                  if (readFrame.Header.Fin && readFrame.Header.Opcode == HeaderOpcode.TextFrame)
                  {
                     string message = System.Text.Encoding.UTF8.GetString(fragmentBuffer, 0, fragmentBufferSize);
                     fragmentBufferSize = 0;
         
                     Log("Received message: " + message, LogLevel.SuperDebug);
                     User.ReceivedMessage(message);
                  }
                  //If user is pinging us, pong them back
                  else if (readFrame.Header.Opcode == HeaderOpcode.PingFrame)
                  {
                     Log("Client ping. Sending pong", LogLevel.SuperDebug);
                     Client.QueueRaw(WebSocketFrame.GetPongFrame().GetRawBytes());
                  }
                  //Oh they're disconnecting? OK then, we need to finish up. Do NOT send more data.
                  else if (readFrame.Header.Opcode == HeaderOpcode.CloseConnectionFrame)
                  {
                     Log("Client is disconnecting: " + readFrame.CloseCode, LogLevel.Debug);
                     Client.QueueRaw(readFrame.GetRawBytes());
                     break;
                  }
               }
               //Oh something went wrong. That's OK I guess.
               else if (dataStatus != DataStatus.WaitingOnData)
               {
                  LogStatus(dataStatus, "Read");
                  break;
               }
            }

            dataStatus = Client.DequeueWrite();

            if (dataStatus != DataStatus.Complete && dataStatus != DataStatus.WaitingOnData)
            {
               LogStatus(dataStatus, "Write");
               break;
            }

            Thread.Sleep(100);
         }

         //Now that we're ending, try to dump out a bit of the write queue.
         Log("Connection spinner finished. Dumping write queue", LogLevel.Debug);
         Client.DumpWriteQueue(TimeSpan.FromSeconds(MaxShutdownSeconds));
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

