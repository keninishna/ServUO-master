using System;
using System.Collections.Generic;
using CustomsFramework;
using Server.Guilds;
using System.IO;
using System.Data.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Diagnostics;

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
            using (Database.UODataContext writedb = new Database.UODataContext(Core.SQLConnect))
            {
                if (!(writedb.DatabaseExists()))
                {
                    Console.WriteLine("No SQL db found, creating...");
                    writedb.CreateDatabase();
                    writedb.Dispose();
                }
            }

            if (permitBackgroundWrite)
            {
                Parallel.Invoke(() =>
                {
                    this.SaveMobiles();
                }, () =>
                {
                    this.SaveItemsSQL();
                }, () =>
                {
                    this.SaveGuildsSQL();
                }, () =>
                {
                    this.SaveDataSQL();
                });
                Console.WriteLine("Save Complete.");
                World.BuffItems.Clear();
                World.BuffMobiles.Clear();
                World.BuffGuild.Clear();
            }
            else
            {
                Stopwatch watch = Stopwatch.StartNew();
                World.BuffMobiles = new List<Mobile>();
                World.BuffItems = new List<Item>();
                World.BuffGuild = new List<BaseGuild>();
                World.BuffSaveData = new List<CustomsFramework.SaveData>();
                Console.WriteLine("");

                Parallel.Invoke(() =>
                {
                    foreach (Mobile m in World.Mobiles.Values)
                    {
                        World.BuffMobiles.Add(m);
                    }
                }, () =>
                {
                    foreach (Item i in World.Items.Values)
                    {
                        World.BuffItems.Add(i);
                    }
                }, () =>
                {
                    foreach (BaseGuild g in BaseGuild.List.Values)
                    {
                        World.BuffGuild.Add(g);
                    }
                }, () =>
                {
                    foreach (SaveData g in World.Data.Values)
                    {
                        World.BuffSaveData.Add(g);
                    }
                });
                Console.WriteLine("Buffer created: " + watch.Elapsed.TotalSeconds);
            }

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
            Stopwatch watch = Stopwatch.StartNew();
            Dictionary<Serial, Mobile> mobiles = World.Mobiles;
            List<Database.Mobile> mobs = new List<Database.Mobile>();
            List<Database.Skill> vsk = new List<Database.Skill>();
            List<Database.MobIndex> mindex = new List<Database.MobIndex>();
            int skillid = 0;

            foreach (Mobile m in World.BuffMobiles)
            {
                var typename = m.GetType();

                Database.Mobile v = new Database.Mobile();

                for (int i = 0; i < m.Skills.Length; i++)
                {
                    if (m.Skills[i] != null && m.Skills[i].Base != 0)
                    {
                        Database.Skill vskill = new Database.Skill();
                        skillid++;
                        vskill.Id = skillid;
                        vskill.Base = ((double)m.Skills[i].Base) * 10;
                        vskill.Cap = (int)m.Skills[i].Cap;
                        vskill.Lock = (byte)m.Skills[i].Lock;
                        vskill.Name = m.Skills[i].Name;
                        vskill.Parent = m.Serial.Value;
                        vsk.Add(vskill);
                    }
                }


                foreach (Item mitem in m.Items)
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
                if (m.Corpse != null)
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
                if (m.Map != null) v.m_Map = (byte)m.Map.MapIndex;
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
                if (m.LastDexGain < DateTime.UtcNow)
                {
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
                if (m.GuildFealty != null)
                {
                    v.m_GuildFealty = m.GuildFealty.Serial.Value;
                }
                if (m.Guild != null)
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
                if (m.LogoutMap != null)
                {
                    v.m_LogoutMap = (byte)m.LogoutMap.MapIndex;
                }
                else
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
                strim.Close();
                mobs.Add(v);
                m.FreeCache();
            }

            for (int i = 0; i < World.m_MobileTypes.Count; ++i)
            {

                Database.MobIndex a = new Database.MobIndex();

                a.MobTypes = (World.m_MobileTypes[i].FullName);
                a.Id = i;
                mindex.Add(a);
            }

            using (Database.UODataContext writedb = new Database.UODataContext(Core.SQLConnect))
            {
                Database.LinqExtension.Truncate(writedb.Mobiles); //drop mobiles table
                Database.LinqExtension.Truncate(writedb.Skills); //drop skills table
                Database.LinqExtension.Truncate(writedb.MobIndexes); //drop mobile index table
                writedb.BulkInsertAll(mobs); //bulk insert mobs
                writedb.BulkInsertAll(vsk); //bulk insert skillz
                writedb.BulkInsertAll(mindex);
            }
            watch.Stop();
            Console.WriteLine("SQL mobile data created: " + watch.Elapsed.TotalSeconds);
        }


        protected void SaveItemsSQL()
        {
            Stopwatch watch = Stopwatch.StartNew();
            Dictionary<Serial, Item> items = World.Items;
            List<Database.Item> itemlist = new List<Database.Item>();
            List<Database.ItemIndex> itemindex = new List<Database.ItemIndex>();

            int itemCount = items.Count;


            foreach (Item item in World.BuffItems)
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
                strim.Close();
                itemlist.Add(t);
                item.FreeCache();
            }

            for (int i = 0; i < World.m_ItemTypes.Count; ++i)
            {
                Database.ItemIndex a = new Database.ItemIndex();

                a.ItemTypes = (World.m_ItemTypes[i].FullName);
                a.Id = i;

                itemindex.Add(a);
            }

                using (Database.UODataContext writedb = new Database.UODataContext(Core.SQLConnect))
            {
                Database.LinqExtension.Truncate(writedb.ItemIndexes); //drop items table
                Database.LinqExtension.Truncate(writedb.Items); //drop items table
                writedb.BulkInsertAll(itemindex); //bulk insert itemindex
                writedb.BulkInsertAll(itemlist); //bulk insert items
            }

            watch.Stop();
            Console.WriteLine("SQL Item data created: " + watch.Elapsed.TotalSeconds);
        }

        protected void SaveGuildsSQL()
        {
            Stopwatch watch = Stopwatch.StartNew();
            List<Database.Guild> GuildList = new List<Database.Guild>();
            List<Database.GuildWar> WarList = new List<Database.GuildWar>();
            List<Database.GuildAlliance> GuildAlliances = new List<Database.GuildAlliance>();
            int index = 0;

            foreach (BaseGuild guild in World.BuffGuild)
            {
                List<Database.GuildWar> gwl = new List<Database.GuildWar>();
                Database.Guild g = new Database.Guild();
                Database.GuildAlliance ga = new Database.GuildAlliance();
                g.Id = guild.Id;

                g = guild.Serialize(g);
                GuildList.Add(g);
                gwl = guild.Serialize(gwl, ref index);
                if (gwl != null) WarList.AddRange(gwl);
                ga = guild.Serialize(ga);
                if (ga != null) GuildAlliances.Add(ga);
            }

            using (Database.UODataContext writedb = new Database.UODataContext(Core.SQLConnect))
            {
                Database.LinqExtension.Truncate(writedb.GuildAlliances); //drop items table
                Database.LinqExtension.Truncate(writedb.Guilds); //drop items table
                Database.LinqExtension.Truncate(writedb.GuildWars); //drop items table
                writedb.BulkInsertAll(GuildAlliances); //bulk insert items
                writedb.BulkInsertAll(GuildList); //bulk insert items
                writedb.BulkInsertAll(WarList); //bulk insert items
            }
            watch.Stop();
            Console.WriteLine("SQL Guild data created: " + watch.Elapsed.TotalSeconds);
        }

        protected void SaveDataSQL()
        {
            Dictionary<CustomSerial, SaveData> data = World.Data;
            List<Database.SaveData> s = new List<Database.SaveData>();
            List<Database.SaveDataIndex> si = new List<Database.SaveDataIndex>();

            foreach (SaveData saveData in World.BuffSaveData)
            {
                MemoryStream stream = new MemoryStream();
                GenericWriter bin = new BinaryFileWriter(stream, true);

                Database.SaveData sd = new Database.SaveData();
                sd.TypeID = saveData._TypeID;
                sd.Serial = (int)saveData.Serial;
                sd.Id = (int)saveData.Serial;
                saveData.Serialize(sd);
                saveData.Serialize(bin);
                bin.Close();
                sd.Serialized = Convert.ToBase64String(stream.ToArray());
                s.Add(sd);
                stream.Close();
            }

            for (int i = 0; i < World._DataTypes.Count; ++i)
            {
                Database.SaveDataIndex sdi = new Database.SaveDataIndex();
                sdi.DataTypes = World._DataTypes[i].FullName;
                sdi.Id = i;
                si.Add(sdi);
            }

            using (Database.UODataContext writedb = new Database.UODataContext(Core.SQLConnect))
            {
                Database.LinqExtension.Truncate(writedb.SaveDataIndexes); //drop items table
                Database.LinqExtension.Truncate(writedb.SaveDatas); //drop items table
                writedb.BulkInsertAll(si); //bulk insert items
                writedb.BulkInsertAll(s); //bulk insert items
            }

        }
    }
 

}

