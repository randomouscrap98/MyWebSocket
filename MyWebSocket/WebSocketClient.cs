using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;

namespace MyWebSocket
{
   /// <summary>
   /// The status of a "partially" blocking IO operation
   /// </summary>
   public enum DataStatus
   {
      Complete,
      WaitingOnData,
      EndOfStream,
      ClosedStreamError,
      ClosedSocketError,
      SocketExceptionError,
      DataFormatError,
      InternalError,
      UnsupportedError,
      OversizeError,
      UnknownError
   }

   /// <summary>
   /// Represents a client connected over websockets. Main class of websocket library: controls
   /// messages sent and received on the websocket, including the HTTP handshake.
   /// </summary>
   public class WebSocketClient
   {
      public readonly TcpClient Client;
      public readonly int MaxReceiveSize;

      public readonly Object outputLock = new object();
      private Queue<Tuple<string, System.Text.Encoding>> outputBuffer = new Queue<Tuple<string, System.Text.Encoding>>();

      private NetworkStream stream;
      private byte[] messageBuffer;
      //private byte[] fragmentBuffer;
      private int messageBufferSize = 0;
      //private int fragmentBufferSize = 0;
      private HTTPClientHandshake parsedHandshake = null;

      public bool HandShakeComplete
      {
         get { return parsedHandshake != null; }
      }

      public WebSocketClient(TcpClient newClient, int maxReceiveSize = 16384)
      {
         Client = newClient;
         stream = Client.GetStream();

         MaxReceiveSize = maxReceiveSize;
         messageBuffer = new byte[MaxReceiveSize + 1];
         //fragmentBuffer = new byte[MaxReceiveSize];
      }

      public void Close()
      {
         stream.Close();
         Client.Close();
      }

      /// <summary>
      /// Attempts to pull all the data and properly parse it for the HTTP handshake portion of
      /// a WebSocket connection. Performs minimal blocking.
      /// </summary>
      /// <returns>The read handshake.</returns>
      public DataStatus TryReadHandshake(out HTTPClientHandshake result, out string error)
      {
         result = new HTTPClientHandshake();
         error = "";

         //You've already done the handshake, you idiot.
         if (HandShakeComplete)
         {
            result = parsedHandshake;
            return DataStatus.Complete;
         }

         //Pull a chunk of data (as much as we can) from the stream and store it in our internal buffer.
         DataStatus readStatus = GenericRead();

         //If there was an error (anything other than "completion"), return the error.
         if (readStatus != DataStatus.Complete)
            return readStatus;
         
         //Now let's see if we read the whole header by searching for the header ending symbol.
         string handshake = System.Text.Encoding.ASCII.GetString(messageBuffer, 0, messageBufferSize);
         int handshakeEnd = handshake.IndexOf("\r\n\r\n");

         //We read the whole header, now it's time to parse it.
         if(handshakeEnd >= 0)
         {
            if(HTTPClientHandshake.TryParse(handshake, out result, out error))
            {
               //Push the data in the buffer back. We may have read a bit of the new data.
               messageBuffer.TruncateBeginning(handshakeEnd + 4);
               messageBufferSize -= (handshakeEnd + 4);

               parsedHandshake = result;
               return DataStatus.Complete;
            }
            else
            {
               return DataStatus.DataFormatError;
            }
         }
         else
         {
            //If we didn't read the whole header, we're still basically waiting on data.
            return DataStatus.WaitingOnData;
         }
      }

      /// <summary>
      /// Attempts to read an entire frame. Returns the frame if one was successfully read.
      /// </summary>
      /// <returns>The status of the read operation</returns>
      /// <param name="frame">The parsed frame (on success)</param>
      public DataStatus TryReadFrame(out WebSocketFrame frame)
      {
         frame = new WebSocketFrame();

         //Pull a chunk of data (as much as we can) from the stream and store it in our internal buffer.
         DataStatus readStatus = GenericRead();

         //If there was an error (anything other than "completion"), return the error.
         if (readStatus != DataStatus.Complete)
            return readStatus;
         
         //We need at least 2 bytes to complete the header.
         if (messageBufferSize < 2)
            return DataStatus.WaitingOnData;
         
         //If the complete header hasn't been read yet, we're still waiting for it.
         if (messageBufferSize < WebSocketHeader.FullHeaderSize(messageBuffer))
            return DataStatus.WaitingOnData;

         WebSocketHeader header = new WebSocketHeader();

         //If we can't parse the header at this point, we have some serious issues.
         if (!WebSocketHeader.TryParse(messageBuffer, out header))
            return DataStatus.InternalError;
         
         //Too much data
         if (header.FrameSize > MaxReceiveSize)
            return DataStatus.OversizeError;
         
         //We have the whole header, but do we have the whole message? if not, we're still waiting on data.
         if (messageBufferSize < header.FrameSize)
            return DataStatus.WaitingOnData;

         //Oh, we have the whole message. Uhh ok then, let's make sure the header fields are correct
         //before continuing. RSV needs to be 0 (may change later) and all client messages must be masked.
         if (!header.Masked || header.RSV != 0)
            return DataStatus.DataFormatError;

         //Oh is this... a binary frame? Dawg... don't gimme that crap.
         if (header.Opcode == HeaderOpcode.BinaryFrame)
            return DataStatus.UnsupportedError;

         //Initialize a frame with our newly parsed data
         frame = new WebSocketFrame(header, messageBuffer.Take(header.FrameSize).ToArray());

         //Remove the message data from the buffer
         messageBuffer.TruncateBeginning(header.FrameSize);
         messageBufferSize -= header.FrameSize;

         return DataStatus.Complete;

         //Dump data into fragment buffer if this is a message frame.
//         if (header.Opcode == HeaderOpcode.ContinueFrame || header.Opcode == HeaderOpcode.TextFrame)
//         {
//            Array.Copy(frame.PayloadData, 0, fragmentBuffer, fragmentBufferSize, header.PayloadSize);
//            fragmentBufferSize += header.PayloadSize;
//         }

         //Only convert fragment buffer into message if this is the final frame
//         if (header.Fin)
//         {
//            message = System.Text.Encoding.UTF8.GetString(fragmentBuffer, 0, fragmentBufferSize);
//            fragmentBufferSize = 0;
//
//            messageBuffer.TruncateBeginning(header.FrameSize);
//            messageBufferSize -= header.FrameSize;
//
//            return DataStatus.Complete;
//         }
      }

      /// <summary>
      /// Only reads as much data as possible into the internal read buffer.
      /// </summary>
      /// <returns>A status representing what happened during the read.</returns>
      private DataStatus GenericRead()
      {
         try
         {
            //DataAvailable only tells us if there is any data to be read on the stream,
            //not if the stream is closed. If it's closed, we 
            if (!stream.DataAvailable)
               return DataStatus.WaitingOnData;
         }
         catch(Exception e)
         {
            //We need to report the error based on what kind of exception we got.
            if (e is ObjectDisposedException)
               return DataStatus.ClosedStreamError;
            else if (e is IOException)
               return DataStatus.ClosedSocketError;
            else if (e is SocketException)
               return DataStatus.SocketExceptionError;
            else
               return DataStatus.UnknownError;
         }

         try
         {
            int bytesRead = stream.Read(messageBuffer, messageBufferSize, messageBuffer.Length - messageBufferSize);

            if(bytesRead <= 0)
               return DataStatus.EndOfStream;

            //Acknowledge the read bytes in the buffer.
            messageBufferSize += bytesRead;

            return DataStatus.Complete;
         }
         catch(Exception e)
         {
            if (e is ArgumentException || e is ArgumentNullException || e is ArgumentOutOfRangeException)
               return DataStatus.InternalError;
            else if (e is IOException)
               return DataStatus.SocketExceptionError;
            else if (e is ObjectDisposedException)
               return DataStatus.ClosedStreamError;
            else
               return DataStatus.UnknownError;
         }
      }

      /// <summary>
      /// Handshakes are a bit special, so use this to enqueue them.
      /// </summary>
      /// <param name="handshake">Handshake.</param>
      public void QueueHandshakeWrite(HTTPServerHandshake handshake)
      {
         QueueWrite(handshake.ToString(), System.Text.Encoding.ASCII);
      }

      /// <summary>
      /// Adds a message to the output queue. Does NOT immediately write the message!
      /// </summary>
      /// <param name="message">Message.</param>
      /// <param name="encoding">Encoding.</param>
      public void QueueWrite(string message, System.Text.Encoding encoding = null)
      {
         if (encoding == null)
            encoding = System.Text.Encoding.UTF8;
         
         lock (outputLock)
         {
            outputBuffer.Enqueue(Tuple.Create(message, encoding));
         }
      }

      /// <summary>
      /// This pops one write off the queue and actually writes it to the network.
      /// </summary>
      public DataStatus DequeueWrite()
      {
         Tuple<string, System.Text.Encoding> output = null;

         lock (outputLock)
         {
            output = outputBuffer.Dequeue();
         }

         //In the future, this section may be extracted to a threadpool
         try
         {
            byte[] bytes = output.Item2.GetBytes(output.Item1);
            stream.Write(bytes, 0, bytes.Length);
            return DataStatus.Complete;
         }
         catch(Exception e)
         {
            if (e is ArgumentNullException || e is ArgumentOutOfRangeException)
               return DataStatus.InternalError;
            else if (e is IOException)
               return DataStatus.SocketExceptionError;
            else if (e is ObjectDisposedException)
               return DataStatus.ClosedStreamError;
            else
               return DataStatus.UnknownError;
         }
      }
   }
}

