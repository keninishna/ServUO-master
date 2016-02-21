using System.Data.Linq;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data.Linq.Mapping;
using System.Linq;
using System.Data;

namespace Database
{

    partial class UODataContext
    {
        partial void OnCreated()
        {
            CommandTimeout = 5 * 60;
        }

        public void BulkInsertAll<T>(IEnumerable<T> entities)
        {
            using (var conn = new SqlConnection(Connection.ConnectionString))
            {
                conn.Open();

                Type t = typeof(T);

                var tableAttribute = (TableAttribute)t.GetCustomAttributes(
                    typeof(TableAttribute), false).Single();

                var bulkCopy = new SqlBulkCopy(conn)
                {
                    DestinationTableName = tableAttribute.Name
                };

                var properties = t.GetProperties().Where(EventTypeFilter).ToArray();
                var table = new DataTable();

                foreach (var property in properties)
                {
                    Type propertyType = property.PropertyType;
                    if (propertyType.IsGenericType &&
                        propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        propertyType = Nullable.GetUnderlyingType(propertyType);
                    }

                        table.Columns.Add(new DataColumn(property.Name, propertyType));
                    
                }


                foreach (var entity in entities)
                {
                    table.Rows.Add(
                        properties.Select(
                        property => property.GetValue(entity, null) ?? DBNull.Value
                        ).ToArray());

                }
                bulkCopy.WriteToServer(table);
            }
        }

        private bool EventTypeFilter(System.Reflection.PropertyInfo p)
        {
            var attribute = Attribute.GetCustomAttribute(p,
                typeof(AssociationAttribute)) as AssociationAttribute;
            if (p.Name == "Skills") return false;
            if (p.Name == "Items") return false;
            if (p.Name == "Mobiles") return false;

            if (attribute == null) return true;
            if (attribute.IsForeignKey == false) return true;

            return false;
        }
    }
}

namespace Database
{

    

    public static class LinqExtension
    {
        public static void Truncate<TEntity>(this Table<TEntity> table) where TEntity : class
        {
            var rowType = table.GetType().GetGenericArguments()[0];
            var tableName = table.Context.Mapping.GetTable(rowType).TableName;
            var sqlCommand = String.Format("TRUNCATE TABLE {0}", tableName);
            table.Context.ExecuteCommand(sqlCommand);
        }
    }
}

