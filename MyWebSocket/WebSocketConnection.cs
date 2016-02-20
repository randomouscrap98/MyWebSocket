using System;
using MyExtensions.Logging;
using System.Threading;
using System.Collections.Generic;

namespace MyWebSocket
{
   public enum WebSocketState
   {
      None,
      Startup,
      Connected
   }

   /// <summary>
   /// A spinner which handles user connections. This is the main class! It's what lets people
   /// talk to each other!
   /// </summary>
   public class WebSocketConnection : IDisposable
   {
      //Shhhh, don't look at them!
      //private WebSocketState internalState;
      //private WebSocketSettings settings;
      //private DateTime lastTest = DateTime.Now;

      private WebSocketClient client;
      public readonly WebSocketUser User;
      public WebSocketState State = WebSocketState.None;
      public DateTime LastTest = DateTime.Now;

      private byte[] fragmentBuffer;
      private int fragmentBufferSize = 0;
      private Logger logger;

      public readonly long ID;
      private static long NextID = 1;
      private static readonly object idLock = new object();

      public WebSocketConnection(WebSocketClient supportingClient, WebSocketUser newUser, Logger logger) //, WebSocketSettings settings, WebSocketUser newUser)
      {
         client = supportingClient;
         this.logger = logger;
         ID = GenerateID();

         User = newUser; //Server.Settings.Generator();

         fragmentBuffer = new byte[Client.MaxReceiveSize];
      }

      public WebSocketClient Client
      {
         get { return client; }
      }

      public void Log(string message, LogLevel level = LogLevel.Normal)
      {
         logger.LogGeneral(message, level, "Connection" + ID);
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
         if (client != null)
         {
            client.Dispose();
            client = null;
         }
      }

//      public void LogStatus(DataStatus status, string caller)
//      {
//         Action<string, LogLevel> Slog = (message, level) =>
//         {
//            Log(caller + ": " + message, level);
//         };
//
//         if (status == DataStatus.ClosedSocketError)
//            Slog("Client endpoint closed socket", LogLevel.Warning);
//         else if (status == DataStatus.ClosedStreamError)
//            Slog("Stream closed by itself", LogLevel.Warning);
//         else if (status == DataStatus.DataFormatError)
//            Slog("Data was in an unrecognized format!", LogLevel.Warning);
//         else if (status == DataStatus.EndOfStream)
//            Slog("Somehow, the end of the stream data was reached", LogLevel.Warning);
//         else if (status == DataStatus.InternalError)
//            Slog("Something broke within the WebSocket library!", LogLevel.Error);
//         else if (status == DataStatus.OversizeError)
//            Slog("Data too large; not accepting", LogLevel.Warning);
//         else if (status == DataStatus.SocketExceptionError)
//            Slog("The socket encountered an exception", LogLevel.Error);
//         else if (status == DataStatus.UnknownError)
//            Slog("An unknown error occurred in the WebSocket library!", LogLevel.Error);
//         else if (status == DataStatus.UnsupportedError)
//            Slog("Tried to use an unsupported WebSocket feature!", LogLevel.Warning);
//      }

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

      /// <summary>
      /// Process given frame. Returns true if the connection can continue
      /// </summary>
      /// <returns><c>true</c> if everything was fine, <c>false</c> otherwise.</returns>
      /// <param name="readFrame">Read frame.</param>
      public bool ProcessFrame(WebSocketFrame readFrame, out byte[] outputBytes, out string message)
      {
         outputBytes = null;
         message = "";

         //If it's a message frame or PART of a message frame, we should add the payload to the 
         //fragment buffer. The fragment buffer will be complete if this is a fin frame (see next statement)
         if (readFrame.Header.Opcode == HeaderOpcode.ContinueFrame || readFrame.Header.Opcode == HeaderOpcode.TextFrame)
         {
            if (readFrame.Header.Opcode == HeaderOpcode.ContinueFrame)
               Log("Received fragmented frame.", LogLevel.SuperDebug);
            
            Array.Copy(readFrame.PayloadData, 0, fragmentBuffer, fragmentBufferSize, readFrame.Header.PayloadSize);
            fragmentBufferSize += readFrame.Header.PayloadSize;
         }

         //Only convert fragment buffer into message if this is the final frame and it's a text frame
         if (readFrame.Header.Fin && readFrame.Header.Opcode == HeaderOpcode.TextFrame)
         {
            message = System.Text.Encoding.UTF8.GetString(fragmentBuffer, 0, fragmentBufferSize);
            fragmentBufferSize = 0;

            Log("Received message: " + message, LogLevel.SuperDebug);
            //User.ReceivedMessage(message);
         }
         else if (readFrame.Header.Opcode == HeaderOpcode.PingFrame)
         {
            Log("Client ping. Sending pong", LogLevel.SuperDebug);
            outputBytes = WebSocketFrame.GetPongFrame().GetRawBytes();
            //connection.Client.QueueRaw(WebSocketFrame.GetPongFrame().GetRawBytes());
         }
         //Oh they're disconnecting? OK then, we need to finish up. Do NOT send more data.
         else if (readFrame.Header.Opcode == HeaderOpcode.CloseConnectionFrame)
         {
            Log("Client is disconnecting: " + readFrame.CloseCode, LogLevel.Debug);
            readFrame.Header.Masked = false;
            outputBytes = readFrame.GetRawBytes();
            //connection.Client.QueueRaw(readFrame.GetRawBytes());
            return false;
         }

         return true;
      }

//      /// <summary>
//      /// Perform the server reply handshake for the given client handshake
//      /// </summary>
//      /// <returns><c>true</c>, if to handshake was replyed, <c>false</c> otherwise.</returns>
//      /// <param name="readHandshake">Read handshake.</param>
//      public bool ReplyToHandshake(HTTPClientHandshake readHandshake)
//      {
//         if (readHandshake.Service != settings.Service)
//         {
//            Client.QueueHandshakeMessage(HTTPServerHandshake.GetBadRequest());
//            return false;
//         }
//
//         //Generate a responding handshake, but strip all extensions and protocols.
//         HTTPServerHandshake response = HTTPServerHandshake.GetResponseForClientHandshake(readHandshake);
//         response.AcceptedProtocols.Clear();
//         response.AcceptedExtensions.Clear();
//
//         Client.QueueHandshakeMessage(response);
//         internalState = WebSocketState.Connected;
//         lastTest = DateTime.Now;
//         Log("WebSocket handshake complete", LogLevel.Debug);
//
//         return true;
//      }
//
//      /// <summary>
//      /// Try to process a given handshake. Also performs logic on the status and error of the read operation
//      /// used to retrieve said handshake so you don't have to.
//      /// </summary>
//      /// <returns><c>true</c>, if handshake was processed, <c>false</c> otherwise.</returns>
//      /// <param name="readHandshake">Read handshake.</param>
//      /// <param name="status">Status.</param>
//      /// <param name="error">Error.</param>
//      public bool ProcessHandshake(HTTPClientHandshake readHandshake, DataStatus status, string error)
//      {
//         if (status != DataStatus.Complete)
//         {
//            LogStatus(status, "Handshake");
//
//            if (!string.IsNullOrWhiteSpace(error))
//               Log("Extra handshake error information: " + error);
//
//            //Oohhh it was the CLIENT trying to make us do something we don't like! OK then,
//            //let's tell them why they suck!
//            if (status == DataStatus.DataFormatError)
//               Client.QueueHandshakeMessage(HTTPServerHandshake.GetBadRequest());
//
//            return false;
//         }
//            
//         return ReplyToHandshake(readHandshake);
//      }
//
//      protected override void Spin()
//      {
//         string error = "";
//         DataStatus dataStatus;
//         WebSocketFrame readFrame;
//         internalState = WebSocketState.Startup;
//
//         while (!shouldStop)
//         {
//            //In the beginning, we wait for a handshake dawg.
//            if (internalState == WebSocketState.Startup)
//            {
//               //Oof, you're taking too long!
//               if ((DateTime.Now - lastTest) > settings.HandshakeTimeout)
//               {
//                  Log("Handshake timeout", LogLevel.Warning);
//                  break;
//               }
//
//               HTTPClientHandshake readHandshake;
//               dataStatus = Client.TryReadHandshake(out readHandshake, out error);
//
//               if (dataStatus != DataStatus.WaitingOnData)
//               {
//                  if (!ProcessHandshake(readHandshake, readHandshake, error))
//                     break;
//               }
//            }
//            else if (internalState == WebSocketState.Connected)
//            {
//               //Ping if we're already in a connected state
//               if ((DateTime.Now - lastTest) > settings.PingInterval)
//               {
//                  Log("Sending heartbeat", LogLevel.SuperDebug);
//                  Client.QueueRaw(WebSocketFrame.GetPongFrame().GetRawBytes());
//                  lastTest = DateTime.Now;
//               }
//
//               dataStatus = Client.TryReadFrame(out readFrame);
//
//               //Ah, we got a full frame from the client! Let's see what it is
//               if (dataStatus == DataStatus.Complete)
//               {
//                  //If it's a message frame or PART of a message frame, we should add the payload to the 
//                  //fragment buffer. The fragment buffer will be complete if this is a fin frame (see next statement)
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
//                  //If user is pinging us, pong them back
//                  else if (readFrame.Header.Opcode == HeaderOpcode.PingFrame)
//                  {
//                     Log("Client ping. Sending pong", LogLevel.SuperDebug);
//                     Client.QueueRaw(WebSocketFrame.GetPongFrame().GetRawBytes());
//                  }
//                  //Oh they're disconnecting? OK then, we need to finish up. Do NOT send more data.
//                  else if (readFrame.Header.Opcode == HeaderOpcode.CloseConnectionFrame)
//                  {
//                     Log("Client is disconnecting: " + readFrame.CloseCode, LogLevel.Debug);
//                     readFrame.Header.Masked = false;
//                     Client.QueueRaw(readFrame.GetRawBytes());
//                     break;
//                  }
//               }
//               //Oh something went wrong. That's OK I guess.
//               else if (dataStatus != DataStatus.WaitingOnData)
//               {
//                  LogStatus(dataStatus, "Read");
//                  break;
//               }
//            }
//
//            dataStatus = Client.DequeueWrite();
//
//            if (dataStatus != DataStatus.Complete && dataStatus != DataStatus.WaitingOnData)
//            {
//               LogStatus(dataStatus, "Write");
//               break;
//            }
//
//            Thread.Sleep(100);
//         }
//         
//         //Now that we're ending, try to dump out a bit of the write queue.
//         Log("Connection spinner finished. Dumping write queue", LogLevel.Debug);
//         Client.DumpWriteQueue(Server.Settings.ShutdownTimeout);
//
//         User.ClosedConnection();
//      }

      private static long GenerateID()
      {
         lock (idLock)
         {
            return NextID++;
         }
      }
   }
}

