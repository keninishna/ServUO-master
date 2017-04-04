# [ServUO SQL]

Servuo SQL is a fork of Servuo to use a SQL database for save states. It uses multi threading to load from the SQL database to speed up load times however even with 100% CPU usage world loads are on average slower than the standard serialized world saves. The serialization methods currently used are very efficient however they are prone to corruption and are very difficult to modify. As of right now only base attribute properties are directly saved to the SQL database and the extended property attributes are serialized and tagged at the end of the column of the item/mobile. This at least allows for some control over the world saves and can be easily implemented into other widely available software that use SQL. 


### Version
Publish 54. Old I know I will update to latest commits soon.

### Installation

1. Install Microsoft SQL server or SQL server express from the microsoft website.
2. If you are not running under Administrator account. Make sure the account ServUOSQL is running under has been given permissions to make changes to the SQL server.
3. Modify the autosave.cfg file under /Config/ folder to the following if you want to save and load to SQL:
SQLSaveEnabled=true
SQLLoadEnabled=true
SQLConnect=Data Source=localhost;Initial Catalog=UO;Integrated Security=True
4. Run ServUO.

If you already have a world save from another version of ServUO change the SQLLoadEnabled=false and copy the /Save/ folder to the ServuoSQL /Save/ folder and start ServuoSQL from that and then type save in the console to save it to the SQL database. Change SQLLoadEnabled=true back after you have successfully saved to the db.

### Linux
I am able to build and run this under mono, however linux does not support mssql very well and I'm not sure how functional mono's linq2sql is. I was unable to get it to connect to a remote sql instance on windows either. I plan on eventually adding support for a 3rd party framework and adding PostgreSQL support. 

### Stuff to do
Add a SQL save command and configuration and status to in game admin gump.
Create SQL equivalent classes to Serialize and Deserialize that can be ovverriden so future use is easy to implement.
Create extended attribute values tables that can hold any amount of any type of properties that would need to be saved.
Add support to save both regular serialized way and SQL save at the same time. This would give shard owners some simple redundancy.


License
----

GPL V2




   [ServUO]: <https://servuo.com>
   [quickstart]: <https://www.servuo.com/tutorials/getting-started-with-servuo.2/>
