using System;
using MyWebSocket;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MyExtensions.Logging;

namespace WebSocketRunner
{
   class MainClass
   {
      public static void Main(string[] args)
      {
         Logger logger = new Logger(100, "", LogLevel.SuperDebug);

         WebSocketServer server = new WebSocketServer(
            new WebSocketSettings(45695, "chat", () => { return new EchoUser(); }, logger));

         if (!server.Start())
         {
            Console.WriteLine("Cannot start server!");
            return;
         }

         Console.WriteLine("Press any key to quit");
         Console.Read();

         if(!server.Stop())
         {
            Console.WriteLine("Cannot stop server!");
            return;
         }
      }
   }

   public class EchoUser : WebSocketUser
   {
      public override void ReceivedMessage(string message)
      {
         Send("I got: " + message);

         if (message.Contains("broadcast"))
            Broadcast("Broadcast: " + message);
      }
   }
}
