using System;

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
}

