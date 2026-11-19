using ip7Messanger.Base;
using ip7Messanger.Configs;
using ip7Messanger.Terminal;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ip7Messanger
{
    public class Storage
    {
        public readonly string Logs;
        public readonly string Chats;
        public readonly string Media;
        public readonly string Photos;
        public readonly string Voices;
        public readonly string Videos;
        public readonly string Other;
        public readonly string Configs;
        private string CreateOrCheckDirectory(string base_part, string path_part)
        {
            string full_path = Path.Combine(base_part, path_part);
            if (!Directory.Exists(full_path))
            {
                Directory.CreateDirectory(full_path);
            }
            return full_path;
        }
        public Storage()
        {
            string current_app_path = Directory.GetCurrentDirectory();
            Logs = CreateOrCheckDirectory(current_app_path, Consts.LOGS_FOLDER_NAME);
            Chats = CreateOrCheckDirectory(current_app_path, Consts.CHATS_FOLDER_NAME);
            Media = CreateOrCheckDirectory(current_app_path, Consts.MEDIA_FOLDER_NAME);
            Configs = CreateOrCheckDirectory(current_app_path, Consts.CONFIGS_FOLDER_NAME);
            Photos = CreateOrCheckDirectory(Media, Consts.PHOTOS_FOLDER_NAME);
            Voices = CreateOrCheckDirectory(Media, Consts.VOICES_FOLDER_NAME);
            Videos = CreateOrCheckDirectory(Media, Consts.VIDEOS_FOLDER_NAME);
            Other = CreateOrCheckDirectory(Media, Consts.OTHER_FOLDER_NAME);            
        }

        private (MemberSavableConfig, MemberSavableConfig) ReadSavedConfigs(string local, string remote)
        {
            return (
                MemberSavableConfig.Deserialize(File.ReadAllText(Path.Combine(Configs, $"{local}.json")))!,
                MemberSavableConfig.Deserialize(File.ReadAllText(Path.Combine(Configs, $"{remote}.json")))!
                );
        }

        private (Member, Member) CreateMembersFromSavedConfigs((MemberSavableConfig, MemberSavableConfig) cfgs)
        {
            return (
                new Member(cfgs.Item1),
                new Member(cfgs.Item2)
                );
        }

        public void SaveConfigs((Member, Member) members)
        {
            SaveConfigs((members.Item1.SavableConfig, members.Item2.SavableConfig));
        }

        public void SaveConfigs((MemberSavableConfig, MemberSavableConfig) cfgs)
        {            
            File.WriteAllText(Path.Combine(Configs, $"{cfgs.Item1.Name}.json"), cfgs.Item1.Serialize());
            File.WriteAllText(Path.Combine(Configs, $"{cfgs.Item2.Name}.json"), cfgs.Item2.Serialize());
        }

        public (Member, Member) GetSavedMembers(string local, string remote)
        {
            try
            {
                return CreateMembersFromSavedConfigs(ReadSavedConfigs(local, remote));
            } catch (FileNotFoundException)
            {                
                MemberSavableConfig smd_l = new MemberSavableConfig(local, "local", Consts.DefaultPort, RandomNumberGenerator.GetBytes(32));
                MemberSavableConfig smd_r = new MemberSavableConfig(remote, "26.16.184.160", Consts.DefaultPort, RandomNumberGenerator.GetBytes(32));
                SaveConfigs((smd_l, smd_r));
                return CreateMembersFromSavedConfigs(ReadSavedConfigs(local, remote));
            }
        }
    }
}
