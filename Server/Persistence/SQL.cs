using System;
using System.Collections.Generic;
using CustomsFramework;
using Server.Guilds;
using System.IO;
using System.Data.Linq;
using System.Text;

namespace Server
{


    public class SQL : SaveStrategy
    {

        public static SaveOption SaveType = SaveOption.Normal;
        private readonly Queue<Item> _decayQueue;


        public SQL()
        {
            this._decayQueue = new Queue<Item>();
        }

        public enum SaveOption
        {
            Normal,
            Threaded
        }


        public override string Name
        {
            get
            {
                return "SQL";
            }
        }
        protected bool UseSequentialWriters
        {
            get
            {
                return (SQL.SaveType == SaveOption.Normal);
            }
        }

        public override void Save(SaveMetrics metrics, bool permitBackgroundWrite)
        {

            this.SaveMobiles();
            this.SaveItemsSQL();
            //this.SaveItems();
            this.SaveGuilds();
            this.SaveData();


        }

        
        public override void ProcessDecay()
        {
            while (this._decayQueue.Count > 0)
            {
                Item item = this._decayQueue.Dequeue();

                if (item.OnDecay())
                {
                    item.Delete();
                }
            }
        }

        protected void SaveMobiles()
        {
            Dictionary<Serial, Mobile> mobiles = World.Mobiles;
            List<Database.Mobile> mobs = new List<Database.Mobile>();
            List<Database.Skill> vsk = new List<Database.Skill>();
            List<Database.MobIndex> mindex = new List<Database.MobIndex>();
            int skillid = 0;

            foreach (Mobile m in mobiles.Values)
            {
                var typename = m.GetType();

                Database.Mobile v = new Database.Mobile();

                for(int i =0; i< m.Skills.Length; i++)
                {
                    if (m.Skills[i] != null && m.Skills[i].Base != 0)
                    {

                        Database.Skill vskill = new Database.Skill();
                        skillid++;
                        vskill.Id = skillid;
                        vskill.Base = ((double)m.Skills[i].Base)*10;
                        vskill.Cap = (int)m.Skills[i].Cap;
                        vskill.Lock = (byte)m.Skills[i].Lock;
                        vskill.Name = m.Skills[i].Name;
                        vskill.Parent = m.Serial.Value;
                        vsk.Add(vskill);
                    }
                }


                foreach(Item mitem in m.Items)
                {
                    v.m_Items += mitem.Serial.Value + ";";
                }

                v.mType = typename.FullName;

                v.mTypeRef = m.m_TypeRef;
                v.Id = m.Serial.Value;
                v.Serial = m.Serial.Value;
                v.m_IgnoreMobiles = m.IgnoreMobiles;
                v.m_AccessLevel = (byte)m.AccessLevel;
                v.m_AutoPageNotify = m.AutoPageNotify;
                v.m_BaseSoundID = m.BaseSoundID;
                v.m_Blessed = m.Blessed;
                v.m_Body = m.Body;
                v.m_CanSwim = m.CanSwim;
                v.m_CantWalk = m.CantWalk;
                if(m.Corpse != null)
                {
                    v.m_Corpse = m.Corpse.Serial.Value;
                }
                v.m_CreationTime = m.CreationTime;
                v.m_Criminal = m.Criminal;
                v.m_Dex = m.Dex;
                v.m_Direction = (byte)m.Direction;
                v.m_DisarmReady = m.DisarmReady;
                v.m_EmoteHue = m.EmoteHue;
                v.m_Fame = m.Fame;
                v.m_Hidden = m.Hidden;
                v.m_Hits = m.Hits;
                if (m.Holding != null)
                {
                    v.m_Holding = m.Holding.Serial.Value;
                }
                v.m_Hue = m.Hue;
                v.m_Int = m.Int;

                v.m_Karma = m.Karma;
                v.m_Language = m.Language;
                v.m_Locationx = m.Location.X;
                v.m_Locationy = m.Location.Y;
                v.m_Locationz = m.Location.Z;
                v.m_MagicDamageAbsorb = m.MagicDamageAbsorb;
                v.m_Mana = m.Mana;
                v.m_Map = (byte)m.Map.MapIndex;
                v.m_Name = m.Name;
                v.m_NameHue = m.NameHue;
                v.m_Player = m.Player;
                v.m_SpeechHue = m.SpeechHue;
                v.m_Squelched = m.Squelched;
                v.m_Stam = m.Stam;
                v.m_StatCap = m.StatCap;
                v.m_Str = m.Str;
                v.m_StunReady = m.StunReady;
                v.m_VirtualArmor = m.VirtualArmor;
                v.m_Warmode = m.Warmode;
                v.m_WhisperHue = m.WhisperHue;
                v.m_YellHue = m.YellHue;
                    // v.Poison = (byte)m.Poison;

                    //-------------------------
                if (m.Player)
                {
                    v.Account = m.Account.Username;
                }
                else
                {
                    v.Account = "NPC";
                }
                v.m_BAC = m.BAC;
                v.m_BaseSoundID = m.BaseSoundID;
                v.m_Blessed = m.Blessed;
                if(m.LastDexGain < DateTime.UtcNow){
                    v.m_LastDexGain = DateTime.UtcNow;
                }
                else
                {
                    v.m_LastDexGain = m.LastDexGain;
                }
                if (m.LastStrGain < DateTime.UtcNow)
                {
                    v.m_LastStrGain = DateTime.UtcNow;
                }
                else
                {
                    v.m_LastStrGain = m.LastDexGain;
                }
                if (m.LastIntGain < DateTime.UtcNow)
                {
                    v.m_LastIntGain = DateTime.UtcNow;
                }
                else
                {
                    v.m_LastIntGain = m.LastDexGain;
                }
                v.m_Hair = (byte)m.HairItemID;
                v.m_FacialHair = (byte)m.FacialHairItemID;
                v.m_Race = (byte)m.Race.RaceIndex;
                v.m_ShortTermMurders = m.ShortTermMurders;
                v.m_FollowersMax = m.FollowersMax;
                if(m.GuildFealty != null) {
                    v.m_GuildFealty = m.GuildFealty.Serial.Value;
                }
                    if(m.Guild != null)
                {
                    v.m_Guild = m.Guild.Id;
                }
                   
                v.m_DisplayGuildTitle = m.DisplayGuildTitle;
                v.m_Hunger = m.Hunger;
                v.m_Kills = m.Kills;
                v.m_GuildTitle = m.GuildTitle;
                v.m_Female = m.Female;
                v.m_Player = m.Player;
                v.m_Title = m.Title;
                v.m_Profile = m.Profile;
                v.m_ProfileLocked = m.ProfileLocked;
                v.m_LogoutLocationx = m.LogoutLocation.X;
                v.m_LogoutLocationy = m.LogoutLocation.Y;
                v.m_LogoutLocationz = m.LogoutLocation.Z;
                if (m.LogoutMap != null) { v.m_LogoutMap = (byte)m.LogoutMap.MapIndex; } else
                {
                    v.m_LogoutMap = (byte)(0xFF);
                }
                v.m_StrLock = (byte)m.StrLock;
                v.m_DexLock = (byte)m.DexLock;
                v.m_IntLock = (byte)m.IntLock;
                v.m_Hidden = m.Hidden;

                MemoryStream strim = new MemoryStream();
                GenericWriter bin = new BinaryFileWriter(strim, true);
                m.Serialize(bin);
                bin.Close();
                v.Data = Convert.ToBase64String(strim.ToArray());

                mobs.Add(v);
                m.FreeCache();
            }

            for (int i = 0; i < World.m_MobileTypes.Count; ++i) { 

            Database.MobIndex a = new Database.MobIndex();

                a.MobTypes = (World.m_MobileTypes[i].FullName);
                a.Id = i;

                mindex.Add(a);
            }

            using (Database.UODataContext writedb = new Database.UODataContext())
            {
               Database.LinqExtension.Truncate(writedb.Mobiles); //drop mobiles table
               Database.LinqExtension.Truncate(writedb.Skills); //drop skills table
                Database.LinqExtension.Truncate(writedb.MobIndexes); //drop skills table
                writedb.BulkInsertAll(mobs); //bulk insert mobs
               writedb.BulkInsertAll(vsk); //bulk insert skillz
                writedb.BulkInsertAll(mindex);
            }


        }


        protected void SaveItems()
        {
            Dictionary<Serial, Item> items = World.Items;

            GenericWriter idx;
            GenericWriter tdb;
            GenericWriter bin;

            if (this.UseSequentialWriters)
            {
                idx = new BinaryFileWriter(World.ItemIndexPath, false);
                tdb = new BinaryFileWriter(World.ItemTypesPath, false);
                bin = new BinaryFileWriter(World.ItemDataPath, true);
            }
            else
            {
                idx = new AsyncWriter(World.ItemIndexPath, false);
                tdb = new AsyncWriter(World.ItemTypesPath, false);
                bin = new AsyncWriter(World.ItemDataPath, true);
            }

            idx.Write((int)items.Count);
            foreach (Item item in items.Values)
            {
                if (item.Decays && item.Parent == null && item.Map != Map.Internal && (item.LastMoved + item.DecayTime) <= DateTime.UtcNow)
                {
                    this._decayQueue.Enqueue(item);
                }

                long start = bin.Position;

                idx.Write((int)item.m_TypeRef);
                idx.Write((int)item.Serial);
                idx.Write((long)start);

                item.Serialize(bin);

                idx.Write((int)(bin.Position - start));

                item.FreeCache();
            }

            tdb.Write((int)World.m_ItemTypes.Count);
            for (int i = 0; i < World.m_ItemTypes.Count; ++i)
                tdb.Write(World.m_ItemTypes[i].FullName);

            idx.Close();
            tdb.Close();
            bin.Close();
        }
        
        protected void SaveItemsSQL()
        {
            Dictionary<Serial, Item> items = World.Items;
            List<Database.Item> itemlist = new List<Database.Item>();
            List<Database.ItemIndex> itemindex = new List<Database.ItemIndex>();


            foreach (Item item in items.Values)
            {

                MemoryStream strim = new MemoryStream();
                GenericWriter bin = new BinaryFileWriter(strim, true);
                if (item.Decays && item.Parent == null && item.Map != Map.Internal && (item.LastMoved + item.DecayTime) <= DateTime.UtcNow)
                {
                    this._decayQueue.Enqueue(item);
                }

                Database.Item t = new Database.Item();

                t.TypeID = item.m_TypeRef;
                t.Serial = item.Serial.Value;
                t.Id = item.Serial.Value;
                t = item.Serialize(t);
                item.Serialize(bin);
                bin.Close();
                t.strim = Convert.ToBase64String(strim.ToArray());
                itemlist.Add(t);
                item.FreeCache();
            }
            using (Database.UODataContext writedb = new Database.UODataContext())
            {
                Database.LinqExtension.Truncate(writedb.Items); //drop items table
                writedb.BulkInsertAll(itemlist); //bulk insert items
            }


            for (int i = 0; i < World.m_ItemTypes.Count; ++i)
            {
                Database.ItemIndex a = new Database.ItemIndex();

                a.ItemTypes = (World.m_ItemTypes[i].FullName);
                a.Id = i;

                itemindex.Add(a);
            }

            using (Database.UODataContext writedb = new Database.UODataContext())
            {
                Database.LinqExtension.Truncate(writedb.ItemIndexes); //drop items table
                writedb.BulkInsertAll(itemindex); //bulk insert items
            }

        }
    

        protected void SaveGuilds()
        {
            GenericWriter idx;
            GenericWriter bin;


                idx = new AsyncWriter(World.GuildIndexPath, false);
                bin = new AsyncWriter(World.GuildDataPath, true);
            

            idx.Write((int)BaseGuild.List.Count);
            foreach (BaseGuild guild in BaseGuild.List.Values)
            {
                long start = bin.Position;

                idx.Write((int)0);//guilds have no typeid
                idx.Write((int)guild.Id);
                idx.Write((long)start);

                guild.Serialize(bin);


                idx.Write((int)(bin.Position - start));
            }

            idx.Close();
            bin.Close();
        }

        protected void SaveData()
        {
            Dictionary<CustomSerial, SaveData> data = World.Data;

            GenericWriter indexWriter;
            GenericWriter typeWriter;
            GenericWriter dataWriter;

            indexWriter = new BinaryFileWriter(World.DataIndexPath, false);
            typeWriter = new BinaryFileWriter(World.DataTypesPath, false);
            dataWriter = new BinaryFileWriter(World.DataBinaryPath, true);


            indexWriter.Write(data.Count);

            foreach (SaveData saveData in data.Values)
            {
                long start = dataWriter.Position;

                indexWriter.Write(saveData._TypeID);
                indexWriter.Write((int)saveData.Serial);
                indexWriter.Write(start);

                saveData.Serialize(dataWriter);



                indexWriter.Write((int)(dataWriter.Position - start));
            }

            typeWriter.Write(World._DataTypes.Count);

            for (int i = 0; i < World._DataTypes.Count; ++i)
                typeWriter.Write(World._DataTypes[i].FullName);

            indexWriter.Close();
            typeWriter.Close();
            dataWriter.Close();
        }

    }
 

}

