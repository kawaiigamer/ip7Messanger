using ip7Messanger.Base;
using ip7Messanger.Configs;
using ip7Messanger.Logging;
using ip7Messanger.Network;
using ip7Messanger.Terminal;

namespace ip7Messanger
{
    internal class Program
    {        
        private static IASMSession CreateIASMSession(string local_user_config_name, string remote_user_config_name)
        {
            IASMSession session = new IASMSession();
            session.Storage = new Storage();
            var configs = session.Storage.GetSavedMembers(local_user_config_name, remote_user_config_name);
            session.LocalMember = configs.Item1;
            session.RemoteMember = configs.Item2;
            session.Ui = new TerminalManager(session);
            session.Ui.Initialize();
            session.Logger = new GenericLogger(session);
            return session;
        }

        public static async Task<int> Main(string[] args)
        {
            IASMSession session = CreateIASMSession("ゆきえ", "VL");
            await using (var network = new NetworkLayer(session))
            {
                network.PacketReceived += packet =>
                {

                };

                while (true)
                {
                    string message = session.Ui.ReadInputLine(async isTyping =>
                    {
                        session.LocalMember.Statistics.Writing = isTyping;
                        session.Ui.UpdateHeader();
                        if (network.IsConnected)
                        {
                            var action = isTyping ? PacketAction.WRITING : PacketAction.NOT_WRITING;
                            await network.SendActionAsync(action);
                        }

                    });

                    if (string.IsNullOrWhiteSpace(message))
                        continue;

                    if (message == "/save")
                    {
                        await session.Logger.FlushAsync();
                        continue;
                    }

                    if (message.StartsWith("/l"))
                    {
                        session.Logger.D(message);
                        continue;
                    }
                                            

                    if (message == "/exit")
                        return 0;

                    if (network.IsConnected)
                    {
                        await session.Network.SendTextAsync(PacketType.TEXT, message);
                    }
                    session.Ui.WriteMessage(message, true);

                }
                
            }

            //using var handler = new MainHandler();
            //Task processingTask = handler.HandleProcessAsync();
            return 0;
        }
    }
}
