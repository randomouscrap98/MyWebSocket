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
         BaseClass thing = BaseClass.Maker(() => new DerivedClass());

         Console.WriteLine("Thing is a: " + thing.GetType());

         WebSocketServer server = new WebSocketServer(45695);

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

   class BaseClass
   {
      public int someint = 14;

      public static BaseClass Maker(Func<BaseClass> makerFunction)
      {
         return makerFunction();
      }
   }

   class DerivedClass : BaseClass
   {
      public int otherint = 15;
   }
}
