using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace MyWebSocket
{
   /// <summary>
   /// Represents the Client side of a WebSocket HTTP handshake. Only stores fields 
   /// that are absolutely necessary.
   /// </summary>
   public class HTTPClientHandshake
   {
      public const string ExpectedWebSocketVersion = "13";
      public const string ExpectedHTTPVersion = "1.1";

      /// <summary>
      /// All the fields we expect to see in a client WebSocket handshake (for the expected WebSocket version)
      /// </summary>
      public static readonly List<string> ExpectedFields = new List<string>() 
      {
         "Host", "Upgrade", "Connection", "Sec-WebSocket-Version", "Sec-WebSocket-Key"
      };

      public string HTTPVersion;
      public string Service;
      public string Host; 
      public string Key;
      public string Origin = "";
      public List<String> Protocols = new List<string>();
      public List<string> Extensions = new List<string>();

      public HTTPClientHandshake()
      {
      }
         
      /// <summary>
      /// Attempt to parse the given text into an HTTPClientHandshake object
      /// </summary>
      /// <returns><c>true</c>, if parse was successful <c>false</c> otherwise.</returns>
      /// <param name="text">The complete HTTP header to be parsed</param>
      /// <param name="result">The parsed header as an HTTPClientHandshake object</param>
      /// <param name="error">Any error message from parsing... just for you!</param>
      public static bool TryParse(string text, out HTTPClientHandshake result, out string error)
      {
         //Set some initial dingles
         error = "";
         result = new HTTPClientHandshake();

         List<string> lines;
         List<string> lineArguments;
         List<string> subArguments;

         //First, replace the garbage.
         text = text.Replace("\r\n", "\n");

         //Now actually get the lines, dawg.
         lines = text.Split("\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList();
         lines = lines.Select(x => x.Trim()).ToList();

         //Man, don't be givin' us no flack
         if (lines.Count < 1)
         {
            error = "Handshake was empty!";
            return false;
         }
         
         //First line MUST be the GET thing. Break by space.
         lineArguments = Regex.Split(lines[0], @"\s+").ToList();

         //It's a bad request or something.
         if (lineArguments.Count != 3 || lineArguments[0].ToUpperInvariant() != "GET")
         {
            error = "HTTP Request was poorly formatted! (1st argument)";
            return false;
         }

         //Retrieve the service
         subArguments = lineArguments[1].Split("/".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList();

         if (subArguments.Count < 1)
         {
            error = "HTTP Request was poorly formatted! (2nd argument)";
            return false;
         }

         result.Service = subArguments[subArguments.Count - 1];

         //Retrieve the HTTP version
         subArguments = lineArguments[2].Split("/".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList();

         if (subArguments.Count != 2 || subArguments[0].ToUpperInvariant() != "HTTP")
         {
            error = "HTTP Request was poorly formatted! (3rd argument)";
            return false;
         }
         else if ((new Version(subArguments[1])).CompareTo(new Version(ExpectedHTTPVersion)) < 0)
         {
            error = "HTTP version too low! (expected " + ExpectedHTTPVersion + "+)";
            return false;
         }

         result.HTTPVersion = subArguments[1];

         Dictionary<string, bool> completes = ExpectedFields.ToDictionary(x => x, y => false);

         //OK, NOW we can start retrieving those fields dawg. Some are required.
         for (int i = 1; i < lines.Count; i++)
         {
            Match match = Regex.Match(lines[i], @"^([a-zA-Z\-]+)\s*:\s*(.+)$");

            //Ignore bad lines
            if (!match.Success)
               continue;

            string key = match.Groups[1].Value.Trim();
            string value = match.Groups[2].Value.Trim();

            //Check expected field values for correctness
            if (key == "Upgrade" && value.ToLowerInvariant() != "websocket")
            {
               error = "Bad Upgrade field!";
               return false;
            }
            else if (key == "Connection" && value != "Upgrade")
            {
               error = "Bad Connection field!";
               return false;
            }
            else if (key == "Sec-WebSocket-Version" && value != ExpectedWebSocketVersion)
            {
               error = "Bad Sec-WebSocket-Version! (expected " + ExpectedWebSocketVersion + ")";
               return false;
            }
            else if (key == "Host")
            {
               result.Host = value;
            }
            else if (key == "Sec-WebSocket-Key")
            {
               result.Key = value;
            }
            else if (key == "Origin")
            {
               result.Origin = value;
            }
            else if (key == "Sec-WebSocket-Protocol")
            {
               result.Protocols = WebSocketHelper.Explode(value);
            }
            else if (key == "Sec-WebSocket-Extensions")
            {
               result.Extensions = WebSocketHelper.Explode(value);
            }

            //If this is an expected field, say that we saw it (we would've quit if it was bad)
            if (completes.ContainsKey(key))
               completes[key] = true;
         }

         return true;
      }
   }

   /// <summary>
   /// Represents the Server side of a WebSocket HTTP handshake. Only stores fields 
   /// </summary>
   public class HTTPServerHandshake
   {
      public const string GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
      public string Status;
      public string AcceptKey;
      public string HTTPVersion;
      public List<string> AcceptedProtocols = new List<string>();
      public Dictionary<string, string> ExtraFields = new Dictionary<string, string>();

      /// <summary>
      /// Returns a <see cref="System.String"/> that represents the current <see cref="MyWebSocket.HTTPServerHandshake"/>.
      /// Usuable as the HTTP return header for completing the HTTP WebSocket handshake
      /// </summary>
      /// <returns>A <see cref="System.String"/> that represents the current <see cref="MyWebSocket.HTTPServerHandshake"/>.</returns>
      public override string ToString()
      {
         string baseString = "HTTP/" + HTTPVersion + " " + Status + "\r\n" +
            string.Join("", ExtraFields.Select(x => x.Key + ": " + x.Value + "\r\n"));

         if (Status.Contains("101"))
         {
            return baseString +
               "Upgrade: websocket\r\n" +
               "Connection: Upgrade\r\n" +
               "Sec-WebSocket-Accept: " + AcceptKey + "\r\n" +
               "\r\n";
         }
         else
         {
            return baseString + "\r\n";
         }
      }

      /// <summary>
      /// Create the WebSocket server accept key using the concat-SHA1-base64 sequence.
      /// </summary>
      /// <returns>The server accept key.</returns>
      /// <param name="key">Key from an HTTPClientHandshake</param>
      public static string GenerateAcceptKey(string key)
      {
         string fullKey = key + GUID;

         using (SHA1 sha1 = SHA1.Create())
         {
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(fullKey));
            fullKey = Convert.ToBase64String(hash);
         }

         return fullKey;
      }

      public static HTTPServerHandshake GetResponseForClientHandshake(HTTPClientHandshake handshake)
      {
         HTTPServerHandshake response = new HTTPServerHandshake();
         response.AcceptedProtocols = new List<string>(handshake.Protocols);
         response.AcceptKey = GenerateAcceptKey(handshake.Key);
         response.HTTPVersion = handshake.HTTPVersion;
         response.Status = "101 Switching Protocols";
         return response;
      }

      public static HTTPServerHandshake GetBadRequest(Dictionary<string, string> extras)
      {
         HTTPServerHandshake response = new HTTPServerHandshake();
         response.Status = "400 Bad Request";
         response.ExtraFields = extras;
         return response;
      }
   }
}

