using System;
using MyWebSocket;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace WebSocketRunner
{
   class MainClass
   {
      public static void Main(string[] args)
      {
         WebSocketServer server = new WebSocketServer();

         if (!server.Start(45695))
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
}
