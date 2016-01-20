using System;
using System.Linq;

namespace MyWebSocket
{
   public enum HeaderOpcode
   {
      ContinueFrame = 0x0,
      TextFrame = 0x1,
      BinaryFrame = 0x2,
      Unused1 = 0x3,
      Unused2 = 0x4,
      Unused3 = 0x5,
      Unused4 = 0x6,
      Unused5 = 0x7,
      CloseConnectionFrame = 0x8,
      PingFrame = 0x9,
      PongFrame = 0xA,
      Unused6 = 0xB,
      Unused7 = 0xC,
      Unused8 = 0xD,
      Unused9 = 0xE,
      Unused10 = 0xF,
   }

   /// <summary>
   /// Represents all the data parsed from a WebSocket header.
   /// </summary>
   public class WebSocketHeader
   {
      public bool Fin = false;
      public bool Masked = false;
      public int RSV = 0;
      public int PayloadSize = 0;
      public int HeaderSize = 0;

      private int OpcodeRaw = 0;

      public byte[] Mask = new byte[4];

      public WebSocketHeader()
      {
      }

      public int FrameSize
      {
         get { return PayloadSize + HeaderSize; }
      }

      public HeaderOpcode Opcode
      {
         get { return (HeaderOpcode)OpcodeRaw; }
      }

      /// <summary>
      /// Attempt to parse a series of ordered bytes into a WebSocketHeader. Bytes should be taken directly
      /// from whatever network stream you're using.
      /// </summary>
      /// <returns><c>true</c>, if parsed correctly, <c>false</c> otherwise.</returns>
      /// <param name="bytes">Header bytes</param>
      /// <param name="result">Parsed header information</param>
      public static bool TryParse(byte[] bytes, out WebSocketHeader result)
      {
         result = new WebSocketHeader();
         result.HeaderSize = FullHeaderSize(bytes);

         if (result.HeaderSize > bytes.Length)
            return false;

         //First byte encoding
         result.Fin = (bytes[0] & 128) > 0;
         result.RSV = (bytes[0] & 112) >> 4;
         result.OpcodeRaw = (bytes[0] & 15);

         //Second byte encoding
         result.Masked = (bytes[1] & 128) > 0;
         result.PayloadSize = (bytes[1] & 127);

         int maskByte = 2;

         //Get all the bytes of the payload
         if (result.PayloadSize == 126)
         {
            result.PayloadSize = bytes.Skip(2).Take(2).ParseInt();
            maskByte += 2;
         }
         else if (result.PayloadSize == 127)
         {
            result.PayloadSize = bytes.Skip(2).Take(4).ParseInt();
            maskByte += 4;
         }

         //Get all the bytes of the mask
         if (result.Masked)
            result.Mask = bytes.Skip(maskByte).Take(4).ToArray();

         return true;
      }

      /// <summary>
      /// Based on a minimal amount of bytes, figure out the full size of the header. Can compute 
      /// based on just 2 bytes.
      /// </summary>
      /// <returns>The full header size</returns>
      /// <param name="bytes">At least the initial 2 bytes from the header.</param>
      public static int FullHeaderSize(byte[] bytes)
      {
         int size = 2;

         //Minimum amount of bytes required is 2
         if (bytes.Length < size)
            return -1;

         //This is the "mask" bit. If a mask is present, the header size is increased by 32 bits (4 bytes)
         if ((bytes[1] & 128) > 0)
            size += 4;

         //When payload size is 126, we're looking at 2 extra bytes to store the payloadsize
         if ((bytes[1] & ~128) == 126)
            size += 2;
         //When the payload size is max, use 4 bytes to state the payload size.
         else if ((bytes[1] & ~128) == 127)
            size += 4;

         return size;
      }
   }

   public class WebSocketFrame
   {
      public readonly WebSocketHeader Header;
      public readonly byte[] PayloadData;

      /// <summary>
      /// Initialize an empty (invalid) frame.
      /// </summary>
      public WebSocketFrame()
      {
         Header = new WebSocketHeader();
         PayloadData = new byte[1];
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="MyWebSocket.WebSocketFrame"/> class.
      /// </summary>
      /// <param name="header">A preparsed header object</param>
      /// <param name="frameOrPayload">Full frame data or just the payload (leave masked)</param>
      public WebSocketFrame(WebSocketHeader header, byte[] frameOrPayload)
      {
         Header = header;

         //Set the payload data bytes based on the length of the given byte array.
         if (frameOrPayload.Length == header.PayloadSize)
            PayloadData = frameOrPayload;
         else if (frameOrPayload.Length == header.FrameSize)
            PayloadData = frameOrPayload.Skip(header.HeaderSize).Take(header.PayloadSize).ToArray();
         else
            PayloadData = new byte[1];

         //Now let's just unmask the data and finally get the hell outta dodge.
         for (int i = 0; i < Header.PayloadSize; i++)
            PayloadData[i] ^= Header.Mask[i % 4];
      }

      /// <summary>
      /// Does this frame make any sense? 
      /// </summary>
      /// <value><c>true</c> if valid frame; otherwise, <c>false</c>.</value>
      public bool ValidFrame
      {
         get
         {
            return PayloadData.Length == Header.PayloadSize && Header.HeaderSize > 0;
         }
      }

      /// <summary>
      /// Retrieve the payload data as a string.
      /// </summary>
      /// <value>The data as string.</value>
      public string DataAsString
      {
         get { return System.Text.Encoding.UTF8.GetString(PayloadData, 0, Header.PayloadSize); }
      }

      /// <summary>
      /// Get the size of the current frame (header + payload)
      /// </summary>
      /// <value>The size of the frame.</value>
      public int FrameSize
      {
         get { return Header.FrameSize; }
      }

      /// <summary>
      /// Get the actual integer close code (shouldn't be necessary)
      /// </summary>
      /// <value>The close code raw.</value>
      public int CloseCodeRaw
      {
         get
         {
            if (Header.Opcode == HeaderOpcode.CloseConnectionFrame)
            {
               if (PayloadData.Length >= 2)
                  return PayloadData.Take(2).ParseInt();
               else
                  return (int)CloseStatus.BadStatus;
            }
            else
            {
               return (int)CloseStatus.NoStatus;
            }
         }
      }

      /// <summary>
      /// Get the close code. Will return CloseCode.NoStatus if this is not a close frame.
      /// </summary>
      /// <value>The close code.</value>
      public CloseStatus CloseCode
      {
         get
         {
            int code = CloseCodeRaw;

            if (Enum.IsDefined(typeof(CloseStatus), code))
               return (CloseStatus)code;
            else
               return CloseStatus.BadStatus;
         }
      }
   }
}

