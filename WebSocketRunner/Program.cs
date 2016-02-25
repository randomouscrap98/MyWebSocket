using System;
using MyWebSocket;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MyExtensions.Logging;
using System.Threading.Tasks;

namespace WebSocketRunner
{
   class MainClass
   {
      public static void Main(string[] args)
      {
         Console.WriteLine("DOING SOMEWTHING");
         Logger logger = new Logger(100, "", LogLevel.SuperDebug);

//         byte[] thing = new byte[0];
//         Dude whatever = new Dude();
//         //MyExtensions.MySerialize.SaveObject<Dude>("whatever.txt", whatever);
//         Console.ReadKey();
//         MyExtensions.MySerialize.LoadObject<Dude>("whatever.txt", out whatever);
//         Console.WriteLine("Your string: " + whatever);
//         Console.ReadKey();

         WebSocketServerAsync server = new WebSocketServerAsync(
            new WebSocketSettings(45695, "chat", () => { return new EchoUser(); }, logger));

         Task serverWaitable = server.StartAsync();

         Console.WriteLine("Press any key to quit");
         Console.Read();

         Console.WriteLine("*Trying to stop server");
         server.Stop();
         Console.WriteLine("*Server stopped. Now waiting on async context");
         serverWaitable.Wait(server.Settings.ShutdownTimeout);

//         WebSocketServer server = new WebSocketServer(
//            new WebSocketSettings(45695, "chat", () => { return new EchoUser(); }, logger));
//
//         if (!server.Start())
//         {
//            Console.WriteLine("Cannot start server!");
//            return;
//         }
//
//         Console.WriteLine("Press any key to quit");
//         Console.Read();
//
//         if(!server.Stop())
//         {
//            Console.WriteLine("Cannot stop server!");
//            return;
//         }
      }
   }
   public class Dude
   {
      public string whatever;
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
