using System;
using System.Collections.Generic;
using CustomsFramework;
using Server.Guilds;
using Db4objects.Db4o;

namespace Server
{
    public class OODBSave : SaveStrategy
    {
        public static SaveOption SaveType = SaveOption.Normal;
        private readonly Queue<Item> _decayQueue;
        private bool _permitBackgroundWrite;
        public OODBSave()
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
                return "OODB";
            }
        }
        protected bool PermitBackgroundWrite
        {
            get
            {
                return this._permitBackgroundWrite;
            }
            set
            {
                this._permitBackgroundWrite = value;
            }
        }

        public override void Save(SaveMetrics metrics, bool permitBackgroundWrite)
        {
            this._permitBackgroundWrite = permitBackgroundWrite;

            this.SaveMobiles(metrics);
            this.SaveItems(metrics);
            this.SaveGuilds(metrics);
            this.SaveData(metrics);
            World.NotifyDiskWriteComplete();
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

        protected void SaveMobiles(SaveMetrics metrics)
        {
            using (IObjectContainer db = Db4oFactory.OpenFile(World.DBPath))
            {
                db.Store(World.Mobiles);
                db.Commit();
                db.Close();
            }


        }

        protected void SaveItems(SaveMetrics metrics)
        {
            using (IObjectContainer db = Db4oFactory.OpenFile(World.DBPath))
            {
                db.Store(World.Items);
                db.Commit();
                db.Close();
            }

        }

        protected void SaveGuilds(SaveMetrics metrics)
        {

            using (IObjectContainer db = Db4oFactory.OpenFile(World.DBPath))
            {
                db.Store(BaseGuild.List.Values);
                db.Commit();
                db.Close();
            }
        }

        protected void SaveData(SaveMetrics metrics)
        {
            using (IObjectContainer db = Db4oFactory.OpenFile(World.DBPath))
            {
                db.Store(World.Data);
                db.Commit();
                db.Close();
            }

        }
    }
}