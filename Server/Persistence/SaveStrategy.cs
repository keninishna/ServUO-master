using System;

namespace Server
{
    public abstract class SaveStrategy
    {
        public abstract string Name { get; }
        public static SaveStrategy Acquire()
        {

            if (Core.MultiProcessor)
            {
                int processorCount = Core.ProcessorCount;

#if DynamicSaveStrategy
                if (processorCount > 2)
                {
                            if (Core.UseSQL)
            {
                return new SQL();
            }else{
                    return new DynamicSaveStrategy();
                }
                }
#else
                if (processorCount > 16)
                {
                    if (Core.UseSQL)
                    {
                        return new SQL();
                    }
                    else {
                        return new ParallelSaveStrategy(processorCount);
                    }
                }
#endif
                else
                {
                    if (Core.UseSQL)
                    {
                        return new SQL();
                    }
                    else {
                        return new DualSaveStrategy();
                    }
                }
            }
            else
            {
                if (Core.UseSQL)
                {
                    return new SQL();
                }
                else {
                    return new StandardSaveStrategy();
                }
            }
        }

        public abstract void Save(SaveMetrics metrics, bool permitBackgroundWrite);

        public abstract void ProcessDecay();
    }
}