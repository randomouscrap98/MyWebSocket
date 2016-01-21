using System;
using System.Collections.Generic;
using System.Linq;

namespace MyWebSocket
{
   public enum CloseStatus
   {
      Normal = 1000,
      GoingAway = 1001,
      ProtocolError = 1002,
      UnsupportedDataType = 1003,
      Reserved1 = 1004,
      Reserved2 = 1005,
      Reserved3 = 1006,
      InconsistentData = 1007,
      PolicyViolation = 1008,
      MessageTooBig = 1009,
      ExpectedExtension = 1010,
      UnexpectedError = 1011,
      Reserved4 = 1015,
      NoStatus = 4000,
      BadStatus = 4001
   }

   public enum SpinStatus
   {
      Spinning,
      Complete,
      None,
      Starting,
      Error
   }

   public static class WebSocketHelper
   {
      public static List<string> Explode(string text, bool trim = true)
      {
         return text.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Select(
            x => trim ? x.Trim() : x).ToList();
      }

      public static void TruncateBeginning<T>(this T[] array, int start)
      {
         for (int i = 0; i < array.Count() - start; i++)
            array[i] = array[start + i];
      }

      public static int ParseInt(this IEnumerable<byte> array)
      {
         int result = 0;
         int multiplier = 1;

         for (int i = 0; i < array.Count(); i++)
         {
            result += multiplier * array.ElementAt(i);
            multiplier *= 256;
         }

         return result;
      }
   }
}

