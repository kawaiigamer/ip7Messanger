using System;
using System.Collections.Generic;
using System.Text;

namespace ip7Messanger.Network
{
    public enum PacketType : sbyte
    {
        INFO = -3,
        SYSTEM = -2,
        ACTION = -1,
        PING = 0,
        TEXT = 1,
        MEDIA = 2,
        IMG = 3,
        AUDIO = 4,
        OTHER = 5
    }
}
