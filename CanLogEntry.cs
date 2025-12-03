using System;
using System.Collections.Generic;
using System.Text;

namespace MyCanBusTool
{
    public class CanLogEntry
    {
        public string Timestamp { get; set; }
        public string Id { get; set; }
        public byte Dlc { get; set; }
        public string Data { get; set; }
    }
}
