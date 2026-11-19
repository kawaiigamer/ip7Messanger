using System;
using System.Collections.Generic;
using System.Text;

namespace ip7Messanger.Network
{
    public enum PacketAction : byte
    {
        WRITING = 1,
        NOT_WRITING = 2,
        ONLINE = 3,
        OFFLINE = 4,
        ROTATE_KEY = 5
    }
}
