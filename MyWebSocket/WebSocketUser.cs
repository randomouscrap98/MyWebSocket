﻿using System;
using System.Collections.Generic;

namespace MyWebSocket
{
   public class WebSocketUser
   {
      private Func<List<WebSocketUser>> GetAllUsersPlaceholder = null;
      private Action<string> BroadcastPlaceholder = null;
      private Action<string> SendPlaceholder = null;
      private Action CloseSelfPlaceholder = null;

      public WebSocketUser()
      {
         
      }

      public void SetSendPlaceholder(Action<string> function)
      {
         SendPlaceholder = function;
      }

      public void SetGetAllUsersPlaceholder(Func<List<WebSocketUser>> function)
      {
         GetAllUsersPlaceholder = function;
      }

      public void SetBroadcastPlaceholder(Action<string> function)
      {
         BroadcastPlaceholder = function;
      }

      public void SetCloseSelfPlaceholder(Action function)
      {
         CloseSelfPlaceholder = function;
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

      public void Send(string message)
      {
         if (SendPlaceholder != null)
            SendPlaceholder(message);
      }

      public void CloseSelf()
      {
         if (CloseSelfPlaceholder != null)
            CloseSelfPlaceholder();
      }

      /// <summary>
      /// This function is called when the websocket receives a message. Override this function
      /// to perform actions on received messages.
      /// </summary>
      /// <param name="message">Message.</param>
      public virtual void ReceivedMessage(string message)
      {

      }

      /// <summary>
      /// Called when the websocket connection has closed. This is AFTER closing.
      /// </summary>
      public virtual void ClosedConnection()
      {

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

