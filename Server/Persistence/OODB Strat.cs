using System;
using System.Threading;

namespace Server
{
    public sealed class OOBStrat : StandardSaveStrategy
    {
        public OOBStrat()
        {
        }

        public override string Name
        {
            get
            {
                return "OOB";
            }
        }
        public override void Save(SaveMetrics metrics, bool permitBackgroundWrite)
        {
            this.PermitBackgroundWrite = permitBackgroundWrite;

            Thread saveThread = new Thread(delegate ()
            {
                this.SaveItems(metrics);
            });

            saveThread.Name = "Item Save Subset";
            saveThread.Start();

            this.SaveMobiles(metrics);
            this.SaveGuilds(metrics);
            this.SaveData(metrics);

            saveThread.Join();

            if (permitBackgroundWrite && this.UseSequentialWriters)	//If we're permitted to write in the background, but we don't anyways, then notify.
                World.NotifyDiskWriteComplete();
        }
    }
}