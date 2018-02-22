using NLog;
using sensu_client.net.pluginterface;
using sensu_client.net_mssql;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace sensu_client.net.plugin
{
    public static class SQLHelper
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static SqlConnection GetSqlConnection (string m_sql_server, string m_sql_user="", string m_sql_user_password="")
        {

            SqlConnection m_sql_connection;

            if (!String.IsNullOrEmpty(m_sql_user) && !String.IsNullOrEmpty(m_sql_user_password))
            {
                m_sql_connection = new SqlConnection(String.Format("Data Source={0};User ID={1};Password={2};MultipleActiveResultSets=true", m_sql_server, m_sql_user, m_sql_user_password));
                Log.Debug("Creating standard SQL Connection");
            }
            else
            {
                //SSPI Auth
                m_sql_connection = new SqlConnection(String.Format("Data Source={0};Integrated Security=SSPI;MultipleActiveResultSets=true", m_sql_server));
                Log.Debug("Creating SSPI SQL Connection");
            }

            return m_sql_connection;

        }

        public static StringBuilder GetPerformanceCounters(SqlConnection m_sql_connection)
        {
            return ExecuteAndParseMetricQuery(m_sql_connection, MetricType.PerformanceCounter, net_mssql.Properties.Settings.Default.PerformanceCounter);
        }

        public static StringBuilder GetPerformanceMetrics(SqlConnection m_sql_connection)
        {
            return ExecuteAndParseMetricQuery(m_sql_connection, MetricType.PerformanceMetrics, net_mssql.Properties.Settings.Default.PerformanceMetric);
        }

        public static StringBuilder GetMemoryClerk(SqlConnection m_sql_connection)
        {
            return ExecuteAndParseMetricQuery(m_sql_connection, MetricType.MemoryClerk, net_mssql.Properties.Settings.Default.MemoryClerk);
        }

        public static StringBuilder GetDatabaseIO(SqlConnection m_sql_connection)
        {
            return ExecuteAndParseMetricQuery(m_sql_connection, MetricType.DatabaseIO, net_mssql.Properties.Settings.Default.DatabaseIO);
        }

        public static StringBuilder GetDatabaseSize(SqlConnection m_sql_connection)
        {
            return ExecuteAndParseMetricQuery(m_sql_connection, MetricType.DatabaseSize, net_mssql.Properties.Settings.Default.DatabaseSize);
        }

        public static StringBuilder GetWaitStats(SqlConnection m_sql_connection)
        {
            return ExecuteAndParseMetricQuery(m_sql_connection, MetricType.WaitStats, net_mssql.Properties.Settings.Default.WaitStats);
        }

        public static StringBuilder GetServerProperties(SqlConnection m_sql_connection)
        {
            return ExecuteAndParseMetricQuery(m_sql_connection, MetricType.ServerProperties, net_mssql.Properties.Settings.Default.ServerProperties);
        }

        public static ExecuteResult CheckAvailbilityGroups(SqlConnection m_sql_connection)
        {
            Log.Info("Calling Execute");

            return ExecuteAndParseCheck(m_sql_connection, CheckType.AvailbilityGroup, "SELECT * FROM sys.dm_hadr_availability_group_states");
        }

        public static ExecuteResult CheckAvailbilityGroupDatabases(SqlConnection m_sql_connection)
        {
            return ExecuteAndParseCheck(m_sql_connection, CheckType.AvailbilityGroupDatabases, "SELECT db_name(database_id) AS 'DBName',is_local, database_state_desc, synchronization_health_desc FROM sys.dm_hadr_database_replica_states WHERE is_local = 1;");
        }

        private static List<string>GetDefaultColumnBlacklist()
        {
            List<string> m_blacklist = new List<string>();

            m_blacklist.Add("measurement");
            m_blacklist.Add("sql_instance");
            m_blacklist.Add("host");
            m_blacklist.Add("database_name");

            return m_blacklist;
        }

        private static string GetSQLServerName(SqlConnection m_sql_connection)
        { 
       
            string m_servername = string.Empty;
            SqlCommand m_sql_command = null;
            SqlDataReader m_sql_reader=null;

            try
            {
                m_sql_command = new SqlCommand("SELECT @@SERVERNAME AS 'ServerName'");
                m_sql_command.Connection = m_sql_connection;
                m_sql_reader = m_sql_command.ExecuteReader();
                m_sql_command.Dispose();

                if (m_sql_reader != null && m_sql_reader.HasRows == true)

                {
                    m_sql_reader.Read();
                    m_servername = GetValueByColumnName(m_sql_reader, "ServerName");

                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
            finally
            {
                if(m_sql_reader != null)
                {
                    m_sql_reader.Close();
                }
            }

            return m_servername; 
        }

        private static List<string>ParseColumns(SqlDataReader m_sql_reader,List<string> m_blacklist)
        {

            List<string> m_columns = new List<string>();

            Log.Debug("Parsing Columns...");

            for (int i = 3; i < m_sql_reader.FieldCount; i++)
            {

                string m_colname = m_sql_reader.GetName(i);

                if (!GetDefaultColumnBlacklist().Any(m_blacklist_col => m_colname.ToLower().Equals(m_blacklist_col.ToLower())))
                {
                    m_columns.Add(m_colname);
                }
           
            }

            Log.Debug("Done!");

            return m_columns;

        }

        private static string GetValueByColumnName (SqlDataReader m_sql_reader, string m_columname,bool m_escape_chars =false)
        {

            string m_value = m_sql_reader.GetValue(m_sql_reader.GetOrdinal(m_columname)).ToString();

            if (m_escape_chars == true)
            {


                m_value = m_value.Replace(" ", "");

                m_value = m_value.Replace("(ms/sec)", "ms_per_second");
                m_value = m_value.Replace("/sec", "_per_second");
                m_value = m_value.Replace("(KB)", "_kb");
                m_value = m_value.Replace("file(s)", "files");
                m_value = m_value.Replace("File(s)", "Files");
                m_value = m_value.Replace("%", "_percent");

                m_value = m_value.Replace(":", "_");     
                m_value = m_value.Replace("/", "_");
                m_value = m_value.Replace("(", "_");
                m_value = m_value.Replace(")", "_");
                m_value = m_value.Replace("\\", "_");

                //Safty first
                m_value = m_value.Replace(" ", "_");
            }
           
            return m_value;

        }

        private static ExecuteResult ExecuteAndParseCheck(SqlConnection m_sql_connection, CheckType m_checktype, string m_sqlscript)

        {
            ExecuteResult m_result = new ExecuteResult();
            SqlCommand m_sqlcommand = new SqlCommand(m_sqlscript);
            SqlDataReader m_sql_reader = null;
            string m_servername = String.Empty;

            try
            {

                m_sqlcommand.Connection = m_sql_connection;
                Log.Debug("Executing SQL Command...");
                m_sql_reader = m_sqlcommand.ExecuteReader();
                Log.Debug("Done!");
                m_sqlcommand.Dispose();

                if (m_sql_reader != null && m_sql_reader.HasRows == true)
                {

                    string m_recovery_health = string.Empty;
                    string m_synchronization_health = string.Empty;
                    string m_database_state = string.Empty;
                    string m_primary_replica_node = string.Empty;

                    switch (m_checktype)
                    {

                        case CheckType.AvailbilityGroup:

                            m_servername = GetSQLServerName(m_sql_connection);

                            if (string.IsNullOrWhiteSpace(m_servername))
                            {
                                throw new Exception("failed to get sql servername");
                            }

                            //we should get only one row here
                            m_sql_reader.Read();

                            m_primary_replica_node = GetValueByColumnName(m_sql_reader ,"primary_replica");

                            m_synchronization_health = GetValueByColumnName(m_sql_reader, "synchronization_health_desc");

                            if (m_servername.Equals(m_primary_replica_node))
                            {
                                m_recovery_health = GetValueByColumnName(m_sql_reader, "primary_recovery_health_desc");
                            }
                            else
                            {
                                //we are on the secondary node
                                m_recovery_health = GetValueByColumnName(m_sql_reader, "secondary_recovery_health_desc");
                            }


                            if (m_synchronization_health.ToUpper().Equals("HEALTHY") && m_recovery_health.ToUpper().Equals("ONLINE"))
                            {
                               
                                m_result.Output = String.Format("CheckMSSQLAvailbilityGroup OK: Node Recovery State: {0}; Node Synchronization Health: {1}",m_recovery_health ,m_synchronization_health);
                                m_result.ExitCode = 0;

                            }
                            else
                            {
                                m_result.Output = String.Format("CheckMSSQLAvailbilityGroup CRITICAL: Node Recovery State: {0}; Node Synchronization Health: {1}", m_recovery_health, m_synchronization_health);
                                m_result.ExitCode = 2;
                            }

                            break;


                        case CheckType.AvailbilityGroupDatabases:

                            int m_count_crit = 0;

                            while (m_sql_reader.Read())

                            {

                                m_synchronization_health = GetValueByColumnName(m_sql_reader, "synchronization_health_desc");
                                m_database_state = GetValueByColumnName(m_sql_reader, "database_state_desc");

                                if (!m_synchronization_health.ToUpper().Equals("HEALTHY") || !m_database_state.ToUpper().Equals("ONLINE"))
                                {
                                    m_count_crit += 1;
                                }

                            }


                            if (m_count_crit >= 1)
                            {
                                m_result.Output = String.Format("CheckMSSQLAvailbilityGroupDatabases CRITICAL: {0} Databases are not HEALTY or ONLINE", m_count_crit);
                                m_result.ExitCode = 2;
                            }
                            else
                            {
                                m_result.Output = String.Format("CheckMSSQLAvailbilityGroupDatabases OK: All Databases are HEALTY and ONLINE");
                                m_result.ExitCode = 0;
                            }

                            break;

                                default:

                            throw new Exception("undefined checktype!");

                    }

                }
                else
                {
                    Log.Error("SQL Reader is null or has no rows!");
                }

                }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                m_result = null;
            }
            finally
            {
                if (m_sql_reader != null)
                {
                    m_sql_reader.Close();
                }
            }

            return m_result;

        }

        private static StringBuilder ExecuteAndParseMetricQuery(SqlConnection m_sql_connection, MetricType m_metrictype, string m_sqlscript)

        {

            StringBuilder m_result = new StringBuilder();
            SqlCommand m_sqlcommand = new SqlCommand(m_sqlscript);
            SqlDataReader m_sql_reader = null;

            try
            {

                m_sqlcommand.Connection = m_sql_connection;

                Log.Debug("Executing SQL Command...");

                m_sql_reader = m_sqlcommand.ExecuteReader();

                Log.Debug("Done!");

                m_sqlcommand.Dispose();

                if (m_sql_reader != null && m_sql_reader.HasRows == true)
                {

                    List<string> m_columns = new List<string>();
                    List<string> m_metric_blacklist = new List<string>();

                    switch (m_metrictype)

                    {

                        case MetricType.DatabaseIO:

                            m_metric_blacklist = GetDefaultColumnBlacklist();
                            m_metric_blacklist.Add("file_type");

                            double m_total_read_bytes_row = 0;
                            double m_total_write_bytes_row = 0;
                            double m_total_read_bytes_log = 0;
                            double m_total_write_bytes_log = 0;
                            string m_sql_instance = String.Empty;

                            m_columns = ParseColumns(m_sql_reader, m_metric_blacklist);

                            if (m_columns.Count == 0)
                            {
                                throw new Exception("failed to get columns!");
                            }

                            while (m_sql_reader.Read())

                            {
                                foreach (string m_column in m_columns)
                                {

                                   //host.mssql.measurement.sql_instance.databasename.filetype.field value timestamp
                                    m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3}.{4}.{5} {6} {7}", Helper.GetFQDN().ToLower(),m_metrictype.ToString(), GetValueByColumnName(m_sql_reader, "sql_instance"), GetValueByColumnName(m_sql_reader, "database_name"), GetValueByColumnName(m_sql_reader, "file_type"),m_column.ToLower(), GetValueByColumnName(m_sql_reader ,m_column), Helper.CreateTimeStamp().ToString()));

                                }

                                if (GetValueByColumnName(m_sql_reader, "file_type").ToLower().Equals("rows"))
                                {

                                    m_total_read_bytes_row += Double.Parse(GetValueByColumnName(m_sql_reader, "read_bytes"));
                                    m_total_write_bytes_row += Double.Parse(GetValueByColumnName(m_sql_reader, "write_bytes"));

                                }
                                else
                                {
                                    m_total_read_bytes_log += Double.Parse(GetValueByColumnName(m_sql_reader, "read_bytes"));
                                    m_total_write_bytes_log += Double.Parse(GetValueByColumnName(m_sql_reader, "write_bytes"));
                                }

                                m_sql_instance = GetValueByColumnName(m_sql_reader, "sql_instance");

                            }

                            m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3}.{4}.{5} {6} {7}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), m_sql_instance, "Total", "ROWS", "read_bytes", m_total_read_bytes_row, Helper.CreateTimeStamp().ToString()));
                            m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3}.{4}.{5} {6} {7}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), m_sql_instance, "Total", "ROWS", "write_bytes", m_total_write_bytes_row, Helper.CreateTimeStamp().ToString()));

                            m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3}.{4}.{5} {6} {7}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), m_sql_instance, "Total", "LOG", "read_bytes", m_total_read_bytes_log, Helper.CreateTimeStamp().ToString()));
                            m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3}.{4}.{5} {6} {7}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), m_sql_instance, "Total", "LOG", "write_bytes", m_total_write_bytes_log, Helper.CreateTimeStamp().ToString()));

                            break;


                        case MetricType.ServerProperties:

                            m_metric_blacklist = GetDefaultColumnBlacklist();

                            m_columns = ParseColumns(m_sql_reader, m_metric_blacklist);

                            while (m_sql_reader.Read())

                            {
                                foreach (string m_column in m_columns)
                                {
                                    //host.mssql.measurement.sql_instance.field value timestamp
                                    m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3} {4} {5}", Helper.GetFQDN().ToLower(),m_metrictype.ToString(), GetValueByColumnName(m_sql_reader, "sql_instance"), m_column.ToLower(), GetValueByColumnName(m_sql_reader, m_column), Helper.CreateTimeStamp().ToString()));
                                }

                            }

                            break;


                        case MetricType.PerformanceCounter:

                            while (m_sql_reader.Read())

                            {

                                string m_instance = GetValueByColumnName(m_sql_reader, "instance", true);

                                if (String.IsNullOrWhiteSpace (m_instance))
                                {
                                    m_instance = "none";
                                }

                                //host.mssql.measurement.sql_instance.object.instance/database.counter/field value timestamp
                                m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3}.{4}.{5} {6} {7}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), GetValueByColumnName(m_sql_reader, "sql_instance"), GetValueByColumnName(m_sql_reader, "object", true), m_instance, GetValueByColumnName(m_sql_reader, "counter", true), GetValueByColumnName(m_sql_reader, "value"), Helper.CreateTimeStamp().ToString()));

                            }

                            break;


                        case MetricType.PerformanceMetrics:

                            m_metric_blacklist = GetDefaultColumnBlacklist();
                            m_columns = ParseColumns(m_sql_reader, m_metric_blacklist);
                            m_metric_blacklist.Add("type");

                            while (m_sql_reader.Read())

                            {
                                foreach (string m_column in m_columns)
                                {
                                    //host.mssql.measurement.sql_instance.field value timestamp
                                    Log.Debug("Column Name:{0}", m_column);
                                    m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3} {4} {5}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), GetValueByColumnName(m_sql_reader, "sql_instance"), m_column, GetValueByColumnName(m_sql_reader, m_column), Helper.CreateTimeStamp().ToString()));
                                }
                 
                            }

                            break;

                        case MetricType.WaitStats:

                            m_metric_blacklist = GetDefaultColumnBlacklist();
                            m_metric_blacklist.Add("wait_category");
                            m_metric_blacklist.Add("wait_type");

                            m_columns = ParseColumns(m_sql_reader, m_metric_blacklist);

                            while (m_sql_reader.Read())

                            {
                                foreach (string m_column in m_columns)
                                {
                                    //host.mssql.measurement.sql_instance.waittype.waitcategory.field value timestamp
                                    m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3}.{4}.{5} {6} {7}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), GetValueByColumnName(m_sql_reader, "sql_instance"), GetValueByColumnName(m_sql_reader, "wait_type"), GetValueByColumnName(m_sql_reader, "wait_category",true), m_column.ToLower(), GetValueByColumnName(m_sql_reader, m_column), Helper.CreateTimeStamp().ToString()));
                                }

                            }

                            break;


                        case MetricType.MemoryClerk:

                            while (m_sql_reader.Read())

                            {

                                //host.mssql.measurement.sql_instance.field value timestamp
                                m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3} {4} {5}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), GetValueByColumnName(m_sql_reader, "sql_instance"), GetValueByColumnName(m_sql_reader, "clerk_type", true), GetValueByColumnName(m_sql_reader, "size_kb"), Helper.CreateTimeStamp().ToString()));

                            }

                            break;


                        case MetricType.DatabaseSize:

                            m_metric_blacklist = GetDefaultColumnBlacklist();
                            m_metric_blacklist.Add("type");

                            m_columns = ParseColumns(m_sql_reader, m_metric_blacklist);

                            if (m_columns.Count == 0)
                            {
                                throw new Exception("failed to get columns!");
                            }

                            while (m_sql_reader.Read())

                            {
                                foreach (string m_column in m_columns)
                                {
                                    //host.mssql.measurement.sql_instance.databasename.field value timestamp
                                    m_result.AppendLine(String.Format("{0}.mssql.{1}.{2}.{3}.{4} {5} {6}", Helper.GetFQDN().ToLower(), m_metrictype.ToString(), GetValueByColumnName(m_sql_reader, "sql_instance"), m_column, GetValueByColumnName(m_sql_reader, "measurement"), GetValueByColumnName(m_sql_reader, m_column), Helper.CreateTimeStamp().ToString()));
                                }

                            }

                            break;

                        default:
                            throw new Exception("undefined metrictype!");
                    }

                }
                else
                {
                    Log.Error("SQL Reader is null or has no rows!");
                }

            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                m_result = null;
            }
            finally
            {
                if (m_sql_reader != null)
                {
                    m_sql_reader.Close();
                }
            }

            return m_result;

        }

    }
}
