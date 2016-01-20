using System;
using MyExtensions.Logging;

namespace MyWebSocket
{
   public class WebSocketSpinner
   {
      public readonly WebSocketServer Server;
      public readonly WebSocketClient Client;
      public readonly WebSocketUser User;
      public readonly long ID;

      //Shhhh, don't look at them!
      private static long NextID = 1;
      private static readonly object idLock = new object();

      public WebSocketSpinner(WebSocketServer managingServer, WebSocketClient supportingClient)
      {
         Client = supportingClient;
         Server = managingServer;
         User = new WebSocketUser();
         ID = GenerateID();
      }

      public void Log(string message, LogLevel level = LogLevel.Normal)
      {
         Server.Log(message, level, "WebSocketSpinner" + ID);
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

