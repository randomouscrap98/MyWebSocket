using System;
using System.Collections.Generic;

namespace MyWebSocket
{
   //public delegate void BroadcastAnnouncer(string message);

   public class WebSocketUser
   {
      //public event BroadcastAnnouncer BroadcastEvent;
      private Func<List<WebSocketUser>> GetAllUsersPlaceholder = null;
      private Action<string> BroadcastPlaceholder = null;

      public WebSocketUser()
      {
         
      }

      public void SetGetAllUsersPlaceholder(Func<List<WebSocketUser>> function)
      {
         if(function != null)
            GetAllUsersPlaceholder = function;
      }

      public void SetBroadcastPlaceholder(Action<string> function)
      {
         if(function != null)
            BroadcastPlaceholder = function;
      }

      /// <summary>
      /// Broadcast the specified message to all connected users.
      /// </summary>
      /// <param name="message">Message.</param>
      public void Broadcast(string message)
      {
         if (BroadcastPlaceholder != null)
            BroadcastPlaceholder(message);
      }

      public List<WebSocketUser> GetAllUsers()
      {
         if (GetAllUsersPlaceholder != null)
            return GetAllUsersPlaceholder();
         else
            return new List<WebSocketUser>();
      }
   }
}

