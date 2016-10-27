using System;
using MyWebSocket;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MyExtensions.Logging;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WebSocketRunner
{
   class MainClass
   {
      public static void Main(string[] args)
      {
         Console.WriteLine("DOING SOMEWTHING");

         //This allows you to log stuff to a file and/or the console. You can set the various settings
         //for the logger, then pass it to the websocket server 
         Logger logger = new Logger(100, "", LogLevel.SuperDebug);

         //I only support the asynchronous version now. The websocketserver takes one "WebSocketSettings"
         //object, which describes how the websocket server acts. At a minimum, you need the port,
         //a name for the protocol (anything, as long as it matches in the JS), and a "lambda" which
         //describes how to make a websocket user. A websocket user is a class which allows you to
         //intercept the messages that a connection receives. The WebSocketServerAsync class handles absolutely
         //everything for you; all you have to do is make a class in which you handle the messages. A new
         //web socket user class is created for each connection (user). My websocket user is called "EchoUser",
         //since it just echos everything is receives.
         WebSocketServerAsync server = new WebSocketServerAsync(
            new WebSocketSettings(45695, "chat", () => { return new EchoUser(); }, logger));

         //When you start an asynchronous function, you get back a "task" which is basically like an "I'm running"
         //token. The server is now running in the background, and you can stop it using this Task.
         Task serverWaitable = server.StartAsync();

         //The code will wait at the Console.Read until a user presses a button. Since the server is running in the 
         //background, it's OK that we're waiting here.
         Console.WriteLine("Press any key to quit");
         Console.Read();

         //If you want to safely shut down the server, you should definitely call the Stop function on it.
         Console.WriteLine("*Trying to stop server");
         server.Stop();

         //Now you just have to wait for the background thread to stop. This is where we use that "Task"
         Console.WriteLine("*Server stopped. Now waiting on async context");
         serverWaitable.Wait(server.Settings.ShutdownTimeout);
      }
   }

   //The : means we're "deriving" from another class. It basically just means we are a "sub-species" of the
   //given class. It's like how an apple is also a fruit: our EchoUser is also a WebSocketUser, and obtains all
   //the abilities of the WebSocketUser base class.
   public class EchoUser : WebSocketUser
   {
      //We override the functionality for receiving messages using this function. Now we can tell the
      //websocket server how to handle messages it receives
      public override void ReceivedMessage(string message)
      {
         //"Send" will send the given message back to the current user only.
         Send("I got: " + message);

         try
         {
            dynamic json = JsonConvert.DeserializeObject(message);
            string first = json.message;
            first = System.Security.SecurityElement.Escape(first);
            Send("Got a good JSON message: " + first);
         }
         catch(Exception)
         {
            Send("Got a bad JSON message");
         }

         //"Broadcast" will send the given message to all connected users.
         if (message.Contains("broadcast"))
            Broadcast("Broadcast: " + message);
      }
   }
}
