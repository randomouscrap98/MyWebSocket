using System;
using System.Linq;
using System.Collections.Generic;

namespace MyWebSocket
{
   /// <summary>
   /// All possible frame opcodes for a WebSocket Header
   /// </summary>
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

      private int OpcodeRaw = 0;

      public byte[] Mask = new byte[4];

      public WebSocketHeader()
      {
      }

      /// <summary>
      /// Gets the size of the header computed from mask and payload size.
      /// </summary>
      /// <value>The size of the header.</value>
      public int HeaderSize
      {
         get
         {
            int size = 2;

            if (Masked)
               size += 4;

            if (PayloadIdentifier == 126)
               size += 2;
            else if (PayloadIdentifier == 127)
               size += 8;

            return size;
         }
      }

      public int FrameSize
      {
         get { return PayloadSize + HeaderSize; }
      }

      public HeaderOpcode Opcode
      {
         get { return (HeaderOpcode)OpcodeRaw; }
         set { OpcodeRaw = (int)value; }
      }

      /// <summary>
      /// Get the 7 bit payload value that goes in the header (not necessarily the full payload size)
      /// </summary>
      /// <value>The payload identifier.</value>
      public int PayloadIdentifier
      {
         get
         {
            if (PayloadSize < 126)
               return PayloadSize;
            else if (PayloadSize < 65536)
               return 126;
            else
               return 127;
         }
      }

      /// <summary>
      /// Get head as bytes (for use in transmitting)
      /// </summary>
      /// <returns>The raw bytes.</returns>
      public byte[] GetRawBytes()
      {
         List<byte> bytes = new List<byte>();

         //First two bytes are always the same fields.
         bytes.Add((byte)(((Fin ? 1 : 0) << 7) + (RSV << 4) + OpcodeRaw));
         bytes.Add((byte)(((Masked ? 1 : 0) << 7) + PayloadIdentifier));

         //Payload length field size changes based on the length. 
         List<byte> payloadSizeBytes = BitConverter.GetBytes(PayloadSize).ToList();

         //Bytes are only transmitted across the line in big endian
         if (BitConverter.IsLittleEndian)
            payloadSizeBytes.Reverse();

         //We store payload length as an int, but on the line it COULD be up to a long (8 bytes).
         payloadSizeBytes.InsertRange(0, new List<byte>(){0,0,0,0});

         //If the identifier is 126, the size is the next two bytes. Take the last two bytes
         if (PayloadIdentifier == 126)
            bytes.AddRange(payloadSizeBytes.Skip(6));
         //If the identifier is 127, the size is the next eight bytes. Take them all.
         else if (PayloadIdentifier == 127)
            bytes.AddRange(payloadSizeBytes);

         //Add mask field only if we're masking.
         if (Masked)
            bytes.AddRange(Mask);

         return bytes.ToArray();
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
         int headerSize = FullHeaderSize(bytes);

         if (headerSize > bytes.Length)
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
            result.PayloadSize = bytes.Skip(2).Take(8).ParseInt();
            maskByte += 8;
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
            size += 8;

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
         if (Header.Masked)
            ToggleMask();
      }

      private void ToggleMask()
      {
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
         get { return System.Text.Encoding.UTF8.GetString(PayloadData, 0, (int)Header.PayloadSize); }
      }

      public byte[] GetRawBytes()
      {
         List<byte> bytes = Header.GetRawBytes().ToList();

         if (Header.Masked)
         {
            ToggleMask();
            bytes.AddRange(PayloadData);
            ToggleMask();
         }
         else
         {
            bytes.AddRange(PayloadData);
         }

         return bytes.ToArray();
      }

      /// <summary>
      /// Get the size of the current frame (header + payload)
      /// </summary>
      /// <value>The size of the frame.</value>
      public long FrameSize
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

      /// <summary>
      /// Generate a websocket frame for the given text payload
      /// </summary>
      /// <returns>The text frame.</returns>
      /// <param name="payload">Payload.</param>
      public static WebSocketFrame GetTextFrame(byte[] payload)
      {
         WebSocketHeader header = new WebSocketHeader();
         header.Fin = true;
         header.Masked = false;
         header.Opcode = HeaderOpcode.TextFrame;
         header.PayloadSize = payload.Length;

         return new WebSocketFrame(header, payload);
      }

      /// <summary>
      /// Get a whole standard ping frame, completely built.
      /// </summary>
      /// <returns>The ping frame.</returns>
      public static WebSocketFrame GetPingFrame()
      {
         WebSocketHeader header = new WebSocketHeader();
         header.Fin = true;
         header.Masked = false;
         header.Opcode = HeaderOpcode.PingFrame;
         header.PayloadSize = 0;

         return new WebSocketFrame(header, new byte[0]);
      }

      /// <summary>
      /// Get a whole standard pong frame, completely built.
      /// </summary>
      /// <returns>The pong frame.</returns>
      public static WebSocketFrame GetPongFrame()
      {
         WebSocketHeader header = new WebSocketHeader();
         header.Fin = true;
         header.Masked = false;
         header.Opcode = HeaderOpcode.PongFrame;
         header.PayloadSize = 0;

         return new WebSocketFrame(header, new byte[0]);
      }

      /// <summary>
      /// Get a whole basic close frame, completely built.
      /// </summary>
      /// <returns>The close frame.</returns>
      public static WebSocketFrame GetCloseFrame()
      {
         WebSocketHeader header = new WebSocketHeader();
         header.Fin = true;
         header.Masked = false;
         header.Opcode = HeaderOpcode.CloseConnectionFrame;
         header.PayloadSize = 0;

         return new WebSocketFrame(header, new byte[0]);
      }
   }
}

