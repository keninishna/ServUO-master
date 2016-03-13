#region Header
// **********
// ServUO - World.cs
// **********
#endregion

#region References
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

using CustomsFramework;

using Server.Guilds;
using Server.Network;
using System.Data.Linq;
using System.Linq;
using System.Threading.Tasks;
using System.Drawing;
#endregion

namespace Server
{
	public static class World
	{
		private static Dictionary<Serial, Mobile> m_Mobiles;
		private static Dictionary<Serial, Item> m_Items;
		private static Dictionary<CustomSerial, SaveData> _Data;

        private static bool m_Loading;
		private static bool m_Loaded;

		private static bool m_Saving;
		private static readonly ManualResetEvent m_DiskWriteHandle = new ManualResetEvent(true);

		private static Queue<IEntity> _addQueue, _deleteQueue;
		private static Queue<ICustomsEntity> _CustomsAddQueue, _CustomsDeleteQueue;

		public static bool Saving { get { return m_Saving; } }
		public static bool Loaded { get { return m_Loaded; } }
		public static bool Loading { get { return m_Loading; } }


        public static readonly string MobileIndexPath = Path.Combine("Saves/Mobiles/", "Mobiles.idx");
		public static readonly string MobileTypesPath = Path.Combine("Saves/Mobiles/", "Mobiles.tdb");
		public static readonly string MobileDataPath = Path.Combine("Saves/Mobiles/", "Mobiles.bin");

		public static readonly string ItemIndexPath = Path.Combine("Saves/Items/", "Items.idx");
		public static readonly string ItemTypesPath = Path.Combine("Saves/Items/", "Items.tdb");
		public static readonly string ItemDataPath = Path.Combine("Saves/Items/", "Items.bin");

		public static readonly string GuildIndexPath = Path.Combine("Saves/Guilds/", "Guilds.idx");
		public static readonly string GuildDataPath = Path.Combine("Saves/Guilds/", "Guilds.bin");

		public static readonly string DataIndexPath = Path.Combine("Saves/Customs/", "SaveData.idx");
		public static readonly string DataTypesPath = Path.Combine("Saves/Customs/", "SaveData.tdb");
		public static readonly string DataBinaryPath = Path.Combine("Saves/Customs/", "SaveData.bin");

        public static void PrintProperties(object obj, int indent)
        {

            if (obj == null) return;
            string indentString = new string(' ', indent);
            Type objType = obj.GetType();
            PropertyInfo[] properties = objType.GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.GetValue(obj, null) != null)
                {
                    object propValue = property.GetValue(obj, null);

                    if (property.PropertyType.IsPrimitive || property.PropertyType == typeof(string))
                        Console.WriteLine("{0}{1}: {2}", indentString, property.Name, propValue);
                    else if (typeof(IEnumerable<object>).IsAssignableFrom(property.PropertyType))
                    {
                        Console.WriteLine("{0}{1}:", indentString, property.Name);
                        IEnumerable<object> enumerable = (IEnumerable<object>)propValue;
                        foreach (object child in enumerable)
                            PrintProperties(child, indent + 2);
                    }
                    else
                    {
                        Console.WriteLine("{0}{1}:", indentString, property.Name);
                        //PrintProperties(propValue, indent + 2);
                    }
                }
                else
                {
                    return;
                }
            }
        }

        public static void NotifyDiskWriteComplete()
		{
			if (m_DiskWriteHandle.Set())
			{
				Console.WriteLine("Closing Save Files. ");
			}
		}


		public static void WaitForWriteCompletion()
		{
			m_DiskWriteHandle.WaitOne();
		}


        public static Dictionary<Serial, Mobile> Mobiles { get { return m_Mobiles; } }
        public static Dictionary<CustomSerial, SaveData> Data { get { return _Data; } }
        public static Dictionary<Serial, Item> Items { get { return m_Items; } }

        public static List<Mobile> BuffMobiles { get; set; }
        public static List<Item> BuffItems { get; set; }
        public static List<BaseGuild> BuffGuild { get; set; }
        public static List<SaveData> BuffSaveData { get; set; }


        public static bool OnDelete(IEntity entity)
		{
			if (m_Saving || m_Loading)
			{
				if (m_Saving)
				{
					AppendSafetyLog("delete", entity);
				}

				_deleteQueue.Enqueue(entity);

				return false;
			}

			return true;
		}

		public static bool OnDelete(ICustomsEntity entity)
		{
			if (m_Saving || m_Loading)
			{
				if (m_Saving)
				{
					AppendSafetyLog("delete", entity);
				}

				_CustomsDeleteQueue.Enqueue(entity);

				return false;
			}

			return true;
		}

		public static void Broadcast(int hue, bool ascii, string text)
		{
			Packet p;

			if (ascii)
			{
				p = new AsciiMessage(Serial.MinusOne, -1, MessageType.Regular, hue, 3, "System", text);
			}
			else
			{
				p = new UnicodeMessage(Serial.MinusOne, -1, MessageType.Regular, hue, 3, "ENU", "System", text);
			}

			var list = NetState.Instances;

			p.Acquire();

			for (int i = 0; i < list.Count; ++i)
			{
				if (list[i].Mobile != null)
				{
					list[i].Send(p);
				}
			}

			p.Release();

			NetState.FlushAll();
		}

		public static void Broadcast(int hue, bool ascii, string format, params object[] args)
		{
			Broadcast(hue, ascii, String.Format(format, args));
		}

		private interface IEntityEntry
		{
			Serial Serial { get; }
			int TypeID { get; }
			long Position { get; }
			int Length { get; }
		}

		private sealed class GuildEntry : IEntityEntry
		{
			private readonly BaseGuild m_Guild;
			private readonly long m_Position;
			private readonly int m_Length;

			public BaseGuild Guild { get { return m_Guild; } }

			public Serial Serial { get { return m_Guild == null ? 0 : m_Guild.Id; } }

			public int TypeID { get { return 0; } }

			public long Position { get { return m_Position; } }

			public int Length { get { return m_Length; } }

			public GuildEntry(BaseGuild g, long pos, int length)
			{
				m_Guild = g;
				m_Position = pos;
				m_Length = length;
			}
		}

		private sealed class ItemEntry : IEntityEntry
		{
			private readonly Item m_Item;
			private readonly int m_TypeID;
			private readonly string m_TypeName;
			private readonly long m_Position;
			private readonly int m_Length;

			public Item Item { get { return m_Item; } }

			public Serial Serial { get { return m_Item == null ? Serial.MinusOne : m_Item.Serial; } }

			public int TypeID { get { return m_TypeID; } }

			public string TypeName { get { return m_TypeName; } }

			public long Position { get { return m_Position; } }

			public int Length { get { return m_Length; } }

			public ItemEntry(Item item, int typeID, string typeName, long pos, int length)
			{
				m_Item = item;
				m_TypeID = typeID;
				m_TypeName = typeName;
				m_Position = pos;
				m_Length = length;
			}
		}

		private sealed class MobileEntry : IEntityEntry
		{
			private readonly Mobile m_Mobile;
			private readonly int m_TypeID;
			private readonly string m_TypeName;
			private readonly long m_Position;
			private readonly int m_Length;

			public Mobile Mobile { get { return m_Mobile; } }

			public Serial Serial { get { return m_Mobile == null ? Serial.MinusOne : m_Mobile.Serial; } }

			public int TypeID { get { return m_TypeID; } }

			public string TypeName { get { return m_TypeName; } }

			public long Position { get { return m_Position; } }

			public int Length { get { return m_Length; } }

			public MobileEntry(Mobile mobile, int typeID, string typeName, long pos, int length)
			{
				m_Mobile = mobile;
				m_TypeID = typeID;
				m_TypeName = typeName;
				m_Position = pos;
				m_Length = length;
			}
		}

		public sealed class DataEntry : ICustomsEntry
		{
			private readonly SaveData _Data;
			private readonly int _TypeID;
			private readonly string _TypeName;
			private readonly long _Position;
			private readonly int _Length;

			public DataEntry(SaveData data, int typeID, string typeName, long pos, int length)
			{
				_Data = data;
				_TypeID = typeID;
				_TypeName = typeName;
				_Position = pos;
				_Length = length;
			}

			public SaveData Data { get { return _Data; } }
			public CustomSerial Serial { get { return _Data == null ? CustomSerial.MinusOne : _Data.Serial; } }
			public int TypeID { get { return _TypeID; } }
			public string TypeName { get { return _TypeName; } }
			public long Position { get { return _Position; } }
			public int Length { get { return _Length; } }
		}

		private static string m_LoadingType;

		public static string LoadingType { get { return m_LoadingType; } }

		private static readonly Type[] m_SerialTypeArray = new Type[1] {typeof(Serial)};
		private static readonly Type[] _CustomSerialTypeArray = new Type[1] {typeof(CustomSerial)};

		private static List<Tuple<ConstructorInfo, string>> ReadTypes(BinaryReader tdbReader)
		{
			int count = tdbReader.ReadInt32();

			var types = new List<Tuple<ConstructorInfo, string>>(count);

			for (int i = 0; i < count; ++i)
			{
				string typeName = tdbReader.ReadString();

				Type t = ScriptCompiler.FindTypeByFullName(typeName);

				if (t == null)
				{
					Console.WriteLine("failed");

					if (!Core.Service)
					{
						Console.WriteLine("Error: Type '{0}' was not found. Delete all of those types? (y/n)", typeName);

						if (Console.ReadKey(true).Key == ConsoleKey.Y)
						{
							types.Add(null);
							Console.Write("World: Loading...");
							continue;
						}

						Console.WriteLine("Types will not be deleted. An exception will be thrown.");
					}
					else
					{
						Console.WriteLine("Error: Type '{0}' was not found.", typeName);
					}

					throw new Exception(String.Format("Bad type '{0}'", typeName));
				}

				ConstructorInfo ctor = t.GetConstructor(m_SerialTypeArray);
				ConstructorInfo cctor = t.GetConstructor(_CustomSerialTypeArray);

				if (ctor != null)
				{
					types.Add(new Tuple<ConstructorInfo, string>(ctor, typeName));
				}
				else if (cctor != null)
				{
					types.Add(new Tuple<ConstructorInfo, string>(cctor, typeName));
				}
				else
				{
					throw new Exception(String.Format("Type '{0}' does not have a serialization constructor", t));
				}
			}

			return types;
		}



        public static void Load()
		{
			if (m_Loaded)
			{
				return;
			}

			m_Loaded = true;
			m_LoadingType = null;

			Utility.PushColor(ConsoleColor.Yellow);
			Console.WriteLine("World: Loading...");
			Utility.PopColor();

			Stopwatch watch = Stopwatch.StartNew();

			m_Loading = true;

			_addQueue = new Queue<IEntity>();
			_deleteQueue = new Queue<IEntity>();
			_CustomsAddQueue = new Queue<ICustomsEntity>();
			_CustomsDeleteQueue = new Queue<ICustomsEntity>();

			int mobileCount = 0, itemCount = 0, guildCount = 0, dataCount = 0;

			var ctorArgs = new object[1];

			var items = new List<ItemEntry>();
			var mobiles = new List<MobileEntry>();
			var guilds = new List<GuildEntry>();
			var data = new List<DataEntry>();

			if (File.Exists(MobileIndexPath) && File.Exists(MobileTypesPath))
			{
				using (FileStream idx = new FileStream(MobileIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					BinaryReader idxReader = new BinaryReader(idx);

					using (FileStream tdb = new FileStream(MobileTypesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
					{
						BinaryReader tdbReader = new BinaryReader(tdb);

						var types = ReadTypes(tdbReader);

						mobileCount = idxReader.ReadInt32();

						m_Mobiles = new Dictionary<Serial, Mobile>(mobileCount);

						for (int i = 0; i < mobileCount; ++i)
						{
							int typeID = idxReader.ReadInt32();
							int serial = idxReader.ReadInt32();
							long pos = idxReader.ReadInt64();
							int length = idxReader.ReadInt32();

							var objs = types[typeID];

							if (objs == null)
							{
								continue;
							}

							Mobile m = null;
							ConstructorInfo ctor = objs.Item1;
							string typeName = objs.Item2;

							try
							{
								ctorArgs[0] = (Serial)serial;
								m = (Mobile)(ctor.Invoke(ctorArgs));
							}
							catch
							{ }

							if (m != null)
							{
								mobiles.Add(new MobileEntry(m, typeID, typeName, pos, length));
								AddMobile(m);
							}
						}

						tdbReader.Close();
					}

					idxReader.Close();
				}
			}
			else
			{
				m_Mobiles = new Dictionary<Serial, Mobile>();
			}

			if (File.Exists(ItemIndexPath) && File.Exists(ItemTypesPath))
			{
				using (FileStream idx = new FileStream(ItemIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					BinaryReader idxReader = new BinaryReader(idx);

					using (FileStream tdb = new FileStream(ItemTypesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
					{
						BinaryReader tdbReader = new BinaryReader(tdb);

						var types = ReadTypes(tdbReader);

						itemCount = idxReader.ReadInt32();

						m_Items = new Dictionary<Serial, Item>(itemCount);

						for (int i = 0; i < itemCount; ++i)
						{
							int typeID = idxReader.ReadInt32();
							int serial = idxReader.ReadInt32();
							long pos = idxReader.ReadInt64();
							int length = idxReader.ReadInt32();

							var objs = types[typeID];

							if (objs == null)
							{
								continue;
							}

							Item item = null;
							ConstructorInfo ctor = objs.Item1;
							string typeName = objs.Item2;

							try
							{
								ctorArgs[0] = (Serial)serial;
								item = (Item)(ctor.Invoke(ctorArgs));
							}
							catch
							{ }

							if (item != null)
							{
								items.Add(new ItemEntry(item, typeID, typeName, pos, length));
								AddItem(item);
							}
						}

						tdbReader.Close();
					}

					idxReader.Close();
				}
			}
			else
			{
				m_Items = new Dictionary<Serial, Item>();
			}

			if (File.Exists(GuildIndexPath))
			{
				using (FileStream idx = new FileStream(GuildIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					BinaryReader idxReader = new BinaryReader(idx);

					guildCount = idxReader.ReadInt32();

					CreateGuildEventArgs createEventArgs = new CreateGuildEventArgs(-1);
					for (int i = 0; i < guildCount; ++i)
					{
						idxReader.ReadInt32(); //no typeid for guilds
						int id = idxReader.ReadInt32();
						long pos = idxReader.ReadInt64();
						int length = idxReader.ReadInt32();

						createEventArgs.Id = id;
						EventSink.InvokeCreateGuild(createEventArgs);
						BaseGuild guild = createEventArgs.Guild;
						if (guild != null)
						{
							guilds.Add(new GuildEntry(guild, pos, length));
						}
					}

					idxReader.Close();
				}
			}

			if (File.Exists(DataIndexPath) && File.Exists(DataTypesPath))
			{
				using (FileStream indexStream = new FileStream(DataIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					BinaryReader indexReader = new BinaryReader(indexStream);

					using (FileStream typeStream = new FileStream(DataTypesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
					{
						BinaryReader typeReader = new BinaryReader(typeStream);

						var types = ReadTypes(typeReader);

						dataCount = indexReader.ReadInt32();
						_Data = new Dictionary<CustomSerial, SaveData>(dataCount);

						for (int i = 0; i < dataCount; ++i)
						{
							int typeID = indexReader.ReadInt32();
							int serial = indexReader.ReadInt32();
							long pos = indexReader.ReadInt64();
							int length = indexReader.ReadInt32();

							var objects = types[typeID];

							if (objects == null)
							{
								continue;
							}

							SaveData saveData = null;
							ConstructorInfo ctor = objects.Item1;
							string typeName = objects.Item2;

							try
							{
								ctorArgs[0] = (CustomSerial)serial;
								saveData = (SaveData)(ctor.Invoke(ctorArgs));
							}
							catch
							{
								Utility.PushColor(ConsoleColor.Red);
								Console.WriteLine("Error loading {0}, Serial: {1}", typeName, serial);
								Utility.PopColor();
							}

							if (saveData != null)
							{
								data.Add(new DataEntry(saveData, typeID, typeName, pos, length));
								AddData(saveData);
							}
						}

						typeReader.Close();
					}

					indexReader.Close();
				}
			}
			else
			{
				_Data = new Dictionary<CustomSerial, SaveData>();
			}

			bool failedMobiles = false, failedItems = false, failedGuilds = false, failedData = false;
			Type failedType = null;
			Serial failedSerial = Serial.Zero;
			CustomSerial failedCustomSerial = CustomSerial.Zero;
			Exception failed = null;
			int failedTypeID = 0;

			if (File.Exists(MobileDataPath))
			{
				using (FileStream bin = new FileStream(MobileDataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					BinaryFileReader reader = new BinaryFileReader(new BinaryReader(bin));

					for (int i = 0; i < mobiles.Count; ++i)
					{
						MobileEntry entry = mobiles[i];
						Mobile m = entry.Mobile;

						if (m != null)
						{
							reader.Seek(entry.Position, SeekOrigin.Begin);

							try
							{
								m_LoadingType = entry.TypeName;
								m.Deserialize(reader);

								if (reader.Position != (entry.Position + entry.Length))
								{
									throw new Exception(String.Format("***** Bad serialize on {0} *****", m.GetType()));
								}
							}
							catch (Exception e)
							{
								mobiles.RemoveAt(i);

								failed = e;
								failedMobiles = true;
								failedType = m.GetType();
								failedTypeID = entry.TypeID;
								failedSerial = m.Serial;

								break;
							}
						}
					}

					reader.Close();
				}
			}

			if (!failedMobiles && File.Exists(ItemDataPath))
			{
				using (FileStream bin = new FileStream(ItemDataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					BinaryFileReader reader = new BinaryFileReader(new BinaryReader(bin));

					for (int i = 0; i < items.Count; ++i)
					{
						ItemEntry entry = items[i];
						Item item = entry.Item;

						if (item != null)
						{
							reader.Seek(entry.Position, SeekOrigin.Begin);

							try
							{
								m_LoadingType = entry.TypeName;
								item.Deserialize(reader);

								if (reader.Position != (entry.Position + entry.Length))
								{
									throw new Exception(String.Format("***** Bad serialize on {0} *****", item.GetType()));
								}
							}
							catch (Exception e)
							{
								items.RemoveAt(i);

								failed = e;
								failedItems = true;
								failedType = item.GetType();
								failedTypeID = entry.TypeID;
								failedSerial = item.Serial;

								break;
							}
						}
					}

					reader.Close();
				}
			}

			m_LoadingType = null;

			if (!failedMobiles && !failedItems && File.Exists(GuildDataPath))
			{
				using (FileStream bin = new FileStream(GuildDataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					BinaryFileReader reader = new BinaryFileReader(new BinaryReader(bin));

					for (int i = 0; i < guilds.Count; ++i)
					{
						GuildEntry entry = guilds[i];
						BaseGuild g = entry.Guild;

						if (g != null)
						{
							reader.Seek(entry.Position, SeekOrigin.Begin);

							try
							{
								g.Deserialize(reader);

								if (reader.Position != (entry.Position + entry.Length))
								{
									throw new Exception(String.Format("***** Bad serialize on Guild {0} *****", g.Id));
								}
							}
							catch (Exception e)
							{
								guilds.RemoveAt(i);

								failed = e;
								failedGuilds = true;
								failedType = typeof(BaseGuild);
								failedTypeID = g.Id;
								failedSerial = g.Id;

								break;
							}
						}
					}

					reader.Close();
				}
			}

           
            if (!failedMobiles && !failedItems && !failedGuilds && File.Exists(DataBinaryPath))
			{
				using (FileStream stream = new FileStream(DataBinaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					BinaryFileReader reader = new BinaryFileReader(new BinaryReader(stream));

					for (int i = 0; i < data.Count; ++i)
					{
						DataEntry entry = data[i];
						SaveData saveData = entry.Data;

						if (saveData != null)
						{
							reader.Seek(entry.Position, SeekOrigin.Begin);

							try
							{
								m_LoadingType = entry.TypeName;
								saveData.Deserialize(reader);

								if (reader.Position != (entry.Position + entry.Length))
								{
									throw new Exception(String.Format("***** Bad serialize on {0} *****", saveData.GetType()));
								}
							}
							catch (Exception error)
							{
								data.RemoveAt(i);

								failed = error;
								failedData = true;
								failedType = saveData.GetType();
								failedTypeID = entry.TypeID;
								failedCustomSerial = saveData.Serial;

								break;
							}
						}
					}

					reader.Close();
				}
			}

			if (failedItems || failedMobiles || failedGuilds || failedData)
			{
				Utility.PushColor(ConsoleColor.Red);
				Console.WriteLine("An error was encountered while loading a saved object");
				Utility.PopColor();

				Console.WriteLine(" - Type: {0}", failedType);

				if (failedSerial != Serial.Zero)
				{
					Console.WriteLine(" - Serial: {0}", failedSerial);
				}
				else
				{
					Console.WriteLine(" - Serial: {0}", failedCustomSerial);
				}

				if (!Core.Service)
				{
					Console.WriteLine("Delete the object? (y/n)");

					if (Console.ReadKey(true).Key == ConsoleKey.Y)
					{
						if (failedType != typeof(BaseGuild))
						{
							Console.WriteLine("Delete all objects of that type? (y/n)");

							if (Console.ReadKey(true).Key == ConsoleKey.Y)
							{
								if (failedMobiles)
								{
									for (int i = 0; i < mobiles.Count;)
									{
										if (mobiles[i].TypeID == failedTypeID)
										{
											mobiles.RemoveAt(i);
										}
										else
										{
											++i;
										}
									}
								}
								else if (failedItems)
								{
									for (int i = 0; i < items.Count;)
									{
										if (items[i].TypeID == failedTypeID)
										{
											items.RemoveAt(i);
										}
										else
										{
											++i;
										}
									}
								}
								else if (failedData)
								{
									for (int i = 0; i < data.Count;)
									{
										if (data[i].TypeID == failedTypeID)
										{
											data.RemoveAt(i);
										}
										else
										{
											++i;
										}
									}
								}
							}
						}

						SaveIndex(mobiles, MobileIndexPath);
						SaveIndex(items, ItemIndexPath);
						SaveIndex(guilds, GuildIndexPath);
						SaveIndex(DataIndexPath, data);
					}

					Console.WriteLine("After pressing return an exception will be thrown and the server will terminate.");
					Console.ReadLine();
				}
				else
				{
					Utility.PushColor(ConsoleColor.Red);
					Console.WriteLine("An exception will be thrown and the server will terminate.");
					Utility.PopColor();
				}

				throw new Exception(
					String.Format(
						"Load failed (items={0}, mobiles={1}, guilds={2}, data={3}, type={4}, serial={5})",
						failedItems,
						failedMobiles,
						failedGuilds,
						failedData,
						failedType,
						(failedSerial != Serial.Zero ? failedSerial.ToString() : failedCustomSerial.ToString())),
					failed);
			}

			EventSink.InvokeWorldLoad();

			m_Loading = false;

			ProcessSafetyQueues();

			foreach (Item item in m_Items.Values)
			{
				if (item.Parent == null)
				{
					item.UpdateTotals();
				}

				item.ClearProperties();
			}

			foreach (Mobile m in m_Mobiles.Values)
			{
				m.UpdateRegion(); // Is this really needed?
				m.UpdateTotals();

				m.ClearProperties();
			}

			foreach (SaveData saveData in _Data.Values)
			{
				saveData.Prep();
			}

			watch.Stop();

			Utility.PushColor(ConsoleColor.Green);
			Console.WriteLine(
                "...done ({1} items, {2} mobiles, {3} customs) ({0:F2} seconds)",
                watch.Elapsed.TotalSeconds,
				m_Items.Count,
				m_Mobiles.Count,
				_Data.Count);
			Utility.PopColor();
		}

        private static List<ItemEntry> ReadItemTypesSQL(int itemCount, List<Database.Item> it)
        {
            Stopwatch watch = Stopwatch.StartNew();
            List<Database.ItemIndex> ittypes = new List<Database.ItemIndex>();
            using (Database.UODataContext readdb = new Database.UODataContext())
            {
                var dbitems = (from x in readdb.ItemIndexes select x); //google no help
                if (dbitems != null)
                {
                    foreach (Database.ItemIndex dbitem in dbitems)
                    {
                        ittypes.Add(dbitem);
                    }
                }
             }


            int count = ittypes.Count();
            var types = new List<Tuple<ConstructorInfo, string>>(count);
            string typeName = "";
            for (int i = 0; i < count; ++i)
            {

                typeName = ((Database.ItemIndex)ittypes[i]).ItemTypes;

                Type t = ScriptCompiler.FindTypeByFullName(typeName);

                if (t == null)
                {
                    Console.WriteLine("failboat");

                    if (!Core.Service)
                    {
                        Console.WriteLine("Error: Type '{0}' was not found. Delete all of those types? (y/n)", typeName);

                        if (Console.ReadKey(true).Key == ConsoleKey.Y)
                        {
                            types.Add(null);
                            Console.Write("World: Loading...");
                            continue;
                        }

                        Console.WriteLine("Types will not be deleted. An exception will be thrown.");
                    }
                    else
                    {
                        Console.WriteLine("Error: Type '{0}' was not found.", typeName);
                    }

                    throw new Exception(String.Format("Bad type '{0}'", typeName));
                }

                ConstructorInfo ctor = t.GetConstructor(m_SerialTypeArray);
                ConstructorInfo cctor = t.GetConstructor(_CustomSerialTypeArray);

                if (ctor != null)
                {
                    types.Add(new Tuple<ConstructorInfo, string>(ctor, typeName));
                }
                else if (cctor != null)
                {
                    types.Add(new Tuple<ConstructorInfo, string>(cctor, typeName));
                }
                else
                {
                    throw new Exception(String.Format("Type '{0}' does not have a serialization constructor", t));
                }
            }

            var ctorArgs = new object[1];
            List<ItemEntry> items = new List<ItemEntry>();

            for (int i = 0; i < itemCount; ++i)
            {
                int typeID = (int)it[i].TypeID;
                int serial = (int)it[i].Serial;

                var objs = types[typeID];

                if (objs == null)
                {
                    continue;
                }

                Item item = null;
                ConstructorInfo ctor = objs.Item1;
                typeName = objs.Item2;

                try
                {
                    ctorArgs[0] = (Serial)serial;
                    item = (Item)(ctor.Invoke(ctorArgs));
                }
                catch
                { }

                if (item != null)
                {
                    items.Add(new ItemEntry(item, typeID, typeName, 0, 0));
                    AddItem(item);

                }

            }
            watch.Stop();
            Console.WriteLine("LoadItemTypes: " + watch.Elapsed.TotalSeconds);
            return items;
        }

        private static List<MobileEntry> ReadMobTypesSQL(int mobileCount, List<Database.Mobile> v)
        {
            Stopwatch watch = Stopwatch.StartNew();
            List<Database.MobIndex> mobs = new List<Database.MobIndex>();
            using (Database.UODataContext readdb = new Database.UODataContext())
            {
                var mobindex = (from x in readdb.MobIndexes select x); //google no help

                foreach (Database.MobIndex mob in mobindex)
                {
                    mobs.Add(mob);
                }
            }

            int count = mobs.Count();
            var types = new List<Tuple<ConstructorInfo, string>>(count);
            string typeName = "";

            for (int i = 0; i < count; ++i)
            {

                typeName = ((Database.MobIndex)mobs[i]).MobTypes;

                Type t = ScriptCompiler.FindTypeByFullName(typeName);

                if (t == null)
                {
                    Console.WriteLine("failboat");

                    if (!Core.Service)
                    {
                        Console.WriteLine("Error: Type '{0}' was not found. Delete all of those types? (y/n)", typeName);

                        if (Console.ReadKey(true).Key == ConsoleKey.Y)
                        {
                            types.Add(null);
                            Console.Write("World: Loading...");
                            continue;
                        }

                        Console.WriteLine("Types will not be deleted. An exception will be thrown.");
                    }
                    else
                    {
                        Console.WriteLine("Error: Type '{0}' was not found.", typeName);
                    }

                    throw new Exception(String.Format("Bad type '{0}'", typeName));
                }

                ConstructorInfo ctor = t.GetConstructor(m_SerialTypeArray);
                ConstructorInfo cctor = t.GetConstructor(_CustomSerialTypeArray);

                if (ctor != null)
                {
                    types.Add(new Tuple<ConstructorInfo, string>(ctor, typeName));
                }
                else if (cctor != null)
                {
                    types.Add(new Tuple<ConstructorInfo, string>(cctor, typeName));
                }
                else
                {
                    throw new Exception(String.Format("Type '{0}' does not have a serialization constructor", t));
                }
            }

            
            var ctorArgs = new object[1];
            var mobiles = new List<MobileEntry>();

            for (int i = 0; i < mobileCount; ++i)
            {
                int typeID = (int)v[i].mTypeRef;
                int serial = (int)v[i].Serial;
                int pos = 0;
                int length = 0;

                var objs = types[typeID];

                if (objs == null)
                {
                    continue;
                }

                Mobile m = null;
                ConstructorInfo ctor = objs.Item1;
                typeName = objs.Item2;

                try
                {
                    ctorArgs[0] = (Serial)serial;
                    m = (Mobile)(ctor.Invoke(ctorArgs));
                }
                catch
                {
                }

                if (m != null)
                {
                    mobiles.Add(new MobileEntry(m, typeID, typeName, pos, length));
                    AddMobile(m);
                }
            }
            watch.Stop();
            Console.WriteLine("LoadMobTypes: " + watch.Elapsed.TotalSeconds);
            return mobiles;
        }

        private static List<Database.Mobile> LoadMobsSQL()
        {
            Stopwatch watch = Stopwatch.StartNew();
            List<Database.Mobile> v = new List<Database.Mobile>();
            using (Database.UODataContext readdb = new Database.UODataContext() { CommandTimeout = 30, ObjectTrackingEnabled = false })
            {
                var mobs = (from x in readdb.Mobiles select x); //google no help
                if (mobs != null)
                {
                    foreach (Database.Mobile mob in mobs)
                    {
                        v.Add(mob);
                    }
                }
            }
            watch.Stop();
            Console.WriteLine("LoadMobs: " + watch.Elapsed.TotalSeconds);
            return v;
         }

        private static List<Database.Item> LoadItemsSQL()
        {
            List<Database.Item> it = new List<Database.Item>();
            Stopwatch watch = Stopwatch.StartNew();
            using (Database.UODataContext readdb = new Database.UODataContext() { CommandTimeout = 30, ObjectTrackingEnabled = false })
            {
                var dbitems = (from x in readdb.Items select x); //google no help

                if (dbitems != null)
                {
                    foreach (Database.Item dbitem in dbitems)
                    {
                        it.Add(dbitem);
                    }
                }
                else
                {
                    m_Items = new Dictionary<Serial, Item>();
                }
            }
            watch.Stop();
            Console.WriteLine("LoadItems: " + watch.Elapsed.TotalSeconds);
            return it;
        }

        private static List<Database.Skill> LoadSkillsSQL()
        {
            List<Database.Skill> s = new List<Database.Skill>();
            using (Database.UODataContext readdb = new Database.UODataContext())
            {

                var dbskills = (from x in readdb.Skills select x); //google no help

                foreach (Database.Skill dbskill in dbskills)
                {
                    s.Add(dbskill);
                }
            }
            return s;
        }

        private static List<Database.GuildAlliance> LoadGuildAllianceSQL()
        {
            List<Database.GuildAlliance> s = new List<Database.GuildAlliance>();
            using (Database.UODataContext readdb = new Database.UODataContext())
            {

                var dbGuildAlliances = (from x in readdb.GuildAlliances select x); //google no help

                foreach (Database.GuildAlliance GuildAlliance in dbGuildAlliances)
                {
                    s.Add(GuildAlliance);
                }
            }
            return s;
        }

        private static List<Database.Guild> LoadGuildsSQL()
        {
            List<Database.Guild> s = new List<Database.Guild>();
            using (Database.UODataContext readdb = new Database.UODataContext())
            {

                var dbGuilds = (from x in readdb.Guilds select x); //google no help

                foreach (Database.Guild dbGuild in dbGuilds)
                {
                    s.Add(dbGuild);
                }
            }
            return s;
        }

        private static List<Database.GuildWar> LoadGuildWarsSQL()
        {
            List<Database.GuildWar> s = new List<Database.GuildWar>();
            using (Database.UODataContext readdb = new Database.UODataContext())
            {

                var dbwars = (from x in readdb.GuildWars select x); //google no help

                foreach (Database.GuildWar dbwar in dbwars)
                {
                    s.Add(dbwar);
                }
            }
            return s;
        }

        private static List<Database.SaveData> LoadSaveDataSQL()
        {
            List<Database.SaveData> s = new List<Database.SaveData>();
            using (Database.UODataContext readdb = new Database.UODataContext())
            {

                var dbwars = (from x in readdb.SaveDatas select x); //google no help

                foreach (Database.SaveData dbwar in dbwars)
                {
                    s.Add(dbwar);
                }
            }
            return s;
        }


        private static List<DataEntry> ReadDataTypesSQL(List<Database.SaveData> v)
        {

            List<Database.SaveDataIndex> ittypes = new List<Database.SaveDataIndex>();
            using (Database.UODataContext readdb = new Database.UODataContext())
            {
                var dbitems = (from x in readdb.SaveDataIndexes select x); //google no help
                if (dbitems != null)
                {
                    foreach (Database.SaveDataIndex dbitem in dbitems)
                    {
                        ittypes.Add(dbitem);
                    }
                }
            }


            int count = ittypes.Count();
            var types = new List<Tuple<ConstructorInfo, string>>(count);
            string typeName = "";
            for (int i = 0; i < count; ++i)
            {

                typeName = (ittypes[i]).DataTypes;

                Type t = ScriptCompiler.FindTypeByFullName(typeName);

                if (t == null)
                {
                    Console.WriteLine("failboat");

                    if (!Core.Service)
                    {
                        Console.WriteLine("Types will not be deleted. An exception will be thrown.");
                    }
                    else
                    {
                        Console.WriteLine("Error: Type '{0}' was not found.", typeName);
                    }

                    throw new Exception(String.Format("Bad type '{0}'", typeName));
                }

                ConstructorInfo ctor = t.GetConstructor(m_SerialTypeArray);
                ConstructorInfo cctor = t.GetConstructor(_CustomSerialTypeArray);

                if (ctor != null)
                {
                    types.Add(new Tuple<ConstructorInfo, string>(ctor, typeName));
                }
                else if (cctor != null)
                {
                    types.Add(new Tuple<ConstructorInfo, string>(cctor, typeName));
                }
                else
                {
                    throw new Exception(String.Format("Type '{0}' does not have a serialization constructor", t));
                }
            }

            var ctorArgs = new object[1];
            _Data = new Dictionary<CustomSerial, SaveData>(count);
            List<DataEntry> data = new List<DataEntry>();
            for (int i = 0; i < v.Count(); ++i)
            {
                int typeID = (int)v[i].TypeID;
                int serial = (int)v[i].Serial;

                var objs = types[typeID];

                if (objs == null)
                {
                    continue;
                }

                SaveData savedata = null;
                ConstructorInfo ctor = objs.Item1;
                typeName = objs.Item2;

                ctorArgs[0] = (CustomSerial)serial;
                savedata = (SaveData)(ctor.Invoke(ctorArgs));


                if (savedata != null)
                {
                    data.Add(new DataEntry(savedata, typeID, typeName, 0, 0));
                    AddData(savedata);
                }

            }

            return data;
        }

        
        private static bool ProcessMobiles(List<Database.Mobile> v, List<MobileEntry> mobiles, List<Database.Skill> s)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int threads = 8;
            int mobileCount = mobiles.Count();
            int mod = 0;

            int[] group = new int[threads+1];
            group[0] = 0;
            //do some maths
            for (int i = 1; i < threads+1; i++)
            {
                group[i] = Math.Abs(mobileCount / threads) * i;
            }
            Math.DivRem(mobileCount, threads, out mod);
            group[threads] = group[threads] + mod; //add remainder to the last thread'

            Parallel.For(0, threads, index =>
              {
                  Chunk(v, mobiles, s, group[index], ((group[index + 1])));
              });

            for(int i =0; i<mobileCount; i++)
            {
                MobileEntry entry = mobiles[i];
                Mobile m = entry.Mobile;
                m_LoadingType = entry.TypeName;
                byte[] byteArray = Convert.FromBase64String(v[i].Data);
                MemoryStream bin = new MemoryStream(byteArray);
                BinaryFileReader reader = new BinaryFileReader(new BinaryReader(bin));
                m.Deserialize(reader);
                bin.Close();
                reader.Close();
            }

            Console.WriteLine("ProcessMobs: " + watch.Elapsed.TotalSeconds);
            return false;
        }


        private static void Chunk(List<Database.Mobile> v, List<MobileEntry> mobiles, List<Database.Skill> s, int start, int end)
        {
            for (int i = start; i < end; ++i)
            {
                MobileEntry entry = mobiles[i];
                Mobile m = entry.Mobile;

                m_LoadingType = entry.TypeName;
                m.Deserialize(v[i], s); //i see said the blind man

            }
        }

        private static bool ProcessItems(List<Database.Item> it, List<ItemEntry> items)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int itemCount = items.Count();
            

            for (int i = 0; i < itemCount; ++i)
            {
                ItemEntry entry = items[i];
                Item item = entry.Item;

                byte[] byteArray = Convert.FromBase64String(it[i].strim);
                MemoryStream bin = new MemoryStream(byteArray);
                BinaryFileReader reader = new BinaryFileReader(new BinaryReader(bin));

            m_LoadingType = entry.TypeName;
            item.Deserialize(it[i]);
            item.Deserialize(reader);
            }
            watch.Stop();
            Console.WriteLine("ProccessItems: " + watch.Elapsed.TotalSeconds);
            return false;
        }

        private static bool ProcessGuilds(List<Database.Guild> dbg, List<GuildEntry> guilds, List<Database.GuildAlliance> ga, List<Database.GuildWar> gw)
        {
            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < guilds.Count; ++i)
            {
                Database.Guild gp = new Database.Guild();
                GuildEntry entry = guilds[i];
                BaseGuild g = entry.Guild;
                gp = dbg[i];
                if (g != null)
                {
                g.Deserialize(gp);
                    g.Deserialize(ga);
                    g.Deserialize(gw);
                }
            }
            watch.Stop();
            Console.WriteLine("ProccessItems: " + watch.Elapsed.TotalSeconds);
            return false;
        }

        private static bool ProcessSaveData(List<Database.SaveData> a, List<DataEntry> data)
        {
                    for (int i = 0; i < data.Count; ++i)
                    {
                        DataEntry entry = data[i];
                        SaveData saveData = entry.Data;

                                m_LoadingType = entry.TypeName;
                                saveData.Deserialize(a[i]);
                if (a[i].Serialized.Length != 0)
                {
                    byte[] byteArray = Convert.FromBase64String(a[i].Serialized);
                    MemoryStream bin = new MemoryStream(byteArray);
                    BinaryFileReader reader = new BinaryFileReader(new BinaryReader(bin));
                    saveData.Deserialize(reader);
                    bin.Close();
                    reader.Close();
                }
                    }
            return false;
        }

        public static void LoadSQL()
        {
            if (m_Loaded)
            {
                return;
            }

            m_Loaded = true;
            m_LoadingType = null;

            Utility.PushColor(ConsoleColor.Yellow);
            Console.WriteLine("World: Loading from SQL DB...");
            Utility.PopColor();

            Stopwatch watch = Stopwatch.StartNew();

            m_Loading = true;

            _addQueue = new Queue<IEntity>();
            _deleteQueue = new Queue<IEntity>();
            _CustomsAddQueue = new Queue<ICustomsEntity>();
            _CustomsDeleteQueue = new Queue<ICustomsEntity>();

            int mobileCount = 0, itemCount = 0, guildCount = 0, dataCount = 0;

            var ctorArgs = new object[1];
            var guilds = new List<GuildEntry>();
            var data = new List<DataEntry>();
            List<Database.Mobile> v = new List<Database.Mobile>();
            List<Database.Item> it = new List<Database.Item>();
            List<MobileEntry> mobiles = new List<MobileEntry>();
            List<ItemEntry> items = new List<ItemEntry>();
            List<Database.Skill> s = new List<Database.Skill>();
            List<Database.Guild> g = new List<Database.Guild>();
            List<Database.GuildAlliance> ga = new List<Database.GuildAlliance>();
            List<Database.GuildWar> gw = new List<Database.GuildWar>();
            List<Database.SaveData> sdl = new List<Database.SaveData>();


            Parallel.Invoke(() =>
            {
                v = LoadMobsSQL();
            }, () =>
            {
                it = LoadItemsSQL();
            }, () =>
            {
                s = LoadSkillsSQL();
            }, () =>
            {
                g = LoadGuildsSQL();
            }, () =>
            {
                ga = LoadGuildAllianceSQL();
            }, () =>
            {
                gw = LoadGuildWarsSQL();
            }, () =>
            {
                sdl = LoadSaveDataSQL();
            });

            itemCount = it.Count();
            mobileCount = v.Count();
            m_Items = new Dictionary<Serial, Item>(itemCount);
            m_Mobiles = new Dictionary<Serial, Mobile>(mobileCount);

            Parallel.Invoke(() =>
            {
                items = ReadItemTypesSQL(itemCount, it);
            }, () =>
            {
                mobiles = ReadMobTypesSQL(mobileCount, v);
            }, () =>
            {
                data = ReadDataTypesSQL(sdl);
            });
            

            guildCount = g.Count();
            dataCount = sdl.Count();

                    CreateGuildEventArgs createEventArgs = new CreateGuildEventArgs(-1);
            foreach (Database.Guild a in g)
            {
                int id = a.Id;
                createEventArgs.Id = id;
                EventSink.InvokeCreateGuild(createEventArgs);
                BaseGuild guild = createEventArgs.Guild;
                if (guild != null)
                {
                    guilds.Add(new GuildEntry(guild, 0, 0));
                }
            }

            bool failedGuilds = false, failedData = false, failedMobiles = false, failedItems = false;

            failedMobiles = ProcessMobiles(v, mobiles, s);

            failedItems = ProcessItems(it, items);

            failedGuilds = ProcessGuilds(g, guilds, ga, gw);

            failedData = ProcessSaveData(sdl, data);

            Type failedType = null;
            Serial failedSerial = Serial.Zero;
            CustomSerial failedCustomSerial = CustomSerial.Zero;
            Exception failed = null;
            int failedTypeID = 0;


            m_LoadingType = null;


            if (failedItems || failedMobiles || failedGuilds || failedData)
            {
                Utility.PushColor(ConsoleColor.Red);
                Console.WriteLine("An error was encountered while loading a saved object");
                Utility.PopColor();

                Console.WriteLine(" - Type: {0}", failedType);

                if (failedSerial != Serial.Zero)
                {
                    Console.WriteLine(" - Serial: {0}", failedSerial);
                }
                else
                {
                    Console.WriteLine(" - Serial: {0}", failedCustomSerial);
                }

                if (!Core.Service)
                {
                    Console.WriteLine("Delete the object? (y/n)");

                    if (Console.ReadKey(true).Key == ConsoleKey.Y)
                    {
                        if (failedType != typeof(BaseGuild))
                        {
                            Console.WriteLine("Delete all objects of that type? (y/n)");

                            if (Console.ReadKey(true).Key == ConsoleKey.Y)
                            {
                                if (failedMobiles)
                                {
                                    for (int i = 0; i < mobiles.Count;)
                                    {
                                        if (mobiles[i].TypeID == failedTypeID)
                                        {
                                            mobiles.RemoveAt(i);
                                        }
                                        else
                                        {
                                            ++i;
                                        }
                                    }
                                }
                                else if (failedItems)
                                {
                                    for (int i = 0; i < items.Count;)
                                    {
                                        if (items[i].TypeID == failedTypeID)
                                        {
                                            items.RemoveAt(i);
                                        }
                                        else
                                        {
                                            ++i;
                                        }
                                    }
                                }
                                else if (failedData)
                                {
                                    for (int i = 0; i < data.Count;)
                                    {
                                        if (data[i].TypeID == failedTypeID)
                                        {
                                            data.RemoveAt(i);
                                        }
                                        else
                                        {
                                            ++i;
                                        }
                                    }
                                }
                            }
                        }

                        SaveIndex(mobiles, MobileIndexPath);
                        SaveIndex(items, ItemIndexPath);
                        SaveIndex(guilds, GuildIndexPath);
                        SaveIndex(DataIndexPath, data);
                    }

                    Console.WriteLine("After pressing return an exception will be thrown and the server will terminate.");
                    Console.ReadLine();
                }

                {
                    Utility.PushColor(ConsoleColor.Red);
                    Console.WriteLine("An exception will be thrown and the server will terminate.");
                    Utility.PopColor();
                }

                throw new Exception(
                    String.Format(
                        "Load failed (items={0}, mobiles={1}, guilds={2}, data={3}, type={4}, serial={5})",
                        failedItems,
                        failedMobiles,
                        failedGuilds,
                        failedData,
                        failedType,
                        (failedSerial != Serial.Zero ? failedSerial.ToString() : failedCustomSerial.ToString())),
                    failed);
            }

            EventSink.InvokeWorldLoad();

            m_Loading = false;

            ProcessSafetyQueues();

            foreach (Item item in m_Items.Values)
            {
                if (item.Parent == null)
                {
                    item.UpdateTotals();
                }

                item.ClearProperties();
            }

            foreach (Mobile m in m_Mobiles.Values)
            {
                m.UpdateRegion(); // Is this really needed?
                m.UpdateTotals();

                m.ClearProperties();
            }

            foreach (SaveData saveData in _Data.Values)
            {
                saveData.Prep();
            }

            watch.Stop();

            Utility.PushColor(ConsoleColor.Green);
            Console.WriteLine(
                "done ({1} items, {2} mobiles, {3} customs) ({0:F2} seconds)",
                watch.Elapsed.TotalSeconds,
                m_Items.Count,
                m_Mobiles.Count,
                _Data.Count);
            Utility.PopColor();
        }

        private static void ProcessSafetyQueues()
		{
			while (_addQueue.Count > 0)
			{
				IEntity entity = _addQueue.Dequeue();

				Item item = entity as Item;

				if (item != null)
				{
					AddItem(item);
				}
				else
				{
					Mobile mob = entity as Mobile;

					if (mob != null)
					{
						AddMobile(mob);
					}
				}
			}

			while (_deleteQueue.Count > 0)
			{
				IEntity entity = _deleteQueue.Dequeue();

				Item item = entity as Item;

				if (item != null)
				{
					item.Delete();
				}
				else
				{
					Mobile mob = entity as Mobile;

					if (mob != null)
					{
						mob.Delete();
					}
				}
			}

			while (_CustomsAddQueue.Count > 0)
			{
				ICustomsEntity entity = _CustomsAddQueue.Dequeue();

				SaveData data = entity as SaveData;

				if (data != null)
				{
					AddData(data);
				}
			}

			while (_CustomsDeleteQueue.Count > 0)
			{
				ICustomsEntity entity = _CustomsDeleteQueue.Dequeue();

				SaveData data = entity as SaveData;

				if (data != null)
				{
					data.Delete();
				}
			}
		}

		private static void AppendSafetyLog(string action, ICustomsEntity entity)
		{
			string message =
				String.Format(
					"Warning: Attempted to {1} {2} during world save." + "{0}This action could cause inconsistent state." +
					"{0}It is strongly advised that the offending scripts be corrected.",
					Environment.NewLine,
					action,
					entity);

			AppendSafetyLog(message);
		}

		private static void AppendSafetyLog(string action, IEntity entity)
		{
			string message =
				String.Format(
					"Warning: Attempted to {1} {2} during world save." + "{0}This action could cause inconsistent state." +
					"{0}It is strongly advised that the offending scripts be corrected.",
					Environment.NewLine,
					action,
					entity);

			AppendSafetyLog(message);
		}

		private static void AppendSafetyLog(string message)
		{
			Console.WriteLine(message);

			try
			{
				using (StreamWriter op = new StreamWriter("world-save-errors.log", true))
				{
					op.WriteLine("{0}\t{1}", DateTime.UtcNow, message);
					op.WriteLine(new StackTrace(2).ToString());
					op.WriteLine();
				}
			}
			catch
			{ }
		}

		private static void SaveIndex<T>(List<T> list, string path) where T : IEntityEntry
		{
			if (!Directory.Exists("Saves/Mobiles/"))
			{
				Directory.CreateDirectory("Saves/Mobiles/");
			}

			if (!Directory.Exists("Saves/Items/"))
			{
				Directory.CreateDirectory("Saves/Items/");
			}

			if (!Directory.Exists("Saves/Guilds/"))
			{
				Directory.CreateDirectory("Saves/Guilds/");
			}

			using (FileStream idx = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				BinaryWriter idxWriter = new BinaryWriter(idx);

				idxWriter.Write(list.Count);

				for (int i = 0; i < list.Count; ++i)
				{
					T e = list[i];

					idxWriter.Write(e.TypeID);
					idxWriter.Write(e.Serial);
					idxWriter.Write(e.Position);
					idxWriter.Write(e.Length);
				}

				idxWriter.Close();
			}
		}

		private static void SaveIndex<T>(string path, List<T> list) where T : ICustomsEntry
		{
			if (!Directory.Exists("Saves/Customs/"))
			{
				Directory.CreateDirectory("Saves/Customs/");
			}

			using (FileStream indexStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				BinaryWriter indexWriter = new BinaryWriter(indexStream);

				indexWriter.Write(list.Count);

				for (int i = 0; i < list.Count; ++i)
				{
					T e = list[i];

					indexWriter.Write(e.TypeID);
					indexWriter.Write(e.Serial);
					indexWriter.Write(e.Position);
					indexWriter.Write(e.Length);
				}

				indexWriter.Close();
			}
		}

		internal static int m_Saves;

		public static void Save()
		{
			Save(true, false);
		}



        public static void Save(bool message, bool permitBackgroundWrite)
		{
			if (m_Saving)
			{
				return;
			}

			++m_Saves;

			NetState.FlushAll();
			NetState.Pause();

			WaitForWriteCompletion(); //Blocks Save until current disk flush is done.

			m_Saving = true;

			m_DiskWriteHandle.Reset();

			if (message)
			{
				Broadcast(0x35, true, "The world is saving, please wait.");
			}

			SaveStrategy strategy = SaveStrategy.Acquire();
			Console.WriteLine("Core: Using {0} save strategy", strategy.Name.ToLowerInvariant());

			Console.Write("World: Saving...");

			Stopwatch watch = Stopwatch.StartNew();

			if (!Directory.Exists("Saves/Customs/"))
			{
				Directory.CreateDirectory("Saves/Customs/");
			}

			/*using ( SaveMetrics metrics = new SaveMetrics() ) {*/
			strategy.Save(null, permitBackgroundWrite);
			/*}*/

			try
			{
				EventSink.InvokeWorldSave(new WorldSaveEventArgs(message));
			}
			catch (Exception e)
			{
				throw new Exception("World Save event threw an exception.  Save failed!", e);
			}

			watch.Stop();

			m_Saving = false;

			if (!permitBackgroundWrite)
			{
				NotifyDiskWriteComplete();
				//Sets the DiskWriteHandle.  If we allow background writes, we leave this upto the individual save strategies.
			}

			ProcessSafetyQueues();

			strategy.ProcessDecay();

			Console.WriteLine("Save finished in {0:F2} seconds.", watch.Elapsed.TotalSeconds);

			if (message)
			{
				Broadcast(0x35, true, "World save complete. The entire process took {0:F1} seconds.", watch.Elapsed.TotalSeconds);
			}

			NetState.Resume();
            if (Core.UseSQL)
            {
                strategy.Save(null, true);
            }

        }

        internal static List<Type> m_ItemTypes = new List<Type>();
		internal static List<Type> m_MobileTypes = new List<Type>();
		internal static List<Type> _DataTypes = new List<Type>();

		public static IEntity FindEntity(Serial serial)
		{
			if (serial.IsItem)
			{
				return FindItem(serial);
			}
			else if (serial.IsMobile)
			{
				return FindMobile(serial);
			}

			return null;
		}

		public static ICustomsEntity FindCustomEntity(CustomSerial serial)
		{
			if (serial.IsValid)
			{
				return GetData(serial);
			}

			return null;
		}

		public static Mobile FindMobile(Serial serial)
		{
			Mobile mob;

			m_Mobiles.TryGetValue(serial, out mob);

			return mob;
		}

		public static void AddMobile(Mobile m)
		{
			if (m_Saving)
			{
				AppendSafetyLog("add", m);
				_addQueue.Enqueue(m);
			}
			else
			{
				m_Mobiles[m.Serial] = m;
			}
		}

		public static Item FindItem(Serial serial)
		{
			Item item;

			m_Items.TryGetValue(serial, out item);

            return item;
            
		}

		public static void AddItem(Item item)
		{
			if (m_Saving)
			{
				AppendSafetyLog("add", item);
				_addQueue.Enqueue(item);
			}
			else
			{
				m_Items[item.Serial] = item;
			}
		}

		public static void RemoveMobile(Mobile m)
		{
			m_Mobiles.Remove(m.Serial);
		}

		public static void RemoveItem(Item item)
		{
			m_Items.Remove(item.Serial);
		}

		public static void AddData(SaveData data)
		{
			if (m_Saving)
			{
				AppendSafetyLog("add", data);
				_CustomsAddQueue.Enqueue(data);
			}
			else
			{
				_Data[data.Serial] = data;
			}
		}

		public static void RemoveData(SaveData data)
		{
			_Data.Remove(data.Serial);
		}

		public static SaveData GetData(CustomSerial serial)
		{
			SaveData data;

			_Data.TryGetValue(serial, out data);

			return data;
		}

		public static SaveData GetData(string name)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data.Name == name)
				{
					return data;
				}
			}

			return null;
		}

		public static SaveData GetData(Type type)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data.GetType() == type)
				{
					return data;
				}
			}

			return null;
		}

		public static List<SaveData> GetDataList(Type type)
		{
			var results = new List<SaveData>();

			foreach (SaveData data in _Data.Values)
			{
				if (data.GetType() == type)
				{
					results.Add(data);
				}
			}

			return results;
		}

		public static List<SaveData> SearchData(string find)
		{
			var keywords = find.ToLower().Split(' ');
			var results = new List<SaveData>();

			foreach (SaveData data in _Data.Values)
			{
				bool match = true;
				string name = data.Name.ToLower();

				for (int i = 0; i < keywords.Length; i++)
				{
					if (name.IndexOf(keywords[i]) == -1)
					{
						match = false;
					}
				}

				if (match)
				{
					results.Add(data);
				}
			}

			return results;
		}

		public static SaveData GetCore(Type type)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data.GetType() == type)
				{
					return data;
				}
			}

			return null;
		}

		public static List<SaveData> GetCores(Type type)
		{
			var results = new List<SaveData>();

			foreach (SaveData data in _Data.Values)
			{
				if (data.GetType() == type)
				{
					results.Add(data);
				}
			}

			return results;
		}

		public static BaseModule GetModule(Mobile mobile)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.LinkedMobile == mobile)
					{
						return module;
					}
				}
			}

			return null;
		}

		public static List<BaseModule> GetModules(Mobile mobile)
		{
			var results = new List<BaseModule>();

			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.LinkedMobile == mobile)
					{
						results.Add(module);
					}
				}
			}

			return results;
		}

		public static BaseModule GetModule(Item item)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.LinkedItem == item)
					{
						return module;
					}
				}
			}

			return null;
		}

		public static List<BaseModule> GetModules(Item item)
		{
			var results = new List<BaseModule>();

			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.LinkedItem == item)
					{
						results.Add(module);
					}
				}
			}

			return results;
		}

		public static BaseModule GetModule(Mobile mobile, string name)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.Name == name && module.LinkedMobile == mobile)
					{
						return module;
					}
				}
			}

			return null;
		}

		public static List<BaseModule> GetModules(Mobile mobile, string name)
		{
			var results = new List<BaseModule>();

			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.Name == name && module.LinkedMobile == mobile)
					{
						results.Add(module);
					}
				}
			}

			return results;
		}

		public static BaseModule GetModule(Mobile mobile, Type type)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.GetType() == type && module.LinkedMobile == mobile)
					{
						return module;
					}
				}
			}

			return null;
		}

		public static BaseModule GetModule(Item item, string name)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.Name == name && module.LinkedItem == item)
					{
						return module;
					}
				}
			}

			return null;
		}

		public static List<BaseModule> GetModules(Item item, string name)
		{
			var results = new List<BaseModule>();

			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.Name == name && module.LinkedItem == item)
					{
						results.Add(module);
					}
				}
			}

			return results;
		}

		public static BaseModule GetModule(Item item, Type type)
		{
			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					if (module.GetType() == type && module.LinkedItem == item)
					{
						return module;
					}
				}
			}

			return null;
		}

		public static List<BaseModule> SearchModules(Mobile mobile, string text)
		{
			var keywords = text.ToLower().Split(' ');
			var results = new List<BaseModule>();

			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					bool match = true;
					string name = module.Name.ToLower();

					for (int i = 0; i < keywords.Length; i++)
					{
						if (name.IndexOf(keywords[i]) == -1)
						{
							match = false;
						}
					}

					if (match && module.LinkedMobile == mobile)
					{
						results.Add(module);
					}
				}
			}

			return results;
		}

		public static List<BaseModule> SearchModules(Item item, string text)
		{
			var keywords = text.ToLower().Split(' ');
			var results = new List<BaseModule>();

			foreach (SaveData data in _Data.Values)
			{
				if (data is BaseModule)
				{
					BaseModule module = data as BaseModule;

					bool match = true;
					string name = module.Name.ToLower();

					for (int i = 0; i < keywords.Length; i++)
					{
						if (name.IndexOf(keywords[i]) == -1)
						{
							match = false;
						}
					}

					if (match && module.LinkedItem == item)
					{
						results.Add(module);
					}
				}
			}

			return results;
		}
	}
}