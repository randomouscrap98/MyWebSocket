using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;

namespace MyWebSocket
{
   /// <summary>
   /// The status of a "partially" blocking IO operation
   /// </summary>
   public enum DataStatus
   {
      DataAvailable,
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
      CancellationRequest,
      UnknownError
   }

   /// <summary>
   /// Represents a client connected over websockets. Main class of websocket library: controls
   /// messages sent and received on the websocket, including the HTTP handshake.
   /// </summary>
   public class WebSocketClient : IDisposable
   {
      public readonly int MaxReceiveSize;

      public readonly Object outputLock = new object();
      private Queue<byte[]> outputBuffer = new Queue<byte[]>();

      private TcpClient Client;
      private NetworkStream stream;
      private byte[] messageBuffer;
      private int messageBufferSize = 0;
      private HTTPClientHandshake parsedHandshake = null;
      private CancellationTokenSource cancelSource = new CancellationTokenSource();

      public bool HandShakeComplete
      {
         get { return parsedHandshake != null; }
      }

      public WebSocketClient(TcpClient newClient, int maxReceiveSize = Int16.MaxValue)
      {
         Client = newClient;
         stream = Client.GetStream();

         MaxReceiveSize = maxReceiveSize;
         messageBuffer = new byte[MaxReceiveSize + 1];
      }

      /// <summary>
      /// We have both a TcpListener and a stream that need to be disposed of.
      /// </summary>
      /// <remarks>Call <see cref="Dispose"/> when you are finished using the <see cref="MyWebSocket.WebSocketClient"/>. The
      /// <see cref="Dispose"/> method leaves the <see cref="MyWebSocket.WebSocketClient"/> in an unusable state. After
      /// calling <see cref="Dispose"/>, you must release all references to the
      /// <see cref="MyWebSocket.WebSocketClient"/> so the garbage collector can reclaim the memory that the
      /// <see cref="MyWebSocket.WebSocketClient"/> was occupying.</remarks>
      public void Dispose()
      {
         if (cancelSource != null)
         {
            cancelSource.Dispose();
            cancelSource = null;
         }
         if (stream != null)
         {
            stream.Dispose();
            stream = null;
         }
         if (Client != null)
         {
            Client.Close();
            Client = null;
         }
      }

      public void CancelAsyncOperations()
      {
         //We do both just in case. All ASYNC operations are passed this cancellation token
         //(if they accept it), but we also close the stream because cancellation token doesn't work all the time.
         cancelSource.Cancel();
         stream.Close();
      }

      public int WriteQueueSize
      {
         get
         {
            lock (outputLock)
            {
               return outputBuffer.Count;
            }
         }
      }

      public bool Connected
      {
         get { return Client.GetState() == System.Net.NetworkInformation.TcpState.Established; }
      }

      public void MessageBufferPop(int size)
      {
         messageBuffer.TruncateBeginning(size, messageBufferSize);
         messageBufferSize -= size;
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
               MessageBufferPop(handshakeEnd + 4);
//               messageBuffer.TruncateBeginning(handshakeEnd + 4, messageBufferSize);
//               messageBufferSize -= (handshakeEnd + 4);

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
      /// Read and properly parse the message for the HTTP handshake portion of
      /// a WebSocket connection.
      /// </summary>
      /// <returns>The read handshake.</returns>
      public async Task<Tuple<DataStatus, HTTPClientHandshake, string>> ReadHandshakeAsync()
      {
         //You've already done the handshake, you idiot.
         if (HandShakeComplete)
            return Tuple.Create(DataStatus.Complete, parsedHandshake, "");

         string handshake = "";
         int handshakeEnd = 0;
         string error = "";
         HTTPClientHandshake result = new HTTPClientHandshake();

         //Keep repeating until we have the whole handshake. It's OK if the stream stops in the middle
         //of the operation, because we'll just return the proper data status.
         do
         {
            //Pull a chunk of data (as much as we can) from the stream and store it in our internal buffer.
            DataStatus readStatus = await GenericReadAsync();

            //If there was an error (anything other than "completion"), return the error.
            if (readStatus != DataStatus.Complete)
               return Tuple.Create(readStatus, result, "");

            //Now let's see if we read the whole header by searching for the header ending symbol.
            handshake = System.Text.Encoding.ASCII.GetString(messageBuffer, 0, messageBufferSize);
            handshakeEnd = handshake.IndexOf("\r\n\r\n");

         } while(handshakeEnd < 0);

         //We read the whole header, now it's time to parse it.
         if (HTTPClientHandshake.TryParse(handshake, out result, out error))
         {
            //Push the data in the buffer back. We may have read a bit of the new data.
            MessageBufferPop(handshakeEnd + 4);
//            messageBuffer.TruncateBeginning(handshakeEnd + 4, messageBufferSize);
//            messageBufferSize -= (handshakeEnd + 4);

            parsedHandshake = result;
            return Tuple.Create(DataStatus.Complete, result, error);
         }
         else
         {
            return Tuple.Create(DataStatus.DataFormatError, result, error);
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

         //If there was an error (anything other than "completion" or waiting), return the error.
         if (readStatus != DataStatus.Complete)
            return readStatus;

         //We MAY have read more than one frame at a time! Wowie...
//         while (messageBufferSize > 0)
//         {
            //We need at least 2 bytes to complete the header.
            if (messageBufferSize < 2)
               return DataStatus.WaitingOnData;

            byte[] message = messageBuffer.Take(messageBufferSize).ToArray();

            //If the complete header hasn't been read yet, we're still waiting for it.
            if (messageBufferSize < WebSocketHeader.FullHeaderSize(message))
               return DataStatus.WaitingOnData;

            WebSocketHeader header = new WebSocketHeader();

            //If we can't parse the header at this point, we have some serious issues.
            if (!WebSocketHeader.TryParse(message, out header))
               return DataStatus.InternalError;
         
            //Too much data
            if (header.FrameSize > MaxReceiveSize)
            {
               return DataStatus.OversizeError;
            }
         
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
            MessageBufferPop(header.FrameSize);
//            messageBuffer.TruncateBeginning(header.FrameSize, messageBufferSize);
//            messageBufferSize -= header.FrameSize;
         //}

         return DataStatus.Complete;
      }

      /// <summary>
      /// Attempts to read an entire frame. Only finishes when a whole frame is read or an exception is encountered
      /// </summary>
      /// <returns>The status of the read operation</returns>
      /// <param name="frame">The parsed frame (on success)</param>
      public async Task<Tuple<DataStatus, WebSocketFrame>> ReadFrameAsync()
      {
         WebSocketFrame frame = new WebSocketFrame();
         DataStatus readStatus = DataStatus.Complete;
         WebSocketHeader header = null;
         byte[] message = null;

         Func<DataStatus, Tuple<DataStatus,WebSocketFrame>> FramePackage = x => Tuple.Create(x, frame);
         Func<bool> NoFrameInBuffer = () => (header == null || messageBufferSize < header.FrameSize);

         //Continue reading until we get a full frame. If the result is ever NOT complete 
         //(as in no read was performed), we should get the hell outta here
         do
         {
            //Note: we START with parsing the header just in case we have a leftover frame in the buffer.
            //We don't want to read more messages until all frames in the buffer have been parsed.

            //Console.WriteLine("Trying to read whole frame. Buffer: " + messageBufferSize);

            //Oh good, there's at least enough to know the header size.
            if(messageBufferSize >= 2)
            {
               message = messageBuffer.Take(messageBufferSize).ToArray();

               //OK we KNOW we read a whole header at this point.
               if(messageBufferSize >= WebSocketHeader.FullHeaderSize(message))
               {
                  //If we can't parse the header at this point, we have some serious issues.
                  if (!WebSocketHeader.TryParse(message, out header))
                     return FramePackage(DataStatus.InternalError);

                  //Too much data
                  if (header.FrameSize > MaxReceiveSize)
                     return FramePackage(DataStatus.OversizeError);
               }
            }
               
            //Only read if there's no full frame in the buffer.
            if(NoFrameInBuffer())
               readStatus = await GenericReadAsync();

            //If there was an error (anything other than "completion" or waiting), return the error.
            if (readStatus != DataStatus.Complete)
               return FramePackage(readStatus);
            
         }while(NoFrameInBuffer());

         //Oh, we have the whole message. Uhh ok then, let's make sure the header fields are correct
         //before continuing. RSV needs to be 0 (may change later) and all client messages must be masked.
         if (!header.Masked || header.RSV != 0)
            return FramePackage(DataStatus.DataFormatError);

         //Oh is this... a binary frame? Dawg... don't gimme that crap.
         if (header.Opcode == HeaderOpcode.BinaryFrame)
            return FramePackage(DataStatus.UnsupportedError);

         //Initialize a frame with our newly parsed data
         frame = new WebSocketFrame(header, messageBuffer.Take(header.FrameSize).ToArray());

         //Remove the message data from the buffer
         MessageBufferPop(header.FrameSize);

         return FramePackage(DataStatus.Complete);
      }

      //Convert any kind of socket or stream exception into a data status
      private static DataStatus StatusForException(Exception e)
      {
         //We need to report the error based on what kind of exception we got.
         if (e is ArgumentException || e is ArgumentNullException || e is ArgumentOutOfRangeException)
            return DataStatus.InternalError;
         else if (e is ObjectDisposedException)
            return DataStatus.ClosedStreamError;
         else if (e is IOException)
            return DataStatus.ClosedSocketError;
         else if (e is SocketException)
            return DataStatus.SocketExceptionError;
         else
            return DataStatus.UnknownError;
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
            {
               //if there's something in the buffer, it COULD be complete. It doesn't mean it is, but...
               if(messageBufferSize > 0)
                  return DataStatus.Complete;
               else
                  return DataStatus.WaitingOnData;
            }
         }
         catch(Exception e)
         {
            return StatusForException(e);
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
            return StatusForException(e);
         }
      }

      /// <summary>
      /// Only reads as much data as possible into the internal read buffer asynchronously
      /// </summary>
      /// <returns>A status representing what happened during the read.</returns>
      private async Task<DataStatus> GenericReadAsync()
      {
         //Console.WriteLine("About to read more data. Buffer leftover: " + (messageBuffer.Length - messageBufferSize));
         try
         {
            //This will HOPEFULLY resolve issues where the stream is in the middle of reading
            //but it never returns. Maybe...
            while(!stream.DataAvailable)
            {
               //Console.WriteLine("Waiting (delaying)");
               await Task.Delay(100);

               if(cancelSource.IsCancellationRequested)
                  return DataStatus.CancellationRequest;
            }

            /*int bytesRead = await Task.Factory.StartNew(() => stream.Read(messageBuffer, messageBufferSize, 
               messageBuffer.Length - messageBufferSize));*/
            
            //Console.WriteLine("Async read yo");
            int bytesRead = await stream.ReadAsync(messageBuffer, messageBufferSize, 
               messageBuffer.Length - messageBufferSize, cancelSource.Token);
            //Console.WriteLine("Async read done");

            //Console.WriteLine("Just finished reading data");

            if(bytesRead <= 0)
               return DataStatus.EndOfStream;

            //Acknowledge the read bytes in the buffer.
            messageBufferSize += bytesRead;

            return DataStatus.Complete;
         }
         catch(Exception e)
         {
            //Console.WriteLine("Read got rekt");
            return StatusForException(e);
         }
      }

      /// <summary>
      /// Handshakes are a bit special, so use this to enqueue them.
      /// </summary>
      /// <param name="handshake">Handshake.</param>
      public void QueueHandshakeMessage(HTTPServerHandshake handshake)
      {
         QueueRaw(System.Text.Encoding.ASCII.GetBytes(handshake.ToString()));
         //QueueMessage(handshake.ToString(), System.Text.Encoding.ASCII);
      }

      /// <summary>
      /// Adds a message to the output queue. Does NOT immediately write the message!
      /// </summary>
      /// <param name="message">Message.</param>
      /// <param name="encoding">Encoding.</param>
      public void QueueMessage(string message, System.Text.Encoding encoding = null)
      {
         if (encoding == null)
            encoding = System.Text.Encoding.UTF8;

         byte[] bytes = encoding.GetBytes(message);

         QueueRaw(WebSocketFrame.GetTextFrame(bytes).GetRawBytes());
      }

      /// <summary>
      /// Queue some raw data on the write queue (useful for frame queuing)
      /// </summary>
      /// <param name="bytes">Bytes.</param>
      public void QueueRaw(byte[] bytes)
      {
         lock (outputLock)
         {
            outputBuffer.Enqueue(bytes);
         }
      }

      /// <summary>
      /// This pops one write off the queue and actually writes it to the network.
      /// </summary>
      public DataStatus DequeueWrite()
      {
         byte[] bytes = null;

         lock (outputLock)
         {
            if (outputBuffer.Count == 0)
               return DataStatus.WaitingOnData;
            
            bytes = outputBuffer.Dequeue();
         }

         //In the future, this section may be extracted to a threadpool
         try
         {
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
         
      /// <summary>
      /// This pops one write off the queue and actually writes it to the network.
      /// </summary>
      public async Task<DataStatus> WriteRawAsync(byte[] bytes)
      {
         try
         {
            await stream.WriteAsync(bytes, 0, bytes.Length, cancelSource.Token);
            return DataStatus.Complete;
         }
         catch(Exception e)
         {
            return StatusForException(e);
         }
      }

      /// <summary>
      /// Writes the server response handshake async.
      /// </summary>
      /// <returns>The handshake async.</returns>
      /// <param name="handshake">Handshake.</param>
      public async Task<DataStatus> WriteHandshakeAsync(HTTPServerHandshake handshake)
      {
         return await WriteRawAsync(System.Text.Encoding.ASCII.GetBytes(handshake.ToString()));
      }

      /// <summary>
      /// Writes a string message to the websocket asynchronously.
      /// </summary>
      /// <returns>The message async.</returns>
      /// <param name="message">Message.</param>
      public async Task<DataStatus> WriteMessageAsync(string message)
      {
         return await WriteRawAsync(WebSocketFrame.GetTextFrame(System.Text.Encoding.UTF8.GetBytes(message)).GetRawBytes());
      }

      /// <summary>
      /// Attempt flush out the entire write queue in the given amount of time.
      /// </summary>
      /// <param name="timeout">Timeout.</param>
      public void DumpWriteQueue(TimeSpan timeout)
      {
         int oldTimeout = Client.SendTimeout;
         int oldStreamTimeout = stream.WriteTimeout;
         Client.SendTimeout = (int)timeout.TotalMilliseconds;
         stream.WriteTimeout = (int)timeout.TotalMilliseconds;

         DataStatus status = DataStatus.UnknownError;

         do
         {
            status = DequeueWrite();
         } while(status == DataStatus.Complete);

         Client.SendTimeout = oldTimeout;
         stream.WriteTimeout = oldStreamTimeout;
      }
   }
}

