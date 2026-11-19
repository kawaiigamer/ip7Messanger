using System;
using System.Collections.Generic;
using System.Text;

namespace ip7Messanger.Configs
{
    public struct MemberStatistics
    {
        public uint KeyVersion = 0;
        public uint KeyUsage = 0;
        public bool Online = true;
        public bool Writing = false;
        public uint MsgTotal = 0;
        public uint MsgError = 0;
        public uint MsgInfo = 0;
        public uint MsgControl = 0;
        public uint MsgText = 0;
        public uint MsgMedia = 0;
        public uint MsgBytes = 0;
        public double MsgKb() => Math.Round(MsgBytes / 1024.0, 1);
        public MemberStatistics() { }
    }
}
