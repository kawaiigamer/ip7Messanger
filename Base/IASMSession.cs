using ip7Messanger.Configs;
using ip7Messanger.Network;
using ip7Messanger.Terminal;
using System;
using System.Collections.Generic;
using System.Text;

namespace ip7Messanger.Base
{
    public class IASMSession
    {
        public Storage Storage;
        public Member LocalMember;
        public Member RemoteMember;
        public TerminalManager Ui;
        public GenericLogger Logger;
        public NetworkLayer Network;
        public IASMSession() { }
    }
}
